using EfsAiHub.Core.Abstractions.Projects;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace EfsAiHub.Host.Api.Controllers;

[ApiController]
[Route("api/model-catalog")]
[Produces("application/json")]
public class ModelCatalogController : ControllerBase
{
    private readonly IModelCatalogRepository _repo;

    public ModelCatalogController(IModelCatalogRepository repo) => _repo = repo;

    [HttpGet]
    [SwaggerOperation(Summary = "Lista modelos do catálogo", Description = "Filtrar por provider com ?provider=OPENAI")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] string? provider,
        [FromQuery] bool activeOnly = true,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        if (pageSize > 200) pageSize = 200;
        var models = await _repo.GetAllAsync(provider, activeOnly, page, pageSize, ct);
        var total = await _repo.CountAsync(provider, activeOnly, ct);
        return Ok(new { items = models.Select(ModelCatalogResponse.From), total, page, pageSize });
    }

    [HttpGet("{provider}/{id}")]
    [SwaggerOperation(Summary = "Busca modelo por provider e ID")]
    [ProducesResponseType(typeof(ModelCatalogResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(string provider, string id, CancellationToken ct = default)
    {
        var model = await _repo.GetByIdAsync(id, provider, ct);
        return model is null ? NotFound() : Ok(ModelCatalogResponse.From(model));
    }

    [HttpPost]
    [SwaggerOperation(Summary = "Cria ou atualiza um modelo no catálogo")]
    [ProducesResponseType(typeof(ModelCatalogResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Upsert([FromBody] UpsertModelCatalogRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Id) || string.IsNullOrWhiteSpace(request.Provider))
            return BadRequest("Id and Provider are required.");

        var model = new ModelCatalog
        {
            Id           = request.Id.Trim(),
            Provider     = request.Provider.Trim().ToUpperInvariant(),
            DisplayName  = request.DisplayName,
            Description  = request.Description,
            ContextWindow = request.ContextWindow,
            Capabilities = request.Capabilities ?? [],
            IsActive     = request.IsActive ?? true
        };

        var result = await _repo.UpsertAsync(model, ct);
        return Ok(ModelCatalogResponse.From(result));
    }

    [HttpDelete("{provider}/{id}")]
    [SwaggerOperation(Summary = "Desativa um modelo do catálogo (soft delete)")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Deactivate(string provider, string id, CancellationToken ct = default)
    {
        var found = await _repo.SetActiveAsync(id, provider, false, ct);
        return found ? NoContent() : NotFound();
    }
}

public sealed record UpsertModelCatalogRequest(
    string Id,
    string Provider,
    string DisplayName,
    string? Description,
    int? ContextWindow,
    List<string>? Capabilities,
    bool? IsActive);

public sealed record ModelCatalogResponse(
    string Id,
    string Provider,
    string DisplayName,
    string? Description,
    int? ContextWindow,
    List<string> Capabilities,
    bool IsActive,
    DateTime CreatedAt,
    DateTime UpdatedAt)
{
    public static ModelCatalogResponse From(ModelCatalog m) => new(
        m.Id, m.Provider, m.DisplayName, m.Description,
        m.ContextWindow, m.Capabilities, m.IsActive, m.CreatedAt, m.UpdatedAt);
}
