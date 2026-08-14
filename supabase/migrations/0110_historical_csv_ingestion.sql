alter table app.ingestion_runs
  add column source_file_id uuid;

alter table app.ingestion_runs
  add constraint ingestion_runs_source_file_fk
  foreign key (owner_id, source_file_id) references app.source_files(owner_id, id);

create index ingestion_runs_claimable
on app.ingestion_runs(status, next_attempt_at, lease_until, created_at)
where status in ('pending', 'running');

create trigger ingestion_runs_set_updated_at
before update on app.ingestion_runs
for each row execute function app.set_updated_at();

create trigger ingestion_items_set_updated_at
before update on app.ingestion_items
for each row execute function app.set_updated_at();

create trigger activities_set_updated_at
before update on app.activities
for each row execute function app.set_updated_at();

comment on column app.ingestion_runs.source_file_id is
  'Immutable private source processed by this queued run; APP-006 CSV jobs always populate it.';
