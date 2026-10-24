-- Run once in YOUR Supabase project's SQL Editor. This is an installation
-- script, not a CLI migration. No central service or preconfigured school.
begin;
create table public.calendar_state (
  id text primary key check (id = 'main'),
  draft jsonb not null check (jsonb_typeof(draft) = 'object'),
  published jsonb check (published is null or jsonb_typeof(published) = 'object'),
  version text not null default gen_random_uuid()::text
);
create table public.calendar_versions (
  revision text primary key,
  snapshot jsonb not null check (jsonb_typeof(snapshot) = 'object'),
  created_at timestamptz not null default now()
);
alter table public.calendar_state enable row level security;
alter table public.calendar_versions enable row level security;
revoke all on public.calendar_state, public.calendar_versions from anon, authenticated;
grant select, update on public.calendar_state to authenticated;
grant select, insert on public.calendar_versions to authenticated;
grant select on public.calendar_state to service_role;
create policy "admins read state" on public.calendar_state for select to authenticated
  using ((select auth.jwt())->'app_metadata'->>'calendar_admin' = 'true');
create policy "admins edit state" on public.calendar_state for update to authenticated
  using ((select auth.jwt())->'app_metadata'->>'calendar_admin' = 'true')
  with check ((select auth.jwt())->'app_metadata'->>'calendar_admin' = 'true');
create policy "admins read history" on public.calendar_versions for select to authenticated
  using ((select auth.jwt())->'app_metadata'->>'calendar_admin' = 'true');
create policy "admins record history" on public.calendar_versions for insert to authenticated
  with check ((select auth.jwt())->'app_metadata'->>'calendar_admin' = 'true');

create function public.record_calendar_version() returns trigger
language plpgsql security invoker set search_path = '' as $$
begin
  if new.published is not null and new.published is distinct from old.published then
    insert into public.calendar_versions(revision, snapshot)
      values (new.published->>'revision', new.published);
  end if;
  return new;
end;
$$;
revoke all on function public.record_calendar_version() from public, anon;
grant execute on function public.record_calendar_version() to authenticated;
create trigger calendar_publish_history after update on public.calendar_state
  for each row execute function public.record_calendar_version();
insert into public.calendar_state(id,draft) values ('main',jsonb_build_object(
  'schemaVersion',1,'name','우리 학교 학사일정','revision','draft',
  'updatedAt',to_char(now() at time zone 'UTC','YYYY-MM-DD"T"HH24:MI:SS"Z"'),'events','[]'::jsonb));
commit;
