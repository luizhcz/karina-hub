using EfsAiHub.Core.Abstractions.BackgroundServices;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace EfsAiHub.Host.Api.Controllers;

/// <summary>
/// Lista os IHostedService da aplicação a partir do BackgroundServiceRegistry
/// (populado em AddBackgroundServiceRegistry). Apenas leitura — a tela de
/// monitoramento do frontend consome este endpoint.
/// </summary>
[ApiController]
[Route("api/admin/background-services")]
[Produces("application/json")]
public class BackgroundServicesController : ControllerBase
{
    private readonly IBackgroundServiceRegistry _registry;

    public BackgroundServicesController(IBackgroundServiceRegistry registry)
        => _registry = registry;

    [HttpGet]
    [SwaggerOperation(Summary = "Lista background services registrados (nome, descrição, lifecycle, intervalo).")]
    public IActionResult List()
    {
        var items = _registry.GetAll()
            .Select(kvp => new
            {
                name = kvp.Key,
                description = kvp.Value.Description,
                lifecycle = kvp.Value.Lifecycle,
                intervalSeconds = kvp.Value.Interval?.TotalSeconds,
                typeName = kvp.Value.ServiceType.Name,
            })
            .OrderBy(x => x.name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return Ok(new { items, total = items.Count });
    }
}
