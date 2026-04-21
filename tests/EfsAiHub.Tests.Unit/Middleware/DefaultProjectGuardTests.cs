using EfsAiHub.Host.Api.Services;
using EfsAiHub.Host.Api.Middleware;
using EfsAiHub.Host.Api.Configuration;
using EfsAiHub.Core.Abstractions.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace EfsAiHub.Tests.Unit.Middleware;

[Trait("Category", "Unit")]
public class DefaultProjectGuardTests
{
    private static readonly UserIdentityResolver _resolver = new();

    private static DefaultProjectGuard Build(params string[] accountIds)
    {
        var options = Options.Create(new AdminOptions { AccountIds = [..accountIds] });
        return new DefaultProjectGuard(_ => Task.CompletedTask, options, _resolver);
    }

    private static (HttpContext ctx, IProjectContextAccessor accessor) CreateContext(
        string projectId,
        string? accountHeader = null,
        string? path = null)
    {
        var ctx = new DefaultHttpContext();
        ctx.Response.Body = new System.IO.MemoryStream();
        if (accountHeader is not null)
            ctx.Request.Headers["x-efs-account"] = accountHeader;
        if (path is not null)
            ctx.Request.Path = path;

        var accessor = Substitute.For<IProjectContextAccessor>();
        accessor.Current.Returns(new ProjectContext(projectId));

        return (ctx, accessor);
    }

    // ── Gate desabilitado ─────────────────────────────────────────────────────

    [Fact]
    public async Task GateDesabilitado_ListaVazia_PassaTudo()
    {
        var mw = Build(); // empty list → gate disabled
        var (ctx, accessor) = CreateContext("default");

        await mw.InvokeAsync(ctx, accessor);

        ctx.Response.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task GateDesabilitado_ListaVazia_ProjetoDefault_PassaSemAccount()
    {
        var mw = Build();
        var (ctx, accessor) = CreateContext("default"); // no account header

        await mw.InvokeAsync(ctx, accessor);

        ctx.Response.StatusCode.Should().Be(200);
    }

    // ── Projeto não-default ───────────────────────────────────────────────────

    [Fact]
    public async Task ProjetoNaoDefault_ComGateAtivo_PassaSemVerificarAccount()
    {
        var mw = Build("admin-123");
        var (ctx, accessor) = CreateContext("meu-projeto");

        await mw.InvokeAsync(ctx, accessor);

        ctx.Response.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task ProjetoNaoDefault_SemAccountHeader_Passa()
    {
        var mw = Build("admin-123");
        var (ctx, accessor) = CreateContext("projeto-cliente-a");

        await mw.InvokeAsync(ctx, accessor);

        ctx.Response.StatusCode.Should().Be(200);
    }

    // ── Projeto "default" bloqueado ───────────────────────────────────────────

    [Fact]
    public async Task ProjetoDefault_SemHeader_Retorna403()
    {
        var mw = Build("admin-123");
        var (ctx, accessor) = CreateContext("default");

        await mw.InvokeAsync(ctx, accessor);

        ctx.Response.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task ProjetoDefault_AccountNaoAdmin_Retorna403()
    {
        var mw = Build("admin-123");
        var (ctx, accessor) = CreateContext("default", accountHeader: "usuario-comum");

        await mw.InvokeAsync(ctx, accessor);

        ctx.Response.StatusCode.Should().Be(403);
    }

    // ── Admin acessa projeto "default" ────────────────────────────────────────

    [Fact]
    public async Task ProjetoDefault_AccountAdmin_Passa()
    {
        var mw = Build("admin-123");
        var (ctx, accessor) = CreateContext("default", accountHeader: "admin-123");

        await mw.InvokeAsync(ctx, accessor);

        ctx.Response.StatusCode.Should().Be(200);
    }

    // ── Múltiplos admins ──────────────────────────────────────────────────────

    [Fact]
    public async Task MultiplosAdmins_QualquerDeles_Passa()
    {
        var mw = Build("admin-a", "admin-b", "admin-c");
        var (ctx, accessor) = CreateContext("default", accountHeader: "admin-b");

        await mw.InvokeAsync(ctx, accessor);

        ctx.Response.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task MultiplosAdmins_AccountForaDaLista_Retorna403()
    {
        var mw = Build("admin-a", "admin-b");
        var (ctx, accessor) = CreateContext("default", accountHeader: "admin-c");

        await mw.InvokeAsync(ctx, accessor);

        ctx.Response.StatusCode.Should().Be(403);
    }

    // ── Rotas globais (isentas do guard) ────────────────────────────────────

    [Theory]
    [InlineData("/api/agents")]
    [InlineData("/api/agents/my-agent")]
    [InlineData("/api/agents/my-agent/prompts/active")]
    [InlineData("/api/workflows")]
    [InlineData("/api/workflows/wf-1")]
    [InlineData("/api/chat/ag-ui/stream")]
    [InlineData("/dev")]
    public async Task RotaGlobal_ProjetoDefault_SemAdmin_Passa(string path)
    {
        var mw = Build("admin-123");
        var (ctx, accessor) = CreateContext("default", path: path);

        await mw.InvokeAsync(ctx, accessor);

        ctx.Response.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task RotaNaoGlobal_ProjetoDefault_SemAdmin_Retorna403()
    {
        var mw = Build("admin-123");
        var (ctx, accessor) = CreateContext("default", path: "/api/projects");

        await mw.InvokeAsync(ctx, accessor);

        ctx.Response.StatusCode.Should().Be(403);
    }
}
