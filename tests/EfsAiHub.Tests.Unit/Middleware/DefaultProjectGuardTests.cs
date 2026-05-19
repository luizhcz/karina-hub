using EfsAiHub.Core.Abstractions.Identity;
using EfsAiHub.Core.Abstractions.Users;
using EfsAiHub.Host.Api.Configuration;
using EfsAiHub.Host.Api.Middleware;
using EfsAiHub.Host.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace EfsAiHub.Tests.Unit.Middleware;

[Trait("Category", "Unit")]
public class DefaultProjectGuardTests
{
    private static readonly UserIdentityResolver _resolver = new();

    private static DefaultProjectGuard Build(bool gateEnabled = true)
    {
        var options = Options.Create(new AdminOptions { GateEnabled = gateEnabled });
        return new DefaultProjectGuard(_ => Task.CompletedTask, options, _resolver);
    }

    private static (
        HttpContext ctx,
        IProjectContextAccessor projectAccessor,
        IUserContextAccessor userAccessor,
        ITenantContextAccessor tenantAccessor,
        IUserDirectory directory,
        IAdminPermissionEvaluator evaluator)
        Setup(string projectId, User? currentUser = null, string? accountHeader = null, string? path = null)
    {
        var ctx = new DefaultHttpContext();
        ctx.Response.Body = new System.IO.MemoryStream();
        if (accountHeader is not null)
            ctx.Request.Headers["x-efs-account"] = accountHeader;
        if (path is not null)
            ctx.Request.Path = path;

        var project = Substitute.For<IProjectContextAccessor>();
        project.Current.Returns(new ProjectContext(projectId));

        var user = Substitute.For<IUserContextAccessor>();
        user.Current.Returns(currentUser);

        var tenant = Substitute.For<ITenantContextAccessor>();
        tenant.Current.Returns(new TenantContext("default"));

        var directory = Substitute.For<IUserDirectory>();

        var evaluator = Substitute.For<IAdminPermissionEvaluator>();
        evaluator.IsAdmin(Arg.Any<IReadOnlyList<string>?>()).Returns(false);

        return (ctx, project, user, tenant, directory, evaluator);
    }

    private static User AdminUser(string externalId = "admin-1") => new()
    {
        Id = Guid.NewGuid(),
        ExternalUserId = externalId,
        UserType = "admin",
        TenantId = "default",
        IsAdmin = true,
        CreatedAt = DateTime.UtcNow,
        LastSeenAt = DateTime.UtcNow,
    };

    private static User NonAdminUser(string externalId = "user-1") => new()
    {
        Id = Guid.NewGuid(),
        ExternalUserId = externalId,
        UserType = "cliente",
        TenantId = "default",
        IsAdmin = false,
        CreatedAt = DateTime.UtcNow,
        LastSeenAt = DateTime.UtcNow,
    };

    [Fact]
    public async Task GateDesabilitado_PassaTudo()
    {
        var mw = Build(gateEnabled: false);
        var (ctx, project, user, tenant, dir, evaluator) = Setup("default");

        await mw.InvokeAsync(ctx, project, user, tenant, dir, evaluator);

        ctx.Response.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task ProjetoNaoDefault_Passa()
    {
        var mw = Build();
        var (ctx, project, user, tenant, dir, evaluator) = Setup("meu-projeto");

        await mw.InvokeAsync(ctx, project, user, tenant, dir, evaluator);

        ctx.Response.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task ProjetoNaoDefault_SemUsuario_Passa()
    {
        var mw = Build();
        var (ctx, project, user, tenant, dir, evaluator) = Setup("projeto-cliente-a");

        await mw.InvokeAsync(ctx, project, user, tenant, dir, evaluator);

        ctx.Response.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task ProjetoDefault_SemUsuario_Retorna403()
    {
        var mw = Build();
        var (ctx, project, user, tenant, dir, evaluator) = Setup("default");

        await mw.InvokeAsync(ctx, project, user, tenant, dir, evaluator);

        ctx.Response.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task ProjetoDefault_NaoAdmin_Retorna403()
    {
        var mw = Build();
        var (ctx, project, user, tenant, dir, evaluator) = Setup("default", currentUser: NonAdminUser());

        await mw.InvokeAsync(ctx, project, user, tenant, dir, evaluator);

        ctx.Response.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task ProjetoDefault_Admin_Passa()
    {
        var mw = Build();
        var (ctx, project, user, tenant, dir, evaluator) = Setup("default", currentUser: AdminUser());

        await mw.InvokeAsync(ctx, project, user, tenant, dir, evaluator);

        ctx.Response.StatusCode.Should().Be(200);
    }

    [Theory]
    [InlineData("/api/aihub/agents")]
    [InlineData("/api/aihub/agents/my-agent")]
    [InlineData("/api/aihub/agents/my-agent/prompts/active")]
    [InlineData("/api/aihub/workflows")]
    [InlineData("/api/aihub/workflows/wf-1")]
    [InlineData("/api/aihub/chat/ag-ui/stream")]
    [InlineData("/dev")]
    public async Task RotaGlobal_ProjetoDefault_SemAdmin_Passa(string path)
    {
        var mw = Build();
        var (ctx, project, user, tenant, dir, evaluator) = Setup("default", path: path);

        await mw.InvokeAsync(ctx, project, user, tenant, dir, evaluator);

        ctx.Response.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task SseRoute_FallbackQuery_AdminViaDirectory_Passa()
    {
        // EventSource não envia headers customizados; middleware faz fallback
        // pra query param + evaluator + sanity check no diretório.
        var mw = Build();
        var (ctx, project, user, tenant, dir, evaluator) = Setup("default", path: "/api/aihub/executions/exec-1/stream");
        ctx.Request.QueryString = new QueryString("?account=admin-sse&permissions=efs.admin");

        evaluator.IsAdmin(Arg.Is<IReadOnlyList<string>>(p => p.Contains("efs.admin"))).Returns(true);
        dir.GetByExternalIdAsync("admin-sse", "default", Arg.Any<CancellationToken>())
            .Returns(AdminUser("admin-sse"));

        await mw.InvokeAsync(ctx, project, user, tenant, dir, evaluator);

        ctx.Response.StatusCode.Should().Be(200);
    }
}
