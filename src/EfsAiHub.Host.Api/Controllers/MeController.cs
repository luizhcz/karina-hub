using EfsAiHub.Core.Abstractions.Users;
using EfsAiHub.Host.Api.Models.Responses;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace EfsAiHub.Host.Api.Controllers;

/// <summary>
/// Endpoint público que devolve a identidade do caller resolvida pelo
/// <c>UserProvisioningMiddleware</c> + flag <c>isAdmin</c> lida do diretório
/// (tabela aihub.users). Existe pro frontend descobrir se a conta atual é
/// admin SEM precisar bater num endpoint admin-only e receber 403.
/// </summary>
[ApiController]
[Route("api/aihub/me")]
[Produces("application/json")]
public class MeController : ControllerBase
{
    private readonly IUserContextAccessor _userAccessor;

    public MeController(IUserContextAccessor userAccessor)
    {
        _userAccessor = userAccessor;
    }

    [HttpGet]
    [SwaggerOperation(Summary = "Identidade resolvida do caller + flag de admin. Sempre 200; sem identidade retorna { isAdmin: false }.")]
    [ProducesResponseType(typeof(MeResponse), StatusCodes.Status200OK)]
    public IActionResult Get()
    {
        var user = _userAccessor.Current;
        if (user is null)
        {
            return Ok(new MeResponse
            {
                AccountId = null,
                UserType = null,
                IsAdmin = false,
            });
        }

        return Ok(new MeResponse
        {
            AccountId = user.ExternalUserId,
            UserType = user.UserType,
            IsAdmin = user.IsAdmin,
            UserId = user.Id,
            DisplayName = user.DisplayName ?? user.ExternalUserId,
        });
    }
}
