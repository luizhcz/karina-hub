using EfsAiHub.Infra.Persistence.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EfsAiHub.Host.Worker.Services;

/// <summary>
/// Fix #A5 parte B — job diário que dropa partições antigas das tabelas de telemetria
/// e deleta checkpoints órfãos. Assume o esquema particionado mensal do script
/// <c>sprint3_partition_telemetry.sql</c> (workflow_event_audit, tool_invocations,
/// llm_token_usage). Idempotente: consultas em pg_inherits/pg_class.
///
/// Tabelas não-particionadas (workflow_checkpoints) usam DELETE por timestamp.
/// </summary>
public sealed class AuditRetentionService : BackgroundService
{
    private static readonly TimeSpan RunInterval = TimeSpan.FromHours(24);

    private readonly IDbContextFactory<AgentFwDbContext> _factory;
    private readonly WorkflowEngineOptions _options;
    private readonly ILogger<AuditRetentionService> _logger;

    public AuditRetentionService(
        IDbContextFactory<AgentFwDbContext> factory,
        IOptions<WorkflowEngineOptions> options,
        ILogger<AuditRetentionService> logger)
    {
        _factory = factory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Jitter de 60s para não rodar junto com o startup de outros serviços
        try { await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken); }
        catch (OperationCanceledException) { return; }

        using var timer = new PeriodicTimer(RunInterval);
        do
        {
            try { await RunOnceAsync(stoppingToken); }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[AuditRetention] Falha na execução periódica.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);

        var auditCutoff = DateTime.UtcNow.AddDays(-Math.Max(1, _options.AuditRetentionDays));
        var toolCutoff = DateTime.UtcNow.AddDays(-Math.Max(1, _options.ToolInvocationRetentionDays));
        var ckptCutoff = DateTime.UtcNow.AddDays(-Math.Max(1, _options.CheckpointRetentionDays));

        _logger.LogInformation(
            "[AuditRetention] auditCutoff={Audit:o} toolCutoff={Tool:o} ckptCutoff={Ckpt:o}",
            auditCutoff, toolCutoff, ckptCutoff);

        // 1) Drop de partições mensais antigas (nome sufixo YYYY_MM).
        await DropOldPartitionsAsync(ctx, "workflow_event_audit", auditCutoff, ct);
        await DropOldPartitionsAsync(ctx, "tool_invocations", toolCutoff, ct);
        await DropOldPartitionsAsync(ctx, "llm_token_usage", toolCutoff, ct);

        // 2) Orphan cleanup: linhas de observabilidade sem execução correspondente
        await CleanupOrphanedObservabilityAsync(ctx, toolCutoff, ct);

        // 3) Checkpoints órfãos (tabela não-particionada; DELETE por UpdatedAt).
        try
        {
            var deleted = await ctx.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM workflow_checkpoints WHERE \"UpdatedAt\" < {ckptCutoff}", ct);
            if (deleted > 0)
                _logger.LogInformation("[AuditRetention] {Deleted} checkpoints órfãos removidos.", deleted);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[AuditRetention] DELETE de workflow_checkpoints falhou.");
        }

        // 4) Admin audit log — tabela não-particionada; DELETE batched por Timestamp
        // para evitar lock escalation quando volume é alto. Usa AuditRetentionDays
        // (mesmo TTL de workflow_event_audit).
        try
        {
            int totalDeleted = 0, batch;
            do
            {
                batch = await ctx.Database.ExecuteSqlInterpolatedAsync(
                    $@"DELETE FROM aihub.admin_audit_log WHERE ctid IN (
                        SELECT ctid FROM aihub.admin_audit_log
                        WHERE ""Timestamp"" < {auditCutoff}
                        LIMIT 1000)", ct);
                totalDeleted += batch;
                if (batch > 0) await Task.Delay(200, ct);
            }
            while (batch == 1000 && !ct.IsCancellationRequested);

            if (totalDeleted > 0)
                _logger.LogInformation("[AuditRetention] {Count} linhas removidas de admin_audit_log.", totalDeleted);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[AuditRetention] DELETE de admin_audit_log falhou.");
        }
    }

    /// <summary>
    /// Remove linhas de observabilidade (tool_invocations, llm_token_usage) cujas execuções
    /// já foram removidas. Batched delete com ctid para evitar lock escalation.
    /// </summary>
    private async Task CleanupOrphanedObservabilityAsync(AgentFwDbContext ctx, DateTime cutoff, CancellationToken ct)
    {
        var targets = new[]
        {
            (Table: "tool_invocations", ExecutionCol: "\"ExecutionId\"", DateCol: "\"CreatedAt\""),
            (Table: "llm_token_usage", ExecutionCol: "\"ExecutionId\"", DateCol: "\"CreatedAt\"")
        };

        foreach (var (table, execCol, dateCol) in targets)
        {
            try
            {
                int totalDeleted = 0, batch;
                do
                {
                    var sql = $"DELETE FROM aihub.{table} WHERE ctid IN (" +
                        $"SELECT t.ctid FROM aihub.{table} t " +
                        $"WHERE t.{dateCol} < {{0}} " +
                        $"AND NOT EXISTS (SELECT 1 FROM aihub.workflow_executions e WHERE e.\"ExecutionId\" = t.{execCol}) " +
                        "LIMIT 1000)";
                    batch = await ctx.Database.ExecuteSqlRawAsync(
                        sql,
                        parameters: new object[] { cutoff },
                        cancellationToken: ct);
                    totalDeleted += batch;
                    if (batch > 0) await Task.Delay(200, ct);
                }
                while (batch == 1000 && !ct.IsCancellationRequested);

                if (totalDeleted > 0)
                    _logger.LogInformation("[AuditRetention] Removidos {Count} orphans de {Table}.", totalDeleted, table);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[AuditRetention] Falha ao limpar orphans de '{Table}'.", table);
            }
        }
    }

    /// <summary>
    /// Identifica partições filhas de <paramref name="parent"/> cujo range superior seja
    /// inferior ao <paramref name="cutoff"/> e as dropa. Usa pg_inherits + pg_get_expr
    /// para ler o FOR VALUES FROM (...) TO (...) e extrair o TO date.
    /// </summary>
    private async Task DropOldPartitionsAsync(AgentFwDbContext ctx, string parent, DateTime cutoff, CancellationToken ct)
    {
        try
        {
            // Descobre as partições filhas e seus ranges
            var conn = ctx.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open)
                await conn.OpenAsync(ct);

            var partitions = new List<(string Name, DateTime? UpperBound)>();
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
SELECT c.relname, pg_get_expr(c.relpartbound, c.oid)
FROM pg_inherits i
JOIN pg_class p ON p.oid = i.inhparent
JOIN pg_class c ON c.oid = i.inhrelid
WHERE p.relname = @parent;";
                var p = cmd.CreateParameter();
                p.ParameterName = "parent";
                p.Value = parent;
                cmd.Parameters.Add(p);

                await using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    var name = reader.GetString(0);
                    var expr = reader.IsDBNull(1) ? null : reader.GetString(1);
                    partitions.Add((name, ParseUpperBound(expr)));
                }
            }

            foreach (var (name, upper) in partitions)
            {
                if (upper is null) continue;
                if (upper.Value > cutoff) continue;

                // Sanitiza o nome vindo do catálogo: só letras, dígitos e underscore.
                if (!System.Text.RegularExpressions.Regex.IsMatch(name, "^[A-Za-z0-9_]+$"))
                {
                    _logger.LogWarning("[AuditRetention] Nome de partição '{Partition}' suspeito — ignorando.", name);
                    continue;
                }

                try
                {
#pragma warning disable EF1002
                    await ctx.Database.ExecuteSqlRawAsync($"DROP TABLE IF EXISTS \"{name}\" CASCADE", ct);
#pragma warning restore EF1002
                    _logger.LogInformation("[AuditRetention] Partição '{Partition}' dropada (upper={Upper:o}, cutoff={Cutoff:o}).",
                        name, upper.Value, cutoff);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[AuditRetention] Falha ao dropar partição '{Partition}'.", name);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[AuditRetention] Falha ao listar partições de '{Parent}'.", parent);
        }
    }

    /// <summary>
    /// Extrai a data superior de uma expressão tipo
    /// <c>FOR VALUES FROM ('2026-03-01') TO ('2026-04-01')</c>.
    /// Retorna null se não parsear.
    /// </summary>
    internal static DateTime? ParseUpperBound(string? expr)
    {
        if (string.IsNullOrEmpty(expr)) return null;
        const string marker = "TO (";
        var idx = expr.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;
        var start = expr.IndexOf('\'', idx);
        if (start < 0) return null;
        var end = expr.IndexOf('\'', start + 1);
        if (end < 0) return null;
        var s = expr.Substring(start + 1, end - start - 1);
        return DateTime.TryParse(s, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
            out var dt) ? dt : null;
    }
}
