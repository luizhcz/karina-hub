using EfsAiHub.Host.Api.Configuration;
using EfsAiHub.Host.Api.Models.Responses;
using EfsAiHub.Host.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Swashbuckle.AspNetCore.Annotations;

namespace EfsAiHub.Host.Api.Controllers;

/// <summary>
/// Endpoint público que resolve a identidade do caller a partir dos headers
/// e responde com <c>{ accountId, isAdmin }</c>. Existe pro frontend descobrir
/// se a conta atual é admin SEM precisar bater num endpoint admin-only e
/// receber 403 (que polui o console do navegador).
/// </summary>
[ApiController]
[Route("api/aihub/me")]
[Produces("application/json")]
public class MeController : ControllerBase
{
    private readonly UserIdentityResolver _resolver;
    private readonly HashSet<string> _adminAccountIds;

    public MeController(UserIdentityResolver resolver, IOptions<AdminOptions> adminOptions)
    {
        _resolver = resolver;
        _adminAccountIds = new HashSet<string>(adminOptions.Value.AccountIds, StringComparer.Ordinal);
    }

    [HttpGet]
    [SwaggerOperation(Summary = "Identidade resolvida do caller + flag de admin. Sempre 200; sem identidade retorna { isAdmin: false }.")]
    [ProducesResponseType(typeof(MeResponse), StatusCodes.Status200OK)]
    public IActionResult Get()
    {
        var identity = _resolver.TryResolve(Request.Headers, out _);
        if (identity is null)
        {
            return Ok(new MeResponse
            {
                AccountId = null,
                UserType = null,
                IsAdmin = false,
            });
        }

        // AccountIds vazio = gate desabilitado → todo mundo é admin
        // (mesma lógica de AdminGateMiddleware.IsAdminAccount).
        var isAdmin = _adminAccountIds.Count == 0 || _adminAccountIds.Contains(identity.UserId);

        return Ok(new MeResponse
        {
            AccountId = identity.UserId,
            UserType = identity.UserType,
            IsAdmin = isAdmin,
        });
    }
}
