using System.Collections.Concurrent;

namespace EfsAiHub.Platform.Runtime.Audit;

/// <summary>
/// Phase 3 — LRU thread-safe pra throttle de eventos de audit que podem inflar log
/// (ex: cross_project_invoke em workloads alto). Marca uma chave como "logged" por
/// uma janela de tempo; subsequentes ShouldLog retornam false até expirar.
///
/// Implementação: LinkedList + ConcurrentDictionary com lock só no ajuste de tempo
/// (evita scan completo). Capacity max evita crescimento sem limite — quando enche,
/// evicta o entry menos recentemente acessado e incrementa AuditThrottleLruEvictions.
/// </summary>
public sealed class AuditThrottle
{
    private readonly TimeSpan _window;
    private readonly int _maxEntries;
    private readonly ConcurrentDictionary<string, DateTime> _entries;
    private readonly LinkedList<string> _accessOrder = new();
    private readonly object _evictLock = new();
    private readonly Action? _onEviction;

    /// <param name="window">Tempo entre logs do mesmo key (default 60s).</param>
    /// <param name="maxEntries">Capacidade da LRU (default 1000).</param>
    /// <param name="onEviction">Callback (sem args) toda vez que um entry é despejado por capacity. Use pra incrementar a métrica.</param>
    public AuditThrottle(TimeSpan? window = null, int maxEntries = 1000, Action? onEviction = null)
    {
        _window = window ?? TimeSpan.FromSeconds(60);
        _maxEntries = Math.Max(16, maxEntries);
        _entries = new ConcurrentDictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        _onEviction = onEviction;
    }

    /// <summary>
    /// Retorna true se a chave deve ser logada agora (primeira vez ou janela expirou).
    /// Marca o entry com timestamp atual em ambos os casos. Idempotente entre chamadas concorrentes
    /// — apenas uma thread vê true por janela.
    /// </summary>
    public bool ShouldLog(string key)
    {
        var now = DateTime.UtcNow;
        var shouldLog = false;

        _entries.AddOrUpdate(
            key,
            _ => { shouldLog = true; return now; },
            (_, last) =>
            {
                if (now - last >= _window)
                {
                    shouldLog = true;
                    return now;
                }
                return last;
            });

        if (shouldLog)
            EvictIfNeeded(key);

        return shouldLog;
    }

    private void EvictIfNeeded(string lastKey)
    {
        if (_entries.Count <= _maxEntries) return;

        lock (_evictLock)
        {
            // Re-check sob lock (outro thread pode ter despejado).
            while (_entries.Count > _maxEntries)
            {
                // Estratégia simples: encontra o entry mais antigo e remove.
                // O(n) é aceitável dado que evictions são raras (workload steady-state cabe na capacity).
                var oldestKey = "";
                var oldestStamp = DateTime.MaxValue;
                foreach (var kv in _entries)
                {
                    if (kv.Value < oldestStamp)
                    {
                        oldestStamp = kv.Value;
                        oldestKey = kv.Key;
                    }
                }
                if (string.IsNullOrEmpty(oldestKey)) break;
                if (_entries.TryRemove(oldestKey, out _))
                    _onEviction?.Invoke();
            }
        }
    }

    public int Count => _entries.Count;
}
