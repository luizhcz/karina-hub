using EfsAiHub.Platform.Runtime.Interfaces;
using EfsAiHub.Core.Orchestration.Enums;
using EfsAiHub.Core.Orchestration.Coordination;
using EfsAiHub.Core.Abstractions.Execution;
using EfsAiHub.Core.Abstractions.Identity;
using EfsAiHub.Core.Abstractions.Projects;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace EfsAiHub.Platform.Runtime.Services;

public class WorkflowService : IWorkflowService, IWorkflowDispatcher
{
    private readonly IWorkflowDefinitionRepository _definitionRepo;
    private readonly IWorkflowExecutionRepository _executionRepo;
    private readonly WorkflowValidator _validator;
    private readonly EdgeInvariantsValidator _edgeInvariants;
    private readonly WorkflowAgentInvariantsValidator _agentInvariants;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHostApplicationLifetime _appLifetime;
    private readonly IExecutionSlotRegistry _chatRegistry;
    private readonly IProjectContextAccessor _projectAccessor;
    private readonly ITenantContextAccessor _tenantAccessor;
    private readonly IWorkflowVersionRepository? _versionRepo;
    private readonly IAgentVersionRepository? _agentVersionRepo;
    private readonly ICrossNodeBus? _crossBus;
    private readonly IProjectRepository? _projectRepo;
    private readonly ILogger<WorkflowService> _logger;

    public WorkflowService(
        IWorkflowDefinitionRepository definitionRepo,
        IWorkflowExecutionRepository executionRepo,
        WorkflowValidator validator,
        EdgeInvariantsValidator edgeInvariants,
        WorkflowAgentInvariantsValidator agentInvariants,
        IServiceScopeFactory scopeFactory,
        IHostApplicationLifetime appLifetime,
        IExecutionSlotRegistry chatRegistry,
        IProjectContextAccessor projectAccessor,
        ITenantContextAccessor tenantAccessor,
        ILogger<WorkflowService> logger,
        IWorkflowVersionRepository? versionRepo = null,
        IAgentVersionRepository? agentVersionRepo = null,
        ICrossNodeBus? crossBus = null,
        IProjectRepository? projectRepo = null)
    {
        _definitionRepo = definitionRepo;
        _executionRepo = executionRepo;
        _validator = validator;
        _edgeInvariants = edgeInvariants;
        _agentInvariants = agentInvariants;
        _scopeFactory = scopeFactory;
        _appLifetime = appLifetime;
        _chatRegistry = chatRegistry;
        _projectAccessor = projectAccessor;
        _tenantAccessor = tenantAccessor;
        _logger = logger;
        _versionRepo = versionRepo;
        _agentVersionRepo = agentVersionRepo;
        _crossBus = crossBus;
        _projectRepo = projectRepo;
    }

    /// <summary>
    /// Defense-in-depth: implantação tipo Chat só pode ser criada/editada por
    /// projetos com <c>chat_deployment_allowed=true</c>. Frontend já filtra,
    /// mas validar aqui evita criação via API direta. Detecta deploy Chat
    /// quando <c>metadata['deploymentKind']='chat'</c> OU
    /// <c>Configuration.InputMode='Chat'</c> + Graph orchestration.
    /// </summary>
    private async Task EnsureChatDeploymentAllowedAsync(
        WorkflowDefinition definition, CancellationToken ct)
    {
        if (_projectRepo is null) return; // backwards-compat com DI antigo

        var isChatDeploy =
            (definition.Metadata?.TryGetValue("deploymentKind", out var kind) == true
                && string.Equals(kind, "chat", StringComparison.OrdinalIgnoreCase))
            || (string.Equals(definition.Configuration?.InputMode, "Chat", StringComparison.OrdinalIgnoreCase)
                && definition.OrchestrationMode == OrchestrationMode.Graph);
        if (!isChatDeploy) return;

        var project = await _projectRepo.GetByIdAsync(definition.ProjectId, ct);
        if (project is null || !project.ChatDeploymentAllowed)
        {
            throw new UnauthorizedAccessException(
                $"Implantação tipo 'chat' é permitida apenas em projetos com chat_deployment_allowed=true. "
                + $"Projeto '{definition.ProjectId}' não está autorizado.");
        }
    }

    /// <summary>
    /// Para cada <see cref="WorkflowAgentReference"/> sem <c>AgentVersionId</c>, resolve current
    /// Published do agent e popula. UX: caller que não envia pin recebe pin "current" automático;
    /// migração manual via PATCH /api/aihub/workflows/{id}/agents/{agentId}/pin permanece disponível.
    /// </summary>
    private async Task ResolveDefaultPinsAsync(WorkflowDefinition definition, CancellationToken ct)
    {
        if (_agentVersionRepo is null) return;
        foreach (var agentRef in definition.Agents)
        {
            if (!string.IsNullOrEmpty(agentRef.AgentVersionId)) continue;
            var current = await _agentVersionRepo.GetCurrentAsync(agentRef.AgentId, ct);
            if (current is not null) agentRef.AgentVersionId = current.AgentVersionId;
        }
    }

    public async Task<WorkflowDefinition> CreateAsync(WorkflowDefinition definition, CancellationToken ct = default)
    {
        definition.ProjectId = _projectAccessor.Current.ProjectId;

        await EnsureChatDeploymentAllowedAsync(definition, ct);

        await ResolveDefaultPinsAsync(definition, ct);
        var (isValid, errors) = await ValidateAsync(definition, ct);
        if (!isValid)
            throw new ArgumentException($"Definição de workflow inválida: {string.Join(", ", errors)}");

        // Invariantes tipadas (regras de negócio cruzando registries) — falha com
        // envelope estruturado pra controller mapear pra 400 com error_code.
        var invariantErrors = new List<WorkflowInvariantError>();
        invariantErrors.AddRange(await _edgeInvariants.ValidateAsync(definition, ct));
        invariantErrors.AddRange(await _agentInvariants.ValidateAsync(definition, ct));
        if (invariantErrors.Count > 0)
            throw new WorkflowInvariantViolationException(invariantErrors);

        _logger.LogInformation("Criando definição de workflow '{WorkflowId}'", definition.Id);
        return await _definitionRepo.UpsertAsync(definition, ct);
    }

    public Task<WorkflowDefinition?> GetAsync(string id, CancellationToken ct = default)
        => _definitionRepo.GetByIdAsync(id, ct);

    public Task<IReadOnlyList<WorkflowDefinition>> ListAsync(CancellationToken ct = default)
        => _definitionRepo.GetAllAsync(ct);

    public async Task<IReadOnlyList<WorkflowDefinition>> ListByProjectAsync(CancellationToken ct = default)
    {
        var currentProjectId = _projectAccessor.Current.ProjectId;
        var all = await _definitionRepo.GetAllAsync(ct).ConfigureAwait(false);
        return all.Where(w => w.ProjectId == currentProjectId).ToList();
    }

    public async Task<WorkflowDefinition> UpdateAsync(WorkflowDefinition definition, CancellationToken ct = default)
    {
        var existing = await _definitionRepo.GetByIdAsync(definition.Id, ct)
            ?? throw new KeyNotFoundException($"Workflow '{definition.Id}' não encontrado.");

        // Owner gate: HasQueryFilter expõe workflows Visibility=global cross-project pra
        // leitura, então o existing pode ser visível pra um projeto que NÃO é dono. Sem
        // este check, qualquer projeto do tenant editaria o workflow global.
        var currentProjectId = _projectAccessor.Current.ProjectId;
        if (!string.Equals(existing.ProjectId, currentProjectId, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException(
                $"Workflow '{definition.Id}' não pertence ao projeto atual; apenas o projeto dono pode editar.");

        // Preserva ownership/visibility do existing — request DTO não carrega esses campos
        // por design; sem isso o PUT silenciosamente reseta Visibility="project"/ProjectId="default".
        // PATCH /visibility é o único caminho documentado pra mudar Visibility.
        definition.ProjectId = existing.ProjectId;
        definition.TenantId = existing.TenantId;
        definition.Visibility = existing.Visibility;

        await EnsureChatDeploymentAllowedAsync(definition, ct);

        await ResolveDefaultPinsAsync(definition, ct);
        var (isValid, errors) = await ValidateAsync(definition, ct);
        if (!isValid)
            throw new ArgumentException($"Definição de workflow inválida: {string.Join(", ", errors)}");

        var invariantErrors = new List<WorkflowInvariantError>();
        invariantErrors.AddRange(await _edgeInvariants.ValidateAsync(definition, ct));
        invariantErrors.AddRange(await _agentInvariants.ValidateAsync(definition, ct));
        if (invariantErrors.Count > 0)
            throw new WorkflowInvariantViolationException(invariantErrors);

        definition.UpdatedAt = DateTime.UtcNow;
        return await _definitionRepo.UpsertAsync(definition, ct);
    }

    public async Task<WorkflowDefinition> UpdateVisibilityAsync(string id, string newVisibility, CancellationToken ct = default)
    {
        var existing = await _definitionRepo.GetByIdAsync(id, ct)
            ?? throw new KeyNotFoundException($"Workflow '{id}' não encontrado.");

        // Owner gate: só o projeto dono pode alterar visibility (request rolando em outro projeto não pode).
        // Mensagem genérica — não vaza qual é o ProjectId do owner (info-leak menor).
        var currentProjectId = _projectAccessor.Current.ProjectId;
        if (!string.Equals(existing.ProjectId, currentProjectId, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException(
                $"Workflow '{id}' não pertence ao projeto atual; apenas o projeto dono pode alterar visibility.");

        var (isValid, errors) = await _validator.ValidateVisibilityChangeAsync(existing, newVisibility, ct);
        if (!isValid)
            throw new ArgumentException($"Mudança de visibility inválida: {string.Join(", ", errors)}");

        // Idempotência: se não mudou, retorna existing sem upsert (evita audit/cache churn).
        if (string.Equals(existing.Visibility, newVisibility, StringComparison.OrdinalIgnoreCase))
            return existing;

        // Reconstrói definition com novo Visibility (init-only requer rebuild).
        var updated = new WorkflowDefinition
        {
            Id = existing.Id,
            Name = existing.Name,
            Description = existing.Description,
            Version = existing.Version,
            OrchestrationMode = existing.OrchestrationMode,
            Agents = existing.Agents,
            Edges = existing.Edges,
            Executors = existing.Executors,
            RoutingRules = existing.RoutingRules,
            Configuration = existing.Configuration,
            Metadata = existing.Metadata,
            Visibility = newVisibility,
            ProjectId = existing.ProjectId,
            TenantId = existing.TenantId,
            CreatedAt = existing.CreatedAt,
            UpdatedAt = DateTime.UtcNow,
        };

        return await _definitionRepo.UpsertAsync(updated, ct);
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        var existing = await _definitionRepo.GetByIdAsync(id, ct)
            ?? throw new KeyNotFoundException($"Workflow '{id}' não encontrado.");

        var currentProjectId = _projectAccessor.Current.ProjectId;
        if (!string.Equals(existing.ProjectId, currentProjectId, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException(
                $"Workflow '{id}' não pertence ao projeto atual; apenas o projeto dono pode remover.");

        var deleted = await _definitionRepo.DeleteAsync(id, ct);
        if (!deleted)
            throw new KeyNotFoundException($"Workflow '{id}' não encontrado.");
    }

    public async Task<string> TriggerAsync(
        string workflowId,
        string? inputPayload,
        Dictionary<string, string>? metadata = null,
        ExecutionSource source = ExecutionSource.Api,
        ExecutionMode mode = ExecutionMode.Production,
        string? workflowVersionId = null,
        CancellationToken ct = default)
    {
        // Bifurca o load: pin presente carrega snapshot append-only, ausente lê
        // o estado mutável atual. Validação cruzada garante que o snapshot
        // pertence ao workflow alvo — evita pin cruzado entre workflows do
        // mesmo tenant. Quando workflowId não existe no project (HasQueryFilter),
        // o ramo "current" retorna null e o 404 é o mesmo erro que o caller
        // veria sem header — UX consistente.
        WorkflowDefinition? definition;
        if (!string.IsNullOrWhiteSpace(workflowVersionId))
        {
            // Repo opcional na DI (testes unit que não injetam o snapshot). Em
            // produção sempre presente — se chegou null aqui o setup quebrou.
            if (_versionRepo is null)
                throw new InvalidOperationException(
                    "Pin de WorkflowVersion solicitado mas IWorkflowVersionRepository " +
                    "não está disponível no container.");

            definition = await _versionRepo.GetDefinitionSnapshotAsync(workflowVersionId, ct)
                ?? throw new KeyNotFoundException(
                    $"WorkflowVersion '{workflowVersionId}' não encontrada.");

            if (!string.Equals(definition.Id, workflowId, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException(
                    $"WorkflowVersion '{workflowVersionId}' pertence ao workflow " +
                    $"'{definition.Id}', não '{workflowId}'.");
        }
        else
        {
            definition = await _definitionRepo.GetByIdAsync(workflowId, ct)
                ?? throw new KeyNotFoundException($"Workflow '{workflowId}' não encontrado.");
        }

        // Back-pressure global: rejeita ANTES de criar execução no repositório.
        // Evita órfãs em Pending caso o teto esteja batido.
        if (!await _chatRegistry.TryAcquireSlotAsync())
        {
            _logger.LogWarning(
                "Back-pressure: workflow '{WorkflowId}' rejeitado (limite global atingido).",
                workflowId);
            throw new ChatBackPressureException(
                "Limite global de execuções simultâneas atingido. Tente novamente em instantes.");
        }

        var execution = new WorkflowExecution
        {
            ExecutionId = Guid.NewGuid().ToString(),
            WorkflowId = workflowId,
            WorkflowVersionId = workflowVersionId,
            ProjectId = _projectAccessor.Current.ProjectId,
            Status = WorkflowStatus.Pending,
            Input = inputPayload,
            Metadata = metadata != null ? new System.Collections.Concurrent.ConcurrentDictionary<string, string>(metadata) : new()
        };

        try
        {
            await _executionRepo.CreateAsync(execution, ct);
        }
        catch
        {
            // Libera slot adquirido se a criação da execução falhar.
            await _chatRegistry.ReleaseSlotAsync();
            throw;
        }

        // Disparo direto via Task.Run para todas as fontes.
        // O 202 retorna imediatamente; o workflow executa com scope próprio.
        _logger.LogInformation(
            "Execução '{ExecutionId}' iniciada para workflow '{WorkflowId}' (source={Source} version_pin={Pin})",
            execution.ExecutionId, workflowId, source,
            workflowVersionId ?? "current");

        var execCts = new CancellationTokenSource();
        _chatRegistry.Register(execution.ExecutionId, execCts);

        var capturedExecution = execution;
        var capturedDefinition = definition;

        _ = Task.Run(async () =>
        {
            // CTS linked criado DENTRO do Task.Run — lifetime correto (não é disposed com o 202).
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                execCts.Token, _appLifetime.ApplicationStopping);

            // Scope único para toda a execução — permite consistência transacional
            // entre o executor e o writer de estado terminal.
            await using var scope = _scopeFactory.CreateAsyncScope();
            var failureWriter = scope.ServiceProvider.GetRequiredService<ExecutionFailureWriter>();

            try
            {
                var executor = scope.ServiceProvider.GetRequiredService<IWorkflowExecutor>();
                await executor.ExecuteAsync(capturedExecution, capturedDefinition, linked.Token);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Falha ao executar workflow '{ExecutionId}'.",
                    capturedExecution.ExecutionId);
                try
                {
                    await failureWriter.MarkFailedAsync(capturedExecution, ex.Message, ErrorCategory.Unknown);
                }
                catch (Exception updateEx)
                {
                    _logger.LogError(updateEx, "Falha ao persistir status Failed para execução '{ExecutionId}'.",
                        capturedExecution.ExecutionId);
                }
            }
            finally
            {
                _chatRegistry.Cleanup(capturedExecution.ExecutionId);
            }
        }, _appLifetime.ApplicationStopping);

        return execution.ExecutionId;
    }

    public Task<WorkflowExecution?> GetExecutionAsync(string executionId, CancellationToken ct = default)
        => _executionRepo.GetByIdAsync(executionId, ct);

    public Task<IReadOnlyList<WorkflowExecution>> GetExecutionsAsync(
        string workflowId, int page = 1, int pageSize = 20, string? status = null, CancellationToken ct = default)
        => _executionRepo.GetByWorkflowIdAsync(workflowId, page, pageSize, status, ct);

    public async Task<(IReadOnlyList<WorkflowExecution> Items, int Total)> GetAllExecutionsAsync(
        string? workflowId = null, string? status = null, DateTime? from = null, DateTime? to = null,
        int page = 1, int pageSize = 50, CancellationToken ct = default)
    {
        var items = await _executionRepo.GetAllAsync(workflowId, status, from, to, page, pageSize, ct);
        var total = await _executionRepo.CountAsync(workflowId, status, from, to, ct);
        return (items, total);
    }

    public async Task CancelExecutionAsync(string executionId, CancellationToken ct = default)
    {
        var execution = await _executionRepo.GetByIdAsync(executionId, ct)
            ?? throw new KeyNotFoundException($"Execução '{executionId}' não encontrada.");

        if (execution.Status is WorkflowStatus.Completed or WorkflowStatus.Failed or WorkflowStatus.Cancelled)
            throw new InvalidOperationException($"Execução '{executionId}' já está em estado terminal ({execution.Status}).");

        _chatRegistry.TryCancel(executionId);

        // Fix #A1: propaga cancel para outros pods via LISTEN/NOTIFY. Best-effort.
        if (_crossBus is not null)
        {
            _ = Task.Run(async () =>
            {
                try { await _crossBus.PublishCancelAsync(executionId); }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "[WorkflowService] Falha ao publicar cancel cross-pod de '{ExecutionId}'.", executionId);
                }
            });
        }

        _logger.LogInformation("Cancelamento solicitado para execução '{ExecutionId}'", executionId);
    }

    public Task<(bool IsValid, IReadOnlyList<string> Errors)> ValidateAsync(
        WorkflowDefinition definition, CancellationToken ct = default)
        => _validator.ValidateAsync(definition, ct);

    // ═══════════════════════════════════════════════════════════════════════
    // Catalog
    // ═══════════════════════════════════════════════════════════════════════

    public Task<IReadOnlyList<WorkflowDefinition>> ListVisibleAsync(CancellationToken ct = default)
        => _definitionRepo.ListVisibleAsync(
            _projectAccessor.Current.ProjectId,
            _tenantAccessor.Current.TenantId,
            ct);

    public async Task<WorkflowDefinition> CloneAsync(
        string sourceWorkflowId, string? newId = null, CancellationToken ct = default)
    {
        var source = await _definitionRepo.GetByIdAsync(sourceWorkflowId, ct)
            ?? throw new KeyNotFoundException($"Workflow '{sourceWorkflowId}' não encontrado.");

        // Workflows efêmeros de Chat Sandbox são gerenciados pelo ChatSandboxService
        // e limpos pelo background cleanup com base em metadata.kind. Permitir clone
        // herda o marcador "transient" pro clone, que viraria deploy permanente E
        // candidato a deleção pelo cleanup. Bloqueio explícito evita o footgun.
        if (source.Metadata is not null
            && source.Metadata.TryGetValue("kind", out var sourceKind)
            && string.Equals(sourceKind, "chat-sandbox", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Workflows de Chat Sandbox são efêmeros e não podem ser clonados como deploys permanentes. " +
                "Crie um deploy Chat real via tela de Implantações.");
        }

        // Gera novo ID; atribui ao projeto atual; visibilidade "project" por padrão.
        // Create() valida invariantes — lança DomainException se o source estiver inconsistente.
        var cloned = WorkflowDefinition.Create(
            id: newId ?? $"{source.Id}-clone-{Guid.NewGuid().ToString("N")[..8]}",
            name: $"{source.Name} (clone)",
            orchestrationMode: source.OrchestrationMode,
            agents: source.Agents,
            edges: source.Edges,
            executors: source.Executors,
            routingRules: source.RoutingRules,
            configuration: source.Configuration,
            metadata: new Dictionary<string, string>(source.Metadata),
            projectId: _projectAccessor.Current.ProjectId,
            visibility: "project",
            description: source.Description,
            version: source.Version);

        var (isValid, errors) = await ValidateAsync(cloned, ct);
        if (!isValid)
            throw new ArgumentException($"Workflow clonado inválido: {string.Join(", ", errors)}");

        // Source pode ter sido salvo antes de invariants atuais existirem. Sem
        // re-checagem aqui, clone vira bypass legítimo de regras novas (Edge
        // typing, Conversational+Standalone). Mesmo padrão de Create/Update.
        var invariantErrors = new List<WorkflowInvariantError>();
        invariantErrors.AddRange(await _edgeInvariants.ValidateAsync(cloned, ct));
        invariantErrors.AddRange(await _agentInvariants.ValidateAsync(cloned, ct));
        if (invariantErrors.Count > 0)
            throw new WorkflowInvariantViolationException(invariantErrors);

        return await _definitionRepo.UpsertAsync(cloned, ct);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Versioning
    // ═══════════════════════════════════════════════════════════════════════

    public async Task<IReadOnlyList<WorkflowVersion>> ListVersionsAsync(
        string workflowId, CancellationToken ct = default)
    {
        if (_versionRepo is null)
            return Array.Empty<WorkflowVersion>();
        return await _versionRepo.ListByDefinitionAsync(workflowId, ct);
    }

    public async Task<WorkflowVersion?> GetVersionAsync(
        string versionId, CancellationToken ct = default)
    {
        if (_versionRepo is null) return null;
        return await _versionRepo.GetByIdAsync(versionId, ct);
    }

    /// <summary>
    /// Restaura uma WorkflowDefinition a partir de um snapshot versionado.
    /// Desserializa o snapshot armazenado e faz UpsertAsync (que por sua vez gera
    /// uma nova revision — idempotente se o conteúdo não mudou).
    /// </summary>
    public async Task<WorkflowDefinition> RollbackAsync(
        string workflowId, string versionId, CancellationToken ct = default)
    {
        if (_versionRepo is null)
            throw new InvalidOperationException("Workflow versioning is not configured.");

        var version = await _versionRepo.GetByIdAsync(versionId, ct)
            ?? throw new KeyNotFoundException($"WorkflowVersion '{versionId}' não encontrada.");

        if (version.WorkflowDefinitionId != workflowId)
            throw new ArgumentException(
                $"WorkflowVersion '{versionId}' pertence ao workflow '{version.WorkflowDefinitionId}', não '{workflowId}'.");

        var snapshot = await _versionRepo.GetDefinitionSnapshotAsync(versionId, ct)
            ?? throw new InvalidOperationException(
                $"Snapshot corrompido para WorkflowVersion '{versionId}'.");

        _logger.LogInformation(
            "Rollback de workflow '{WorkflowId}' para versão '{VersionId}' (revision {Revision}).",
            workflowId, versionId, version.Revision);

        // UpsertAsync gera nova version (idempotente se hash não mudou)
        return await _definitionRepo.UpsertAsync(snapshot, ct);
    }
}
