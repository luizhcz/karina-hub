using EfsAiHub.Core.Abstractions.Conversations;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace EfsAiHub.Host.Api.Controllers;

[ApiController]
[Route("api/users")]
[Produces("application/json")]
public class UserConversationsController : ControllerBase
{
    private readonly IConversationRepository _convRepo;

    public UserConversationsController(IConversationRepository convRepo)
    {
        _convRepo = convRepo;
    }

    [HttpGet("{userId}/conversations")]
    [SwaggerOperation(Summary = "Lista conversas de um usuário (mais recentes primeiro)")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        string userId,
        [FromQuery] int limit = 50,
        CancellationToken ct = default)
    {
        var sessions = await _convRepo.GetByUserIdAsync(userId, limit, ct);
        return Ok(sessions);
    }
}
