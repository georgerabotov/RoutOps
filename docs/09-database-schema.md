# 09 — Database schema (Supabase / Postgres)

> ⚠️ **Superseded.** The shipped schema is **EF Core migrations on plain Postgres** (no Supabase) —
> entities live in `src/TravelOptimizer.Domain/Entities`. See [`00-AS-BUILT.md`](00-AS-BUILT.md).

Supabase is both the **state store** and the **message bus** between agents (rows, not Kafka). Apply these in order.

## users

```sql
create table users (
  id              uuid primary key default gen_random_uuid(),
  email           text unique not null,
  refresh_token   text,                 -- Google OAuth refresh token (encrypted at rest)
  home_lat        double precision,
  home_lng        double precision,
  whatsapp_number text,
  preferred_mode  text check (preferred_mode in ('TRANSIT','DRIVE','WALK')),
  created_at      timestamptz default now()
);
```

## events_cache

Calendar events + geocoded coordinates.

```sql
create table events_cache (
  id               uuid primary key default gen_random_uuid(),
  user_id          uuid references users(id) on delete cascade,
  gcal_event_id    text not null,
  title            text,
  start_time       timestamptz,
  end_time         timestamptz,
  raw_location     text,
  lat              double precision,
  lng              double precision,
  location_tokens  jsonb default '[]'::jsonb,
  is_virtual       boolean default false,
  geocoded_at      timestamptz,
  unique (user_id, gcal_event_id)
);
```

## route_runs

One row per orchestrator cycle's routing solution for a user.

```sql
create table route_runs (
  id             uuid primary key default gen_random_uuid(),
  user_id        uuid references users(id) on delete cascade,
  computed_at    timestamptz default now(),
  legs_json      jsonb not null,   -- [{leg_index, mode, observed_min, baseline_min, seasonal_expected_min, anomaly_min, status, leave_by}]
  conflicts_json jsonb default '[]'::jsonb  -- infeasible / tight-gap flags
);
```

## events

Normalized disruptions with cluster assignment.

```sql
create table events (
  id               uuid primary key default gen_random_uuid(),
  cluster_id       uuid,
  category         text not null,
  headline         text,
  location_tokens  jsonb default '[]'::jsonb,
  lat              double precision,
  lng              double precision,
  radius_m         integer,
  severity         text,
  source_tier      smallint check (source_tier in (1,2,3)),
  source_name      text,
  trust_score      double precision,
  published_at     timestamptz,
  url              text,
  ingested_at      timestamptz default now()
);
```

## leg_attributions

Links a leg in a route_run to a verified causing event.

```sql
create table leg_attributions (
  id                  uuid primary key default gen_random_uuid(),
  route_run_id        uuid references route_runs(id) on delete cascade,
  leg_index           integer not null,
  event_id            uuid references events(id) on delete set null,
  category            text,
  confidence          double precision,   -- = cluster trust score
  contributed_min_est double precision,
  created_at          timestamptz default now()
);
```

## notifications

```sql
create table notifications (
  id          uuid primary key default gen_random_uuid(),
  user_id     uuid references users(id) on delete cascade,
  channel     text default 'whatsapp',
  body        text,
  leg_index   integer,
  status      text default 'queued' check (status in ('queued','sent','failed')),
  sent_at     timestamptz,
  created_at  timestamptz default now()
);
```

## escalations

```sql
create table escalations (
  id          uuid primary key default gen_random_uuid(),
  user_id     uuid references users(id) on delete cascade,
  reason      text,
  detail      jsonb,
  status      text default 'open' check (status in ('open','resolved')),
  created_at  timestamptz default now(),
  resolved_at timestamptz
);
```

## payments

```sql
create table payments (
  id            uuid primary key default gen_random_uuid(),
  user_id       uuid references users(id) on delete cascade,
  provider      text default 'paypal',
  amount        numeric(10,2),
  currency      text default 'GBP',
  status        text,                 -- created | approved | completed | failed
  provider_ref  text,
  created_at    timestamptz default now()
);
```

## Orchestrator / agent observability

### orchestrator_runs

```sql
create table orchestrator_runs (
  id           uuid primary key default gen_random_uuid(),
  started_at   timestamptz default now(),
  finished_at  timestamptz,
  status       text default 'running' check (status in ('running','ok','degraded','failed')),
  health_score double precision        -- min(agent confidences) * freshness factor
);
```

### agent_runs

```sql
create table agent_runs (
  id             uuid primary key default gen_random_uuid(),
  orchestrator_run_id uuid references orchestrator_runs(id) on delete cascade,
  agent_name     text not null,        -- Calendar Scout | Route Engineer | Horizon Watcher | Comms Dispatcher | Orchestrator
  run_id         text,
  status         text check (status in ('ok','degraded','failed')),
  self_eval_json jsonb,                -- {passed_checks, total_checks, confidence, checks:[...]}
  errors         jsonb default '[]'::jsonb,
  created_at     timestamptz default now()
);
```
