# Migrations — histórico

A pasta `db/` foi consolidada em **3 arquivos** em 2026-04-24:

- `db/schemas.sql` — DDL completo (29 tabelas + índices + constraints).
- `db/views.sql` — materialized views (`v_llm_cost`, `mv_execution_stats_hourly`, `mv_token_usage_hourly`).
- `db/seeds.sql` — dados idempotentes (projeto default, model_catalog, pricing, persona templates, agents, workflows).

Aplicar via `db/apply.sh` (schemas → views → seeds, ordem fixa).

As ~18 migrations individuais + 5 seeds separados + utilitário `schema_drop.sql`
que existiam antes viraram obsoletos: toda DDL virou parte do `schemas.sql`
e todos os dados de seed viraram parte do `seeds.sql`. O histórico
detalhado vive no `git log` — busque por commits tocando `db/migration_*.sql`
ou o commit `chore(db): consolida db/ em 3 arquivos` de 2026-04-24.
