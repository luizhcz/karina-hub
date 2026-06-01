using EfsAiHub.Core.Abstractions.Users;
using EfsAiHub.Host.Api.Configuration;
using EfsAiHub.Host.Api.Services;
using Microsoft.Extensions.Options;
using System.Text.RegularExpressions;

namespace EfsAiHub.Host.Api.Middleware;

/// <summary>
/// Bloqueia acesso a endpoints não-públicos para requests cujo usuário
/// resolvido pelo <see cref="UserProvisioningMiddleware"/> não tenha
/// <see cref="User.IsAdmin"/> = true.
///
/// Endpoints públicos (sem restrição):
///   - /health/*                            (k8s liveness/readiness probes)
///   - /api/aihub/chat/ag-ui/*              (integração de chat)
///   - POST /api/aihub/workflows            (criar workflow)
///   - PUT  /api/aihub/workflows/{id}       (editar workflow — exatamente 3 segmentos)
///   - POST /api/aihub/agents               (criar agent)
///   - PUT  /api/aihub/agents/{id}          (editar agent — exatamente 3 segmentos)
///   - /api/aihub/conversations/*           (conversas — todos os métodos)
///   - GET  /api/aihub/users/{id}/conversations
///   - GET  /api/aihub/projects             (listar projetos)
///   - GET  /api/aihub/projects/{id}        (buscar projeto por ID)
///   - GET  /api/aihub/notifications/*      (bell de notificações renderiza pra qualquer user)
///                                    (sub-rotas como /blocklist NÃO são públicas)
///
/// Retorna 403 Forbidden para demais endpoints quando o usuário corrente
/// não tem IsAdmin=true na tabela aihub.users. Identidade é resolvida
/// pelo UserProvisioningMiddleware antes deste middleware na pipeline —
/// IUserContextAccessor.Current é null quando o request veio sem header
/// de identidade (rotas públicas continuam liberadas pela whitelist).
/// </summary>
public sealed class AdminGateMiddleware
{
    private readonly RequestDelegate _next;
    private readonly UserIdentityResolver _identityResolver;

    // PUT /api/aihub/workflows/{id} — exatamente 3 segmentos (não inclui /rollback, /validate, etc.)
    private static readonly Regex WorkflowEditPattern =
        new(@"^/api/aihub/workflows/[^/]+$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // POST /api/aihub/workflows/{id}/trigger — invocação on-demand de workflow.
    // Liberado pra non-admin com a mesma garantia das outras rotas: WorkflowService
    // valida ownership via HasQueryFilter por project/tenant antes de disparar a
    // execução. /sandbox continua admin-only (não casa aqui).
    private static readonly Regex WorkflowTriggerPattern =
        new(@"^/api/aihub/workflows/[^/]+/trigger$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // GET /api/aihub/workflows/{id}/versions e /versions/{versionId} — leitura de
    // snapshots imutáveis. Liberado pra non-admin: WorkflowService respeita
    // HasQueryFilter por project/tenant no lookup do workflow base.
    private static readonly Regex WorkflowVersionsPattern =
        new(@"^/api/aihub/workflows/[^/]+/versions(/[^/]+)?$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // POST /api/aihub/workflows/{id}/rollback — restaura snapshot histórico como nova
    // revision (append-only, não destrói histórico). Liberado pra non-admin com
    // a mesma garantia: WorkflowService.RollbackAsync valida ownership do
    // workflow base via project/tenant filter.
    private static readonly Regex WorkflowRollbackPattern =
        new(@"^/api/aihub/workflows/[^/]+/rollback$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // GET /api/aihub/workflows/{id}/enabled-status — leitura agregada do Enabled de
    // todos os agentes referenciados. Usado pela tela de implantação pra exibir
    // badge habilitado/desabilitado. WorkflowService faz o lookup dentro do
    // project/tenant scope (HasQueryFilter).
    private static readonly Regex WorkflowEnabledStatusPattern =
        new(@"^/api/aihub/workflows/[^/]+/enabled-status$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // GET /api/aihub/executions/{id} — leitura individual de execução (status + steps).
    // Liberado pra non-admin: WorkflowExecution tem HasQueryFilter strict por
    // ProjectId, então PM só lê execuções do próprio projeto (cross-project →
    // 404). Sub-rotas /full e /stream e a listagem GET /api/aihub/executions continuam
    // admin-only — cobrem mais dados/eventos sensíveis.
    private static readonly Regex ExecutionReadPattern =
        new(@"^/api/aihub/executions/[^/]+$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // GET /api/aihub/executions/{id}/events — polling fallback HTTP (alternativa ao
    // /stream pra clientes sem SSE). Mesmo project scope da rota /stream e do
    // GetById (HasQueryFilter via WorkflowExecution). Identidade via headers
    // padrão (não query param — clients de polling usam fetch, suportam headers).
    private static readonly Regex ExecutionEventsPattern =
        new(@"^/api/aihub/executions/[^/]+/events$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // PUT /api/aihub/agents/{id} — exatamente 3 segmentos
    // Mesmo regex serve pra GET /api/aihub/agents/{id} (read by id) — sub-rotas como
    // /versions, /rollback, /enabled, /visibility, /sandbox NÃO casam (admin-only).
    private static readonly Regex AgentEditPattern =
        new(@"^/api/aihub/agents/[^/]+$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // POST /api/aihub/agents/{id}/edit-draft — fork de agent publicado em rascunho
    // de edição (cria AgentDraft com baseAgentId/baseRevision/isEditDraft=true).
    // Liberado pra non-admin pra que clientes possam editar agentes do próprio
    // projeto via wizard. Outras sub-rotas POST (/versions, /rollback, /sandbox,
    // /compare, /validate) permanecem admin-only.
    private static readonly Regex AgentEditDraftPattern =
        new(@"^/api/aihub/agents/[^/]+/edit-draft$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // /api/aihub/agents/{id}/sessions[/...] — fluxo de chat sandbox que o cliente usa
    // pra testar agentes do próprio projeto. Cobre criar/ler/encerrar sessão e
    // os endpoints /run e /stream. AgentSessionService valida ownership do
    // agent contra o ProjectContext, então non-admin só consegue criar sessão
    // pra agentes visíveis pelo HasQueryFilter (próprio project ou Visibility=
    // global do mesmo tenant).
    private static readonly Regex AgentSessionsPattern =
        new(@"^/api/aihub/agents/[^/]+/sessions(/.+)?$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // GET /api/aihub/agents/{id}/approval-history — trilha unificada de governança
    // do agent (drafts + AdminOverride). Liberado pra non-admin: AgentsController
    // valida ownership via _agentService.GetAsync (que respeita HasQueryFilter)
    // antes de devolver o histórico, então só vem dado de agentes visíveis no
    // project/tenant atual.
    private static readonly Regex AgentApprovalHistoryPattern =
        new(@"^/api/aihub/agents/[^/]+/approval-history$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // GET /api/aihub/agents/{id}/versions e /api/aihub/agents/{id}/versions/{versionId} —
    // leitura de snapshots imutáveis do agent (timeline pra PM/PO comparar
    // revisões). Liberado pra non-admin com a mesma garantia da approval-history:
    // controller chama _agentService.GetAsync antes (HasQueryFilter scope), só
    // depois consulta o IAgentVersionRepository. POST nas mesmas rotas (publish/
    // rollback/compare) continua admin-only por design.
    private static readonly Regex AgentVersionsPattern =
        new(@"^/api/aihub/agents/[^/]+/versions(/[^/]+)?$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // PATCH /api/aihub/agents/{id}/enabled — kill switch do PM/PO sobre seu próprio
    // agent (não passa pelo fluxo de aprovação por design — fast switch). Ownership
    // garantida por _agentService.GetAsync no controller (HasQueryFilter por
    // project/tenant). Toda chamada é auditada (AdminAudit + métrica) com
    // before/after e reason opcional. Visibility/PUT continuam admin-only.
    private static readonly Regex AgentEnabledPattern =
        new(@"^/api/aihub/agents/[^/]+/enabled$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // GET/DELETE /api/aihub/agents/{id}/operational-memory/{scopeId} — non-admin
    // consulta ou reseta a memória do próprio escopo (conversa/sessão).
    // OperationalMemoryRepository herda o HasQueryFilter por ProjectId, então
    // só vem dado dos escopos do project atual. GET-list (sem segmento extra)
    // continua admin-only — segue o padrão de versions/approval-history.
    private static readonly Regex AgentOperationalMemoryScopePattern =
        new(@"^/api/aihub/agents/[^/]+/operational-memory/[^/]+$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // POST /api/aihub/agents/{id}/evaluations/auto-deploy — fluxo composto disparado
    // pelo PM/PO logo após "Implantar agente". Orquestra (gera test cases via
    // wf-gerador-testcases → cria TestSet+EvaluatorConfig do preset → enfileira
    // EvaluationRun) num único request. Ownership do agent via
    // IProjectContextAccessor + HasQueryFilter no controller. POST manual em
    // /evaluations/runs continua admin-only (rota de bypass com config livre).
    private static readonly Regex EvaluationsAutoDeployPattern =
        new(@"^/api/aihub/agents/[^/]+/evaluations/auto-deploy$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // GET /api/aihub/agents/{id}/evaluations/runs — listagem do último run pra alimentar
    // badge na grid de /implantacoes. EvaluationRun tem ProjectId scope no repo
    // (HasQueryFilter), PM só vê runs do próprio projeto.
    private static readonly Regex EvaluationsRunsListByAgentPattern =
        new(@"^/api/aihub/agents/[^/]+/evaluations/runs$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // GET /api/aihub/evaluations/runs/{id} e /results — leitura individual e dos results
    // de uma run. Repo aplica project filter, PM só vê runs do próprio projeto.
    // /cancel, /export, /compare seguem admin-only.
    private static readonly Regex EvaluationsRunReadPattern =
        new(@"^/api/aihub/evaluations/runs/[^/]+(/results)?$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // GET /api/aihub/evaluations/runs/{id}/stream — SSE de progresso. EventSource não
    // envia headers customizados; identidade vai por query param projectId
    // (controller já cobre isso em StreamRun). Mesmo project scope dos GETs acima.
    private static readonly Regex EvaluationsRunStreamPattern =
        new(@"^/api/aihub/evaluations/runs/[^/]+/stream$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // GET /api/aihub/evaluations/runs/{id}/events — polling fallback HTTP (Pattern C).
    // Alternativa ao /stream pra clientes sem SSE. Mesmo project scope (HasQueryFilter
    // no repo) e mesmo fallback de projectId via query param do StreamRun.
    private static readonly Regex EvaluationsRunEventsPattern =
        new(@"^/api/aihub/evaluations/runs/[^/]+/events$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // /api/aihub/analytics/projects/{id}/(overview|timeseries|agents|budget|refresh) —
    // dashboard de uso/custo por projeto. Liberado pra non-admin com a mesma
    // garantia do approval-history: ProjectAnalyticsController.EnsureProjectAccessAsync
    // valida ownership (current.ProjectId == path.projectId OU caller é admin)
    // antes de tocar o repo. `refresh` é POST e apenas invalida o cache do
    // projeto via incremento de versão — sem efeito colateral fora do escopo.
    private static readonly Regex ProjectAnalyticsPattern =
        new(@"^/api/aihub/analytics/projects/[^/]+/(overview|timeseries|agents|budget|refresh)$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // GET /api/aihub/users/{userId}/conversations
    private static readonly Regex UserConversationsPattern =
        new(@"^/api/aihub/users/[^/]+/conversations$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // /api/aihub/agents/{routerId}/quick-actions[/{id}] — CRUD de atalhos do Router.
    // Liberado pra non-admin: o controller já valida via HasQueryFilter (filtro por
    // ProjectId scope) e EnsureRouter rejeita acesso a Router de outro projeto.
    private static readonly Regex RouterQuickActionsPattern =
        new(@"^/api/aihub/agents/[^/]+/quick-actions(/[^/]+)?$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // GET /api/aihub/projects (lista) ou GET /api/aihub/projects/{id} (detalhe). Sub-rotas
    // como /api/aihub/projects/{id}/blocklist são admin-only — caem fora desse pattern.
    private static readonly Regex ProjectsReadPattern =
        new(@"^/api/aihub/projects(/[^/]+)?$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly bool _gateEnabled;

    public AdminGateMiddleware(RequestDelegate next, UserIdentityResolver identityResolver, IOptions<AdminOptions> adminOptions)
    {
        _next = next;
        _identityResolver = identityResolver;
        _gateEnabled = adminOptions.Value.GateEnabled;
    }

    public async Task InvokeAsync(
        HttpContext context,
        IUserContextAccessor userAccessor,
        IUserDirectory directory,
        IAdminPermissionEvaluator adminEvaluator,
        EfsAiHub.Core.Abstractions.Identity.ITenantContextAccessor tenantAccessor)
    {
        // Escape hatch dev/test — GateEnabled=false em appsettings desativa o gate.
        if (!_gateEnabled)
        {
            await _next(context);
            return;
        }

        if (IsPublicRoute(context))
        {
            await _next(context);
            return;
        }

        // Caminho principal: UserProvisioningMiddleware já populou o accessor.
        if (userAccessor.Current?.IsAdmin == true)
        {
            await _next(context);
            return;
        }

        // Fallback SSE: EventSource no browser não envia headers customizados
        // e o provisioning middleware não populou o accessor. Resolver lê
        // identidade + permissions via query param (?account=...&permissions=...).
        // Restrito a /stream pra evitar vazar identidade em URLs de outras rotas.
        var path = context.Request.Path.Value ?? string.Empty;
        if (path.EndsWith("/stream", StringComparison.OrdinalIgnoreCase))
        {
            var identity = _identityResolver.TryResolve(context.Request, out _);
            if (identity is not null && adminEvaluator.IsAdmin(identity.Permissions))
            {
                var tenantId = tenantAccessor.Current.TenantId;
                // Sanity check: garante que o user existe no diretório antes
                // de liberar a rota — admin sem row em aihub.users é estado
                // anômalo e deve cair em 403.
                var user = await directory.GetByExternalIdAsync(identity.UserId, tenantId, context.RequestAborted);
                if (user is not null)
                {
                    await _next(context);
                    return;
                }
            }
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

        // Health endpoints (/health/live, /health/ready) — públicos por design,
        // consumidos por Kubernetes liveness/readiness probes e load balancer.
        // Sem isso o gate retorna 403 e probe marca pod como unhealthy.
        if (path.StartsWith("/health/", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/health", StringComparison.OrdinalIgnoreCase))
            return true;

        // Chat AG-UI — todos os métodos
        if (path.StartsWith("/api/aihub/chat/ag-ui", StringComparison.OrdinalIgnoreCase))
            return true;

        // /api/aihub/me — endpoint público que devolve identidade + flag isAdmin.
        // Frontend usa pra esconder UI admin-only sem precisar bater num endpoint
        // protegido e receber 403 (que polui o console do navegador).
        if (path.Equals("/api/aihub/me", StringComparison.OrdinalIgnoreCase))
            return true;

        // Workflow: implantar/editar/rollback são admin-only (gerenciamento de
        // produção). Non-admin tem leitura (listagem, detail, versões,
        // enabled-status), invocação via /trigger (consumir agente já
        // publicado) e leitura de execuções. Cria/edita workflow é decisão
        // de governance — UI esconde os botões e gate enforça pra que curl
        // direto não burle.

        // GET /api/aihub/workflows/{id} — leitura individual. Tela de Implantações
        // detalha o deploy pra qualquer role.
        if (method.Equals("GET", StringComparison.OrdinalIgnoreCase)
            && WorkflowEditPattern.IsMatch(path))
            return true;

        // GET /api/aihub/workflows — lista do projeto. Tela de Implantações
        // mostra deploys existentes (read-only pra non-admin). Escopo de
        // project/tenant via HasQueryFilter no DbContext.
        if (method.Equals("GET", StringComparison.OrdinalIgnoreCase)
            && path.TrimEnd('/').Equals("/api/aihub/workflows", StringComparison.OrdinalIgnoreCase))
            return true;

        // POST /api/aihub/workflows/{id}/trigger — invocação on-demand de
        // workflow já implantado. Liberado pra non-admin: WorkflowService
        // valida ownership via HasQueryFilter por project/tenant.
        if (method.Equals("POST", StringComparison.OrdinalIgnoreCase)
            && WorkflowTriggerPattern.IsMatch(path))
            return true;

        if (method.Equals("GET", StringComparison.OrdinalIgnoreCase)
            && WorkflowVersionsPattern.IsMatch(path))
            return true;

        if (method.Equals("GET", StringComparison.OrdinalIgnoreCase)
            && WorkflowEnabledStatusPattern.IsMatch(path))
            return true;

        if (method.Equals("GET", StringComparison.OrdinalIgnoreCase)
            && ExecutionReadPattern.IsMatch(path))
            return true;

        // GET /api/aihub/executions/{id}/events — polling fallback (Pattern A).
        if (method.Equals("GET", StringComparison.OrdinalIgnoreCase)
            && ExecutionEventsPattern.IsMatch(path))
            return true;

        // Agent: criar (POST), ler (GET lista + GET /{id}), forkar pra rascunho
        // de edição (POST /{id}/edit-draft). Naturalmente escopadas por project/
        // tenant via HasQueryFilter no DbContext.
        //
        // PUT /api/aihub/agents/{id} foi REMOVIDO da whitelist — atualização de agent
        // já publicado precisa passar pelo fluxo de aprovação (edit-draft →
        // submit → approve), nunca pela API direta. PUT permanece disponível
        // pra admins como break-glass de incidente; non-admin recebe 403 e é
        // empurrado pro caminho governado, mantendo dual-control de mudança
        // comportamental em produção.
        if (method.Equals("POST", StringComparison.OrdinalIgnoreCase)
            && path.TrimEnd('/').Equals("/api/aihub/agents", StringComparison.OrdinalIgnoreCase))
            return true;

        if (method.Equals("GET", StringComparison.OrdinalIgnoreCase)
            && (path.TrimEnd('/').Equals("/api/aihub/agents", StringComparison.OrdinalIgnoreCase)
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

        if (AgentOperationalMemoryScopePattern.IsMatch(path)
            && (method.Equals("GET", StringComparison.OrdinalIgnoreCase)
                || method.Equals("DELETE", StringComparison.OrdinalIgnoreCase)))
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

        // Router Quick Actions — CRUD liberado pra non-admin do projeto. Controller
        // valida ownership do Router (Type=Router) e usa ProjectContext pra scope.
        if (RouterQuickActionsPattern.IsMatch(path))
            return true;

        // Conversations — todos os métodos (chat via REST)
        if (path.StartsWith("/api/aihub/conversations", StringComparison.OrdinalIgnoreCase))
            return true;

        // GET /api/aihub/users/{userId}/conversations
        if (method.Equals("GET", StringComparison.OrdinalIgnoreCase)
            && UserConversationsPattern.IsMatch(path))
            return true;

        // Projects — apenas leitura (GET lista e GET por ID). Sub-rotas
        // (ex: /api/aihub/projects/{id}/blocklist, /api/aihub/projects/{id}/blocklist/violations)
        // permanecem admin-only — fora do regex acima.
        if (method.Equals("GET", StringComparison.OrdinalIgnoreCase)
            && ProjectsReadPattern.IsMatch(path))
            return true;

        // Enums — dados não-sensíveis, necessários para todos os clientes
        if (method.Equals("GET", StringComparison.OrdinalIgnoreCase)
            && path.TrimEnd('/').Equals("/api/aihub/enums", StringComparison.OrdinalIgnoreCase))
            return true;

        // System info — publicBaseUrl que o frontend usa pra mostrar URL real do
        // backend nos exemplos de consumo (tela de implantação). Só metadado
        // não-sensível; demais sub-rotas /api/aihub/system/health/* continuam admin-only.
        if (method.Equals("GET", StringComparison.OrdinalIgnoreCase)
            && path.TrimEnd('/').Equals("/api/aihub/system/info", StringComparison.OrdinalIgnoreCase))
            return true;

        // Notifications (GET) — bell renderiza no Header pra qualquer usuário; visibility
        // já é gateada por agent_definitions.HasQueryFilter (tenant + project).
        if (method.Equals("GET", StringComparison.OrdinalIgnoreCase)
            && path.StartsWith("/api/aihub/notifications", StringComparison.OrdinalIgnoreCase))
            return true;

        // Predefined models (GET) — catálogo público lido pelo AgentForm pra montar
        // o dropdown de presets. CRUD admin vive em /api/aihub/admin/predefined-models e
        // fica sob o gate (path contém "/admin/", não bate aqui).
        if (method.Equals("GET", StringComparison.OrdinalIgnoreCase)
            && path.StartsWith("/api/aihub/predefined-models", StringComparison.OrdinalIgnoreCase))
            return true;

        // Generic tools — read+write liberados pra PMs/POs montarem ferramentas
        // dos próprios projetos pelo MVP. Owner-scope garantido via query filter
        // por ProjectId no DbContext (tool de project A é invisível pra project
        // B mesmo via id direto). DELETE permanece admin-only por design.
        if (path.StartsWith("/api/aihub/generic-tools", StringComparison.OrdinalIgnoreCase)
            && (method.Equals("GET", StringComparison.OrdinalIgnoreCase)
                || method.Equals("POST", StringComparison.OrdinalIgnoreCase)
                || method.Equals("PUT", StringComparison.OrdinalIgnoreCase)))
            return true;

        // MCP servers — read+write liberados pelo mesmo motivo dos generic tools.
        // O controller mora em /api/aihub/admin/mcp-servers (path historicamente prefixado
        // com /admin/), mas o repository é project-scoped via HasQueryFilter, então
        // PMs só enxergam MCPs do próprio projeto. DELETE continua admin-only.
        if (path.StartsWith("/api/aihub/admin/mcp-servers", StringComparison.OrdinalIgnoreCase)
            && (method.Equals("GET", StringComparison.OrdinalIgnoreCase)
                || method.Equals("POST", StringComparison.OrdinalIgnoreCase)
                || method.Equals("PUT", StringComparison.OrdinalIgnoreCase)))
            return true;

        // Agent drafts — read+write liberados pra que clientes não-admin possam
        // criar/editar rascunhos dos próprios projetos. Project-scope garantido via
        // HasQueryFilter no DbContext (rascunho de project A é invisível pra project
        // B mesmo via id direto). POST /{id}/submit também cai aqui pra que o autor
        // submeta o rascunho à aprovação. DELETE permanece admin-only.
        if (path.StartsWith("/api/aihub/agent-drafts", StringComparison.OrdinalIgnoreCase)
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
