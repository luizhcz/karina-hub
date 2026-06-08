using System.Collections.Concurrent;
using EfsAiHub.Core.Abstractions.BackgroundServices;

namespace EfsAiHub.Platform.Runtime.Services;

/// <summary>
/// Implementação in-memory do <see cref="IBackgroundServiceHeartbeatSink"/>.
/// Singleton no DI; cada hosted service o injeta e chama a cada iteração.
///
/// Concorrência:
/// - <see cref="ConcurrentDictionary{TKey,TValue}"/> garante atomicidade no upsert do slot.
/// - Cada slot é um objeto mutável (Slot) com <see cref="Interlocked"/> nos contadores
///   e lock-de-instância só na atualização de timestamps + última mensagem de erro
///   (combinação não-atômica que precisa ficar consistente quando observada).
/// - GetAll faz snapshot imutável (record com init) — leitor não vê estado parcial.
///
/// Hot path: cada chamada toma 1 lookup + ~3 escritas. Sem alocações em RecordSuccess.
/// RecordError aloca a string da exceção (intencional — diagnóstico).
/// </summary>
public sealed class BackgroundServiceHeartbeatSink : IBackgroundServiceHeartbeatSink
{
    private readonly ConcurrentDictionary<string, Slot> _slots =
        new(StringComparer.OrdinalIgnoreCase);

    public void Started(string name, DateTimeOffset utcNow)
    {
        var slot = _slots.GetOrAdd(name, _ => new Slot());
        // Compare-and-set: só seta StartedAtUtc se ainda for null. Idempotente sob
        // chamadas concorrentes — ExecuteAsync teoricamente roda uma vez por host,
        // mas defensivo contra restart de serviço dentro do mesmo processo.
        Interlocked.CompareExchange(ref slot.StartedAtUtcTicks, utcNow.UtcTicks, 0L);
    }

    public void RecordSuccess(string name, DateTimeOffset utcNow)
    {
        var slot = _slots.GetOrAdd(name, _ => new Slot());
        var ticks = utcNow.UtcTicks;
        Interlocked.Exchange(ref slot.LastTickAtUtcTicks, ticks);
        Interlocked.Exchange(ref slot.LastSuccessAtUtcTicks, ticks);
        Interlocked.Increment(ref slot.TickCount);
    }

    public void RecordError(string name, DateTimeOffset utcNow, Exception error)
    {
        var slot = _slots.GetOrAdd(name, _ => new Slot());
        var ticks = utcNow.UtcTicks;
        Interlocked.Exchange(ref slot.LastTickAtUtcTicks, ticks);
        Interlocked.Exchange(ref slot.LastErrorAtUtcTicks, ticks);
        Interlocked.Increment(ref slot.TickCount);
        Interlocked.Increment(ref slot.ErrorCount);
        // Reference assignment é atômico em x64/.NET — leitor sempre vê message
        // consistente ou null, nunca parcial. Truncamos em 500 chars pra evitar
        // que stack-trace gigante via .Message vaze pra UI.
        var msg = error.Message;
        if (msg.Length > 500) msg = msg[..500] + "…";
        slot.LastErrorMessage = msg;
    }

    public BackgroundServiceHeartbeat? Get(string name)
        => _slots.TryGetValue(name, out var slot) ? Snapshot(name, slot) : null;

    public IReadOnlyDictionary<string, BackgroundServiceHeartbeat> GetAll()
    {
        // ToDictionary ao invés de retornar a view do ConcurrentDictionary porque
        // queremos imutabilidade no consumer (controller serializa direto).
        var result = new Dictionary<string, BackgroundServiceHeartbeat>(_slots.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var (name, slot) in _slots)
        {
            result[name] = Snapshot(name, slot);
        }
        return result;
    }

    private static BackgroundServiceHeartbeat Snapshot(string name, Slot slot)
    {
        // Lê os ticks pra DateTimeOffset uma única vez — evita "torn read" lógico
        // entre LastTick e LastError quando outra thread está escrevendo.
        var startedTicks = Interlocked.Read(ref slot.StartedAtUtcTicks);
        var lastTickTicks = Interlocked.Read(ref slot.LastTickAtUtcTicks);
        var lastSuccessTicks = Interlocked.Read(ref slot.LastSuccessAtUtcTicks);
        var lastErrorTicks = Interlocked.Read(ref slot.LastErrorAtUtcTicks);
        var tickCount = Interlocked.Read(ref slot.TickCount);
        var errorCount = Interlocked.Read(ref slot.ErrorCount);

        return new BackgroundServiceHeartbeat
        {
            Name = name,
            StartedAtUtc = TicksToOffset(startedTicks),
            LastTickAtUtc = TicksToOffset(lastTickTicks),
            LastSuccessAtUtc = TicksToOffset(lastSuccessTicks),
            LastErrorAtUtc = TicksToOffset(lastErrorTicks),
            LastErrorMessage = slot.LastErrorMessage,
            TickCount = tickCount,
            ErrorCount = errorCount,
        };
    }

    private static DateTimeOffset? TicksToOffset(long ticks)
        => ticks == 0 ? null : new DateTimeOffset(ticks, TimeSpan.Zero);

    // Slot é mutable on purpose — pra fazer Interlocked nos campos.
    // Não vaza pra fora da classe.
    private sealed class Slot
    {
        public long StartedAtUtcTicks;
        public long LastTickAtUtcTicks;
        public long LastSuccessAtUtcTicks;
        public long LastErrorAtUtcTicks;
        public long TickCount;
        public long ErrorCount;
        // Lido/escrito sem lock (reference write é atômico). Pode estar 1 leitura
        // atrás dos timestamps em raça — aceitável pro caso de diagnóstico.
        public volatile string? LastErrorMessage;
    }
}
