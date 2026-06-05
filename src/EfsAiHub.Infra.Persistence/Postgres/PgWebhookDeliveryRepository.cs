using System.Text.Json;
using EfsAiHub.Core.Agents.Responses;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace EfsAiHub.Infra.Persistence.Postgres;

/// <summary>
/// Persistência de <see cref="WebhookDelivery"/>. Inserts vêm via INSERT
/// inline dentro do CompleteAsync/FailAsync do
/// <see cref="PgBackgroundResponseRepository"/> — esse repository cobre só
/// leitura + lease + status transitions.
/// </summary>
public sealed class PgWebhookDeliveryRepository : IWebhookDeliveryRepository
{
    private readonly IDbContextFactory<AgentFwDbContext> _factory;
    private readonly NpgsqlDataSource _dataSource;

    public PgWebhookDeliveryRepository(
        IDbContextFactory<AgentFwDbContext> factory,
        [FromKeyedServices("general")] NpgsqlDataSource dataSource)
    {
        _factory = factory;
        _dataSource = dataSource;
    }

    public async Task<WebhookDelivery?> GetAsync(string deliveryId, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var row = await ctx.WebhookDeliveries.FindAsync([deliveryId], ct).ConfigureAwait(false);
        return row is null ? null : FromRow(row);
    }

    public async Task<IReadOnlyList<WebhookDelivery>> ListByJobAsync(string jobId, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var rows = await ctx.WebhookDeliveries
            .Where(r => r.JobId == jobId)
            .OrderBy(r => r.CreatedAt)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return rows.Select(FromRow).ToList();
    }

    /// <summary>
    /// UPDATE atômico que mata o lock + transição de status em single statement:
    /// CTE seleciona N pendentes via <c>FOR UPDATE SKIP LOCKED</c>, UPDATE mãe
    /// transiciona pra <c>Delivering</c> com <c>UpdatedAt = NOW()</c> e RETURNING
    /// devolve as rows leasadas. Dois pods pegam rows disjuntas; a transição
    /// fica persistida no DB.
    /// </summary>
    public async Task<IReadOnlyList<WebhookDelivery>> LeasePendingAsync(int batchSize, CancellationToken ct = default)
    {
        if (batchSize <= 0) return Array.Empty<WebhookDelivery>();

        const string sql = """
            WITH picked AS (
                SELECT "DeliveryId"
                FROM aihub.webhook_deliveries
                WHERE "Status" = 'Pending'
                ORDER BY "CreatedAt"
                LIMIT @batchSize
                FOR UPDATE SKIP LOCKED
            )
            UPDATE aihub.webhook_deliveries d
            SET "Status"    = 'Delivering',
                "UpdatedAt" = NOW()
            FROM picked
            WHERE d."DeliveryId" = picked."DeliveryId"
            RETURNING d.*;
            """;

        await using var conn = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("batchSize", batchSize);

        var list = new List<WebhookDelivery>(batchSize);
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            list.Add(ReadFromReader(reader));
        return list;
    }

    public async Task<int> ReclaimStuckDeliveringAsync(TimeSpan stuckThreshold, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE aihub.webhook_deliveries
            SET "Status"    = 'Failed',
                "LastError" = COALESCE("LastError", '') || ' [worker stuck/crashed during delivery]',
                "UpdatedAt" = NOW()
            WHERE "Status"    = 'Delivering'
              AND "UpdatedAt" < NOW() - (@thresholdSeconds || ' seconds')::INTERVAL;
            """;

        await using var conn = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("thresholdSeconds", (int)stuckThreshold.TotalSeconds);
        return await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task MarkDeliveredAsync(string deliveryId, int statusCode, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE aihub.webhook_deliveries
            SET "Status"           = 'Delivered',
                "LastResponseCode" = @code,
                "LastError"        = NULL,
                "DeliveredAt"      = NOW(),
                "UpdatedAt"        = NOW()
            WHERE "DeliveryId" = @id;
            """;

        await using var conn = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", deliveryId);
        cmd.Parameters.AddWithValue("code", statusCode);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task MarkFailedAsync(string deliveryId, string error, int? statusCode, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE aihub.webhook_deliveries
            SET "Status"           = 'Failed',
                "LastResponseCode" = @code,
                "LastError"        = @error,
                "UpdatedAt"        = NOW()
            WHERE "DeliveryId" = @id;
            """;

        await using var conn = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", deliveryId);
        cmd.Parameters.AddWithValue("code", (object?)statusCode ?? DBNull.Value);
        cmd.Parameters.AddWithValue("error", (object?)error ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Deserializa o JSONB de Headers com tolerância — row corrompida (raro)
    /// não derruba <c>ListByJobAsync</c>/<c>LeasePendingAsync</c> inteiro.
    /// </summary>
    private static Dictionary<string, string>? TryDeserializeHeaders(string? json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try { return JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonDefaults.Domain); }
        catch (JsonException) { return null; }
    }

    private static WebhookDelivery FromRow(WebhookDeliveryRow r) => new()
    {
        DeliveryId = r.DeliveryId,
        JobId = r.JobId,
        Url = r.Url,
        HmacSecret = r.HmacSecret,
        Headers = TryDeserializeHeaders(r.Headers),
        Status = Enum.TryParse<WebhookDeliveryStatus>(r.Status, out var s) ? s : WebhookDeliveryStatus.Pending,
        LastResponseCode = r.LastResponseCode,
        LastError = r.LastError,
        DeliveredAt = r.DeliveredAt,
        ProjectId = r.ProjectId,
        TenantId = r.TenantId,
        CreatedAt = r.CreatedAt,
        UpdatedAt = r.UpdatedAt,
    };

    private static WebhookDelivery ReadFromReader(NpgsqlDataReader r)
    {
        var headersJson = r["Headers"] as string;
        var statusStr = r["Status"] as string ?? "Pending";

        return new WebhookDelivery
        {
            DeliveryId = (string)r["DeliveryId"],
            JobId = (string)r["JobId"],
            Url = (string)r["Url"],
            HmacSecret = r["HmacSecret"] as string,
            Headers = TryDeserializeHeaders(headersJson),
            Status = Enum.TryParse<WebhookDeliveryStatus>(statusStr, out var s) ? s : WebhookDeliveryStatus.Pending,
            LastResponseCode = r["LastResponseCode"] as int?,
            LastError = r["LastError"] as string,
            DeliveredAt = r["DeliveredAt"] as DateTime?,
            ProjectId = (string)r["ProjectId"],
            TenantId = (string)r["TenantId"],
            CreatedAt = (DateTime)r["CreatedAt"],
            UpdatedAt = (DateTime)r["UpdatedAt"],
        };
    }
}
