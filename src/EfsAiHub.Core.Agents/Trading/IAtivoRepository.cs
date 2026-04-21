
namespace EfsAiHub.Core.Agents.Trading;

public interface IAtivoRepository
{
    Task<HashSet<string>> GetAllTickersAsync(CancellationToken ct = default);
    Task UpsertAsync(Ativo ativo, CancellationToken ct = default);
    Task<Ativo?> GetByTickerAsync(string ticker, CancellationToken ct = default);
    Task<List<Ativo>> GetOldestUpdatedAsync(int count, CancellationToken ct = default);
    Task<Ativo?> GetFirstByPrefixAsync(string prefix, CancellationToken ct = default);
    Task<List<Ativo>> GetSiblingsByPrefixAsync(string prefix, string excludeTicker, CancellationToken ct = default);
}
