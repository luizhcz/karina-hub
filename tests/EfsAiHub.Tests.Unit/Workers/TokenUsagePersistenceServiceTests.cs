using EfsAiHub.Platform.Runtime.Interfaces;
using EfsAiHub.Core.Abstractions.Observability;
using EfsAiHub.Host.Worker.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace EfsAiHub.Tests.Unit.Workers;

[Trait("Category", "Unit")]
public class TokenUsagePersistenceServiceTests
{
    private static (TokenUsagePersistenceService service, ILlmTokenUsageRepository repo) Build()
    {
        var repo = Substitute.For<ILlmTokenUsageRepository>();
        var sp = Substitute.For<IServiceProvider>();
        sp.GetService(typeof(ILlmTokenUsageRepository)).Returns(repo);

        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(sp);

        var factory = Substitute.For<IServiceScopeFactory>();
        factory.CreateScope().Returns(scope);

        var service = new TokenUsagePersistenceService(
            factory,
            NullLogger<TokenUsagePersistenceService>.Instance);

        return (service, repo);
    }

    private static LlmTokenUsage MakeUsage(string agentId = "agent-1", int tokens = 100) =>
        new() { AgentId = agentId, ModelId = "gpt-4o", TotalTokens = tokens, InputTokens = 70, OutputTokens = 30 };

    // ── Processamento unitário ─────────────────────────────────────────────────

    [Fact]
    public async Task Enqueue_UmItem_ChamaAppendAsync()
    {
        var (service, repo) = Build();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        _ = service.StartAsync(cts.Token);
        service.Enqueue(MakeUsage());

        await Task.Delay(200);
        cts.Cancel();

        await repo.Received(1).AppendAsync(Arg.Any<LlmTokenUsage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Enqueue_ItemComDados_AgentIdPreservado()
    {
        var (service, repo) = Build();
        LlmTokenUsage? captured = null;
        repo.AppendAsync(Arg.Do<LlmTokenUsage>(u => captured = u), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        _ = service.StartAsync(cts.Token);
        service.Enqueue(MakeUsage("agent-boleta", 250));

        await Task.Delay(200);
        cts.Cancel();

        captured.Should().NotBeNull();
        captured!.AgentId.Should().Be("agent-boleta");
        captured.TotalTokens.Should().Be(250);
    }

    // ── Batch ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Enqueue_MultiplosItens_PersisteTodos()
    {
        var (service, repo) = Build();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        _ = service.StartAsync(cts.Token);

        for (var i = 0; i < 8; i++)
            service.Enqueue(MakeUsage($"agent-{i}"));

        await Task.Delay(500);
        cts.Cancel();

        await repo.Received(8).AppendAsync(Arg.Any<LlmTokenUsage>(), Arg.Any<CancellationToken>());
    }

    // ── Resiliência ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Enqueue_ErroNoRepo_ServicoContinuaAtivo()
    {
        // When a batch throws, the service must not crash — it logs and continues.
        var (service, repo) = Build();

        repo.AppendAsync(Arg.Any<LlmTokenUsage>(), Arg.Any<CancellationToken>())
            .Returns(_ => { throw new InvalidOperationException("DB unavailable"); });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        _ = service.StartAsync(cts.Token);

        // Enqueue and wait — service should not throw or stop even on repeated failures
        service.Enqueue(MakeUsage("fail-1"));
        service.Enqueue(MakeUsage("fail-2"));

        await Task.Delay(300);

        // Service is still running (not faulted)
        service.ExecuteTask?.IsCompleted.Should().BeFalse();
        cts.Cancel();
    }

    // ── ITokenUsageSink interface ─────────────────────────────────────────────

    [Fact]
    public void ITokenUsageSink_Writer_NaoNulo()
    {
        var (service, _) = Build();
        ITokenUsageSink sink = service;

        sink.Writer.Should().NotBeNull();
    }

    // ── Shutdown ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task StopAsync_SemItens_EncerraLimpo()
    {
        var (service, _) = Build();
        using var cts = new CancellationTokenSource();

        await service.StartAsync(cts.Token);
        cts.Cancel();

        var stop = async () => await service.StopAsync(CancellationToken.None);
        await stop.Should().NotThrowAsync();
    }
}
