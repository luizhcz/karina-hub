using System.Text.Json;
using EfsAiHub.Core.Abstractions.Observability;
using EfsAiHub.Core.Agents.Evaluation;
using EfsAiHub.Host.Api.Models.Requests.Evaluation;
using EfsAiHub.Host.Api.Models.Responses.Evaluation;
using EfsAiHub.Host.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace EfsAiHub.Host.Api.Controllers;

/// <summary>
/// CRUD do <see cref="EvaluatorConfig"/> por agente (header + versions
/// append-only). Versions são imutáveis; mudança de bindings = nova revision.
/// </summary>
[ApiController]
[Route("api/agents/{agentId}/evaluator-config")]
[Produces("application/json")]
public sealed class AgentEvaluatorConfigController : ControllerBase
{
    private readonly IEvaluatorConfigRepository _configRepo;
    private readonly IEvaluatorConfigVersionRepository _configVersionRepo;
    private readonly IAdminAuditLogger _audit;
    private readonly AdminAuditContext _auditContext;
    private readonly ILogger<AgentEvaluatorConfigController> _logger;

    public AgentEvaluatorConfigController(
        IEvaluatorConfigRepository configRepo,
        IEvaluatorConfigVersionRepository configVersionRepo,
        IAdminAuditLogger audit,
        AdminAuditContext auditContext,
        ILogger<AgentEvaluatorConfigController> logger)
    {
        _configRepo = configRepo;
        _configVersionRepo = configVersionRepo;
        _audit = audit;
        _auditContext = auditContext;
        _logger = logger;
    }

    [HttpGet]
    [SwaggerOperation(Summary = "Config ativa do agente (header + version atual)")]
    [ProducesResponseType(typeof(EvaluatorConfigWithVersionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCurrent(string agentId, CancellationToken ct)
    {
        var config = await _configRepo.GetByAgentDefinitionAsync(agentId, ct);
        if (config is null) return NotFound();

        EvaluatorConfigVersion? current = null;
        if (!string.IsNullOrEmpty(config.CurrentVersionId))
            current = await _configVersionRepo.GetByIdAsync(config.CurrentVersionId, ct);

        return Ok(new EvaluatorConfigWithVersionResponse(
            EvaluatorConfigResponse.FromDomain(config),
            current is null ? null : EvaluatorConfigVersionResponse.FromDomain(current)));
    }

    [HttpGet("history")]
    [SwaggerOperation(Summary = "Histórico de versions do EvaluatorConfig")]
    [ProducesResponseType(typeof(IReadOnlyList<EvaluatorConfigVersionResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetHistory(string agentId, CancellationToken ct)
    {
        var config = await _configRepo.GetByAgentDefinitionAsync(agentId, ct);
        if (config is null) return NotFound();

        var versions = await _configVersionRepo.ListByConfigAsync(config.Id, ct);
        return Ok(versions.Select(EvaluatorConfigVersionResponse.FromDomain));
    }

    [HttpPut]
    [SwaggerOperation(Summary = "Cria/atualiza EvaluatorConfig (publica nova version se bindings mudaram)")]
    [ProducesResponseType(typeof(EvaluatorConfigVersionResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Upsert(
        string agentId, [FromBody] UpsertEvaluatorConfigRequest request, CancellationToken ct)
    {
        if (request.Bindings is null || request.Bindings.Count == 0)
            return BadRequest(new { error = "Bindings não pode ser vazio." });

        if (!Enum.TryParse<SplitterStrategy>(request.Splitter, ignoreCase: true, out var splitter))
            return BadRequest(new { error = $"Splitter inválido: '{request.Splitter}'. Use LastTurn|Full|PerTurn." });

        if (request.NumRepetitions < 1 || request.NumRepetitions > 10)
            return BadRequest(new { error = "NumRepetitions deve estar entre 1 e 10." });

        var bindings = new List<EvaluatorBinding>(request.Bindings.Count);
        for (int i = 0; i < request.Bindings.Count; i++)
        {
            var b = request.Bindings[i];
            if (!Enum.TryParse<EvaluatorKind>(b.Kind, ignoreCase: true, out var kind))
                return BadRequest(new { error = $"Binding[{i}].Kind inválido: '{b.Kind}'. Use Foundry|Local|Meai." });

            JsonDocument? @params = null;
            if (b.Params.HasValue && b.Params.Value.ValueKind != JsonValueKind.Null)
                @params = JsonDocument.Parse(b.Params.Value.GetRawText());

            bindings.Add(new EvaluatorBinding(
                Kind: kind,
                Name: b.Name,
                Params: @params,
                Enabled: b.Enabled,
                Weight: b.Weight,
                BindingIndex: b.BindingIndex));
        }

        // Resolve config header (cria se não existe).
        var existingConfig = await _configRepo.GetByAgentDefinitionAsync(agentId, ct);
        var config = existingConfig ?? new EvaluatorConfig(
            Id: $"ec-{Guid.NewGuid():N}",
            AgentDefinitionId: agentId,
            Name: request.Name,
            CurrentVersionId: null,
            CreatedAt: DateTime.UtcNow,
            UpdatedAt: DateTime.UtcNow,
            CreatedBy: ResolveActorUserId());
        if (existingConfig is null) await _configRepo.UpsertAsync(config, ct);

        var revision = await _configVersionRepo.GetNextRevisionAsync(config.Id, ct);
        var version = EvaluatorConfigVersion.Build(
            evaluatorConfigId: config.Id,
            revision: revision,
            bindings: bindings,
            splitter: splitter,
            numRepetitions: request.NumRepetitions,
            createdBy: ResolveActorUserId(),
            changeReason: request.ChangeReason);

        var persisted = await _configVersionRepo.AppendAsync(version, ct);
        if (persisted.Status != EvaluatorConfigVersionStatus.Published)
            await _configVersionRepo.SetStatusAsync(persisted.EvaluatorConfigVersionId, EvaluatorConfigVersionStatus.Published, ct);

        await _configRepo.SetCurrentVersionAsync(config.Id, persisted.EvaluatorConfigVersionId, ct);

        await _audit.RecordAsync(_auditContext.Build(
            AdminAuditActions.Update,
            "EvaluatorConfig",
            config.Id,
            payloadAfter: AdminAuditContext.Snapshot(EvaluatorConfigVersionResponse.FromDomain(persisted))), ct);

        var refreshed = persisted with { Status = EvaluatorConfigVersionStatus.Published };
        return CreatedAtAction(nameof(GetCurrent), new { agentId }, EvaluatorConfigVersionResponse.FromDomain(refreshed));
    }

    public sealed record EvaluatorConfigWithVersionResponse(
        EvaluatorConfigResponse Config,
        EvaluatorConfigVersionResponse? CurrentVersion);

    private string? ResolveActorUserId()
    {
        var headers = HttpContext?.Request?.Headers;
        if (headers is null) return null;
        var resolver = HttpContext!.RequestServices.GetService<UserIdentityResolver>();
        return resolver?.TryResolve(headers, out _)?.UserId;
    }
}
