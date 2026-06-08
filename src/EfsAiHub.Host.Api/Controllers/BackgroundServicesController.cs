using System.Diagnostics;
using EfsAiHub.Core.Abstractions.BackgroundServices;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace EfsAiHub.Host.Api.Controllers;

/// <summary>
/// Lista os IHostedService da aplicação a partir do BackgroundServiceRegistry
/// (populado em AddBackgroundServiceRegistry) + dados de runtime do
/// IBackgroundServiceHeartbeatSink. Apenas leitura — a tela de monitoramento
/// do frontend consome este endpoint.
/// </summary>
[ApiController]
[Route("api/aihub/admin/background-services")]
[Produces("application/json")]
public class BackgroundServicesController : ControllerBase
{
    // Instante em que o processo subiu. Usado pra calcular uptime no response.
    // Lê de Process.StartTime — cai pra UtcNow se o platform não expuser
    // (containers minimalistas).
    private static readonly DateTimeOffset ProcessStartedAtUtc = ResolveProcessStart();

    private readonly IBackgroundServiceRegistry _registry;
    private readonly IBackgroundServiceHeartbeatSink _heartbeat;

    public BackgroundServicesController(
        IBackgroundServiceRegistry registry,
        IBackgroundServiceHeartbeatSink heartbeat)
    {
        _registry = registry;
        _heartbeat = heartbeat;
    }

    [HttpGet]
    [SwaggerOperation(Summary = "Lista background services registrados (nome, categoria, lifecycle, intervalo, heartbeat) + uptime do processo.")]
    public IActionResult List()
    {
        var heartbeats = _heartbeat.GetAll();
        var items = _registry.GetAll()
            .Select(kvp =>
            {
                heartbeats.TryGetValue(kvp.Key, out var hb);
                return new
                {
                    name = kvp.Key,
                    description = kvp.Value.Description,
                    lifecycle = kvp.Value.Lifecycle,
                    category = kvp.Value.Category.ToString(),
                    intervalSeconds = kvp.Value.Interval?.TotalSeconds,
                    typeName = kvp.Value.ServiceType.Name,
                    heartbeat = hb is null ? null : new
                    {
                        startedAtUtc = hb.StartedAtUtc,
                        lastTickAtUtc = hb.LastTickAtUtc,
                        lastSuccessAtUtc = hb.LastSuccessAtUtc,
                        lastErrorAtUtc = hb.LastErrorAtUtc,
                        lastErrorMessage = hb.LastErrorMessage,
                        tickCount = hb.TickCount,
                        errorCount = hb.ErrorCount,
                    },
                };
            })
            .OrderBy(x => x.category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return Ok(new
        {
            processStartedAtUtc = ProcessStartedAtUtc,
            nowUtc = DateTimeOffset.UtcNow,
            items,
            total = items.Count,
        });
    }

    private static DateTimeOffset ResolveProcessStart()
    {
        try
        {
            return new DateTimeOffset(Process.GetCurrentProcess().StartTime.ToUniversalTime(), TimeSpan.Zero);
        }
        catch
        {
            // Em alguns contêineres minimalistas Process.StartTime lança — fallback
            // pra UtcNow é melhor que crash no startup do controller.
            return DateTimeOffset.UtcNow;
        }
    }
}
