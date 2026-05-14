using EfsAiHub.Core.Abstractions.Identity;
using EfsAiHub.Core.Abstractions.Projects;
using EfsAiHub.Core.Abstractions.Users;
using EfsAiHub.Host.Api.Models.Responses;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace EfsAiHub.Host.Api.Controllers;

/// <summary>
/// Endpoint público que devolve a identidade do caller resolvida pelo
/// <c>UserProvisioningMiddleware</c> + flag <c>isAdmin</c> lida do diretório
/// (tabela aihub.users) + lista de projetos visíveis. Existe pro frontend
/// descobrir o estado do usuário sem precisar bater num endpoint admin-only
/// e receber 403.
/// </summary>
[ApiController]
[Route("api/aihub/me")]
[Produces("application/json")]
public class MeController : ControllerBase
{
    private readonly IUserContextAccessor _userAccessor;
    private readonly ITenantContextAccessor _tenantAccessor;
    private readonly IUserMembershipService _membership;
    private readonly IProjectRepository _projectRepo;

    public MeController(
        IUserContextAccessor userAccessor,
        ITenantContextAccessor tenantAccessor,
        IUserMembershipService membership,
        IProjectRepository projectRepo)
    {
        _userAccessor = userAccessor;
        _tenantAccessor = tenantAccessor;
        _membership = membership;
        _projectRepo = projectRepo;
    }

    [HttpGet]
    [SwaggerOperation(Summary = "Identidade resolvida + flag de admin + projetos visíveis. Sempre 200; sem identidade retorna { isAdmin: false, projects: [] }.")]
    [ProducesResponseType(typeof(MeResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var user = _userAccessor.Current;
        if (user is null)
        {
            return Ok(new MeResponse
            {
                AccountId = null,
                UserType = null,
                IsAdmin = false,
                Projects = [],
            });
        }

        var tenantId = _tenantAccessor.Current.TenantId;
        var allProjects = await _projectRepo.GetByTenantAsync(tenantId, ct);

        IEnumerable<Project> visibleProjects;
        if (user.IsAdmin)
        {
            // Admin enxerga todos os projetos do tenant — inclui o "default"
            // pra que apareça no seletor da UI e seja reconhecido como válido
            // pelo auto-select do Layout (filtrar aqui causava o admin perder
            // a seleção de 'default' a cada render).
            visibleProjects = allProjects;
        }
        else
        {
            var visibleIds = await _membership.GetVisibleProjectIdsAsync(user.ExternalUserId, tenantId, ct)
                             ?? Array.Empty<string>();
            var idSet = new HashSet<string>(visibleIds, StringComparer.Ordinal);
            visibleProjects = allProjects.Where(p => idSet.Contains(p.Id));
        }

        return Ok(new MeResponse
        {
            AccountId = user.ExternalUserId,
            UserType = user.UserType,
            IsAdmin = user.IsAdmin,
            UserId = user.Id,
            DisplayName = user.DisplayName ?? user.ExternalUserId,
            Projects = visibleProjects
                .Select(p => new ProjectRef { Id = p.Id, Name = p.Name })
                .ToList(),
        });
    }
}
