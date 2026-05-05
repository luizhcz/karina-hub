using EfsAiHub.Host.Api.Models.Responses;

namespace EfsAiHub.Host.Api.Controllers;

/// <summary>
/// Endpoint público read-only do catálogo de presets — usado pelo AgentForm
/// pra popular o dropdown "Modelo". Filtra <c>Enabled=true</c> por default
/// (PMs não veem inativos). Admin CRUD vive em /api/aihub/admin/predefined-models.
/// </summary>
[ApiController]
[Route("api/aihub/predefined-models")]
[Produces("application/json")]
public class PredefinedModelsController : ControllerBase
{
    private readonly IPredefinedModelService _service;

    public PredefinedModelsController(IPredefinedModelService service)
    {
        _service = service;
    }

    [HttpGet]
    [SwaggerOperation(Summary = "Lista presets enabled do catálogo (visão pública pra AgentForm).")]
    [ProducesResponseType(typeof(IReadOnlyList<PredefinedModelResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var presets = await _service.ListAsync(includeDisabled: false, ct);
        return Ok(presets.Select(PredefinedModelResponse.FromDomain));
    }

    [HttpGet("{id}")]
    [SwaggerOperation(Summary = "Busca preset pelo Id. 404 também quando disabled.")]
    [ProducesResponseType(typeof(PredefinedModelResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(string id, CancellationToken ct)
    {
        var preset = await _service.GetAsync(id, ct);
        if (preset is null || !preset.Enabled) return NotFound();
        return Ok(PredefinedModelResponse.FromDomain(preset));
    }
}
