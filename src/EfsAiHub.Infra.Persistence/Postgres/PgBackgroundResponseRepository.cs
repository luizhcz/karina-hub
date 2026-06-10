using System.Text.Json;
using EfsAiHub.Core.Agents.Responses;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace EfsAiHub.Infra.Persistence.Postgres;

/// <summary>
/// Persistência de <see cref="BackgroundResponseJob"/>. Lê/escreve via EF pra
/// CRUD simples; usa Npgsql direto pros caminhos que dependem de
/// <c>FOR UPDATE SKIP LOCKED</c> (lease distribuído cross-pod) — EF não expõe
/// row locks de forma confiável.
///
/// Mutadores em jobs "Running" (Complete/Fail/UpdateStep) checam ownership
/// (LeasedBy = @podId AND Status = 'Running') no WHERE como defense-in-depth
/// contra dupla execução quando o reaper rouba o lease.
/// </summary>
public sealed class PgBackgroundResponseRepository : IBackgroundResponseRepository
{
    private readonly IDbContextFactory<AgentFwDbContext> _factory;
    private readonly NpgsqlDataSource _dataSource;
    private readonly ILogger<PgBackgroundResponseRepository> _logger;

    public PgBackgroundResponseRepository(
        IDbContextFactory<AgentFwDbContext> factory,
        [FromKeyedServices("general")] NpgsqlDataSource dataSource,
        ILogger<PgBackgroundResponseRepository> logger)
    {
        _factory = factory;
        _dataSource = dataSource;
        _logger = logger;
    }

    public async Task<BackgroundResponseJob> InsertAsync(BackgroundResponseJob job, CancellationToken ct = default)
    {
        job.UpdatedAt = DateTime.UtcNow;
        await using var ctx = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);
        ctx.BackgroundResponseJobs.Add(ToRow(job));
        await ctx.SaveChangesAsync(ct).ConfigureAwait(false);
        return job;
    }

    public async Task<BackgroundResponseJob?> GetAsync(string jobId, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var row = await ctx.BackgroundResponseJobs.FindAsync([jobId], ct).ConfigureAwait(false);
        return row is null ? null : FromRow(row);
    }

    public async Task<BackgroundResponseJob?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var row = await ctx.BackgroundResponseJobs
            .FirstOrDefaultAsync(r => r.IdempotencyKey == idempotencyKey, ct)
            .ConfigureAwait(false);
        return row is null ? null : FromRow(row);
    }

    public async Task UpdateAsync(BackgroundResponseJob job, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var row = await ctx.BackgroundResponseJobs.FindAsync([job.JobId], ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Job '{job.JobId}' não encontrado.");
        row.Status = job.Status.ToString();
        row.Output = job.Output;
        row.LastError = job.LastError;
        row.Attempt = job.Attempt;
        row.StartedAt = job.StartedAt;
        row.CompletedAt = job.CompletedAt;
        row.CallbackTarget = job.CallbackTarget is null
            ? null
            : JsonSerializer.Serialize(job.CallbackTarget, JsonDefaults.Domain);
        row.WorkflowId = job.WorkflowId;
        row.Step = job.Step;
        row.LeasedBy = job.LeasedBy;
        row.LeaseUntil = job.LeaseUntil;
        row.NextAttemptAt = job.NextAttemptAt;
        row.IngestionContext = job.IngestionContext;
        row.ExecutionId = job.ExecutionId;
        row.ProjectId = job.ProjectId;
        row.TenantId = job.TenantId;
        row.UpdatedAt = DateTime.UtcNow;
        await ctx.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<BackgroundResponseJob>> ListPendingAsync(int limit, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var rows = await ctx.BackgroundResponseJobs
            .Where(r => r.Status == "Queued" || r.Status == "Running")
            .OrderBy(r => r.CreatedAt)
            .Take(limit)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return rows.Select(FromRow).ToList();
    }

    /// <summary>
    /// Pega N jobs Queued elegíveis num único round-trip. Cota por workflow é
    /// aplicada na própria query via subquery correlated — jobs com
    /// <c>WorkflowId</c> cuja contagem de Running já atingiu o cap não são
    /// leasados. Evita o anti-pattern "lease → falha cota → devolve" que
    /// queimaria slot Redis e incrementaria Attempt sem motivo. Jobs com
    /// <c>WorkflowId IS NULL</c> não sofrem cota (caminho legado/admin).
    /// </summary>
    public async Task<IReadOnlyList<BackgroundResponseJob>> TryLeaseAsync(
        int batchSize,
        string podId,
        TimeSpan leaseTtl,
        int perWorkflowCap,
        CancellationToken ct = default)
    {
        if (batchSize <= 0) return Array.Empty<BackgroundResponseJob>();

        // Cota por workflow resolvida via subquery correlated em
        // workflow_definitions (PascalCase via JsonDefaults.Domain).
        // COALESCE fallback é o cap default das options. Workflow sem row
        // em workflow_definitions cai no default.
        //
        // GUARDA: Data é TEXT (não JSONB). JSON malformado faria o cast::jsonb
        // lançar invalid_text_representation pro TryLeaseAsync inteiro —
        // congelando a fila. O regex <c>~ '^\s*\{'</c> filtra rows que não
        // começam com objeto JSON antes do cast.
        //
        // IMPORTANTE: subquery correlated (não LEFT JOIN) — Postgres rejeita
        // <c>FOR UPDATE</c> sobre o lado nullable de OUTER JOIN
        // (SQLSTATE 0A000). Mantemos só a tabela principal no FROM.
        const string sql = """
            WITH picked AS (
                SELECT j."JobId"
                FROM aihub.background_response_jobs j
                WHERE j."Status" = 'Queued'
                  AND (j."NextAttemptAt" IS NULL OR j."NextAttemptAt" <= NOW())
                  AND (
                    j."WorkflowId" IS NULL
                    OR (
                        SELECT COUNT(*)
                        FROM aihub.background_response_jobs r
                        WHERE r."WorkflowId" = j."WorkflowId" AND r."Status" = 'Running'
                    ) < COALESCE(
                        (SELECT
                            CASE WHEN wd."Data" ~ '^\s*\{'
                                 THEN ((wd."Data"::jsonb)->'Configuration'->>'StandaloneMaxConcurrent')::int
                                 ELSE NULL
                            END
                         FROM aihub.workflow_definitions wd
                         WHERE wd."Id" = j."WorkflowId"
                         LIMIT 1),
                        @perWorkflowCap
                    )
                  )
                ORDER BY j."CreatedAt"
                LIMIT @batchSize
                FOR UPDATE SKIP LOCKED
            )
            UPDATE aihub.background_response_jobs j
            SET "Status"     = 'Running',
                "LeasedBy"   = @podId,
                "LeaseUntil" = NOW() + (@ttlSeconds || ' seconds')::INTERVAL,
                "Attempt"    = j."Attempt" + 1,
                "StartedAt"  = COALESCE(j."StartedAt", NOW()),
                "UpdatedAt"  = NOW()
            FROM picked
            WHERE j."JobId" = picked."JobId"
            RETURNING j.*;
            """;

        await using var conn = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("batchSize", batchSize);
        cmd.Parameters.AddWithValue("podId", podId);
        cmd.Parameters.AddWithValue("ttlSeconds", (int)leaseTtl.TotalSeconds);
        cmd.Parameters.AddWithValue("perWorkflowCap", perWorkflowCap);

        var leased = new List<BackgroundResponseJob>(batchSize);
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            leased.Add(ReadJobFromReader(reader));
        return leased;
    }

    public async Task<int> CountRunningByWorkflowAsync(string workflowId, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await ctx.BackgroundResponseJobs
            .CountAsync(r => r.WorkflowId == workflowId && r.Status == "Running", ct)
            .ConfigureAwait(false);
    }

    public async Task<bool> RenewLeaseAsync(string jobId, string podId, TimeSpan extend, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE aihub.background_response_jobs
            SET "LeaseUntil" = NOW() + (@seconds || ' seconds')::INTERVAL,
                "UpdatedAt"  = NOW()
            WHERE "JobId"    = @jobId
              AND "LeasedBy" = @podId
              AND "Status"   = 'Running';
            """;

        await using var conn = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("jobId", jobId);
        cmd.Parameters.AddWithValue("podId", podId);
        cmd.Parameters.AddWithValue("seconds", (int)extend.TotalSeconds);
        var affected = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return affected == 1;
    }

    /// <summary>
    /// Reseta jobs com lease vencido. Quando <c>Attempt &gt;= maxAttempts</c>,
    /// promove direto pra <c>Failed</c> (com LastError descritivo) em vez de
    /// devolver pra Queued — evita loop infinito em job determinísticamente
    /// travado.
    /// </summary>
    public async Task<ReclaimResult> ReclaimExpiredLeasesAsync(TimeSpan reclaimBackoff, int maxAttempts, CancellationToken ct = default)
    {
        // RETURNING dentro de CTE permite contar separadamente quantos foram
        // requeued (Queued) vs failed_max_attempts (Failed). Dashboards usam
        // a métrica downstream pra alertar em "muitos jobs morrendo por
        // MaxAttempts" — sintoma de workflow doente ou rate limit upstream.
        const string sql = """
            WITH updated AS (
                UPDATE aihub.background_response_jobs
                SET "Status"        = CASE WHEN "Attempt" >= @maxAttempts THEN 'Failed' ELSE 'Queued' END,
                    "LastError"     = CASE WHEN "Attempt" >= @maxAttempts
                                           THEN COALESCE("LastError", '') || ' [reaped after MaxAttempts]'
                                           ELSE "LastError"
                                      END,
                    "CompletedAt"   = CASE WHEN "Attempt" >= @maxAttempts THEN NOW() ELSE "CompletedAt" END,
                    "NextAttemptAt" = CASE WHEN "Attempt" >= @maxAttempts
                                           THEN NULL
                                           ELSE NOW() + (@backoffSeconds || ' seconds')::INTERVAL
                                      END,
                    "LeasedBy"      = NULL,
                    "LeaseUntil"    = NULL,
                    "UpdatedAt"     = NOW()
                WHERE "Status"     = 'Running'
                  AND "LeaseUntil" IS NOT NULL
                  AND "LeaseUntil" < NOW()
                RETURNING "Status" AS new_status
            )
            SELECT
                COUNT(*) FILTER (WHERE new_status = 'Queued')::int AS requeued,
                COUNT(*) FILTER (WHERE new_status = 'Failed')::int AS failed_max
            FROM updated;
            """;

        await using var conn = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("backoffSeconds", (int)reclaimBackoff.TotalSeconds);
        cmd.Parameters.AddWithValue("maxAttempts", maxAttempts);

        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            return new ReclaimResult(
                Requeued: reader["requeued"] as int? ?? 0,
                FailedMaxAttempts: reader["failed_max"] as int? ?? 0);
        }
        return new ReclaimResult(0, 0);
    }

    public async Task<bool> UpdateStepAsync(string jobId, string podId, string? step, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE aihub.background_response_jobs
            SET "Step"      = @step,
                "UpdatedAt" = NOW()
            WHERE "JobId"    = @jobId
              AND "LeasedBy" = @podId
              AND "Status"   = 'Running';
            """;

        await using var conn = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("jobId", jobId);
        cmd.Parameters.AddWithValue("podId", podId);
        cmd.Parameters.AddWithValue("step", (object?)step ?? DBNull.Value);
        var affected = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return affected == 1;
    }

    public async Task<bool> UpdateIngestionContextAsync(string jobId, string podId, string? ingestionContextJson, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE aihub.background_response_jobs
            SET "IngestionContext" = @ctx::jsonb,
                "UpdatedAt"        = NOW()
            WHERE "JobId"    = @jobId
              AND "LeasedBy" = @podId
              AND "Status"   = 'Running';
            """;

        await using var conn = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("jobId", jobId);
        cmd.Parameters.AddWithValue("podId", podId);
        cmd.Parameters.AddWithValue("ctx", (object?)ingestionContextJson ?? DBNull.Value);
        var affected = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return affected == 1;
    }

    public async Task<bool> CompleteAsync(string jobId, string podId, string? output, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE aihub.background_response_jobs
            SET "Status"      = 'Completed',
                "Output"      = @output,
                "LastError"   = NULL,
                "CompletedAt" = NOW(),
                "LeasedBy"    = NULL,
                "LeaseUntil"  = NULL,
                "Step"        = NULL,
                "UpdatedAt"   = NOW()
            WHERE "JobId"    = @jobId
              AND "LeasedBy" = @podId
              AND "Status"   = 'Running'
            RETURNING "CallbackTarget", "ProjectId", "TenantId";
            """;

        await using var conn = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var trans = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        var affected = await CompleteWithDeliveryAsync(conn, trans, sql, jobId, podId, output, ct).ConfigureAwait(false);
        await trans.CommitAsync(ct).ConfigureAwait(false);
        return affected;
    }

    public async Task<bool> FailAsync(string jobId, string podId, string lastError, DateTime? nextAttemptAt, bool permanent, CancellationToken ct = default)
    {
        var newStatus = permanent || nextAttemptAt is null ? "Failed" : "Queued";
        var isTerminal = newStatus == "Failed";

        const string sql = """
            UPDATE aihub.background_response_jobs
            SET "Status"        = @status,
                "LastError"     = @error,
                "NextAttemptAt" = @nextAttempt,
                "CompletedAt"   = CASE WHEN @status = 'Failed' THEN NOW() ELSE "CompletedAt" END,
                "LeasedBy"      = NULL,
                "LeaseUntil"    = NULL,
                "UpdatedAt"     = NOW()
            WHERE "JobId"    = @jobId
              AND "LeasedBy" = @podId
              AND "Status"   = 'Running'
            RETURNING "CallbackTarget", "ProjectId", "TenantId";
            """;

        await using var conn = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var trans = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);

        await using var cmd = new NpgsqlCommand(sql, conn, trans);
        cmd.Parameters.AddWithValue("jobId", jobId);
        cmd.Parameters.AddWithValue("podId", podId);
        cmd.Parameters.AddWithValue("status", newStatus);
        cmd.Parameters.AddWithValue("error", (object?)lastError ?? DBNull.Value);
        cmd.Parameters.AddWithValue("nextAttempt", (object?)nextAttemptAt ?? DBNull.Value);

        string? callbackJson = null;
        string? projectId = null;
        string? tenantId = null;
        bool affected = false;
        await using (var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            if (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                callbackJson = reader["CallbackTarget"] as string;
                projectId = reader["ProjectId"] as string;
                tenantId = reader["TenantId"] as string;
                affected = true;
            }
        }

        // Webhook só dispara em terminal Failed. Status=Queued (retry agendado)
        // não emite webhook. Mesma transação garante atomicidade — ou ambos
        // vão pra DB ou nada.
        if (affected && callbackJson is not null && isTerminal)
        {
            await InsertWebhookDeliveryAsync(conn, trans, jobId, callbackJson, projectId, tenantId, ct).ConfigureAwait(false);
        }

        await trans.CommitAsync(ct).ConfigureAwait(false);
        return affected;
    }

    public async Task<bool> DeferAsync(string jobId, string podId, string reason, DateTime nextAttemptAt, CancellationToken ct = default)
    {
        // Backpressure de capacidade: devolve pra Queued e desfaz o incremento
        // de Attempt do lease (GREATEST piso em 0) — o job não chegou a rodar,
        // então não pode contar contra MaxAttempts. Não toca CompletedAt e não
        // dispara webhook (não é terminal). Ownership-aware.
        const string sql = """
            UPDATE aihub.background_response_jobs
            SET "Status"        = 'Queued',
                "LastError"     = @reason,
                "NextAttemptAt" = @nextAttempt,
                "Attempt"       = GREATEST("Attempt" - 1, 0),
                "LeasedBy"      = NULL,
                "LeaseUntil"    = NULL,
                "UpdatedAt"     = NOW()
            WHERE "JobId"    = @jobId
              AND "LeasedBy" = @podId
              AND "Status"   = 'Running';
            """;

        await using var conn = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("jobId", jobId);
        cmd.Parameters.AddWithValue("podId", podId);
        cmd.Parameters.AddWithValue("reason", (object?)reason ?? DBNull.Value);
        cmd.Parameters.AddWithValue("nextAttempt", nextAttemptAt);
        var affected = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return affected == 1;
    }

    /// <summary>
    /// Executa o UPDATE de Complete numa transação aberta e, se houver
    /// CallbackTarget, INSERT em webhook_deliveries no mesmo escopo.
    /// </summary>
    private async Task<bool> CompleteWithDeliveryAsync(
        NpgsqlConnection conn, NpgsqlTransaction trans, string sql,
        string jobId, string podId, string? output, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(sql, conn, trans);
        cmd.Parameters.AddWithValue("jobId", jobId);
        cmd.Parameters.AddWithValue("podId", podId);
        cmd.Parameters.AddWithValue("output", (object?)output ?? DBNull.Value);

        string? callbackJson = null;
        string? projectId = null;
        string? tenantId = null;
        bool affected = false;
        await using (var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            if (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                callbackJson = reader["CallbackTarget"] as string;
                projectId = reader["ProjectId"] as string;
                tenantId = reader["TenantId"] as string;
                affected = true;
            }
        }

        if (affected && callbackJson is not null)
        {
            await InsertWebhookDeliveryAsync(conn, trans, jobId, callbackJson, projectId, tenantId, ct).ConfigureAwait(false);
        }

        return affected;
    }

    private async Task InsertWebhookDeliveryAsync(
        NpgsqlConnection conn, NpgsqlTransaction trans,
        string jobId, string callbackJson, string? projectId, string? tenantId,
        CancellationToken ct)
    {
        // Extrai Url/HmacSecret/Headers do CallbackTarget JSONB via expressões SQL —
        // evita deserializar em memória só pra remontar como INSERT. Casing
        // segue PascalCase (JsonDefaults.Domain). Cláusula WHERE no SELECT
        // skipa o INSERT silenciosamente se Url for null/ausente — sinal de
        // CallbackTarget malformado. Logamos warn pra que esse silêncio fique
        // visível em prod.
        const string insertSql = """
            INSERT INTO aihub.webhook_deliveries
                ("DeliveryId", "JobId", "Url", "HmacSecret", "Headers",
                 "Status", "ProjectId", "TenantId", "CreatedAt", "UpdatedAt")
            SELECT
                @deliveryId,
                @jobId,
                callback->>'Url',
                callback->>'HmacSecret',
                CASE WHEN callback ? 'Headers' THEN callback->'Headers' ELSE NULL END,
                'Pending',
                @projectId,
                @tenantId,
                NOW(),
                NOW()
            FROM (SELECT @callback::jsonb AS callback) j
            WHERE callback->>'Url' IS NOT NULL;
            """;

        await using var cmd = new NpgsqlCommand(insertSql, conn, trans);
        cmd.Parameters.AddWithValue("deliveryId", Guid.NewGuid().ToString("N"));
        cmd.Parameters.AddWithValue("jobId", jobId);
        cmd.Parameters.AddWithValue("callback", callbackJson);
        cmd.Parameters.AddWithValue("projectId", (object?)projectId ?? "default");
        cmd.Parameters.AddWithValue("tenantId", (object?)tenantId ?? "default");
        var inserted = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        if (inserted == 0)
        {
            _logger.LogWarning(
                "[BackgroundResponse] CallbackTarget gravado no job {JobId} mas Url está ausente — webhook NÃO será entregue.",
                jobId);
        }
    }

    public async Task SetExecutionIdAsync(string jobId, string executionId, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE aihub.background_response_jobs
            SET "ExecutionId" = @executionId,
                "UpdatedAt"   = NOW()
            WHERE "JobId" = @jobId;
            """;

        await using var conn = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("jobId", jobId);
        cmd.Parameters.AddWithValue("executionId", executionId);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static BackgroundResponseJobRow ToRow(BackgroundResponseJob j) => new()
    {
        JobId = j.JobId,
        AgentId = j.AgentId,
        AgentVersionId = j.AgentVersionId,
        SessionId = j.SessionId,
        Input = j.Input,
        Status = j.Status.ToString(),
        Output = j.Output,
        LastError = j.LastError,
        Attempt = j.Attempt,
        CallbackTarget = j.CallbackTarget is null ? null : JsonSerializer.Serialize(j.CallbackTarget, JsonDefaults.Domain),
        IdempotencyKey = j.IdempotencyKey,
        CreatedAt = j.CreatedAt,
        StartedAt = j.StartedAt,
        CompletedAt = j.CompletedAt,
        WorkflowId = j.WorkflowId,
        Step = j.Step,
        LeasedBy = j.LeasedBy,
        LeaseUntil = j.LeaseUntil,
        NextAttemptAt = j.NextAttemptAt,
        IngestionContext = j.IngestionContext,
        ExecutionId = j.ExecutionId,
        UpdatedAt = j.UpdatedAt,
        ProjectId = string.IsNullOrWhiteSpace(j.ProjectId) ? "default" : j.ProjectId,
        TenantId = string.IsNullOrWhiteSpace(j.TenantId) ? "default" : j.TenantId
    };

    private static BackgroundResponseJob FromRow(BackgroundResponseJobRow r) => new()
    {
        JobId = r.JobId,
        AgentId = r.AgentId,
        AgentVersionId = r.AgentVersionId,
        SessionId = r.SessionId,
        Input = r.Input,
        Status = Enum.TryParse<BackgroundResponseStatus>(r.Status, out var s) ? s : BackgroundResponseStatus.Queued,
        Output = r.Output,
        LastError = r.LastError,
        Attempt = r.Attempt,
        CallbackTarget = string.IsNullOrEmpty(r.CallbackTarget)
            ? null
            : JsonSerializer.Deserialize<ResponseCallbackTarget>(r.CallbackTarget, JsonDefaults.Domain),
        IdempotencyKey = r.IdempotencyKey,
        CreatedAt = r.CreatedAt,
        StartedAt = r.StartedAt,
        CompletedAt = r.CompletedAt,
        WorkflowId = r.WorkflowId,
        Step = r.Step,
        LeasedBy = r.LeasedBy,
        LeaseUntil = r.LeaseUntil,
        NextAttemptAt = r.NextAttemptAt,
        IngestionContext = r.IngestionContext,
        ExecutionId = r.ExecutionId,
        UpdatedAt = r.UpdatedAt,
        ProjectId = r.ProjectId,
        TenantId = r.TenantId
    };

    // Materialização manual do RETURNING — evita uma segunda query EF e mantém
    // o lease atômico com o SELECT FOR UPDATE.
    private static BackgroundResponseJob ReadJobFromReader(NpgsqlDataReader r)
    {
        var statusStr = r["Status"] as string ?? "Queued";
        var status = Enum.TryParse<BackgroundResponseStatus>(statusStr, out var s)
            ? s
            : BackgroundResponseStatus.Queued;

        var callbackJson = r["CallbackTarget"] as string;

        return new BackgroundResponseJob
        {
            JobId = (string)r["JobId"],
            AgentId = (string)r["AgentId"],
            AgentVersionId = r["AgentVersionId"] as string,
            SessionId = r["SessionId"] as string,
            Input = (string)r["Input"],
            Status = status,
            Output = r["Output"] as string,
            LastError = r["LastError"] as string,
            Attempt = (int)r["Attempt"],
            CallbackTarget = string.IsNullOrEmpty(callbackJson)
                ? null
                : JsonSerializer.Deserialize<ResponseCallbackTarget>(callbackJson, JsonDefaults.Domain),
            IdempotencyKey = r["IdempotencyKey"] as string,
            CreatedAt = (DateTime)r["CreatedAt"],
            StartedAt = r["StartedAt"] as DateTime?,
            CompletedAt = r["CompletedAt"] as DateTime?,
            WorkflowId = r["WorkflowId"] as string,
            Step = r["Step"] as string,
            LeasedBy = r["LeasedBy"] as string,
            LeaseUntil = r["LeaseUntil"] as DateTime?,
            NextAttemptAt = r["NextAttemptAt"] as DateTime?,
            IngestionContext = r["IngestionContext"] as string,
            ExecutionId = r["ExecutionId"] as string,
            UpdatedAt = (DateTime)r["UpdatedAt"],
            ProjectId = (string)r["ProjectId"],
            TenantId = (string)r["TenantId"]
        };
    }
}
