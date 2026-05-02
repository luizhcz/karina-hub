# EfsAiHub.Migrations.PinningV1 (one-shot — temporário)

Script standalone que **regenera** todos os `agent_versions` no formato v1 final
(lossless, sem `SchemaVersion` discriminator) e **auto-pina** todos os workflows
em current de cada agent ref. Workflows com agent refs órfãos são deletados.

> **Este projeto é temporário.** Roda uma vez no deploy do refactor pre-prod, depois é
> deletado do repositório (idempotência via remoção do código, não via tabela auxiliar).

## Como rodar

Setar a connection string do Postgres na env:

```bash
export ConnectionStrings__Postgres="Host=localhost;Port=5432;Database=efs_ai_hub;Username=efs_ai_hub;Password=...;Search Path=aihub"
dotnet run --project src/EfsAiHub.Migrations.PinningV1/
```

Ou via container do compose (uma alternativa para rodar contra o postgres do
`docker-compose.yml`):

```bash
docker compose run --rm \
  -e ConnectionStrings__Postgres="Host=postgres;Port=5432;Database=$POSTGRES_DB;Username=$POSTGRES_USER;Password=$POSTGRES_PASSWORD;Search Path=aihub" \
  backend dotnet run --project /src/src/EfsAiHub.Migrations.PinningV1/
```

## O que faz (em transação única)

1. `DELETE FROM aihub.agent_versions;` — apaga snapshots legacy.
2. `DELETE FROM aihub.admin_audit_log WHERE "Action" = 'workflow.agent_version_auto_pinned';` — limpa rows de auditoria do AutoPin (action removida do código).
3. Para cada `agent_definitions`: deserializa o JSON `Data`, hidrata governança (ProjectId/Visibility/TenantId/AllowedProjectIds), gera `AgentVersion.FromDefinition(revision=1, breakingChange=false)`, INSERT em `agent_versions`. Mapa `agentId → versionId` montado.
4. Para cada `workflow_definitions`:
   - Se algum `agentRef.AgentId` não está no mapa (agent foi deletado): DELETE workflow + workflow_versions correspondentes.
   - Senão: popula `agentRef.AgentVersionId = mapa[agentId]`, re-serializa `Data`, UPDATE.
5. COMMIT. Erro em qualquer ponto → ROLLBACK automático (DB inalterado).

Log de saída resume: `Agents regenerados: X. Workflows pinados: Y. Workflows deletados (orphan): Z`.

### Side effects esperados

- **`background_response_jobs.AgentVersionId`** e **`llm_token_usage.AgentVersionId`** apontam pra os IDs antigos (não há FK hard). Esses campos viram dangling pointers — analytics retroativos por AgentVersionId nesses jobs/tokens não dão JOIN com `agent_versions`. Token usage e jobs **prospectivos** (pós-migration) vão referenciar os AgentVersionIds novos.
- **`evaluation_runs`/`evaluation_results`/`evaluation_run_progress`** são deletados (FK hard pra `agent_versions`). Histórico de runs de avaliação some — re-rodar runs no novo formato é o caminho.
- **Workflows com agent ref órfão** (agent deletado antes da migration) são **deletados** completamente (workflow_definitions + workflow_versions correspondentes).

## Após sucesso — cleanup obrigatório

Confirme via SQL:

```sql
SELECT COUNT(*) FROM aihub.agent_versions;                                     -- = COUNT(agent_definitions)
SELECT COUNT(*) FROM aihub.agent_versions WHERE "BreakingChange" = TRUE;       -- = 0
SELECT COUNT(*) FROM aihub.workflow_definitions
  WHERE "Data"::jsonb @? '$.Agents[*] ? (@.AgentVersionId == null)';           -- = 0
SELECT COUNT(*) FROM aihub.admin_audit_log
  WHERE "Action" = 'workflow.agent_version_auto_pinned';                       -- = 0
```

Tudo OK → remova o projeto do repo:

```bash
git rm -r src/EfsAiHub.Migrations.PinningV1/
dotnet sln efs-ai-hub.sln remove src/EfsAiHub.Migrations.PinningV1/EfsAiHub.Migrations.PinningV1.csproj
git add efs-ai-hub.sln
git commit -m "chore: remove one-shot pinning v1 migration script after successful run"
```
