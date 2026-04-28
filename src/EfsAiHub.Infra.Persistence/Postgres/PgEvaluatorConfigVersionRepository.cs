using System.Text.Json;
using EfsAiHub.Core.Abstractions.Persistence;
using EfsAiHub.Core.Agents.Evaluation;
using Microsoft.EntityFrameworkCore;

namespace EfsAiHub.Infra.Persistence.Postgres;

/// <summary>
/// Append-only de versions de EvaluatorConfig. Idempotência por ContentHash:
/// última revision com mesmo hash é no-op.
/// </summary>
public sealed class PgEvaluatorConfigVersionRepository : IEvaluatorConfigVersionRepository
{
    private readonly IDbContextFactory<AgentFwDbContext> _factory;

    public PgEvaluatorConfigVersionRepository(IDbContextFactory<AgentFwDbContext> factory)
    {
        _factory = factory;
    }

    public async Task<EvaluatorConfigVersion?> GetByIdAsync(string evaluatorConfigVersionId, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var row = await ctx.EvaluatorConfigVersions.FindAsync([evaluatorConfigVersionId], ct);
        return row is null ? null : ToDomain(row);
    }

    public async Task<EvaluatorConfigVersion?> GetCurrentAsync(string evaluatorConfigId, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var row = await ctx.EvaluatorConfigVersions
            .Where(r => r.EvaluatorConfigId == evaluatorConfigId && r.Status == "Published")
            .OrderByDescending(r => r.Revision)
            .FirstOrDefaultAsync(ct);
        return row is null ? null : ToDomain(row);
    }

    public async Task<IReadOnlyList<EvaluatorConfigVersion>> ListByConfigAsync(string evaluatorConfigId, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var rows = await ctx.EvaluatorConfigVersions
            .Where(r => r.EvaluatorConfigId == evaluatorConfigId)
            .OrderByDescending(r => r.Revision)
            .ToListAsync(ct);
        return rows.Select(ToDomain).ToList();
    }

    public async Task<int> GetNextRevisionAsync(string evaluatorConfigId, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var max = await ctx.EvaluatorConfigVersions
            .Where(r => r.EvaluatorConfigId == evaluatorConfigId)
            .Select(r => (int?)r.Revision)
            .MaxAsync(ct);
        return (max ?? 0) + 1;
    }

    public async Task<EvaluatorConfigVersion> AppendAsync(EvaluatorConfigVersion version, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);

        var last = await ctx.EvaluatorConfigVersions
            .Where(r => r.EvaluatorConfigId == version.EvaluatorConfigId)
            .OrderByDescending(r => r.Revision)
            .FirstOrDefaultAsync(ct);
        if (last is not null && last.ContentHash == version.ContentHash)
            return ToDomain(last);

        ctx.EvaluatorConfigVersions.Add(new EvaluatorConfigVersionRow
        {
            EvaluatorConfigVersionId = version.EvaluatorConfigVersionId,
            EvaluatorConfigId = version.EvaluatorConfigId,
            Revision = version.Revision,
            Status = version.Status.ToString(),
            ContentHash = version.ContentHash,
            Bindings = SerializeBindings(version.Bindings),
            Splitter = version.Splitter.ToString(),
            NumRepetitions = version.NumRepetitions,
            CreatedAt = version.CreatedAt,
            CreatedBy = version.CreatedBy,
            ChangeReason = version.ChangeReason
        });
        await ctx.SaveChangesAsync(ct);
        return version;
    }

    public async Task SetStatusAsync(string evaluatorConfigVersionId, EvaluatorConfigVersionStatus status, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var row = await ctx.EvaluatorConfigVersions.FindAsync([evaluatorConfigVersionId], ct)
            ?? throw new InvalidOperationException($"EvaluatorConfigVersion '{evaluatorConfigVersionId}' não encontrada.");
        row.Status = status.ToString();
        await ctx.SaveChangesAsync(ct);
    }

    private static string SerializeBindings(IReadOnlyList<EvaluatorBinding> bindings)
    {
        var rows = bindings.Select(b => new BindingDto(
            b.Kind.ToString(),
            b.Name,
            b.Params?.RootElement.GetRawText(),
            b.Enabled,
            b.Weight,
            b.BindingIndex));
        return JsonSerializer.Serialize(rows, JsonDefaults.Domain);
    }

    private static IReadOnlyList<EvaluatorBinding> DeserializeBindings(string bindingsJson)
    {
        if (string.IsNullOrEmpty(bindingsJson)) return Array.Empty<EvaluatorBinding>();
        var dtos = JsonSerializer.Deserialize<List<BindingDto>>(bindingsJson, JsonDefaults.Domain)
                   ?? new List<BindingDto>();
        return dtos.Select(d => new EvaluatorBinding(
            Kind: Enum.TryParse<EvaluatorKind>(d.Kind, true, out var k) ? k : EvaluatorKind.Local,
            Name: d.Name,
            Params: string.IsNullOrEmpty(d.Params) ? null : JsonDocument.Parse(d.Params),
            Enabled: d.Enabled,
            Weight: d.Weight,
            BindingIndex: d.BindingIndex)).ToList();
    }

    private static EvaluatorConfigVersion ToDomain(EvaluatorConfigVersionRow row)
    {
        var status = Enum.TryParse<EvaluatorConfigVersionStatus>(row.Status, out var s) ? s : EvaluatorConfigVersionStatus.Draft;
        var splitter = Enum.TryParse<SplitterStrategy>(row.Splitter, out var sp) ? sp : SplitterStrategy.LastTurn;
        return new EvaluatorConfigVersion(
            EvaluatorConfigVersionId: row.EvaluatorConfigVersionId,
            EvaluatorConfigId: row.EvaluatorConfigId,
            Revision: row.Revision,
            Status: status,
            ContentHash: row.ContentHash,
            Bindings: DeserializeBindings(row.Bindings),
            Splitter: splitter,
            NumRepetitions: row.NumRepetitions,
            CreatedAt: row.CreatedAt,
            CreatedBy: row.CreatedBy,
            ChangeReason: row.ChangeReason);
    }

    private sealed record BindingDto(string Kind, string Name, string? Params, bool Enabled, double Weight, int BindingIndex);
}
