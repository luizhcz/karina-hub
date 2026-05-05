using System.Text.Json;
using EfsAiHub.Core.Abstractions.Persistence;
using EfsAiHub.Core.Agents;
using EfsAiHub.Core.Agents.Evaluation;
using EfsAiHub.Core.Orchestration.Enums;
using EfsAiHub.Platform.Runtime.Interfaces;

namespace EfsAiHub.Platform.Runtime.Evaluation;

/// <summary>
/// Orquestra o fluxo composto de avaliação automática no deploy:
/// (1) gera test cases sintéticos via <c>wf-gerador-testcases</c>,
/// (2) cria/publica TestSet, (3) cria/publica EvaluatorConfig do preset,
/// (4) enfileira <see cref="EvaluationRun"/>. Atomicidade best-effort:
/// falha do gerador não rollbacka deploy do workflow (soft gate).
/// Dedup por (AgentVersionId, preset) em janela 30min evita custo duplicado.
/// </summary>
public sealed class EvaluationAutoDeployService
{
    private static readonly TimeSpan DedupWindow = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan GeneratorTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan GeneratorPollInterval = TimeSpan.FromMilliseconds(800);
    private const string GeneratorWorkflowId = "wf-gerador-testcases";

    private readonly IAgentDefinitionRepository _agentRepo;
    private readonly IAgentVersionRepository _agentVersionRepo;
    private readonly IWorkflowService _workflowService;
    private readonly IEvaluationTestSetRepository _testSetRepo;
    private readonly IEvaluationTestSetVersionRepository _testSetVersionRepo;
    private readonly IEvaluatorConfigRepository _configRepo;
    private readonly IEvaluatorConfigVersionRepository _configVersionRepo;
    private readonly IEvaluationRunRepository _runRepo;
    private readonly IEvaluationService _evaluationService;
    private readonly ILogger<EvaluationAutoDeployService> _logger;

    public EvaluationAutoDeployService(
        IAgentDefinitionRepository agentRepo,
        IAgentVersionRepository agentVersionRepo,
        IWorkflowService workflowService,
        IEvaluationTestSetRepository testSetRepo,
        IEvaluationTestSetVersionRepository testSetVersionRepo,
        IEvaluatorConfigRepository configRepo,
        IEvaluatorConfigVersionRepository configVersionRepo,
        IEvaluationRunRepository runRepo,
        IEvaluationService evaluationService,
        ILogger<EvaluationAutoDeployService> logger)
    {
        _agentRepo = agentRepo;
        _agentVersionRepo = agentVersionRepo;
        _workflowService = workflowService;
        _testSetRepo = testSetRepo;
        _testSetVersionRepo = testSetVersionRepo;
        _configRepo = configRepo;
        _configVersionRepo = configVersionRepo;
        _runRepo = runRepo;
        _evaluationService = evaluationService;
        _logger = logger;
    }

    public async Task<AutoDeployResult> RunAsync(AutoDeployServiceRequest request, CancellationToken ct = default)
    {
        var def = await _agentRepo.GetByIdAsync(request.AgentDefinitionId, ct)
            ?? throw new EvaluationValidationException($"AgentDefinition '{request.AgentDefinitionId}' não encontrada.");

        var agentVersionId = request.AgentVersionId;
        if (string.IsNullOrEmpty(agentVersionId))
        {
            var current = await _agentVersionRepo.GetCurrentAsync(def.Id, ct)
                ?? throw new EvaluationValidationException($"AgentDefinition '{def.Id}' não tem AgentVersion published.");
            agentVersionId = current.AgentVersionId;
        }

        var spec = PresetCatalog.Get(request.Preset);
        var presetTag = request.Preset.ToString().ToLowerInvariant();

        // Dedup: re-deploy do mesmo agent+preset em janela 30min reusa run existente.
        // Failed/Cancelled liberam retry; Pending/Running/Completed deduplicam.
        var existing = await _runRepo.FindRecentByAgentVersionAndPresetAsync(agentVersionId, presetTag, DedupWindow, ct);
        if (existing is not null)
        {
            _logger.LogInformation(
                "[AutoDeploy] Dedup: run '{RunId}' já existe para AgentVersion '{Aid}' preset '{Preset}'.",
                existing.RunId, agentVersionId, presetTag);
            return new AutoDeployResult(
                RunId: existing.RunId,
                TestSetVersionId: existing.TestSetVersionId,
                EvaluatorConfigVersionId: existing.EvaluatorConfigVersionId,
                Preset: request.Preset,
                CaseCount: existing.CasesTotal,
                EstimatedCostUsd: spec.EstimatedCostUsd,
                EstimatedDurationSeconds: spec.EstimatedDurationSeconds,
                Status: existing.Status,
                DeduplicatedFromExisting: true,
                GeneratorFailed: false);
        }

        // 1. Gerar test cases via wf-gerador-testcases.
        var generated = await GenerateTestCasesAsync(def, spec.CaseCount, ct);
        if (generated is null || generated.testCases.Count == 0)
        {
            return new AutoDeployResult(
                RunId: null,
                TestSetVersionId: null,
                EvaluatorConfigVersionId: null,
                Preset: request.Preset,
                CaseCount: 0,
                EstimatedCostUsd: spec.EstimatedCostUsd,
                EstimatedDurationSeconds: spec.EstimatedDurationSeconds,
                Status: null,
                DeduplicatedFromExisting: false,
                GeneratorFailed: true);
        }

        // 2. Filtra cases com tools órfãs (defesa silenciosa — gerador pode alucinar).
        var validToolNames = def.Tools
            .Where(t => !string.IsNullOrEmpty(t.Name))
            .Select(t => t.Name!)
            .ToHashSet(StringComparer.Ordinal);
        var validCases = generated.testCases
            .Where(c => c.expectedToolCalls.All(t => validToolNames.Contains(t)))
            .ToList();
        if (validCases.Count == 0)
        {
            _logger.LogWarning(
                "[AutoDeploy] Gerador produziu {N} cases, todos com tools órfãs — abortando run.",
                generated.testCases.Count);
            return new AutoDeployResult(
                RunId: null,
                TestSetVersionId: null,
                EvaluatorConfigVersionId: null,
                Preset: request.Preset,
                CaseCount: 0,
                EstimatedCostUsd: spec.EstimatedCostUsd,
                EstimatedDurationSeconds: spec.EstimatedDurationSeconds,
                Status: null,
                DeduplicatedFromExisting: false,
                GeneratorFailed: true);
        }

        // 3. Cria TestSet header.
        var testSetId = $"ats-{def.Id}-{DateTime.UtcNow:yyyyMMddHHmmss}";
        var testSet = new EvaluationTestSet(
            Id: testSetId,
            ProjectId: request.ProjectId,
            Name: $"auto-deploy {def.Id} {DateTime.UtcNow:yyyy-MM-dd HH:mm}",
            Description: $"Test set sintético gerado pelo auto-deploy preset {presetTag}.",
            Visibility: TestSetVisibility.Project,
            CurrentVersionId: null,
            CreatedAt: DateTime.UtcNow,
            UpdatedAt: DateTime.UtcNow,
            CreatedBy: request.TriggeredBy);
        await _testSetRepo.UpsertAsync(testSet, ct);

        // 4. Build version (status Draft) + cases atômico.
        var revision = await _testSetVersionRepo.GetNextRevisionAsync(testSetId, ct);
        var draftCases = validCases.Select((c, i) => BuildCase(c, i)).ToList();
        var version = EvaluationTestSetVersion.Build(
            testSetId, revision, draftCases,
            createdBy: request.TriggeredBy,
            changeReason: $"auto-deploy:{presetTag}");

        // CaseId persiste como gerado em BuildCase; TestSetVersionId pluga aqui.
        var hydratedCases = draftCases.Select(c => c with { TestSetVersionId = version.TestSetVersionId }).ToList();
        var savedVersion = await _testSetVersionRepo.AppendAsync(version, hydratedCases, ct);
        await _testSetVersionRepo.SetStatusAsync(savedVersion.TestSetVersionId, TestSetVersionStatus.Published, ct);
        await _testSetRepo.SetCurrentVersionAsync(testSetId, savedVersion.TestSetVersionId, ct);

        // 5. Cria/atualiza EvaluatorConfig + Version.
        var existingConfig = await _configRepo.GetByAgentDefinitionAsync(def.Id, ct);
        var configId = existingConfig?.Id ?? $"ec-{def.Id}";
        var config = new EvaluatorConfig(
            Id: configId,
            AgentDefinitionId: def.Id,
            Name: existingConfig?.Name ?? $"Config auto-deploy {def.Id}",
            CurrentVersionId: existingConfig?.CurrentVersionId,
            CreatedAt: existingConfig?.CreatedAt ?? DateTime.UtcNow,
            UpdatedAt: DateTime.UtcNow,
            CreatedBy: existingConfig?.CreatedBy ?? request.TriggeredBy);
        await _configRepo.UpsertAsync(config, ct);

        var configRevision = await _configVersionRepo.GetNextRevisionAsync(configId, ct);
        var configVersion = EvaluatorConfigVersion.Build(
            configId, configRevision, spec.Bindings,
            spec.Splitter, spec.NumRepetitions,
            createdBy: request.TriggeredBy,
            changeReason: $"auto-deploy:{presetTag}");
        var savedConfigVersion = await _configVersionRepo.AppendAsync(configVersion, ct);
        await _configVersionRepo.SetStatusAsync(savedConfigVersion.EvaluatorConfigVersionId, EvaluatorConfigVersionStatus.Published, ct);
        await _configRepo.SetCurrentVersionAsync(configId, savedConfigVersion.EvaluatorConfigVersionId, ct);

        // 6. Enqueue run via EvaluationService (validações de tools órfãs já passaram via filtro acima).
        var enqueueResult = await _evaluationService.EnqueueAutoDeployAsync(new EnqueueAutoDeployRequest(
            ProjectId: request.ProjectId,
            AgentDefinitionId: def.Id,
            AgentVersionId: agentVersionId,
            TestSetVersionId: savedVersion.TestSetVersionId,
            EvaluatorConfigVersionId: savedConfigVersion.EvaluatorConfigVersionId,
            Preset: presetTag,
            DeployedFromWorkflowId: request.DeployedFromWorkflowId,
            TriggeredBy: request.TriggeredBy), ct);

        return new AutoDeployResult(
            RunId: enqueueResult.RunId,
            TestSetVersionId: savedVersion.TestSetVersionId,
            EvaluatorConfigVersionId: savedConfigVersion.EvaluatorConfigVersionId,
            Preset: request.Preset,
            CaseCount: hydratedCases.Count,
            EstimatedCostUsd: spec.EstimatedCostUsd,
            EstimatedDurationSeconds: spec.EstimatedDurationSeconds,
            Status: enqueueResult.Status,
            DeduplicatedFromExisting: enqueueResult.DeduplicatedFromExisting,
            GeneratorFailed: false);
    }

    private async Task<GeneratedTestCasesOutput?> GenerateTestCasesAsync(
        AgentDefinition def,
        int targetCaseCount,
        CancellationToken ct)
    {
        var inputJson = JsonSerializer.Serialize(new
        {
            agentName = def.Name,
            description = def.Description,
            instructions = def.Instructions,
            tools = def.Tools
                .Where(t => !string.IsNullOrEmpty(t.Name))
                .Select(t => new { name = t.Name, type = t.Type })
                .ToList(),
            targetCaseCount = targetCaseCount
        });

        string executionId;
        try
        {
            executionId = await _workflowService.TriggerAsync(
                GeneratorWorkflowId,
                inputJson,
                metadata: new Dictionary<string, string> { ["source"] = "auto-deploy" },
                ct: ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AutoDeploy] Trigger do gerador-testcases falhou para agent '{AgentId}'.", def.Id);
            return null;
        }

        var deadline = DateTime.UtcNow + GeneratorTimeout;
        while (DateTime.UtcNow < deadline)
        {
            try { await Task.Delay(GeneratorPollInterval, ct); }
            catch (OperationCanceledException) { return null; }

            var execution = await _workflowService.GetExecutionAsync(executionId, ct);
            if (execution is null) continue;

            if (execution.Status == WorkflowStatus.Completed)
            {
                if (string.IsNullOrEmpty(execution.Output))
                {
                    _logger.LogWarning("[AutoDeploy] Gerador completou sem output (executionId={Eid}).", executionId);
                    return null;
                }
                try
                {
                    return JsonSerializer.Deserialize<GeneratedTestCasesOutput>(
                        execution.Output,
                        new JsonSerializerOptions(JsonSerializerDefaults.Web));
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "[AutoDeploy] Output do gerador inválido (executionId={Eid}).", executionId);
                    return null;
                }
            }
            if (execution.Status is WorkflowStatus.Failed or WorkflowStatus.Cancelled)
            {
                _logger.LogWarning(
                    "[AutoDeploy] Gerador terminou em {Status} (executionId={Eid}).",
                    execution.Status, executionId);
                return null;
            }
        }

        _logger.LogWarning("[AutoDeploy] Gerador timeout em {Sec}s (executionId={Eid}).", GeneratorTimeout.TotalSeconds, executionId);
        return null;
    }

    private static EvaluationTestCase BuildCase(GeneratedTestCase c, int index)
    {
        // Convert array<string> de tool names → JsonDocument [{name:"x"}, {name:"y"}]
        // (formato esperado em EvaluationTestCase.ExpectedToolCalls e validado em
        // EvaluatorFactory.ValidateCasesAgainstAgent).
        JsonDocument? expectedToolCalls = null;
        if (c.expectedToolCalls is { Count: > 0 })
        {
            var serialized = JsonSerializer.Serialize(
                c.expectedToolCalls.Select(name => new { name }).ToList());
            expectedToolCalls = JsonDocument.Parse(serialized);
        }

        return new EvaluationTestCase(
            CaseId: Guid.NewGuid().ToString("N"),
            TestSetVersionId: string.Empty, // hidratado pelo caller após Build
            Index: index,
            Input: c.input ?? string.Empty,
            ExpectedOutput: c.expectedOutput,
            ExpectedToolCalls: expectedToolCalls,
            Tags: c.tags ?? new List<string>(),
            Weight: c.weight,
            CreatedAt: DateTime.UtcNow);
    }
}

public sealed record AutoDeployServiceRequest(
    string ProjectId,
    string AgentDefinitionId,
    AutoDeployPreset Preset,
    string? AgentVersionId,
    string? DeployedFromWorkflowId,
    string? TriggeredBy);

public sealed record AutoDeployResult(
    string? RunId,
    string? TestSetVersionId,
    string? EvaluatorConfigVersionId,
    AutoDeployPreset Preset,
    int CaseCount,
    decimal EstimatedCostUsd,
    int EstimatedDurationSeconds,
    EvaluationRunStatus? Status,
    bool DeduplicatedFromExisting,
    bool GeneratorFailed);

// Shape do output do agent gerador-testcases (StructuredOutput strict-mode).
internal sealed class GeneratedTestCasesOutput
{
    public List<GeneratedTestCase> testCases { get; set; } = new();
    public string? summary { get; set; }
    public int quantity { get; set; }
}

internal sealed class GeneratedTestCase
{
    public string? input { get; set; }
    public string? expectedOutput { get; set; }
    public List<string> expectedToolCalls { get; set; } = new();
    public List<string> tags { get; set; } = new();
    public double weight { get; set; }
}
