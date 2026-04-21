using System.Collections.Concurrent;

namespace EfsAiHub.Host.Api.Services;

/// <summary>
/// Gerencia locks por conversa para evitar race conditions em envio concorrente
/// de mensagens (dois requests simultâneos disparando dois workflows).
/// Usa SemaphoreSlim(1,1) por conversationId com eviction automática.
/// </summary>
public sealed class ConversationLockManager
{
    private readonly ConcurrentDictionary<string, LockEntry> _locks = new();
    private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Adquire o lock para a conversa. Retorna um IDisposable que libera o lock ao ser disposed.
    /// Lança TimeoutException se não conseguir adquirir dentro de 10 segundos.
    /// </summary>
    public async Task<IDisposable> AcquireAsync(string conversationId, CancellationToken ct = default)
    {
        var entry = _locks.GetOrAdd(conversationId, _ => new LockEntry());
        Interlocked.Increment(ref entry.RefCount);

        var acquired = await entry.Semaphore.WaitAsync(LockTimeout, ct);
        if (!acquired)
        {
            DecrementAndEvict(conversationId, entry);
            throw new TimeoutException(
                $"Timeout ao adquirir lock para conversa '{conversationId}'. Outra operação está em andamento.");
        }

        return new LockRelease(this, conversationId, entry);
    }

    private void DecrementAndEvict(string conversationId, LockEntry entry)
    {
        if (Interlocked.Decrement(ref entry.RefCount) <= 0)
            _locks.TryRemove(conversationId, out _);
    }

    private sealed class LockEntry
    {
        public readonly SemaphoreSlim Semaphore = new(1, 1);
        public int RefCount;
    }

    private sealed class LockRelease(
        ConversationLockManager manager, string conversationId, LockEntry entry) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            entry.Semaphore.Release();
            manager.DecrementAndEvict(conversationId, entry);
        }
    }
}
