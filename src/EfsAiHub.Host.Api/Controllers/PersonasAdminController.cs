using EfsAiHub.Core.Abstractions.Identity.Persona;
using EfsAiHub.Core.Abstractions.Observability;
using EfsAiHub.Host.Api.Services;
using EfsAiHub.Platform.Runtime.Execution;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace EfsAiHub.Host.Api.Controllers;

/// <summary>
/// Endpoints administrativos para debug e gestão do cache de persona.
/// A fonte de verdade é a API externa — este controller não expõe CRUD
/// (mudanças voltam via invalidação do cache).
/// </summary>
[ApiController]
[Route("api/admin/personas")]
[Produces("application/json")]
public class PersonasAdminController : ControllerBase
{
    private readonly IPersonaProvider _provider;
    private readonly CachedPersonaProvider _cache;
    private readonly IAdminAuditLogger _audit;
    private readonly AdminAuditContext _auditContext;

    public PersonasAdminController(
        IPersonaProvider provider,
        CachedPersonaProvider cache,
        IAdminAuditLogger audit,
        AdminAuditContext auditContext)
    {
        _provider = provider;
        _cache = cache;
        _audit = audit;
        _auditContext = auditContext;
    }

    [HttpGet("{userId}")]
    [SwaggerOperation(Summary = "Resolve a persona de um usuário (debug — passa pelo cache normal)")]
    public async Task<IActionResult> Get(
        string userId,
        [FromQuery] string userType = "cliente",
        CancellationToken ct = default)
    {
        var persona = await _provider.ResolveAsync(userId, userType, ct);

        // LGPD art. 37 — trilha de consulta de dados pessoais. Resource id
        // é composta (userType:userId) pra permitir filtragem por tipo.
        await _audit.RecordAsync(_auditContext.Build(
            AdminAuditActions.Read,
            AdminAuditResources.PersonaCache,
            $"{userType}:{userId}"), ct);

        return Ok(persona);
    }

    [HttpPost("{userId}/invalidate")]
    [SwaggerOperation(Summary = "Invalida a entrada de cache (L1 + L2 Redis) para um usuário")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Invalidate(
        string userId,
        [FromQuery] string userType = "cliente",
        CancellationToken ct = default)
    {
        await _cache.InvalidateAsync(userId, userType);
        await _audit.RecordAsync(_auditContext.Build(
            AdminAuditActions.Delete,
            AdminAuditResources.PersonaCache,
            $"{userType}:{userId}"), ct);
        return NoContent();
    }
}
