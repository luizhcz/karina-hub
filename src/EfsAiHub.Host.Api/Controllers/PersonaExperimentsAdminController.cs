using EfsAiHub.Core.Abstractions.Identity;
using EfsAiHub.Core.Abstractions.Identity.Persona;
using EfsAiHub.Core.Abstractions.Observability;
using EfsAiHub.Host.Api.Models.Requests;
using EfsAiHub.Host.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace EfsAiHub.Host.Api.Controllers;

/// <summary>
/// CRUD de experiments A/B de templates de persona + endpoint de resultados
/// agregados por variant.
///
/// Isolamento: scopes project-aware só aparecem pro project dono.
/// Ver <see cref="PersonaPromptTemplatesAdminController"/> pro padrão canonical.
/// </summary>
[ApiController]
[Route("api/admin/persona-experiments")]
[Produces("application/json")]
public class PersonaExperimentsAdminController : ControllerBase
{
    private readonly IPersonaPromptExperimentRepository _repo;
    private readonly IPersonaPromptTemplateRepository _templateRepo;
    private readonly IAdminAuditLogger _audit;
    private readonly AdminAuditContext _auditContext;
    private readonly IProjectContextAccessor _projectAccessor;

    public PersonaExperimentsAdminController(
        IPersonaPromptExperimentRepository repo,
        IPersonaPromptTemplateRepository templateRepo,
        IAdminAuditLogger audit,
        AdminAuditContext auditContext,
        IProjectContextAccessor projectAccessor)
    {
        _repo = repo;
        _templateRepo = templateRepo;
        _audit = audit;
        _auditContext = auditContext;
        _projectAccessor = projectAccessor;
    }

    [HttpGet]
    [SwaggerOperation(Summary = "Lista experiments do project corrente")]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var projectId = _projectAccessor.Current.ProjectId;
        var items = await _repo.GetByProjectAsync(projectId, ct);

        await _audit.RecordAsync(_auditContext.Build(
            AdminAuditActions.Read,
            AdminAuditResources.PersonaPromptExperiment,
            "*"), ct);

        return Ok(items);
    }

    [HttpGet("{id:int}")]
    [SwaggerOperation(Summary = "Detalhe de um experiment")]
    public async Task<IActionResult> GetById(int id, CancellationToken ct)
    {
        var item = await _repo.GetByIdAsync(id, ct);
        if (item is null) return NotFound();
        if (item.ProjectId != _projectAccessor.Current.ProjectId) return NotFound();

        await _audit.RecordAsync(_auditContext.Build(
            AdminAuditActions.Read,
            AdminAuditResources.PersonaPromptExperiment,
            id.ToString()), ct);

        return Ok(item);
    }

    [HttpGet("{id:int}/results")]
    [SwaggerOperation(Summary = "Resultados agregados por variant (A/B)")]
    public async Task<IActionResult> GetResults(int id, CancellationToken ct)
    {
        var exp = await _repo.GetByIdAsync(id, ct);
        if (exp is null) return NotFound();
        if (exp.ProjectId != _projectAccessor.Current.ProjectId) return NotFound();

        var results = await _repo.GetResultsAsync(id, ct);

        await _audit.RecordAsync(_auditContext.Build(
            AdminAuditActions.Read,
            AdminAuditResources.PersonaPromptExperiment,
            $"{id}:results"), ct);

        return Ok(new
        {
            experiment = exp,
            results,
        });
    }

    [HttpPost]
    [SwaggerOperation(Summary = "Cria experiment A/B pra um scope")]
    public async Task<IActionResult> Create(
        [FromBody] PersonaPromptExperimentCreateRequest request,
        CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        var projectId = _projectAccessor.Current.ProjectId;

        if (!IsScopeAccessibleByCurrentProject(request.Scope))
            return BadRequest(new { error = $"Scope '{request.Scope}' não é acessível pelo project corrente." });

        // Variants distintas e existentes: previne experiment "quebrado" que
        // degrada silently no composer.
        if (request.VariantAVersionId == request.VariantBVersionId)
            return BadRequest(new { error = "VariantA e VariantB não podem apontar pra mesma version — experiment comparando conteúdo idêntico é lixo." });

        var a = await _templateRepo.GetVersionByIdAsync(request.VariantAVersionId, ct);
        if (a is null)
            return BadRequest(new { error = $"VariantAVersionId '{request.VariantAVersionId}' não existe." });
        var b = await _templateRepo.GetVersionByIdAsync(request.VariantBVersionId, ct);
        if (b is null)
            return BadRequest(new { error = $"VariantBVersionId '{request.VariantBVersionId}' não existe." });
        if (a.TemplateId != b.TemplateId)
            return BadRequest(new { error = "Variants precisam ser versions do mesmo template." });

        // UNIQUE parcial no DB garante também, mas resposta clara evita 500.
        var existingActive = await _repo.GetActiveAsync(projectId, request.Scope, ct);
        if (existingActive is not null)
            return Conflict(new { error = $"Experiment ativo já existe pro scope '{request.Scope}' (id={existingActive.Id}). Encerre-o antes de criar outro." });

        // Actor canônico vive em admin_audit_log; CreatedBy do domínio fica null.
        var created = await _repo.CreateAsync(new PersonaPromptExperiment
        {
            ProjectId = projectId,
            Scope = request.Scope,
            Name = request.Name,
            VariantAVersionId = request.VariantAVersionId,
            VariantBVersionId = request.VariantBVersionId,
            TrafficSplitB = request.TrafficSplitB,
            Metric = request.Metric,
        }, ct);

        await _audit.RecordAsync(_auditContext.Build(
            AdminAuditActions.Create,
            AdminAuditResources.PersonaPromptExperiment,
            created.Id.ToString(),
            payloadAfter: AdminAuditContext.Snapshot(created)), ct);

        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpPost("{id:int}/end")]
    [SwaggerOperation(Summary = "Encerra um experiment (seta EndedAt). Idempotente.")]
    public async Task<IActionResult> End(int id, CancellationToken ct)
    {
        var exp = await _repo.GetByIdAsync(id, ct);
        if (exp is null) return NotFound();
        if (exp.ProjectId != _projectAccessor.Current.ProjectId) return NotFound();

        await _repo.EndAsync(id, ct);

        await _audit.RecordAsync(_auditContext.Build(
            AdminAuditActions.Update,
            AdminAuditResources.PersonaPromptExperiment,
            $"{id}:end"), ct);

        return NoContent();
    }

    [HttpDelete("{id:int}")]
    [SwaggerOperation(Summary = "Deleta experiment (sem cascatear em llm_token_usage)")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var exp = await _repo.GetByIdAsync(id, ct);
        if (exp is null) return NotFound();
        if (exp.ProjectId != _projectAccessor.Current.ProjectId) return NotFound();

        await _repo.DeleteAsync(id, ct);

        await _audit.RecordAsync(_auditContext.Build(
            AdminAuditActions.Delete,
            AdminAuditResources.PersonaPromptExperiment,
            id.ToString()), ct);

        return NoContent();
    }

    // Espelha a regra do controller de templates — mantida local pra evitar
    // dependência cruzada entre controllers.
    private bool IsScopeAccessibleByCurrentProject(string scope)
    {
        var currentProject = _projectAccessor.Current.ProjectId;

        if (scope.StartsWith("global:", StringComparison.Ordinal)) return true;
        if (scope.StartsWith("agent:", StringComparison.Ordinal)) return true;

        if (scope.StartsWith("project:", StringComparison.Ordinal))
        {
            var rest = scope.AsSpan("project:".Length);
            var colon = rest.IndexOf(':');
            if (colon <= 0) return false;
            var scopeProjectId = rest[..colon];
            return scopeProjectId.SequenceEqual(currentProject);
        }

        return false;
    }
}
