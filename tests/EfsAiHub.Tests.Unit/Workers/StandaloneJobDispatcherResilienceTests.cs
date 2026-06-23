using EfsAiHub.Core.Abstractions.BackgroundServices;
using EfsAiHub.Core.Abstractions.Execution;
using EfsAiHub.Core.Agents.Responses;
using EfsAiHub.Host.Worker.Services;
using EfsAiHub.Host.Worker.Services.Handlers;
using EfsAiHub.Platform.Runtime.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EfsAiHub.Tests.Unit.Workers;

/// <summary>
/// Blindagem do host: o <see cref="StandaloneJobDispatcherService.ExecuteAsync"/>
/// NUNCA pode deixar uma exceção escapar — com o default BackgroundService
/// (StopHost) isso derrubaria a aplicação inteira.
/// </summary>
public sealed class StandaloneJobDispatcherResilienceTests
{
    [Fact]
    public async Task Dependencia_que_lanca_no_loop_e_capturada_e_nao_faulta_o_servico()
    {
        var slots = Substitute.For<IDistributedSlotCounter>();
        slots.GetActiveCountAsync(Arg.Any<string>())
            .Returns<int>(_ => throw new InvalidOperationException("boom no poll (ex.: Redis fora)"));
        var heartbeat = Substitute.For<IBackgroundServiceHeartbeatSink>();

        var svc = Build(slots, heartbeat, NullLogger<StandaloneJobDispatcherService>.Instance);

        await svc.StartAsync(CancellationToken.None);
        await Task.Delay(200);
        await svc.StopAsync(CancellationToken.None);

        svc.ExecuteTask.Should().NotBeNull();
        svc.ExecuteTask!.IsFaulted.Should().BeFalse("ExecuteAsync não pode faultar — derrubaria o host");
        heartbeat.Received().RecordError(Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<Exception>());
    }

    [Fact]
    public async Task Logger_que_lanca_nao_propaga_do_ExecuteAsync()
    {
        // Sem a proteção do log (try em volta do LogError + guard externo), um logger
        // que explode dentro do tratamento de erro escaparia → host derrubado.
        var slots = Substitute.For<IDistributedSlotCounter>();
        slots.GetActiveCountAsync(Arg.Any<string>())
            .Returns<int>(_ => throw new InvalidOperationException("boom"));
        var heartbeat = Substitute.For<IBackgroundServiceHeartbeatSink>();

        var svc = Build(slots, heartbeat, new ThrowingLogger());

        await svc.StartAsync(CancellationToken.None);
        await Task.Delay(150);
        await svc.StopAsync(CancellationToken.None);

        svc.ExecuteTask.Should().NotBeNull();
        svc.ExecuteTask!.IsFaulted.Should().BeFalse("nem um logger que lança pode faultar o ExecuteAsync");
    }

    private static StandaloneJobDispatcherService Build(
        IDistributedSlotCounter slots,
        IBackgroundServiceHeartbeatSink heartbeat,
        ILogger<StandaloneJobDispatcherService> logger) =>
        new(
            Substitute.For<IBackgroundResponseRepository>(),
            slots,
            Array.Empty<IStandaloneJobHandler>(),
            Options.Create(new StandalonePoolsOptions
            {
                Enabled = true,
                PollIdleSeconds = 1,
                BatchSize = 5,
                GlobalConcurrency = 10,
            }),
            heartbeat,
            logger);

    private sealed class ThrowingLogger : ILogger<StandaloneJobDispatcherService>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
            => throw new InvalidOperationException("logger explodiu");
    }
}
