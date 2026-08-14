param(
    [Parameter(Mandatory = $true)]
    [string] $OutputPath,

    [ValidateRange(1, 10000)]
    [int] $Rows = 460
)

$ErrorActionPreference = 'Stop'

$headers = @(
    'source_row_number', 'provisional_activity_key', 'garmin_activity_id',
    'activity_type', 'activity_type_source', 'activity_category', 'started_at_local',
    'favorite', 'title', 'distance_value', 'distance_unit', 'calories_kcal',
    'duration_seconds', 'average_heart_rate_bpm', 'maximum_heart_rate_bpm',
    'aerobic_training_effect', 'average_cadence', 'maximum_cadence', 'cadence_unit',
    'average_pace_seconds', 'maximum_pace_seconds', 'pace_basis', 'average_speed_kph',
    'maximum_speed_kph', 'total_ascent_m', 'total_descent_m',
    'average_stride_length_m', 'average_vertical_ratio_percent',
    'average_vertical_oscillation_cm', 'average_ground_contact_time_ms',
    'ground_contact_balance_source', 'ground_contact_balance_left_percent',
    'ground_contact_balance_right_percent', 'grade_adjusted_pace_seconds_per_km',
    'normalized_power_w', 'training_stress_score', 'average_power_w', 'maximum_power_w',
    'total_cycles_or_strokes', 'average_swolf', 'average_stroke_rate_per_minute',
    'steps', 'total_repetitions', 'total_sets', 'body_battery_drain',
    'minimum_temperature_c', 'decompression_required', 'best_lap_seconds', 'lap_count',
    'maximum_temperature_c', 'average_respiration_brpm', 'minimum_respiration_brpm',
    'maximum_respiration_brpm', 'moving_time_seconds', 'elapsed_time_seconds',
    'minimum_altitude_m', 'maximum_altitude_m'
)

$invariant = [Globalization.CultureInfo]::InvariantCulture
$records = for ($index = 1; $index -le $Rows; $index++) {
    $activityType = switch ($index % 7) {
        0 { 'running' }
        1 { 'treadmill_running' }
        2 { 'strength_training' }
        3 { 'walking' }
        4 { 'indoor_cycling' }
        5 { 'pool_swimming' }
        default { 'cardio' }
    }
    $category = switch ($activityType) {
        'running' { 'running' }
        'treadmill_running' { 'running' }
        'strength_training' { 'strength' }
        'walking' { 'walking' }
        'indoor_cycling' { 'cycling' }
        'pool_swimming' { 'swimming' }
        default { 'cardio' }
    }
    $hasDistance = $activityType -notin @('strength_training', 'cardio')
    $hasPace = $activityType -in @('running', 'treadmill_running')
    $bytes = [Text.Encoding]::UTF8.GetBytes("synthetic-activity-$index")
    $key = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
    $values = [ordered]@{}
    foreach ($header in $headers) {
        $values[$header] = ''
    }

    $values.source_row_number = ($index + 1).ToString($invariant)
    $values.provisional_activity_key = $key
    $values.garmin_activity_id = if ($index -le 2) { (90000000000L + $index).ToString($invariant) } else { '' }
    $values.activity_type = $activityType
    $values.activity_type_source = "Synthetic $activityType"
    $values.activity_category = $category
    $values.started_at_local = [datetime]::new(2025, 1, 1).AddDays($index - 1).ToString("yyyy-MM-dd'T'HH:mm:ss", $invariant)
    $values.favorite = 'false'
    $values.title = if ($index -eq 10) { 'Synthetic, quoted "session"' } else { "Synthetic session $index" }
    $values.distance_value = if ($hasDistance) { (3 + ($index % 15)).ToString($invariant) } else { '0' }
    $values.distance_unit = if ($hasDistance) { 'km' } else { '' }
    $values.calories_kcal = if ($index % 10 -eq 0) { '' } else { (100 + $index).ToString($invariant) }
    $values.duration_seconds = (900 + $index).ToString($invariant)
    $values.average_heart_rate_bpm = if ($index % 11 -eq 0) { '' } else { '145' }
    $values.maximum_heart_rate_bpm = if ($index % 11 -eq 0) { '' } else { '172' }
    $values.average_cadence = if ($hasPace) { '165' } else { '' }
    $values.cadence_unit = if ($hasPace) { 'steps_per_minute' } else { '' }
    $values.average_pace_seconds = if ($hasPace) { '360' } else { '' }
    $values.pace_basis = if ($hasPace) { 'per_km' } else { '' }
    $values.average_speed_kph = if ($hasDistance) { '10' } else { '' }
    $values.training_stress_score = '0'
    $values.body_battery_drain = if ($index % 3 -eq 0) { '-2' } else { '' }
    $values.decompression_required = 'false'
    $values.lap_count = if ($hasDistance) { '1' } else { '' }
    $values.moving_time_seconds = (890 + $index).ToString($invariant)
    $values.elapsed_time_seconds = (900 + $index).ToString($invariant)
    [pscustomobject]$values
}

$resolvedParent = Split-Path -Parent ([IO.Path]::GetFullPath($OutputPath))
if (-not (Test-Path -LiteralPath $resolvedParent -PathType Container)) {
    throw "Output directory does not exist: $resolvedParent"
}

$records |
    ConvertTo-Csv -NoTypeInformation -UseQuotes Always |
    Set-Content -LiteralPath $OutputPath -Encoding utf8NoBOM

$result = [ordered]@{
    path = [IO.Path]::GetFullPath($OutputPath)
    rows = $Rows
    columns = $headers.Count
    synthetic = $true
}
$result | ConvertTo-Json -Compress
