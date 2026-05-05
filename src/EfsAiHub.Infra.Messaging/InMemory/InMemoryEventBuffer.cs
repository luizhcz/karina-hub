using System.Collections.Concurrent;
using EfsAiHub.Core.Abstractions.Events;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EfsAiHub.Infra.Messaging.InMemory;

/// <summary>
/// Implementação in-memory de <see cref="IEventBuffer"/>. Cada streamKey é um
/// ring buffer (drop-oldest) com counter monotônico, signal de long-poll e
/// flag terminal. Cleanup roda em <see cref="InMemoryEventBufferCleaner"/>.
///
/// <para>
/// Arquitetura escolhida pra refletir a semântica de Redis Streams: troca de
/// implementação é binding único de DI quando virar N pods.
/// </para>
/// </summary>
public sealed class InMemoryEventBuffer : IEventBuffer
{
    private readonly ConcurrentDictionary<string, StreamState> _streams = new(StringComparer.Ordinal);
    private readonly InMemoryEventBufferOptions _options;
    private readonly ILogger<InMemoryEventBuffer> _logger;

    public InMemoryEventBuffer(IOptions<InMemoryEventBufferOptions> options, ILogger<InMemoryEventBuffer> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public Task<long> AppendAsync(string streamKey, BufferEvent ev, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var state = _streams.GetOrAdd(streamKey, _ => new StreamState());
        long seq;
        TaskCompletionSource<bool>? signalToFire;

        lock (state.Lock)
        {
            if (state.Terminal)
            {
                throw new InvalidOperationException($"Stream '{streamKey}' is terminal — append rejected.");
            }

            seq = ++state.NextSeq;
            var record = new BufferedRecord(seq, ev.Type, ev.PayloadJson, ev.OccurredAt);
            state.Records.Add(record);

            // Drop-oldest quando excede MAXLEN — espelha XADD MAXLEN ~ do Redis.
            if (state.Records.Count > _options.MaxLengthPerStream)
            {
                int excess = state.Records.Count - _options.MaxLengthPerStream;
                state.Records.RemoveRange(0, excess);
                Interlocked.Add(ref _overflowCount, excess);
            }

            state.LastActivity = DateTimeOffset.UtcNow;

            signalToFire = state.NewDataSignal;
            state.NewDataSignal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        signalToFire?.TrySetResult(true);
        return Task.FromResult(seq);
    }

    public async Task<BufferPage> ReadSinceAsync(string streamKey, long since, int limit, TimeSpan waitFor, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var state = _streams.GetOrAdd(streamKey, _ => new StreamState());

        var first = SnapshotSince(state, since, limit);
        if (first.Events.Count > 0 || waitFor <= TimeSpan.Zero || first.Terminal)
        {
            return first;
        }

        Task signal;
        lock (state.Lock)
        {
            signal = state.NewDataSignal.Task;
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var delay = Task.Delay(waitFor, cts.Token);
        var winner = await Task.WhenAny(signal, delay).ConfigureAwait(false);

        if (winner == signal)
        {
            cts.Cancel();
        }
        else
        {
            // Long-poll timeout — re-snapshot mesmo assim pra capturar terminal flag eventual.
            try { await delay.ConfigureAwait(false); } catch (OperationCanceledException) { }
        }

        ct.ThrowIfCancellationRequested();
        return SnapshotSince(state, since, limit);
    }

    public Task MarkTerminalAsync(string streamKey, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (!_streams.TryGetValue(streamKey, out var state))
        {
            // MarkTerminal em stream sem appends — cria placeholder vazio terminal pra
            // futuros reads retornarem terminal=true imediatamente.
            state = _streams.GetOrAdd(streamKey, _ => new StreamState());
        }

        TaskCompletionSource<bool>? signalToFire;
        lock (state.Lock)
        {
            if (state.Terminal) return Task.CompletedTask;
            state.Terminal = true;
            state.TerminalAt = DateTimeOffset.UtcNow;
            state.LastActivity = state.TerminalAt.Value;
            signalToFire = state.NewDataSignal;
            state.NewDataSignal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        signalToFire?.TrySetResult(true);
        return Task.CompletedTask;
    }

    private static BufferPage SnapshotSince(StreamState state, long since, int limit)
    {
        if (limit <= 0) limit = 100;

        lock (state.Lock)
        {
            var matches = new List<BufferedRecord>(Math.Min(limit, state.Records.Count));
            long nextSince = since;

            foreach (var rec in state.Records)
            {
                if (rec.Seq <= since) continue;
                matches.Add(rec);
                nextSince = rec.Seq;
                if (matches.Count >= limit) break;
            }

            return new BufferPage(matches, nextSince, state.Terminal);
        }
    }

    // Métricas / introspeção (consumidas pelo cleaner + future telemetry).
    private long _overflowCount;
    public long OverflowCount => Interlocked.Read(ref _overflowCount);
    public int ActiveStreamCount => _streams.Count;

    internal IReadOnlyDictionary<string, StreamState> StreamsForCleanup => _streams;

    internal bool TryRemove(string streamKey, out StreamState? removed)
    {
        var ok = _streams.TryRemove(streamKey, out var state);
        removed = state;
        return ok;
    }

    internal sealed class StreamState
    {
        public readonly object Lock = new();
        public readonly List<BufferedRecord> Records = new();
        public long NextSeq;
        public DateTimeOffset LastActivity = DateTimeOffset.UtcNow;
        public bool Terminal;
        public DateTimeOffset? TerminalAt;
        public TaskCompletionSource<bool> NewDataSignal = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
