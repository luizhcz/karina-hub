using System.Text.Json;
using EfsAiHub.Core.Orchestration.Workflows;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace EfsAiHub.Infra.Persistence.Postgres;

/// <summary>
/// Append-only store de snapshots imutáveis de workflow.
/// Idempotência por <see cref="WorkflowVersion.ContentHash"/>: se a última revision já carrega
/// o mesmo hash, retorna-a em vez de criar uma duplicata.
///
/// O campo Snapshot armazena a WorkflowDefinition serializada para rollback determinístico.
/// Padrão idêntico ao PgAgentVersionRepository.
/// </summary>
public sealed class PgWorkflowVersionRepository : IWorkflowVersionRepository
{
    private readonly IDbContextFactory<AgentFwDbContext> _factory;

    public PgWorkflowVersionRepository(IDbContextFactory<AgentFwDbContext> factory)
    {
        _factory = factory;
    }

    public async Task<WorkflowVersion?> GetByIdAsync(string workflowVersionId, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var row = await ctx.WorkflowVersions.FindAsync([workflowVersionId], ct);
        return row is null ? null : ToVersion(row);
    }

    public async Task<WorkflowVersion?> GetCurrentAsync(string workflowDefinitionId, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var row = await ctx.WorkflowVersions
            .Where(r => r.WorkflowDefinitionId == workflowDefinitionId && r.Status == "Published")
            .OrderByDescending(r => r.Revision)
            .FirstOrDefaultAsync(ct);
        return row is null ? null : ToVersion(row);
    }

    public async Task<IReadOnlyList<WorkflowVersion>> ListByDefinitionAsync(
        string workflowDefinitionId, CancellationToken ct = default)
    {
        try
        {
            await using var ctx = await _factory.CreateDbContextAsync(ct);
            var rows = await ctx.WorkflowVersions
                .Where(r => r.WorkflowDefinitionId == workflowDefinitionId)
                .OrderByDescending(r => r.Revision)
                .ToListAsync(ct);
            return rows.Select(ToVersion).ToList();
        }
        catch (PostgresException ex) when (ex.SqlState == "42P01") // undefined_table
        {
            return Array.Empty<WorkflowVersion>();
        }
    }

    public async Task<int> GetNextRevisionAsync(string workflowDefinitionId, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var max = await ctx.WorkflowVersions
            .Where(r => r.WorkflowDefinitionId == workflowDefinitionId)
            .Select(r => (int?)r.Revision)
            .MaxAsync(ct);
        return (max ?? 0) + 1;
    }

    public async Task<WorkflowVersion> AppendAsync(WorkflowVersion version, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);

        // Idempotência por hash: se a última revision já carrega esse content hash, no-op.
        var last = await ctx.WorkflowVersions
            .Where(r => r.WorkflowDefinitionId == version.WorkflowDefinitionId)
            .OrderByDescending(r => r.Revision)
            .FirstOrDefaultAsync(ct);

        if (last is not null && last.ContentHash == version.ContentHash)
            return ToVersion(last);

        var row = new WorkflowVersionRow
        {
            WorkflowVersionId = version.WorkflowVersionId,
            WorkflowDefinitionId = version.WorkflowDefinitionId,
            Revision = version.Revision,
            CreatedAt = version.CreatedAt,
            CreatedBy = version.CreatedBy,
            ChangeReason = version.ChangeReason,
            Status = version.Status.ToString(),
            ContentHash = version.ContentHash,
            Snapshot = version.DefinitionSnapshot ?? "{}"
        };

        ctx.WorkflowVersions.Add(row);
        await ctx.SaveChangesAsync(ct);
        return version;
    }

    /// <summary>Reconstrói WorkflowDefinition a partir do snapshot JSONB armazenado.</summary>
    public async Task<WorkflowDefinition?> GetDefinitionSnapshotAsync(
        string workflowVersionId, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var row = await ctx.WorkflowVersions.FindAsync([workflowVersionId], ct);
        if (row is null) return null;
        return JsonSerializer.Deserialize<WorkflowDefinition>(row.Snapshot, JsonDefaults.Domain);
    }

    private static WorkflowVersion ToVersion(WorkflowVersionRow row)
    {
        return new WorkflowVersion(
            WorkflowVersionId: row.WorkflowVersionId,
            WorkflowDefinitionId: row.WorkflowDefinitionId,
            Revision: row.Revision,
            CreatedAt: row.CreatedAt,
            CreatedBy: row.CreatedBy,
            ChangeReason: row.ChangeReason,
            Status: Enum.TryParse<WorkflowVersionStatus>(row.Status, out var s) ? s : WorkflowVersionStatus.Published,
            ContentHash: row.ContentHash);
    }
}
