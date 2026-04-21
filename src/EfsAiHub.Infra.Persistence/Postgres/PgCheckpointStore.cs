using Microsoft.EntityFrameworkCore;

namespace EfsAiHub.Infra.Persistence.Postgres;

/// <summary>
/// Checkpoint store durável em PostgreSQL.
/// Sobrevive a restarts do processo — workflows interrompidos podem ser retomados.
/// Ativado via WorkflowEngine:CheckpointMode = "Postgres" no appsettings.
/// </summary>
public class PgCheckpointStore(IDbContextFactory<AgentFwDbContext> factory) : ICheckpointStore
{
    public async Task SaveCheckpointAsync(string executionId, byte[] checkpointData, CancellationToken ct = default)
    {
        await using var ctx = await factory.CreateDbContextAsync(ct);
        // Upsert atômico: elimina race entre Find+Add/Update quando duas chamadas concorrentes
        // tentam salvar checkpoint para o mesmo ExecutionId.
        var now = DateTime.UtcNow;
        await ctx.Database.ExecuteSqlInterpolatedAsync($@"
INSERT INTO workflow_checkpoints (""ExecutionId"", ""Data"", ""UpdatedAt"")
VALUES ({executionId}, {checkpointData}, {now})
ON CONFLICT (""ExecutionId"") DO UPDATE
SET ""Data"" = EXCLUDED.""Data"",
    ""UpdatedAt"" = EXCLUDED.""UpdatedAt"";
", ct);
    }

    public async Task<byte[]?> LoadCheckpointAsync(string executionId, CancellationToken ct = default)
    {
        await using var ctx = await factory.CreateDbContextAsync(ct);
        var row = await ctx.WorkflowCheckpoints
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.ExecutionId == executionId, ct);
        return row?.Data;
    }

    public async Task DeleteCheckpointAsync(string executionId, CancellationToken ct = default)
    {
        await using var ctx = await factory.CreateDbContextAsync(ct);
        var row = await ctx.WorkflowCheckpoints.FindAsync([executionId], ct);
        if (row is not null)
        {
            ctx.WorkflowCheckpoints.Remove(row);
            await ctx.SaveChangesAsync(ct);
        }
    }
}
