<#
.SYNOPSIS
Fills planned_session_blocks/planned_session_exercises from a JSON content file.

.DESCRIPTION
Structured exercise blocks have no authoring surface: the API exposes only
ScheduledDate and Objective (PlannedSessionEndpoints), and the trigger
app.reject_published_plan_content_change refuses any write that lands on a
published version. The only writers today are supabase/seed.sql and
app.clone_training_plan_draft, so populating a real plan needs this script.

It emits one transactional SQL script that reuses the owner isolation the API
applies (set local role rp_api plus request.jwt.claim.sub) and then, inside a
single DO block:

  1. reuses the pending draft of the plan, or clones one from the published
     version (app.clone_training_plan_draft, which fails when a draft exists);
  2. replaces the blocks -- and the main_set summary, when the file carries one
     -- of every session named in the content file. A block may carry no
     exercises: a running session is a sequence of steps (warm up, intervals,
     recoveries) that the catalogue does not hold, and its instructions say it;
  3. publishes the draft when -Publish is given.

Each session is matched by scheduledDate, optionally narrowed by sessionType and
modality when the week schedules more than one session that day. scheduledDate
also accepts today[+-N], which the local seed needs: it schedules its week from
current_date, so a fixed date stops matching the day after it is written.

Run it with -DatabaseUrl to apply the script through psql, or without it to
print the SQL for the Supabase SQL editor.

Publishing supersedes the running version, and activity_session_links,
planned_session_outcomes and session_checkins stay attached to the sessions of
the superseded version -- the dashboard counts the published version only, so a
week already executed reads as pending afterwards. Populate and publish before
the sessions are executed.

.EXAMPLE
pwsh ./scripts/Set-PlannedSessionBlocks.ps1 `
    -ContentPath ./scripts/planned-session-blocks.sample.json `
    -OwnerId 11111111-1111-4111-8111-111111111111 `
    -DatabaseUrl postgresql://postgres:postgres@127.0.0.1:54322/postgres `
    -Publish
#>
param(
    [Parameter(Mandatory = $true)]
    [string] $ContentPath,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[0-9a-fA-F-]{36}$')]
    [string] $OwnerId,

    [string] $DatabaseUrl,

    [string] $SqlOutPath,

    [switch] $Publish
)

$ErrorActionPreference = 'Stop'

$resolvedContent = (Resolve-Path -LiteralPath $ContentPath).Path
$content = Get-Content -LiteralPath $resolvedContent -Raw -Encoding utf8 | ConvertFrom-Json

if (-not $content.planName) { throw 'The content file needs a planName.' }
if (-not $content.sessions -or $content.sessions.Count -eq 0) { throw 'The content file needs at least one session.' }

$blockTypes = @('warmup', 'main', 'cooldown', 'circuit', 'mobility')
foreach ($session in $content.sessions) {
    # The local seed schedules its week from current_date, so a fixed ISO date
    # stops matching the day after the file is written. today[+-N] keeps a
    # sample runnable, and resolves here so the SQL still carries a real date.
    if ($session.scheduledDate -match '^today(?:\s*([+-])\s*(\d+))?$') {
        $offset = if ($Matches[2]) { [int] $Matches[2] } else { 0 }
        if ($Matches[1] -eq '-') { $offset = -$offset }
        $resolved = (Get-Date).Date.AddDays($offset).ToString('yyyy-MM-dd')
        Write-Host "Session '$($session.scheduledDate)' resolves to $resolved."
        $session.scheduledDate = $resolved
    }
    elseif ($session.scheduledDate -notmatch '^\d{4}-\d{2}-\d{2}$') {
        throw "Session scheduledDate '$($session.scheduledDate)' is neither an ISO date nor today[+-N]."
    }
    if ($null -ne $session.mainSet -and $session.mainSet -isnot [string]) {
        throw "Session $($session.scheduledDate) has a mainSet that is not text."
    }
    if (-not $session.blocks -or $session.blocks.Count -eq 0) {
        throw "Session $($session.scheduledDate) has no blocks; an empty list would only delete the existing ones."
    }
    foreach ($block in $session.blocks) {
        if ($block.blockType -notin $blockTypes) {
            throw "Session $($session.scheduledDate) uses block type '$($block.blockType)'; allowed: $($blockTypes -join ', ')."
        }
        if (-not $block.instructions) { throw "Every block of $($session.scheduledDate) needs instructions." }
        # A block without exercises is legitimate and the running sessions need
        # it: a warm-up, an interval and its recovery are steps of the day, not
        # movements the exercise catalogue holds. The instructions carry them.
        foreach ($exercise in $block.exercises) {
            if (-not $exercise.exerciseSlug) { throw "Every exercise of $($session.scheduledDate) needs an exerciseSlug." }
            if ($null -eq $exercise.sets -and $null -eq $exercise.repetitionsMin -and $null -eq $exercise.durationSeconds) {
                throw "Exercise '$($exercise.exerciseSlug)' needs sets, repetitionsMin or durationSeconds (planned_session_exercises check)."
            }
        }
    }
}

# The payload travels as a dollar-quoted jsonb literal, so the only sequence
# that could break out of it is the closing tag itself.
$payload = $content | ConvertTo-Json -Depth 12 -Compress
if ($payload.Contains('$rp_content$')) { throw 'The content file cannot contain the string $rp_content$.' }

$rationale = if ($content.draftRationale) { $content.draftRationale } else { 'Populate the structured exercise blocks.' }

# Correlation ids come from the caller, as they do in the API: rp_api has no
# usage on the extensions schema, so gen_random_uuid() is out of reach here.
$cloneCorrelationId = [Guid]::NewGuid()
$publishCorrelationId = [Guid]::NewGuid()

$publishStep = if ($Publish) {
    @"
  perform app.publish_training_plan_version(draft_version.id, '$publishCorrelationId');
  raise notice 'Draft v% published.', draft_version.version_number;
"@
} else {
    @'
  raise notice 'Draft v% left unpublished; publish it from the plan screen or rerun with -Publish.', draft_version.version_number;
'@
}

$sql = @"
-- Generated by scripts/Set-PlannedSessionBlocks.ps1 from $([IO.Path]::GetFileName($resolvedContent)).
-- Owner isolation mirrors OwnerTransactionContext: role rp_api plus the jwt claim.
begin;

set local role rp_api;
select
  set_config('request.jwt.claim.sub', '$OwnerId', true),
  set_config('request.jwt.claim.role', 'authenticated', true),
  set_config(
    'request.jwt.claims',
    jsonb_build_object('sub', '$OwnerId', 'role', 'authenticated')::text,
    true);

do `$rp`$
declare
  owner_id_value uuid := app.current_owner_id();
  content jsonb := `$rp_content`$$payload`$rp_content`$::jsonb;
  plan_row app.training_plans%rowtype;
  published_version app.training_plan_versions%rowtype;
  draft_version app.training_plan_versions%rowtype;
  session_entry jsonb;
  block_entry jsonb;
  exercise_entry jsonb;
  block_position integer;
  exercise_position integer;
  target_session_id uuid;
  match_count integer;
  match_summary text;
  new_block_id uuid;
  revision_id uuid;
begin
  if owner_id_value is null then
    raise exception 'Owner context is missing: request.jwt.claim.sub is not set.';
  end if;

  select * into plan_row
  from app.training_plans
  where owner_id = owner_id_value and name = content->>'planName';
  if not found then
    raise exception 'No training plan named % for this owner.', content->>'planName';
  end if;

  select * into draft_version
  from app.training_plan_versions
  where owner_id = owner_id_value
    and training_plan_id = plan_row.id
    and status = 'draft';

  if found then
    raise notice 'Reusing the pending draft v%.', draft_version.version_number;
  else
    select * into published_version
    from app.training_plan_versions
    where owner_id = owner_id_value
      and training_plan_id = plan_row.id
      and status = 'published';
    if not found then
      raise exception 'Plan % has no published version to clone a draft from.', plan_row.name;
    end if;

    draft_version := app.clone_training_plan_draft(
      plan_row.id,
      published_version.id,
      '$($rationale -replace "'", "''")',
      '$cloneCorrelationId');
    raise notice 'Draft v% cloned from v%.',
      draft_version.version_number, published_version.version_number;
  end if;

  for session_entry in select * from jsonb_array_elements(content->'sessions') loop
    -- The date alone does not identify a session: nothing stops a week from
    -- scheduling strength and a run on the same day, so sessionType and
    -- modality narrow it down. Count first and name what was found, instead of
    -- writing the blocks onto an arbitrary row or failing without a clue.
    select count(*), string_agg(
             session_type || coalesce(' (' || modality || ')', ''), ', ' order by session_type)
      into match_count, match_summary
    from app.planned_sessions
    where owner_id = owner_id_value
      and training_plan_version_id = draft_version.id
      and scheduled_date = (session_entry->>'scheduledDate')::date
      and (session_entry->>'sessionType' is null
           or session_type = session_entry->>'sessionType')
      and (session_entry->>'modality' is null
           or modality = session_entry->>'modality');

    if match_count = 0 then
      raise exception 'No session on % in draft v% matches %.',
        session_entry->>'scheduledDate',
        draft_version.version_number,
        coalesce(
          nullif(concat_ws(' / ', session_entry->>'sessionType', session_entry->>'modality'), ''),
          'that date');
    elsif match_count > 1 then
      raise exception 'Draft v% has % sessions on %: %. Add sessionType or modality to the content file.',
        draft_version.version_number, match_count,
        session_entry->>'scheduledDate', match_summary;
    end if;

    select id into target_session_id
    from app.planned_sessions
    where owner_id = owner_id_value
      and training_plan_version_id = draft_version.id
      and scheduled_date = (session_entry->>'scheduledDate')::date
      and (session_entry->>'sessionType' is null
           or session_type = session_entry->>'sessionType')
      and (session_entry->>'modality' is null
           or modality = session_entry->>'modality');

    -- main_set has no writer anywhere else (the API only exposes ScheduledDate
    -- and Objective). With blocks present the UI shows it as their short
    -- summary, so leaving the previous prose there would restate the whole
    -- session above the blocks it is meant to introduce.
    if session_entry ? 'mainSet' then
      update app.planned_sessions
      set main_set = nullif(session_entry->>'mainSet', '')
      where owner_id = owner_id_value and id = target_session_id;
    end if;

    delete from app.planned_session_blocks
    where owner_id = owner_id_value and planned_session_id = target_session_id;

    for block_entry, block_position in
      select value, ordinality from jsonb_array_elements(session_entry->'blocks') with ordinality
    loop
      insert into app.planned_session_blocks (
        owner_id, planned_session_id, position, block_type, repeat_count, instructions)
      values (
        owner_id_value, target_session_id, block_position,
        block_entry->>'blockType',
        coalesce((block_entry->>'repeatCount')::integer, 1),
        block_entry->>'instructions')
      returning id into new_block_id;

      for exercise_entry, exercise_position in
        select value, ordinality from jsonb_array_elements(block_entry->'exercises') with ordinality
      loop
        -- Latest revision of the slug: the catalogue is append-only, so the
        -- highest version_number is the technique currently in force.
        select revision.id into strict revision_id
        from app.exercise_revisions revision
        join app.exercises exercise
          on exercise.owner_id = revision.owner_id
         and exercise.id = revision.exercise_id
        where exercise.owner_id = owner_id_value
          and exercise.slug = exercise_entry->>'exerciseSlug'
        order by revision.version_number desc
        limit 1;

        insert into app.planned_session_exercises (
          owner_id, planned_session_block_id, exercise_revision_id, position,
          sets, repetitions_min, repetitions_max, duration_seconds, rest_seconds,
          load_value, load_unit, target_rpe, target_rir, tempo, side, note)
        values (
          owner_id_value, new_block_id, revision_id, exercise_position,
          (exercise_entry->>'sets')::integer,
          (exercise_entry->>'repetitionsMin')::integer,
          (exercise_entry->>'repetitionsMax')::integer,
          (exercise_entry->>'durationSeconds')::numeric,
          (exercise_entry->>'restSeconds')::numeric,
          (exercise_entry->>'loadValue')::numeric,
          exercise_entry->>'loadUnit',
          (exercise_entry->>'targetRpe')::numeric,
          (exercise_entry->>'targetRir')::numeric,
          exercise_entry->>'tempo',
          exercise_entry->>'side',
          exercise_entry->>'note');
      end loop;
    end loop;

    raise notice 'Session % rebuilt with % blocks.',
      session_entry->>'scheduledDate',
      jsonb_array_length(session_entry->'blocks');
  end loop;

$publishStep
end
`$rp`$;

commit;
"@

if ($SqlOutPath) {
    $sql | Set-Content -LiteralPath $SqlOutPath -Encoding utf8 -NoNewline
}

if (-not $DatabaseUrl) {
    if (-not $SqlOutPath) { $sql }
    return
}

$psql = Get-Command psql -ErrorAction SilentlyContinue
if ($null -eq $psql) {
    throw 'psql is required to apply the script; rerun without -DatabaseUrl and paste the SQL into the Supabase SQL editor.'
}

$scriptPath = Join-Path ([IO.Path]::GetTempPath()) "planned-session-blocks-$([Guid]::NewGuid()).sql"
# psql reads the file with the client encoding, which on Windows comes from the
# console code page, not from the file. The content is Spanish prose the athlete
# reads inside the session, so an unset encoding stores mojibake without error.
$previousClientEncoding = $env:PGCLIENTENCODING
$env:PGCLIENTENCODING = 'UTF8'
try {
    $sql | Set-Content -LiteralPath $scriptPath -Encoding utf8 -NoNewline
    & $psql.Source @('--set', 'ON_ERROR_STOP=1', '--no-psqlrc', '--file', $scriptPath, $DatabaseUrl)
    if ($LASTEXITCODE -ne 0) { throw "psql exited with code $LASTEXITCODE; nothing was committed." }
}
finally {
    $env:PGCLIENTENCODING = $previousClientEncoding
    Remove-Item -LiteralPath $scriptPath -ErrorAction SilentlyContinue
}
