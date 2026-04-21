using EfsAiHub.Host.Api.Services;
using EfsAiHub.Host.Api.Middleware;
using EfsAiHub.Host.Api.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace EfsAiHub.Tests.Unit.Middleware;

[Trait("Category", "Unit")]
public class AdminGateMiddlewareTests
{
    private static readonly UserIdentityResolver _resolver = new();

    private static AdminGateMiddleware Build(params string[] accountIds)
    {
        var options = Options.Create(new AdminOptions { AccountIds = [..accountIds] });
        return new AdminGateMiddleware(_ => Task.CompletedTask, options, _resolver);
    }

    private static HttpContext CreateContext(string method, string path, string? accountHeader = null)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Method = method;
        ctx.Request.Path = path;
        if (accountHeader is not null)
            ctx.Request.Headers["x-efs-account"] = accountHeader;
        ctx.Response.Body = new System.IO.MemoryStream();
        return ctx;
    }

    [Fact]
    public async Task GateDesabilitado_ListaVazia_PassaTudo()
    {
        var mw = Build(); // empty list → disabled
        var ctx = CreateContext("GET", "/api/admin/secret");

        await mw.InvokeAsync(ctx);

        ctx.Response.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task RotaPublica_AgUi_PassaSemVerificarAccount()
    {
        var mw = Build("admin-123");
        var ctx = CreateContext("POST", "/api/chat/ag-ui/stream");

        await mw.InvokeAsync(ctx);

        ctx.Response.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task RotaPublica_PostWorkflows_PassaSemAccount()
    {
        var mw = Build("admin-123");
        var ctx = CreateContext("POST", "/api/workflows");

        await mw.InvokeAsync(ctx);

        ctx.Response.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task RotaPublica_PutWorkflow_PassaSemAccount()
    {
        var mw = Build("admin-123");
        var ctx = CreateContext("PUT", "/api/workflows/wf-abc");

        await mw.InvokeAsync(ctx);

        ctx.Response.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task RotaProtegida_SemHeader_Retorna403()
    {
        var mw = Build("admin-123");
        var ctx = CreateContext("GET", "/api/agents");

        await mw.InvokeAsync(ctx);

        ctx.Response.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task RotaProtegida_HeaderCorreto_Passa()
    {
        var mw = Build("admin-123");
        var ctx = CreateContext("GET", "/api/agents", accountHeader: "admin-123");

        await mw.InvokeAsync(ctx);

        ctx.Response.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task RotaProtegida_HeaderErrado_Retorna403()
    {
        var mw = Build("admin-123");
        var ctx = CreateContext("DELETE", "/api/agents/agent-1", accountHeader: "outro-account");

        await mw.InvokeAsync(ctx);

        ctx.Response.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task RotaPublica_Conversations_PassaSemAccount()
    {
        var mw = Build("admin-123");
        var ctx = CreateContext("GET", "/api/conversations/conv-1");

        await mw.InvokeAsync(ctx);

        ctx.Response.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task RotaPublica_Projects_Get_PassaSemAccount()
    {
        var mw = Build("admin-123");
        var ctx = CreateContext("GET", "/api/projects");

        await mw.InvokeAsync(ctx);

        ctx.Response.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task PutWorkflowComSubpath_NaoEPublico()
    {
        // PUT /api/workflows/{id}/rollback tem mais segmentos → não é rota pública de edição
        var mw = Build("admin-123");
        var ctx = CreateContext("PUT", "/api/workflows/wf-1/rollback");

        await mw.InvokeAsync(ctx);

        ctx.Response.StatusCode.Should().Be(403);
    }

    // ── Múltiplos admins ──────────────────────────────────────────────────────

    [Fact]
    public async Task MultiplosAdmins_QualquerDeles_Passa()
    {
        var mw = Build("admin-a", "admin-b", "admin-c");
        var ctx = CreateContext("GET", "/api/agents", accountHeader: "admin-b");

        await mw.InvokeAsync(ctx);

        ctx.Response.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task MultiplosAdmins_AccountForaDaLista_Retorna403()
    {
        var mw = Build("admin-a", "admin-b");
        var ctx = CreateContext("GET", "/api/agents", accountHeader: "admin-c");

        await mw.InvokeAsync(ctx);

        ctx.Response.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task UmAdmin_OutroAccount_Retorna403()
    {
        var mw = Build("admin-only");
        var ctx = CreateContext("DELETE", "/api/workflows/wf-1", accountHeader: "not-admin");

        await mw.InvokeAsync(ctx);

        ctx.Response.StatusCode.Should().Be(403);
    }
}
