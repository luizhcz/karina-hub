using EfsAiHub.Core.Abstractions.Identity;
using EfsAiHub.Core.Abstractions.Users;
using EfsAiHub.Host.Api.Configuration;
using EfsAiHub.Host.Api.Middleware;
using EfsAiHub.Host.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace EfsAiHub.Tests.Unit.Middleware;

[Trait("Category", "Unit")]
public class AdminGateMiddlewareTests
{
    private static readonly UserIdentityResolver _resolver = new();

    private static AdminGateMiddleware Build(bool gateEnabled = true)
    {
        var options = Options.Create(new AdminOptions { GateEnabled = gateEnabled });
        return new AdminGateMiddleware(_ => Task.CompletedTask, _resolver, options);
    }

    private static (HttpContext ctx, IUserContextAccessor accessor, IUserDirectory dir, ITenantContextAccessor tenant) Setup(
        string method,
        string path,
        User? currentUser = null,
        string? accountHeader = null,
        string tenantId = "default")
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Method = method;
        ctx.Request.Path = path;
        if (accountHeader is not null)
            ctx.Request.Headers["x-efs-account"] = accountHeader;
        ctx.Response.Body = new System.IO.MemoryStream();

        var accessor = Substitute.For<IUserContextAccessor>();
        accessor.Current.Returns(currentUser);

        var directory = Substitute.For<IUserDirectory>();

        var tenant = Substitute.For<ITenantContextAccessor>();
        tenant.Current.Returns(new TenantContext(tenantId));

        return (ctx, accessor, directory, tenant);
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
        var (ctx, accessor, dir, tenant) = Setup("GET", "/api/aihub/admin/secret");

        await mw.InvokeAsync(ctx, accessor, dir, tenant);

        ctx.Response.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task RotaPublica_AgUi_PassaSemUsuario()
    {
        var mw = Build();
        var (ctx, accessor, dir, tenant) = Setup("POST", "/api/aihub/chat/ag-ui/stream");

        await mw.InvokeAsync(ctx, accessor, dir, tenant);

        ctx.Response.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task PostWorkflows_AdminOnly_BloqueiaNaoAdmin()
    {
        var mw = Build();
        var (ctx, accessor, dir, tenant) = Setup("POST", "/api/aihub/workflows", currentUser: NonAdminUser());

        await mw.InvokeAsync(ctx, accessor, dir, tenant);

        ctx.Response.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task PutWorkflow_AdminOnly_BloqueiaNaoAdmin()
    {
        var mw = Build();
        var (ctx, accessor, dir, tenant) = Setup("PUT", "/api/aihub/workflows/wf-abc", currentUser: NonAdminUser());

        await mw.InvokeAsync(ctx, accessor, dir, tenant);

        ctx.Response.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task RotaProtegida_SemUsuario_Retorna403()
    {
        var mw = Build();
        var (ctx, accessor, dir, tenant) = Setup("GET", "/api/aihub/agents/agent-1/sandbox-sessions");

        await mw.InvokeAsync(ctx, accessor, dir, tenant);

        ctx.Response.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task RotaProtegida_AdminUser_Passa()
    {
        var mw = Build();
        var (ctx, accessor, dir, tenant) = Setup(
            "GET",
            "/api/aihub/agents/agent-1/sandbox-sessions",
            currentUser: AdminUser());

        await mw.InvokeAsync(ctx, accessor, dir, tenant);

        ctx.Response.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task RotaProtegida_NaoAdmin_Retorna403()
    {
        var mw = Build();
        var (ctx, accessor, dir, tenant) = Setup(
            "DELETE",
            "/api/aihub/agents/agent-1",
            currentUser: NonAdminUser());

        await mw.InvokeAsync(ctx, accessor, dir, tenant);

        ctx.Response.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task RotaPublica_Conversations_PassaSemUsuario()
    {
        var mw = Build();
        var (ctx, accessor, dir, tenant) = Setup("GET", "/api/aihub/conversations/conv-1");

        await mw.InvokeAsync(ctx, accessor, dir, tenant);

        ctx.Response.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task RotaPublica_Projects_Get_PassaSemUsuario()
    {
        var mw = Build();
        var (ctx, accessor, dir, tenant) = Setup("GET", "/api/aihub/projects");

        await mw.InvokeAsync(ctx, accessor, dir, tenant);

        ctx.Response.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task PutWorkflowComSubpath_NaoEPublico_Bloqueia()
    {
        // PUT /api/aihub/workflows/{id}/rollback tem mais segmentos — não casa a whitelist.
        var mw = Build();
        var (ctx, accessor, dir, tenant) = Setup(
            "PUT",
            "/api/aihub/workflows/wf-1/rollback",
            currentUser: NonAdminUser());

        await mw.InvokeAsync(ctx, accessor, dir, tenant);

        ctx.Response.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task SseRoute_FallbackQuery_AdminViaDirectory_Passa()
    {
        // SSE: EventSource não envia headers customizados, então userAccessor.Current
        // fica null. Middleware faz fallback: lê identidade via query param e consulta
        // diretório direto pra ver se é admin.
        var mw = Build();
        var (ctx, accessor, dir, tenant) = Setup(
            "GET",
            "/api/aihub/executions/exec-1/stream");
        ctx.Request.QueryString = new QueryString("?account=admin-sse");

        dir.GetByExternalIdAsync("admin-sse", "default", Arg.Any<CancellationToken>())
            .Returns(AdminUser("admin-sse"));

        await mw.InvokeAsync(ctx, accessor, dir, tenant);

        ctx.Response.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task SseRoute_FallbackQuery_NaoAdmin_Retorna403()
    {
        var mw = Build();
        var (ctx, accessor, dir, tenant) = Setup(
            "GET",
            "/api/aihub/executions/exec-1/stream");
        ctx.Request.QueryString = new QueryString("?account=cliente-sse");

        dir.GetByExternalIdAsync("cliente-sse", "default", Arg.Any<CancellationToken>())
            .Returns(NonAdminUser("cliente-sse"));

        await mw.InvokeAsync(ctx, accessor, dir, tenant);

        ctx.Response.StatusCode.Should().Be(403);
    }
}
