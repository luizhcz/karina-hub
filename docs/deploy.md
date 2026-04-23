# Deploy — EfsAiHub

Este documento cobre os passos operacionais para subir o stack em um
ambiente novo ou aplicar mudanças de schema/seed em ambiente existente.

---

## Schema + migrations + seeds (dev + CI/CD)

Use o wrapper `db/apply.sh` — ordena schema → migrations → seeds
alfabeticamente (que coincide com a ordem cronológica dos nomes) e
aplica com `psql -v ON_ERROR_STOP=on`, parando na primeira falha.

### Uso

```bash
# Variáveis PG* no ambiente:
PGHOST=localhost PGPORT=5432 PGUSER=efs_ai_hub PGPASSWORD=agentfw123 PGDATABASE=efs_ai_hub \
    ./db/apply.sh

# Ou passando DATABASE_URL:
./db/apply.sh 'postgres://user:pass@host:5432/dbname'

# Docker compose local:
docker cp ./db repositorio-postgres-1:/tmp/db
docker exec repositorio-postgres-1 bash -c \
    'PGPASSWORD=agentfw123 PGHOST=localhost PGUSER=efs_ai_hub PGDATABASE=efs_ai_hub bash /tmp/db/apply.sh'
```

### Ordem aplicada

1. `db/schema.sql` — DDL base (todas as tabelas, índices, sequences). Idempotente via `IF NOT EXISTS`.
2. `db/migration_*.sql` em ordem alfabética. Lista cronológica em [docs/migrations.md](migrations.md). Todas devem ser idempotentes (repetir não quebra, apenas no-op).
3. `db/seed_*.sql` em ordem alfabética. Tipicamente usam `ON CONFLICT (unique_col) DO NOTHING` para preservar customizações pós-seed.

### Requisitos

- `psql` no PATH (cliente PostgreSQL 12+).
- Conexão PG válida (env ou URL).
- Todas as migrations novas devem ser idempotentes por convenção — o apply.sh usa
  `ON_ERROR_STOP=on`, logo uma migration não-idempotente quebra o pipeline em reruns.

---

## Pré-requisitos de deploy da app

### Backend (`src/EfsAiHub.Host.Api`)

Variáveis obrigatórias (ver `appsettings.json` e `.env.example`):

- `ConnectionStrings:AgentFw` — Postgres.
- `ConnectionStrings:Redis` — Redis.
- `OpenAI:ApiKey` ou `AzureOpenAI:*` — LLM provider.
- `Persona:BaseUrl` + `Persona:ApiKey` — opcional; se vazio, a feature
  persona cai em Anonymous (feature desligada em runtime).

### Docker compose local

```bash
docker compose up -d --build
# Backend: http://localhost:5189
# Frontend: http://localhost:3000
```

### Rebuild só o backend (deploy incremental)

```bash
docker compose up -d --build backend
```

---

## Ordem de deploy com mudança de schema

Quando uma release inclui migration destrutiva ou coluna nova usada
pelo app:

1. **Aplicar migration no DB** (`./db/apply.sh`) ANTES de subir o novo
   binário. Coluna nova com `DEFAULT` cobre compat; binário antigo
   continua funcionando sem ver a coluna.
2. **Subir binário novo**. Ele escreve na coluna nova.
3. **Migrations destrutivas** (DROP COLUMN) — só **depois** de o
   binário ≥ 1 release ter ignorado a coluna. Ver [Fase 9 do plano persona](adr/)
   como exemplo.

Violar essa ordem = rollback kill: se o binário novo quebrar e voltar
pro anterior, o anterior não sabe da coluna nova (ok) mas se já fez
DROP no schema, rolar back o schema não é trivial.

---

## Troubleshooting

### `apply.sh` parou com erro

- `ON_ERROR_STOP=on` propaga erro pro bash. Mensagem final mostra qual
  migration/seed foi o último a rodar. Corrigir e rodar de novo — idempotência
  garante que as já aplicadas são no-op.

### Migration legacy com referência a coluna removida

Pattern: envolver o `CREATE INDEX` (ou DDL equivalente) em
`DO $$ BEGIN IF EXISTS (information_schema.columns…) THEN EXECUTE … END IF; END $$`.
Ver `db/migration_composite_indexes.sql` como exemplo.
