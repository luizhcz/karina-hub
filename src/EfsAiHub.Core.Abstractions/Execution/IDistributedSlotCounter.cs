namespace EfsAiHub.Core.Abstractions.Execution;

/// <summary>
/// Contador distribuído de slots de concorrência via Redis.
/// Permite back-pressure cross-pod: quando N pods compartilham um limite global,
/// TryAcquireAsync garante que o total entre todos os pods não exceda maxSlots.
/// TTL de segurança auto-libera slots se o pod morrer sem Release.
/// </summary>
public interface IDistributedSlotCounter
{
    /// <summary>
    /// Tenta adquirir um slot no scope indicado.
    /// Retorna true se adquirido (total &lt; maxSlots), false se esgotado.
    /// O slot expira automaticamente após <paramref name="ttl"/> se não for liberado.
    /// </summary>
    Task<bool> TryAcquireAsync(string scope, int maxSlots, TimeSpan ttl);

    /// <summary>Libera um slot previamente adquirido.</summary>
    Task ReleaseAsync(string scope);

    /// <summary>Retorna a contagem atual de slots ativos para o scope.</summary>
    Task<int> GetActiveCountAsync(string scope);
}
