using EfsAiHub.Platform.Runtime.Interfaces;
using EfsAiHub.Core.Orchestration.Enums;
using EfsAiHub.Core.Orchestration.Coordination;
using EfsAiHub.Core.Abstractions.Execution;
using EfsAiHub.Core.Abstractions.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace EfsAiHub.Platform.Runtime.Services;

public class WorkflowService : IWorkflowService, IWorkflowDispatcher
{
    private readonly IWorkflowDefinitionRepository _definitionRepo;
    private readonly IWorkflowExecutionRepository _executionRepo;
    private readonly WorkflowValidator _validator;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHostApplicationLifetime _appLifetime;
    private readonly IExecutionSlotRegistry _chatRegistry;
    private readonly IProjectContextAccessor _projectAccessor;
    private readonly IWorkflowVersionRepository? _versionRepo;
    private readonly ICrossNodeBus? _crossBus;
    private readonly ILogger<WorkflowService> _logger;

    public WorkflowService(
        IWorkflowDefinitionRepository definitionRepo,
        IWorkflowExecutionRepository executionRepo,
        WorkflowValidator validator,
        IServiceScopeFactory scopeFactory,
        IHostApplicationLifetime appLifetime,
        IExecutionSlotRegistry chatRegistry,
        IProjectContextAccessor projectAccessor,
        ILogger<WorkflowService> logger,
        IWorkflowVersionRepository? versionRepo = null,
        ICrossNodeBus? crossBus = null)
    {
        _definitionRepo = definitionRepo;
        _executionRepo = executionRepo;
        _validator = validator;
        _scopeFactory = scopeFactory;
        _appLifetime = appLifetime;
        _chatRegistry = chatRegistry;
        _projectAccessor = projectAccessor;
        _logger = logger;
        _versionRepo = versionRepo;
        _crossBus = crossBus;
    }

    public async Task<WorkflowDefinition> CreateAsync(WorkflowDefinition definition, CancellationToken ct = default)
    {
        definition.ProjectId = _projectAccessor.Current.ProjectId;

        var (isValid, errors) = await ValidateAsync(definition, ct);
        if (!isValid)
            throw new ArgumentException($"Definição de workflow inválida: {string.Join(", ", errors)}");

        _logger.LogInformation("Criando definição de workflow '{WorkflowId}'", definition.Id);
        return await _definitionRepo.UpsertAsync(definition, ct);
    }

    public Task<WorkflowDefinition?> GetAsync(string id, CancellationToken ct = default)
        => _definitionRepo.GetByIdAsync(id, ct);

    public Task<IReadOnlyList<WorkflowDefinition>> ListAsync(CancellationToken ct = default)
        => _definitionRepo.GetAllAsync(ct);

    public async Task<WorkflowDefinition> UpdateAsync(WorkflowDefinition definition, CancellationToken ct = default)
    {
        _ = await _definitionRepo.GetByIdAsync(definition.Id, ct)
            ?? throw new KeyNotFoundException($"Workflow '{definition.Id}' não encontrado.");

        var (isValid, errors) = await ValidateAsync(definition, ct);
        if (!isValid)
            throw new ArgumentException($"Definição de workflow inválida: {string.Join(", ", errors)}");

        definition.UpdatedAt = DateTime.UtcNow;
        return await _definitionRepo.UpsertAsync(definition, ct);
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
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
        CancellationToken ct = default)
    {
        var definition = await _definitionRepo.GetByIdAsync(workflowId, ct)
            ?? throw new KeyNotFoundException($"Workflow '{workflowId}' não encontrado.");

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
        _logger.LogInformation("Execução '{ExecutionId}' iniciada para workflow '{WorkflowId}' ({Source})",
            execution.ExecutionId, workflowId, source);

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
        => _definitionRepo.ListVisibleAsync(_projectAccessor.Current.ProjectId, ct);

    public async Task<WorkflowDefinition> CloneAsync(
        string sourceWorkflowId, string? newId = null, CancellationToken ct = default)
    {
        var source = await _definitionRepo.GetByIdAsync(sourceWorkflowId, ct)
            ?? throw new KeyNotFoundException($"Workflow '{sourceWorkflowId}' não encontrado.");

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
