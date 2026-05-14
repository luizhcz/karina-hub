using EfsAiHub.Core.Abstractions.Identity;
using EfsAiHub.Core.Abstractions.Users;
using EfsAiHub.Host.Api.Middleware;
using Microsoft.AspNetCore.Http;

namespace EfsAiHub.Tests.Unit.Middleware;

[Trait("Category", "Unit")]
public class ProjectMiddlewareAclTests
{
    private static readonly ProjectMiddleware _middleware = new(_ => Task.CompletedTask);

    private static (
        HttpContext ctx,
        IProjectContextAccessor projectAccessor,
        IUserContextAccessor userAccessor,
        ITenantContextAccessor tenantAccessor,
        IUserMembershipService membership)
        Setup(string? projectIdHeader, User? currentUser = null)
    {
        var ctx = new DefaultHttpContext();
        ctx.Response.Body = new System.IO.MemoryStream();
        if (projectIdHeader is not null)
            ctx.Request.Headers["x-project-id"] = projectIdHeader;

        // FakeProjectContextAccessor captura writes de Current e devolve no read —
        // o middleware faz set-then-read, então um Substitute puro de propriedade
        // mockada perderia o valor entre as duas chamadas.
        var project = new FakeProjectContextAccessor();

        var user = Substitute.For<IUserContextAccessor>();
        user.Current.Returns(currentUser);

        var tenant = Substitute.For<ITenantContextAccessor>();
        tenant.Current.Returns(new TenantContext("default"));

        var membership = Substitute.For<IUserMembershipService>();
        return (ctx, project, user, tenant, membership);
    }

    private static User AdminUser() => new()
    {
        Id = Guid.NewGuid(),
        ExternalUserId = "admin-1",
        UserType = "admin",
        TenantId = "default",
        IsAdmin = true,
        CreatedAt = DateTime.UtcNow,
        LastSeenAt = DateTime.UtcNow,
    };

    private static User NonAdminUser() => new()
    {
        Id = Guid.NewGuid(),
        ExternalUserId = "cliente-1",
        UserType = "cliente",
        TenantId = "default",
        IsAdmin = false,
        CreatedAt = DateTime.UtcNow,
        LastSeenAt = DateTime.UtcNow,
    };

    [Fact]
    public async Task SemUsuario_NaoAplicaAcl_Passa()
    {
        var (ctx, project, user, tenant, mem) = Setup(projectIdHeader: "proj-x");

        await _middleware.InvokeAsync(ctx, project, user, tenant, mem);

        ctx.Response.StatusCode.Should().Be(200);
        await mem.DidNotReceiveWithAnyArgs().IsAuthorizedAsync(default!, default!, default!);
    }

    [Fact]
    public async Task Admin_NaoAplicaAcl_Passa()
    {
        var (ctx, project, user, tenant, mem) = Setup(projectIdHeader: "proj-x", currentUser: AdminUser());

        await _middleware.InvokeAsync(ctx, project, user, tenant, mem);

        ctx.Response.StatusCode.Should().Be(200);
        await mem.DidNotReceiveWithAnyArgs().IsAuthorizedAsync(default!, default!, default!);
    }

    [Fact]
    public async Task NonAdmin_ProjetoDefault_NaoAplicaAcl_Passa()
    {
        // Default é delegado pro DefaultProjectGuard — ACL aqui não roda.
        var (ctx, project, user, tenant, mem) = Setup(projectIdHeader: null, currentUser: NonAdminUser());

        await _middleware.InvokeAsync(ctx, project, user, tenant, mem);

        ctx.Response.StatusCode.Should().Be(200);
        await mem.DidNotReceiveWithAnyArgs().IsAuthorizedAsync(default!, default!, default!);
    }

    [Fact]
    public async Task NonAdmin_ProjetoVinculado_Passa()
    {
        var nonAdmin = NonAdminUser();
        var (ctx, project, user, tenant, mem) = Setup(projectIdHeader: "proj-x", currentUser: nonAdmin);
        mem.IsAuthorizedAsync(nonAdmin.ExternalUserId, "default", "proj-x", Arg.Any<CancellationToken>())
            .Returns(true);

        await _middleware.InvokeAsync(ctx, project, user, tenant, mem);

        ctx.Response.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task NonAdmin_ProjetoNaoVinculado_Retorna403()
    {
        var nonAdmin = NonAdminUser();
        var (ctx, project, user, tenant, mem) = Setup(projectIdHeader: "proj-x", currentUser: nonAdmin);
        mem.IsAuthorizedAsync(nonAdmin.ExternalUserId, "default", "proj-x", Arg.Any<CancellationToken>())
            .Returns(false);

        await _middleware.InvokeAsync(ctx, project, user, tenant, mem);

        ctx.Response.StatusCode.Should().Be(403);
    }

    private sealed class FakeProjectContextAccessor : IProjectContextAccessor
    {
        public ProjectContext Current { get; set; } = ProjectContext.Default;
    }
}
