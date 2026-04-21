using System.Runtime.CompilerServices;
using System.Text.Json;
using EfsAiHub.Core.Orchestration.Executors;
using EfsAiHub.Platform.Runtime.Middlewares;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using AiChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace EfsAiHub.Tests.Unit.Guards;

[Trait("Category", "Unit")]
public class AccountGuardChatClientTests
{
    private static AccountGuardChatClient BuildSut(string responseText, string? accountPattern = null)
    {
        var settings = accountPattern is not null
            ? new Dictionary<string, string> { ["accountPattern"] = accountPattern }
            : null;

        return new AccountGuardChatClient(
            new StubChatClient(responseText),
            "test-agent",
            settings,
            NullLogger<AccountGuardChatClientTests>.Instance);
    }

    private static IEnumerable<AiChatMessage> WithSystem(string account) =>
    [
        new AiChatMessage(ChatRole.System, $"conta: {account}"),
        new AiChatMessage(ChatRole.User, "consultar"),
    ];

    private static IEnumerable<AiChatMessage> WithoutAccount() =>
    [
        new AiChatMessage(ChatRole.User, "consultar"),
    ];

    // ── GetResponseAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetResponseAsync_ContaCorreta_NaoSubstitui()
    {
        var sut = BuildSut("Sua conta 12345 possui saldo.");
        var response = await sut.GetResponseAsync(WithSystem("12345"));
        response.Text.Should().Contain("12345");
    }

    [Fact]
    public async Task GetResponseAsync_ContaErradaMesmoComprimento_Substitui()
    {
        var sut = BuildSut("Sua conta 99999 possui saldo.");
        var response = await sut.GetResponseAsync(WithSystem("12345"));
        response.Text.Should().Contain("12345");
        response.Text.Should().NotContain("99999");
    }

    [Fact]
    public async Task GetResponseAsync_ComprimentoMuitoDiferente_NaoSubstitui()
    {
        // 1234567890 (10 dígitos) vs 12345 (5 dígitos) → diferença > 1 → não substitui
        var sut = BuildSut("Saldo R$ 1234567890 reais.");
        var response = await sut.GetResponseAsync(WithSystem("12345"));
        response.Text.Should().Contain("1234567890");
    }

    [Fact]
    public async Task GetResponseAsync_SemConta_RespostaInalterada()
    {
        var sut = BuildSut("Conta 99999 consultada.");
        var response = await sut.GetResponseAsync(WithoutAccount());
        response.Text.Should().Contain("99999");
    }

    [Fact]
    public async Task GetResponseAsync_PatternConta_ExtraiConta()
    {
        var sut = BuildSut("Resultado para conta 77777.");
        var msgs = new AiChatMessage[]
        {
            new(ChatRole.System, "conta: 12345 autorizado"),
            new(ChatRole.User, "consultar"),
        };
        var response = await sut.GetResponseAsync(msgs);
        response.Text.Should().Contain("12345");
        response.Text.Should().NotContain("77777");
    }

    [Fact]
    public async Task GetResponseAsync_PatternAccount_ExtraiConta()
    {
        var sut = BuildSut("Resultado para conta 77777.");
        var msgs = new AiChatMessage[]
        {
            new(ChatRole.System, "account: 12345"),
            new(ChatRole.User, "consultar"),
        };
        var response = await sut.GetResponseAsync(msgs);
        response.Text.Should().Contain("12345");
        response.Text.Should().NotContain("77777");
    }

    // ── GetStreamingResponseAsync ─────────────────────────────────────────────

    [Fact]
    public async Task GetStreamingResponseAsync_ContaErrada_Substitui()
    {
        var sut = BuildSut("Conta 99999 ok");
        var chunks = new List<string>();
        await foreach (var update in sut.GetStreamingResponseAsync(WithSystem("12345")))
            if (update.Text is not null) chunks.Add(update.Text);

        chunks.Should().Contain(t => t.Contains("12345"));
        chunks.Should().NotContain(t => t.Contains("99999"));
    }

    [Fact]
    public async Task GetStreamingResponseAsync_ContaCorreta_NaoModifica()
    {
        var sut = BuildSut("Conta 12345 ok");
        var chunks = new List<string>();
        await foreach (var update in sut.GetStreamingResponseAsync(WithSystem("12345")))
            if (update.Text is not null) chunks.Add(update.Text);

        chunks.Should().Contain(t => t.Contains("12345"));
    }
}

internal sealed class StubChatClient : IChatClient
{
    private readonly string _text;
    public StubChatClient(string text) => _text = text;
    public ChatClientMetadata Metadata => new("stub", null, null);

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<AiChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
        => Task.FromResult(new ChatResponse(
            [new AiChatMessage(ChatRole.Assistant, _text)]));

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<AiChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return new ChatResponseUpdate(ChatRole.Assistant, _text);
        await Task.CompletedTask;
    }

    public object? GetService(Type serviceType, object? key = null) => null;
    public void Dispose() { }
}
