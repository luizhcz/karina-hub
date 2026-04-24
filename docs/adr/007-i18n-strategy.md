# ADR 007 — i18n: react-i18next (front) + CultureInfo (back) com escopo incremental

**Status:** Aceito
**Data:** 2026-04-23
**Fase:** 8

## Context

Persona template renderiza campos como `{{is_offshore}}` → `"sim"`/`"não"`
hardcoded em `UserPersona.GetPlaceholderValue`. Quando o stack ganhar
tenant com usuários em inglês (próximos quarters), o LLM precisa
receber `"yes"`/`"no"` pra não gerar resposta misturada. UI admin
também é 100% pt-BR hoje.

Decisão a tomar: **como introduzir i18n** sem refactor massivo agora,
mas deixando infra pronta pra expandir.

## Decision

### Backend: `CultureInfo.CurrentUICulture` + `RequestLocalizationMiddleware`

Em vez de mudar a assinatura de `UserPersona.GetPlaceholderValue(string)`
pra aceitar `IStringLocalizer` (heavy, polui `Core.Abstractions`), criar
`PersonaBooleanFormat.Format(bool)` que lê `CultureInfo.CurrentUICulture`.

- `CurrentUICulture` é AsyncLocal no .NET Core — propaga por request.
- `RequestLocalizationMiddleware` já setado no pipeline antes do
  `PersonaResolutionMiddleware` detecta culture via `Accept-Language`
  header (ou providers customizados no futuro — ex:
  `ConversationSession.Locale`).
- Default: `pt-BR`. Suportadas: `pt-BR`, `en-US`.

**Trade-off aceito:** `GetPlaceholderValue` deixa de ser puramente
determinístico (depende de culture ambient). Documentado no XML-doc
do método. Em contrapartida: zero mudança de assinatura em qualquer
callsite.

### Frontend: `react-i18next` + namespace `persona`, migração incremental

- `react-i18next` + `i18next-browser-languagedetector` instalados.
  Versões fixadas (`i18next@^23`, `react-i18next@^14`) — alinhadas
  com React 19.
- Setup em `frontend/src/i18n/index.ts` — init inline (side-effect
  import em `main.tsx`).
- **Namespaces por feature** (hoje: `persona`). JSON files em
  `frontend/src/locales/{pt-BR,en-US}/persona.json`.
- **Migração incremental**: páginas não-persona continuam com strings
  hardcoded até serem migradas. Proof de concepto: `PersonaExperimentsPage`
  (100% migrada em F8).

**Detecção de idioma**: navigator.language / cookie / localStorage
via `LanguageDetector`. Fallback `pt-BR`. Datas + números usam
`Intl.*` passando `i18n.language` — UI formata locale-aware sem
depender do JS engine default.

## Consequences

### Positive
- Zero mudança de assinatura em `UserPersona` → zero callsite tocado.
- Frontend tem ponto canônico pra adicionar strings (`locales/*.json`)
  — novos devs não precisam pensar em qual camada instrumentar.
- Culture do backend (`CurrentUICulture`) flui naturalmente pra
  `PersonaTemplateRenderer` — testável via troca de culture no teste.
- Namespace `persona` no front escopa a migração — não precisa migrar
  tudo ao mesmo tempo.

### Negative
- **`GetPlaceholderValue` ambient** depende de culture context.
  Testes precisam setar `CurrentUICulture` explicitamente (padrão
  implementado em `PersonaTemplateRendererTests` via `IDisposable`).
- **Só 1 página migrada**: `PersonaExperimentsPage`. Outras páginas
  admin de persona (`PersonaTemplatesListPage`, `PersonaTemplateEditPage`,
  `PersonaTemplateVersionsPage`, `PersonasAdminPage`) seguem pt-BR
  hardcoded. Backlog **I18N-MIGRATE** pra migrar conforme demanda.
- **Sem cobertura no backend**: endpoints que retornam strings
  (`errors`, `descriptions`) continuam hardcoded — só a renderização
  de persona boolean foi localizada. Aceitável pro MVP; endpoints
  admin são consumidos pela UI, que tem sua própria camada de i18n.
- **Dependências extras** (i18next, react-i18next, browser-languagedetector):
  bundle size +~30KB gzipped.

### Notes sobre regra de "qual culture usar"

Locale do **prompt do LLM** (persona template render) precisa seguir
o **usuário final** que vai ler a resposta, não o admin que está
operando. Hoje seguimos `Accept-Language` do request HTTP — isso é
aproximação boa:

- Chat: end-user está no navegador fazendo o request → culture bate.
- Execução standalone (jobs/worker): não tem HTTP request → cai no
  default `pt-BR`. Aceitável enquanto não houver usuários non-pt-BR
  em jobs async.

Quando aparecer caso de "admin pt-BR operando em nome de cliente
en-US" (impersonation), vamos precisar de campo `ConversationSession.Locale`
propagando pro `ExecutionContext` e um `RequestCultureProvider` custom.
Backlog: **I18N-CONTEXT-AWARE**.

## Files
- `src/EfsAiHub.Core.Abstractions/Identity/Persona/PersonaBooleanFormat.cs` (novo)
- `src/EfsAiHub.Core.Abstractions/Identity/Persona/UserPersona.cs` (replace hardcoded `"sim"/"não"` por `PersonaBooleanFormat.Format`)
- `src/EfsAiHub.Host.Api/Extensions/WebApplicationExtensions.cs` (middleware)
- `frontend/src/i18n/index.ts` (novo)
- `frontend/src/locales/{pt-BR,en-US}/persona.json` (novos)
- `frontend/src/main.tsx` (import do i18n)
- `frontend/src/features/admin/PersonaExperimentsPage.tsx` (proof migration)
- `tests/EfsAiHub.Tests.Unit/Personas/PersonaTemplateRendererTests.cs` (tests i18n)
