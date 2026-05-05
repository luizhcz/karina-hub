using EfsAiHub.Host.Api.Services;
using EfsAiHub.Host.Api.Configuration;
using Microsoft.Extensions.Options;
using System.Text.RegularExpressions;

namespace EfsAiHub.Host.Api.Middleware;

/// <summary>
/// Bloqueia acesso a endpoints não-públicos para requests cuja identidade
/// (resolvida via <see cref="UserIdentityResolver"/>) não conste em
/// <see cref="AdminOptions.AccountIds"/>.
///
/// Endpoints públicos (sem restrição):
///   - /api/chat/ag-ui/*              (integração de chat)
///   - POST /api/workflows            (criar workflow)
///   - PUT  /api/workflows/{id}       (editar workflow — exatamente 3 segmentos)
///   - POST /api/agents               (criar agent)
///   - PUT  /api/agents/{id}          (editar agent — exatamente 3 segmentos)
///   - /api/conversations/*           (conversas — todos os métodos)
///   - GET  /api/users/{id}/conversations
///   - GET  /api/projects             (listar projetos)
///   - GET  /api/projects/{id}        (buscar projeto por ID)
///   - GET  /api/notifications/*      (bell de notificações renderiza pra qualquer user)
///                                    (sub-rotas como /blocklist NÃO são públicas)
///
/// Retorna 403 Forbidden para demais endpoints sem account admin.
/// Gate desabilitado se <see cref="AdminOptions.AccountIds"/> for vazia.
/// </summary>
public sealed class AdminGateMiddleware
{
    private readonly RequestDelegate _next;
    private readonly HashSet<string> _adminAccountIds;
    private readonly UserIdentityResolver _identityResolver;

    // PUT /api/workflows/{id} — exatamente 3 segmentos (não inclui /rollback, /validate, etc.)
    private static readonly Regex WorkflowEditPattern =
        new(@"^/api/workflows/[^/]+$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // POST /api/workflows/{id}/trigger — invocação on-demand de workflow.
    // Liberado pra non-admin com a mesma garantia das outras rotas: WorkflowService
    // valida ownership via HasQueryFilter por project/tenant antes de disparar a
    // execução. /sandbox continua admin-only (não casa aqui).
    private static readonly Regex WorkflowTriggerPattern =
        new(@"^/api/workflows/[^/]+/trigger$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // GET /api/workflows/{id}/versions e /versions/{versionId} — leitura de
    // snapshots imutáveis. Liberado pra non-admin: WorkflowService respeita
    // HasQueryFilter por project/tenant no lookup do workflow base.
    private static readonly Regex WorkflowVersionsPattern =
        new(@"^/api/workflows/[^/]+/versions(/[^/]+)?$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // POST /api/workflows/{id}/rollback — restaura snapshot histórico como nova
    // revision (append-only, não destrói histórico). Liberado pra non-admin com
    // a mesma garantia: WorkflowService.RollbackAsync valida ownership do
    // workflow base via project/tenant filter.
    private static readonly Regex WorkflowRollbackPattern =
        new(@"^/api/workflows/[^/]+/rollback$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // GET /api/workflows/{id}/enabled-status — leitura agregada do Enabled de
    // todos os agentes referenciados. Usado pela tela de implantação pra exibir
    // badge habilitado/desabilitado. WorkflowService faz o lookup dentro do
    // project/tenant scope (HasQueryFilter).
    private static readonly Regex WorkflowEnabledStatusPattern =
        new(@"^/api/workflows/[^/]+/enabled-status$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // GET /api/executions/{id} — leitura individual de execução (status + steps).
    // Liberado pra non-admin: WorkflowExecution tem HasQueryFilter strict por
    // ProjectId, então PM só lê execuções do próprio projeto (cross-project →
    // 404). Sub-rotas /full e /stream e a listagem GET /api/executions continuam
    // admin-only — cobrem mais dados/eventos sensíveis.
    private static readonly Regex ExecutionReadPattern =
        new(@"^/api/executions/[^/]+$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // GET /api/executions/{id}/events — polling fallback HTTP (alternativa ao
    // /stream pra clientes sem SSE). Mesmo project scope da rota /stream e do
    // GetById (HasQueryFilter via WorkflowExecution). Identidade via headers
    // padrão (não query param — clients de polling usam fetch, suportam headers).
    private static readonly Regex ExecutionEventsPattern =
        new(@"^/api/executions/[^/]+/events$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // PUT /api/agents/{id} — exatamente 3 segmentos
    // Mesmo regex serve pra GET /api/agents/{id} (read by id) — sub-rotas como
    // /versions, /rollback, /enabled, /visibility, /sandbox NÃO casam (admin-only).
    private static readonly Regex AgentEditPattern =
        new(@"^/api/agents/[^/]+$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // POST /api/agents/{id}/edit-draft — fork de agent publicado em rascunho
    // de edição (cria AgentDraft com baseAgentId/baseRevision/isEditDraft=true).
    // Liberado pra non-admin pra que clientes possam editar agentes do próprio
    // projeto via wizard. Outras sub-rotas POST (/versions, /rollback, /sandbox,
    // /compare, /validate) permanecem admin-only.
    private static readonly Regex AgentEditDraftPattern =
        new(@"^/api/agents/[^/]+/edit-draft$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // /api/agents/{id}/sessions[/...] — fluxo de chat sandbox que o cliente usa
    // pra testar agentes do próprio projeto. Cobre criar/ler/encerrar sessão e
    // os endpoints /run e /stream. AgentSessionService valida ownership do
    // agent contra o ProjectContext, então non-admin só consegue criar sessão
    // pra agentes visíveis pelo HasQueryFilter (próprio project ou Visibility=
    // global do mesmo tenant).
    private static readonly Regex AgentSessionsPattern =
        new(@"^/api/agents/[^/]+/sessions(/.+)?$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // GET /api/agents/{id}/approval-history — trilha unificada de governança
    // do agent (drafts + AdminOverride). Liberado pra non-admin: AgentsController
    // valida ownership via _agentService.GetAsync (que respeita HasQueryFilter)
    // antes de devolver o histórico, então só vem dado de agentes visíveis no
    // project/tenant atual.
    private static readonly Regex AgentApprovalHistoryPattern =
        new(@"^/api/agents/[^/]+/approval-history$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // GET /api/agents/{id}/versions e /api/agents/{id}/versions/{versionId} —
    // leitura de snapshots imutáveis do agent (timeline pra PM/PO comparar
    // revisões). Liberado pra non-admin com a mesma garantia da approval-history:
    // controller chama _agentService.GetAsync antes (HasQueryFilter scope), só
    // depois consulta o IAgentVersionRepository. POST nas mesmas rotas (publish/
    // rollback/compare) continua admin-only por design.
    private static readonly Regex AgentVersionsPattern =
        new(@"^/api/agents/[^/]+/versions(/[^/]+)?$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // PATCH /api/agents/{id}/enabled — kill switch do PM/PO sobre seu próprio
    // agent (não passa pelo fluxo de aprovação por design — fast switch). Ownership
    // garantida por _agentService.GetAsync no controller (HasQueryFilter por
    // project/tenant). Toda chamada é auditada (AdminAudit + métrica) com
    // before/after e reason opcional. Visibility/PUT continuam admin-only.
    private static readonly Regex AgentEnabledPattern =
        new(@"^/api/agents/[^/]+/enabled$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // POST /api/agents/{id}/evaluations/auto-deploy — fluxo composto disparado
    // pelo PM/PO logo após "Implantar agente". Orquestra (gera test cases via
    // wf-gerador-testcases → cria TestSet+EvaluatorConfig do preset → enfileira
    // EvaluationRun) num único request. Ownership do agent via
    // IProjectContextAccessor + HasQueryFilter no controller. POST manual em
    // /evaluations/runs continua admin-only (rota de bypass com config livre).
    private static readonly Regex EvaluationsAutoDeployPattern =
        new(@"^/api/agents/[^/]+/evaluations/auto-deploy$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // GET /api/agents/{id}/evaluations/runs — listagem do último run pra alimentar
    // badge na grid de /implantacoes. EvaluationRun tem ProjectId scope no repo
    // (HasQueryFilter), PM só vê runs do próprio projeto.
    private static readonly Regex EvaluationsRunsListByAgentPattern =
        new(@"^/api/agents/[^/]+/evaluations/runs$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // GET /api/evaluations/runs/{id} e /results — leitura individual e dos results
    // de uma run. Repo aplica project filter, PM só vê runs do próprio projeto.
    // /cancel, /export, /compare seguem admin-only.
    private static readonly Regex EvaluationsRunReadPattern =
        new(@"^/api/evaluations/runs/[^/]+(/results)?$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // GET /api/evaluations/runs/{id}/stream — SSE de progresso. EventSource não
    // envia headers customizados; identidade vai por query param projectId
    // (controller já cobre isso em StreamRun). Mesmo project scope dos GETs acima.
    private static readonly Regex EvaluationsRunStreamPattern =
        new(@"^/api/evaluations/runs/[^/]+/stream$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // GET /api/evaluations/runs/{id}/events — polling fallback HTTP (Pattern C).
    // Alternativa ao /stream pra clientes sem SSE. Mesmo project scope (HasQueryFilter
    // no repo) e mesmo fallback de projectId via query param do StreamRun.
    private static readonly Regex EvaluationsRunEventsPattern =
        new(@"^/api/evaluations/runs/[^/]+/events$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // GET /api/analytics/projects/{id}/(overview|timeseries|agents|budget) —
    // dashboard de uso/custo por projeto. Liberado pra non-admin com a mesma
    // garantia do approval-history: ProjectAnalyticsController.EnsureProjectAccessAsync
    // valida ownership (current.ProjectId == path.projectId OU caller é admin)
    // antes de tocar o repo. Regex restrita aos 4 sufixos pra evitar vazamento
    // de sub-rotas futuras (ex.: POST /refresh) que escapem revisão deste
    // middleware.
    private static readonly Regex ProjectAnalyticsPattern =
        new(@"^/api/analytics/projects/[^/]+/(overview|timeseries|agents|budget)$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // GET /api/users/{userId}/conversations
    private static readonly Regex UserConversationsPattern =
        new(@"^/api/users/[^/]+/conversations$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // GET /api/projects (lista) ou GET /api/projects/{id} (detalhe). Sub-rotas
    // como /api/projects/{id}/blocklist são admin-only — caem fora desse pattern.
    private static readonly Regex ProjectsReadPattern =
        new(@"^/api/projects(/[^/]+)?$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public AdminGateMiddleware(RequestDelegate next, IOptions<AdminOptions> options, UserIdentityResolver identityResolver)
    {
        _next = next;
        _adminAccountIds = new HashSet<string>(options.Value.AccountIds, StringComparer.Ordinal);
        _identityResolver = identityResolver;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Gate desabilitado (dev/test)
        if (_adminAccountIds.Count == 0)
        {
            await _next(context);
            return;
        }

        if (IsPublicRoute(context))
        {
            await _next(context);
            return;
        }

        // Rotas SSE (EventSource no browser não envia headers customizados):
        // aceita identidade via query param como fallback. Restrito a /stream
        // pra evitar vazar identidade em URLs de outras rotas.
        var path = context.Request.Path.Value ?? string.Empty;
        var isSseRoute = path.EndsWith("/stream", StringComparison.OrdinalIgnoreCase);
        var identity = isSseRoute
            ? _identityResolver.TryResolve(context.Request, out _)
            : _identityResolver.TryResolve(context.Request.Headers, out _);
        if (identity != null && _adminAccountIds.Contains(identity.UserId))
        {
            await _next(context);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(new
        {
            error = "Acesso negado. Este endpoint requer permissão de administrador."
        });
    }

    private bool IsPublicRoute(HttpContext ctx)
    {
        var method = ctx.Request.Method;
        var path = ctx.Request.Path.Value ?? string.Empty;

        // Chat AG-UI — todos os métodos
        if (path.StartsWith("/api/chat/ag-ui", StringComparison.OrdinalIgnoreCase))
            return true;

        // Workflow: apenas criar (POST) e editar (PUT /{id})
        if (method.Equals("POST", StringComparison.OrdinalIgnoreCase)
            && path.TrimEnd('/').Equals("/api/workflows", StringComparison.OrdinalIgnoreCase))
            return true;

        if (method.Equals("PUT", StringComparison.OrdinalIgnoreCase)
            && WorkflowEditPattern.IsMatch(path))
            return true;

        // GET /api/workflows/{id} — leitura individual. Necessário pra tela de
        // implantação detectar workflow já existente (idempotência por agentId).
        if (method.Equals("GET", StringComparison.OrdinalIgnoreCase)
            && WorkflowEditPattern.IsMatch(path))
            return true;

        // GET /api/workflows — lista do projeto. Usado pela tela de Implantações
        // pra mostrar o que o PM já implantou. Escopo de project/tenant garantido
        // pelo HasQueryFilter no DbContext.
        if (method.Equals("GET", StringComparison.OrdinalIgnoreCase)
            && path.TrimEnd('/').Equals("/api/workflows", StringComparison.OrdinalIgnoreCase))
            return true;

        if (method.Equals("POST", StringComparison.OrdinalIgnoreCase)
            && WorkflowTriggerPattern.IsMatch(path))
            return true;

        if (method.Equals("GET", StringComparison.OrdinalIgnoreCase)
            && WorkflowVersionsPattern.IsMatch(path))
            return true;

        if (method.Equals("POST", StringComparison.OrdinalIgnoreCase)
            && WorkflowRollbackPattern.IsMatch(path))
            return true;

        if (method.Equals("GET", StringComparison.OrdinalIgnoreCase)
            && WorkflowEnabledStatusPattern.IsMatch(path))
            return true;

        if (method.Equals("GET", StringComparison.OrdinalIgnoreCase)
            && ExecutionReadPattern.IsMatch(path))
            return true;

        // GET /api/executions/{id}/events — polling fallback (Pattern A).
        if (method.Equals("GET", StringComparison.OrdinalIgnoreCase)
            && ExecutionEventsPattern.IsMatch(path))
            return true;

        // Agent: criar (POST), ler (GET lista + GET /{id}), forkar pra rascunho
        // de edição (POST /{id}/edit-draft). Naturalmente escopadas por project/
        // tenant via HasQueryFilter no DbContext.
        //
        // PUT /api/agents/{id} foi REMOVIDO da whitelist — atualização de agent
        // já publicado precisa passar pelo fluxo de aprovação (edit-draft →
        // submit → approve), nunca pela API direta. PUT permanece disponível
        // pra admins como break-glass de incidente; non-admin recebe 403 e é
        // empurrado pro caminho governado, mantendo dual-control de mudança
        // comportamental em produção.
        if (method.Equals("POST", StringComparison.OrdinalIgnoreCase)
            && path.TrimEnd('/').Equals("/api/agents", StringComparison.OrdinalIgnoreCase))
            return true;

        if (method.Equals("GET", StringComparison.OrdinalIgnoreCase)
            && (path.TrimEnd('/').Equals("/api/agents", StringComparison.OrdinalIgnoreCase)
                || AgentEditPattern.IsMatch(path)))
            return true;

        if (method.Equals("POST", StringComparison.OrdinalIgnoreCase)
            && AgentEditDraftPattern.IsMatch(path))
            return true;

        if (AgentSessionsPattern.IsMatch(path)
            && (method.Equals("GET", StringComparison.OrdinalIgnoreCase)
                || method.Equals("POST", StringComparison.OrdinalIgnoreCase)
                || method.Equals("DELETE", StringComparison.OrdinalIgnoreCase)))
            return true;

        if (method.Equals("GET", StringComparison.OrdinalIgnoreCase)
            && AgentApprovalHistoryPattern.IsMatch(path))
            return true;

        if (method.Equals("GET", StringComparison.OrdinalIgnoreCase)
            && AgentVersionsPattern.IsMatch(path))
            return true;

        if (method.Equals("PATCH", StringComparison.OrdinalIgnoreCase)
            && AgentEnabledPattern.IsMatch(path))
            return true;

        // Auto-deploy de avaliação (PM/PO chama logo após implantar agente).
        if (method.Equals("POST", StringComparison.OrdinalIgnoreCase)
            && EvaluationsAutoDeployPattern.IsMatch(path))
            return true;

        // Listagem do último run pra alimentar badge na grid de /implantacoes.
        if (method.Equals("GET", StringComparison.OrdinalIgnoreCase)
            && EvaluationsRunsListByAgentPattern.IsMatch(path))
            return true;

        // Leitura de run individual + results (drill-down do PM no card de status).
        if (method.Equals("GET", StringComparison.OrdinalIgnoreCase)
            && EvaluationsRunReadPattern.IsMatch(path))
            return true;

        // SSE de progresso (EventSource). Identidade via query param já tratada acima.
        if (method.Equals("GET", StringComparison.OrdinalIgnoreCase)
            && EvaluationsRunStreamPattern.IsMatch(path))
            return true;

        // Polling fallback (Pattern C) — mesma garantia de project scope do /stream.
        if (method.Equals("GET", StringComparison.OrdinalIgnoreCase)
            && EvaluationsRunEventsPattern.IsMatch(path))
            return true;

        if (method.Equals("GET", StringComparison.OrdinalIgnoreCase)
            && ProjectAnalyticsPattern.IsMatch(path))
            return true;

        // Conversations — todos os métodos (chat via REST)
        if (path.StartsWith("/api/conversations", StringComparison.OrdinalIgnoreCase))
            return true;

        // GET /api/users/{userId}/conversations
        if (method.Equals("GET", StringComparison.OrdinalIgnoreCase)
            && UserConversationsPattern.IsMatch(path))
            return true;

        // Projects — apenas leitura (GET lista e GET por ID). Sub-rotas
        // (ex: /api/projects/{id}/blocklist, /api/projects/{id}/blocklist/violations)
        // permanecem admin-only — fora do regex acima.
        if (method.Equals("GET", StringComparison.OrdinalIgnoreCase)
            && ProjectsReadPattern.IsMatch(path))
            return true;

        // Enums — dados não-sensíveis, necessários para todos os clientes
        if (method.Equals("GET", StringComparison.OrdinalIgnoreCase)
            && path.TrimEnd('/').Equals("/api/enums", StringComparison.OrdinalIgnoreCase))
            return true;

        // System info — publicBaseUrl que o frontend usa pra mostrar URL real do
        // backend nos exemplos de consumo (tela de implantação). Só metadado
        // não-sensível; demais sub-rotas /api/system/health/* continuam admin-only.
        if (method.Equals("GET", StringComparison.OrdinalIgnoreCase)
            && path.TrimEnd('/').Equals("/api/system/info", StringComparison.OrdinalIgnoreCase))
            return true;

        // Notifications (GET) — bell renderiza no Header pra qualquer usuário; visibility
        // já é gateada por agent_definitions.HasQueryFilter (tenant + project).
        if (method.Equals("GET", StringComparison.OrdinalIgnoreCase)
            && path.StartsWith("/api/notifications", StringComparison.OrdinalIgnoreCase))
            return true;

        // Predefined models (GET) — catálogo público lido pelo AgentForm pra montar
        // o dropdown de presets. CRUD admin vive em /api/admin/predefined-models e
        // fica sob o gate (path contém "/admin/", não bate aqui).
        if (method.Equals("GET", StringComparison.OrdinalIgnoreCase)
            && path.StartsWith("/api/predefined-models", StringComparison.OrdinalIgnoreCase))
            return true;

        // Generic tools — read+write liberados pra PMs/POs montarem ferramentas
        // dos próprios projetos pelo MVP. Owner-scope garantido via query filter
        // por ProjectId no DbContext (tool de project A é invisível pra project
        // B mesmo via id direto). DELETE permanece admin-only por design.
        if (path.StartsWith("/api/generic-tools", StringComparison.OrdinalIgnoreCase)
            && (method.Equals("GET", StringComparison.OrdinalIgnoreCase)
                || method.Equals("POST", StringComparison.OrdinalIgnoreCase)
                || method.Equals("PUT", StringComparison.OrdinalIgnoreCase)))
            return true;

        // MCP servers — read+write liberados pelo mesmo motivo dos generic tools.
        // O controller mora em /api/admin/mcp-servers (path historicamente prefixado
        // com /admin/), mas o repository é project-scoped via HasQueryFilter, então
        // PMs só enxergam MCPs do próprio projeto. DELETE continua admin-only.
        if (path.StartsWith("/api/admin/mcp-servers", StringComparison.OrdinalIgnoreCase)
            && (method.Equals("GET", StringComparison.OrdinalIgnoreCase)
                || method.Equals("POST", StringComparison.OrdinalIgnoreCase)
                || method.Equals("PUT", StringComparison.OrdinalIgnoreCase)))
            return true;

        // Agent drafts — read+write liberados pra que clientes não-admin possam
        // criar/editar rascunhos dos próprios projetos. Project-scope garantido via
        // HasQueryFilter no DbContext (rascunho de project A é invisível pra project
        // B mesmo via id direto). POST /{id}/submit também cai aqui pra que o autor
        // submeta o rascunho à aprovação. DELETE permanece admin-only.
        if (path.StartsWith("/api/agent-drafts", StringComparison.OrdinalIgnoreCase)
            && (method.Equals("GET", StringComparison.OrdinalIgnoreCase)
                || method.Equals("POST", StringComparison.OrdinalIgnoreCase)
                || method.Equals("PUT", StringComparison.OrdinalIgnoreCase)))
            return true;

        // Developer portal (dev-only, served via EmbeddedResource)
        if (path.Equals("/dev", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }
}
