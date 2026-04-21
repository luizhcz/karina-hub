using Microsoft.EntityFrameworkCore;

namespace EfsAiHub.Infra.Persistence.Postgres;

public class PgAtivoRepository : IAtivoRepository
{
    private readonly IDbContextFactory<AgentFwDbContext> _factory;

    public PgAtivoRepository(IDbContextFactory<AgentFwDbContext> factory)
        => _factory = factory;

    public async Task<HashSet<string>> GetAllTickersAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var tickers = await db.Ativos.AsNoTracking()
            .Select(a => a.Ticker)
            .ToListAsync(ct);
        return tickers.ToHashSet();
    }

    public async Task UpsertAsync(Ativo ativo, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var existing = await db.Ativos.FirstOrDefaultAsync(a => a.Ticker == ativo.Ticker, ct);

        if (existing is null)
        {
            ativo.CreatedAt = ativo.UpdatedAt = DateTime.UtcNow;
            db.Ativos.Add(new AtivoRow
            {
                Ticker = ativo.Ticker,
                Nome = ativo.Nome,
                Setor = ativo.Setor,
                Descricao = ativo.Descricao,
                CreatedAt = ativo.CreatedAt,
                UpdatedAt = ativo.UpdatedAt
            });
        }
        else
        {
            existing.Nome = ativo.Nome;
            existing.Setor = ativo.Setor;
            existing.Descricao = ativo.Descricao;
            existing.UpdatedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task<Ativo?> GetByTickerAsync(string ticker, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.Ativos.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Ticker == ticker, ct);

        return row is null ? null : MapToDomain(row);
    }

    public async Task<List<Ativo>> GetOldestUpdatedAsync(int count, CancellationToken ct = default)
    {
        if (count <= 0) return [];

        await using var db = await _factory.CreateDbContextAsync(ct);
        var rows = await db.Ativos.AsNoTracking()
            .OrderBy(a => a.UpdatedAt)
            .Take(count)
            .ToListAsync(ct);

        return rows.Select(MapToDomain).ToList();
    }

    public async Task<Ativo?> GetFirstByPrefixAsync(string prefix, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(prefix) || prefix.Length < 4) return null;

        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.Ativos.AsNoTracking()
            .Where(a => a.Ticker.StartsWith(prefix) && a.Descricao != null && a.Descricao.Length > 50)
            .OrderByDescending(a => a.UpdatedAt)
            .FirstOrDefaultAsync(ct);

        return row is null ? null : MapToDomain(row);
    }

    public async Task<List<Ativo>> GetSiblingsByPrefixAsync(string prefix, string excludeTicker, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(prefix) || prefix.Length < 4) return [];

        await using var db = await _factory.CreateDbContextAsync(ct);
        var rows = await db.Ativos.AsNoTracking()
            .Where(a => a.Ticker.StartsWith(prefix) && a.Ticker != excludeTicker)
            .ToListAsync(ct);

        return rows.Select(MapToDomain).ToList();
    }

    private static Ativo MapToDomain(AtivoRow row) => new()
    {
        Ticker = row.Ticker,
        Nome = row.Nome,
        Setor = row.Setor,
        Descricao = row.Descricao,
        CreatedAt = row.CreatedAt,
        UpdatedAt = row.UpdatedAt
    };
}
