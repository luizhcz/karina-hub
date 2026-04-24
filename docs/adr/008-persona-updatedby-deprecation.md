# ADR 008 — Deprecar `UpdatedBy` de `persona_prompt_templates`

**Status:** Aceito (fase 1: app ignora; fase 2: DROP da coluna em release futura)
**Data:** 2026-04-23
**Fase:** 9

## Context

`persona_prompt_templates` ganhou a coluna `UpdatedBy` no MVP da feature
persona pra rastrear quem editou o template. Em F1 introduzimos o
`admin_audit_log` como trilha canônica de todas as mutações admin —
inclusive `PersonaPromptTemplate` via `AdminAuditResources.PersonaPromptTemplate`.

Isso gerou duplicação: `persona_prompt_templates.UpdatedBy` e
`admin_audit_log.ActorUserId` (linha com action=update) passaram a
carregar a mesma info, com risco de drift. Já na F5.5 o controller
passou a popular `UpdatedBy = null` sempre.

## Decision

**Release atual (F9)**: remover `UpdatedBy` do domain, DTO, EF entity,
mapper e frontend interface. DB mantém a coluna.

**Release subsequente**: aplicar
`db/migration_persona_templates_drop_updatedby.sql` depois de
confirmar em produção que:

1. O app com F9 está rodando há ≥ 1 deploy cycle.
2. Nenhum consumidor externo (BI, read replica, dashboard) lê a
   coluna.
3. Backup recente da tabela existe.
4. Ops aprovou janela de manutenção.

**Por que não junto na mesma release**: catch do tech lead externo
durante o plan review — se o app release tiver bug e precisar de
rollback, o schema que suportava UpdatedBy não volta. DROP de coluna
não tem rollback trivial (precisaria de ALTER ADD + backfill de
audit_log).

## Consequences

### Positive
- Actor canônico fica em `admin_audit_log` (append-only, retenção
  própria). Um só local consultar.
- Reduz o schema de `persona_prompt_templates` em 1 coluna sem perder
  info histórica (audit trail persiste).
- Precedente pra próximos refactors de "campo actor em tabela vs
  audit log" — actor vai sempre pro audit log.

### Negative
- Duas fases de deploy: coordenação com ops.
- Durante a janela entre F9 (app ignora) e DROP, rows novas têm
  `UpdatedBy = NULL` mas rows antigas mantêm valor — se alguém
  acessar por raw SQL fica com data inconsistency. Aceitável porque
  ninguém depende dela depois de F9.
- Se audit retention for agressiva (pouca retenção), perder
  `UpdatedBy` historical significa perder actor pra edits antigos.
  Mitigação: aumentar retention do audit de persona específico via
  config (hoje default 90 dias — documentar em operations).

## Files

### Modified (release atual)
- `src/EfsAiHub.Core.Abstractions/Identity/Persona/PersonaPromptTemplate.cs`
- `src/EfsAiHub.Infra.Persistence/DbContext/AgentFwDbContext.cs` (entity
  + OnModelCreating)
- `src/EfsAiHub.Infra.Persistence/Postgres/PgPersonaPromptTemplateRepository.cs`
- `src/EfsAiHub.Host.Api/Controllers/PersonaPromptTemplatesAdminController.cs`
- `frontend/src/api/personaTemplates.ts`

### Prepared (apply em release futura)
- `db/migration_persona_templates_drop_updatedby.sql`
