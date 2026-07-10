create table if not exists public.miko_memory (
    id text primary key,
    data jsonb not null default '{}'::jsonb,
    updated_at timestamptz not null default now()
);

create or replace function public.set_updated_at()
returns trigger
language plpgsql
as $$
begin
    new.updated_at = now();
    return new;
end;
$$;

drop trigger if exists set_miko_memory_updated_at on public.miko_memory;
create trigger set_miko_memory_updated_at
before update on public.miko_memory
for each row
execute function public.set_updated_at();
