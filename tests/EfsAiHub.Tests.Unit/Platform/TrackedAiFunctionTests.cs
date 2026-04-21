using System.Threading.Channels;
using EfsAiHub.Core.Abstractions.Observability;
using EfsAiHub.Platform.Runtime.Factories;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace EfsAiHub.Tests.Unit.Platform;

[Trait("Category", "Unit")]
public class TrackedAiFunctionTests
{
    private static (TrackedAIFunction tracked, Channel<ToolInvocation> channel) Build(
        AIFunction inner,
        string agentId = "agent-1")
    {
        var channel = Channel.CreateUnbounded<ToolInvocation>();
        var tracked = new TrackedAIFunction(
            inner,
            agentId,
            channel.Writer,
            NullLogger<TrackedAIFunction>.Instance);
        return (tracked, channel);
    }

    // ── Metadata delegation ───────────────────────────────────────────────────

    [Fact]
    public void Name_DelegaParaInner()
    {
        var inner = AIFunctionFactory.Create(
            () => "result",
            new AIFunctionFactoryOptions { Name = "minha-funcao" });

        var (tracked, _) = Build(inner);

        tracked.Name.Should().Be("minha-funcao");
    }

    [Fact]
    public void Description_DelegaParaInner()
    {
        var inner = AIFunctionFactory.Create(
            () => "result",
            new AIFunctionFactoryOptions { Name = "f", Description = "Minha descrição" });

        var (tracked, _) = Build(inner);

        tracked.Description.Should().Be("Minha descrição");
    }

    // ── Invocação bem-sucedida ────────────────────────────────────────────────

    [Fact]
    public async Task InvokeAsync_Sucesso_RetornaResultadoDoInner()
    {
        var inner = AIFunctionFactory.Create(
            () => "resposta-ok",
            new AIFunctionFactoryOptions { Name = "test-func" });

        var (tracked, _) = Build(inner);

        var result = await tracked.InvokeAsync(new AIFunctionArguments());

        result?.ToString().Should().Be("resposta-ok");
    }

    [Fact]
    public async Task InvokeAsync_Sucesso_NaoEnfileiraSemExecutionId()
    {
        // Without ExecutionContext (DelegateExecutor.Current.Value is null), no invocation is persisted
        var inner = AIFunctionFactory.Create(
            () => "ok",
            new AIFunctionFactoryOptions { Name = "func" });

        var (tracked, channel) = Build(inner);

        await tracked.InvokeAsync(new AIFunctionArguments());

        // No executionId available → PersistInvocation short-circuits
        channel.Reader.TryRead(out _).Should().BeFalse();
    }

    // ── Invocação com falha ───────────────────────────────────────────────────

    [Fact]
    public async Task InvokeAsync_InnerLancaException_RetornaFriendlyError()
    {
        var inner = AIFunctionFactory.Create(
            (string param) => { throw new InvalidOperationException("API indisponível"); return ""; },
            new AIFunctionFactoryOptions { Name = "fail-func" });

        var (tracked, _) = Build(inner);

        var result = await tracked.InvokeAsync(new AIFunctionArguments { ["param"] = "x" });

        result?.ToString().Should().Contain("Tool Error");
        result?.ToString().Should().Contain("fail-func");
    }

    [Fact]
    public async Task InvokeAsync_InnerLancaException_NaoRelancaException()
    {
        var inner = AIFunctionFactory.Create(
            (string param) => { throw new InvalidOperationException("DB error"); return ""; },
            new AIFunctionFactoryOptions { Name = "db-func" });

        var (tracked, _) = Build(inner);

        var act = async () => await tracked.InvokeAsync(new AIFunctionArguments { ["param"] = "x" });

        // TrackedAIFunction catches exception and returns friendly error string
        await act.Should().NotThrowAsync();
    }

    // ── JsonSchema delegation ─────────────────────────────────────────────────

    [Fact]
    public void JsonSchema_DelegaParaInner()
    {
        var inner = AIFunctionFactory.Create(
            (string nome) => nome,
            new AIFunctionFactoryOptions { Name = "schema-func" });

        var (tracked, _) = Build(inner);

        // Schema should reflect the 'nome' parameter
        tracked.JsonSchema.ValueKind.Should().NotBe(System.Text.Json.JsonValueKind.Undefined);
    }
}
