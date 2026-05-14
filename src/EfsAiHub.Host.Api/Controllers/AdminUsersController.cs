using EfsAiHub.Core.Abstractions.Identity;
using EfsAiHub.Core.Abstractions.Observability;
using EfsAiHub.Core.Abstractions.Users;
using EfsAiHub.Host.Api.Middleware;
using EfsAiHub.Host.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Swashbuckle.AspNetCore.Annotations;

namespace EfsAiHub.Host.Api.Controllers;

/// <summary>
/// Endpoints admin-only pra gerenciar o diretório de usuários e seus
/// vínculos de projeto. Gating é feito pelo AdminGateMiddleware — qualquer
/// rota sob /api/aihub/admin/users requer IsAdmin=true no usuário corrente.
/// </summary>
[ApiController]
[Route("api/aihub/admin/users")]
[Produces("application/json")]
public class AdminUsersController : ControllerBase
{
    private readonly IUserDirectory _directory;
    private readonly IUserMembershipService _membership;
    private readonly ITenantContextAccessor _tenantAccessor;
    private readonly IUserContextAccessor _userAccessor;
    private readonly IAdminAuditLogger _audit;
    private readonly AdminAuditContext _auditContext;
    private readonly IMemoryCache _cache;

    public AdminUsersController(
        IUserDirectory directory,
        IUserMembershipService membership,
        ITenantContextAccessor tenantAccessor,
        IUserContextAccessor userAccessor,
        IAdminAuditLogger audit,
        AdminAuditContext auditContext,
        IMemoryCache cache)
    {
        _directory = directory;
        _membership = membership;
        _tenantAccessor = tenantAccessor;
        _userAccessor = userAccessor;
        _audit = audit;
        _auditContext = auditContext;
        _cache = cache;
    }

    [HttpGet]
    [SwaggerOperation(Summary = "Lista paginada de usuários do tenant. Busca por externalUserId/displayName.")]
    [ProducesResponseType(typeof(AdminUsersListResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken ct = default)
    {
        var tenantId = _tenantAccessor.Current.TenantId;
        var (items, total) = await _directory.ListAsync(tenantId, search, page, pageSize, ct);

        var projectCounts = new Dictionary<Guid, int>();
        foreach (var u in items)
        {
            var projects = await _membership.GetProjectsForUserAsync(u.Id, ct);
            projectCounts[u.Id] = projects.Count;
        }

        return Ok(new AdminUsersListResponse
        {
            Page = page,
            PageSize = pageSize,
            Total = total,
            Items = items.Select(u => new AdminUserSummary
            {
                Id = u.Id,
                ExternalUserId = u.ExternalUserId,
                UserType = u.UserType,
                DisplayName = u.DisplayName ?? u.ExternalUserId,
                IsAdmin = u.IsAdmin,
                CreatedAt = u.CreatedAt,
                LastSeenAt = u.LastSeenAt,
                ProjectCount = projectCounts.GetValueOrDefault(u.Id),
            }).ToList(),
        });
    }

    [HttpGet("{id:guid}")]
    [SwaggerOperation(Summary = "Detalhe de usuário com lista de projetos vinculados.")]
    [ProducesResponseType(typeof(AdminUserDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var user = await _directory.GetByIdAsync(id, ct);
        if (user is null || user.TenantId != _tenantAccessor.Current.TenantId)
            return NotFound();

        var projects = await _membership.GetProjectsForUserAsync(user.Id, ct);
        return Ok(new AdminUserDetailResponse
        {
            Id = user.Id,
            ExternalUserId = user.ExternalUserId,
            UserType = user.UserType,
            DisplayName = user.DisplayName ?? user.ExternalUserId,
            IsAdmin = user.IsAdmin,
            CreatedAt = user.CreatedAt,
            LastSeenAt = user.LastSeenAt,
            ProjectIds = projects,
        });
    }

    [HttpPut("{id:guid}/projects")]
    [SwaggerOperation(Summary = "Substitui o set de projetos vinculados ao usuário.")]
    [ProducesResponseType(typeof(AdminUserDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetProjects(Guid id, [FromBody] SetProjectsRequest request, CancellationToken ct)
    {
        var user = await _directory.GetByIdAsync(id, ct);
        if (user is null || user.TenantId != _tenantAccessor.Current.TenantId)
            return NotFound();

        var actorExternalId = _auditContext.GetActorUserId();
        var projectIds = request.ProjectIds ?? new List<string>();
        var before = await _membership.GetProjectsForUserAsync(user.Id, ct);

        await _membership.AssignProjectsAsync(user.Id, projectIds, actorExternalId, ct);

        // Invalida o cache de provisioning também — caso a UI dispare uma
        // request imediata pra esse user, ele já enxerga o novo set sem TTL.
        UserProvisioningMiddleware.InvalidateCache(_cache, user.TenantId, user.ExternalUserId);

        await _audit.RecordAsync(_auditContext.Build(
            AdminAuditActions.UserProjectsAssigned,
            AdminAuditResources.User,
            user.Id.ToString(),
            payloadBefore: AdminAuditContext.Snapshot(new { projectIds = before }),
            payloadAfter: AdminAuditContext.Snapshot(new { projectIds })), ct);

        var after = await _membership.GetProjectsForUserAsync(user.Id, ct);
        return Ok(new AdminUserDetailResponse
        {
            Id = user.Id,
            ExternalUserId = user.ExternalUserId,
            UserType = user.UserType,
            DisplayName = user.DisplayName ?? user.ExternalUserId,
            IsAdmin = user.IsAdmin,
            CreatedAt = user.CreatedAt,
            LastSeenAt = user.LastSeenAt,
            ProjectIds = after,
        });
    }

    [HttpPatch("{id:guid}")]
    [SwaggerOperation(Summary = "Atualiza IsAdmin e/ou DisplayName.")]
    [ProducesResponseType(typeof(AdminUserDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Patch(Guid id, [FromBody] PatchUserRequest request, CancellationToken ct)
    {
        var user = await _directory.GetByIdAsync(id, ct);
        if (user is null || user.TenantId != _tenantAccessor.Current.TenantId)
            return NotFound();

        var auditEntries = new List<AdminAuditEntry>();

        if (request.IsAdmin.HasValue && request.IsAdmin.Value != user.IsAdmin)
        {
            await _directory.SetAdminAsync(user.Id, request.IsAdmin.Value, ct);
            auditEntries.Add(_auditContext.Build(
                AdminAuditActions.UserAdminFlagChanged,
                AdminAuditResources.User,
                user.Id.ToString(),
                payloadBefore: AdminAuditContext.Snapshot(new { isAdmin = user.IsAdmin }),
                payloadAfter: AdminAuditContext.Snapshot(new { isAdmin = request.IsAdmin.Value })));
        }

        if (!string.IsNullOrWhiteSpace(request.DisplayName) && request.DisplayName != user.DisplayName)
        {
            await _directory.SetDisplayNameAsync(user.Id, request.DisplayName, ct);
            auditEntries.Add(_auditContext.Build(
                AdminAuditActions.UserDisplayNameChanged,
                AdminAuditResources.User,
                user.Id.ToString(),
                payloadBefore: AdminAuditContext.Snapshot(new { displayName = user.DisplayName }),
                payloadAfter: AdminAuditContext.Snapshot(new { displayName = request.DisplayName })));
        }

        // Invalida caches do user pra que próxima request enxergue o estado novo
        // sem esperar o TTL — sobretudo crítico pra promoções/rebaixamentos de admin.
        UserProvisioningMiddleware.InvalidateCache(_cache, user.TenantId, user.ExternalUserId);
        _membership.InvalidateForUser(user.TenantId, user.ExternalUserId);

        foreach (var entry in auditEntries)
            await _audit.RecordAsync(entry, ct);

        var refreshed = await _directory.GetByIdAsync(user.Id, ct);
        var projects = await _membership.GetProjectsForUserAsync(user.Id, ct);
        return Ok(new AdminUserDetailResponse
        {
            Id = refreshed!.Id,
            ExternalUserId = refreshed.ExternalUserId,
            UserType = refreshed.UserType,
            DisplayName = refreshed.DisplayName ?? refreshed.ExternalUserId,
            IsAdmin = refreshed.IsAdmin,
            CreatedAt = refreshed.CreatedAt,
            LastSeenAt = refreshed.LastSeenAt,
            ProjectIds = projects,
        });
    }
}

public sealed class AdminUsersListResponse
{
    public required int Page { get; init; }
    public required int PageSize { get; init; }
    public required int Total { get; init; }
    public required IReadOnlyList<AdminUserSummary> Items { get; init; }
}

public sealed class AdminUserSummary
{
    public required Guid Id { get; init; }
    public required string ExternalUserId { get; init; }
    public required string UserType { get; init; }
    public required string DisplayName { get; init; }
    public required bool IsAdmin { get; init; }
    public required DateTime CreatedAt { get; init; }
    public required DateTime LastSeenAt { get; init; }
    public required int ProjectCount { get; init; }
}

public sealed class AdminUserDetailResponse
{
    public required Guid Id { get; init; }
    public required string ExternalUserId { get; init; }
    public required string UserType { get; init; }
    public required string DisplayName { get; init; }
    public required bool IsAdmin { get; init; }
    public required DateTime CreatedAt { get; init; }
    public required DateTime LastSeenAt { get; init; }
    public required IReadOnlyList<string> ProjectIds { get; init; }
}

public sealed class SetProjectsRequest
{
    public List<string>? ProjectIds { get; set; }
}

public sealed class PatchUserRequest
{
    public bool? IsAdmin { get; set; }
    public string? DisplayName { get; set; }
}
