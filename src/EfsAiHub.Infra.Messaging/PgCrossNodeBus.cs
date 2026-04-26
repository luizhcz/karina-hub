using System.Text.Json;
using EfsAiHub.Core.Abstractions.Persistence;
using EfsAiHub.Core.Orchestration.Coordination;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace EfsAiHub.Infra.Messaging;

/// <summary>
/// Implementação do <see cref="ICrossNodeBus"/> usando pg_notify no pool "general".
/// Conexão curta por publish — cada chamada é sub-milissegundo no happy path.
/// </summary>
public sealed class PgCrossNodeBus : ICrossNodeBus
{
    public const string CancelChannel = "efs_exec_cancel";
    public const string HitlResolvedChannel = "efs_hitl_resolved";

    private readonly NpgsqlDataSource _generalDataSource;
    private readonly ILogger<PgCrossNodeBus> _logger;

    public PgCrossNodeBus(
        [FromKeyedServices("general")] NpgsqlDataSource generalDataSource,
        ILogger<PgCrossNodeBus> logger)
    {
        _generalDataSource = generalDataSource;
        _logger = logger;
    }

    public async Task PublishCancelAsync(string executionId, CancellationToken ct = default)
    {
        var payload = JsonSerializer.Serialize(new { executionId }, JsonDefaults.Domain);
        await NotifyAsync(CancelChannel, payload, ct);
    }

    public async Task PublishHitlResolvedAsync(
        string interactionId, string resolution, bool approved, string resolvedBy, CancellationToken ct = default)
    {
        var payload = JsonSerializer.Serialize(new { interactionId, resolution, approved, resolvedBy }, JsonDefaults.Domain);
        await NotifyAsync(HitlResolvedChannel, payload, ct);
    }

    private async Task NotifyAsync(string channel, string payload, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                await using var conn = await _generalDataSource.OpenConnectionAsync(ct);
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT pg_notify(@channel, @payload)";
                cmd.Parameters.AddWithValue("channel", channel);
                cmd.Parameters.AddWithValue("payload", payload);
                await cmd.ExecuteNonQueryAsync(ct);
                return;
            }
            catch (NpgsqlException ex) when (attempt == 0 && ex.IsTransient)
            {
                _logger.LogWarning(ex, "[CrossNodeBus] NOTIFY transiente, retry em 500ms. Channel={Channel}.", channel);
                await Task.Delay(500, ct);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                // Non-transient ou último attempt: log e desiste (best-effort)
                _logger.LogWarning(ex, "[CrossNodeBus] Falha ao NOTIFY {Channel}.", channel);
                return;
            }
        }
    }
}
