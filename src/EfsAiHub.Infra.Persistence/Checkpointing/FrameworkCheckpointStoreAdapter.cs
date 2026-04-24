using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using EfsAiHub.Core.Orchestration.Workflows;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.Checkpointing;

namespace EfsAiHub.Infra.Persistence.Checkpointing;

/// <summary>
/// Ponte entre o <see cref="ICheckpointStore{JsonElement}"/> do Microsoft.Agents.AI.Workflows
/// (prerelease) e nosso <see cref="EfsAiHub.Core.Orchestration.Workflows.ICheckpointStore"/> de byte[].
///
/// Estratégia:
///  - Cada checkpoint é serializado como um envelope JSON UTF-8:
///       {"checkpointId":"...","data":&lt;JsonElement&gt;}
///  - O sessionId do framework corresponde ao nosso executionId (chave do store).
///  - Guardamos o índice de CheckpointInfo por sessão em memória. Como só usamos o
///    mais recente para o recovery de HITL, sobrescrevemos o envelope salvo e o
///    índice a cada novo checkpoint (pragmatismo — o store durável do framework
///    normalmente guarda histórico, mas não precisamos disso no caso de uso atual).
///  - Cada método é envolto em try/catch com log; falhas não derrubam o runtime.
/// </summary>
public sealed class FrameworkCheckpointStoreAdapter : ICheckpointStore<JsonElement>
{
    private readonly EfsAiHub.Core.Orchestration.Workflows.ICheckpointStore _inner;
    private readonly ILogger<FrameworkCheckpointStoreAdapter> _logger;
    private readonly ConcurrentDictionary<string, List<CheckpointInfo>> _index = new();

    public FrameworkCheckpointStoreAdapter(
        EfsAiHub.Core.Orchestration.Workflows.ICheckpointStore inner,
        ILogger<FrameworkCheckpointStoreAdapter> logger)
    {
        _inner = inner;
        _logger = logger;
    }

    public async ValueTask<CheckpointInfo> CreateCheckpointAsync(
        string sessionId, JsonElement value, CheckpointInfo? parent)
    {
        try
        {
            var checkpointId = Guid.NewGuid().ToString("N");
            var info = new CheckpointInfo(sessionId, checkpointId);

            var envelope = new Envelope
            {
                CheckpointId = checkpointId,
                Data = value
            };
            var bytes = JsonSerializer.SerializeToUtf8Bytes(envelope);

            await _inner.SaveCheckpointAsync(sessionId, bytes);

            // Mantém o índice em memória — para efeito de HITL recovery somente o
            // mais recente importa, mas preservamos o histórico dentro do processo.
            _index.AddOrUpdate(sessionId,
                _ => new List<CheckpointInfo> { info },
                (_, list) =>
                {
                    lock (list) { list.Add(info); }
                    return list;
                });

            return info;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[FrameworkCheckpointStoreAdapter] Falha ao persistir checkpoint da sessão '{SessionId}'.",
                sessionId);
            throw;
        }
    }

    public async ValueTask<JsonElement> RetrieveCheckpointAsync(string sessionId, CheckpointInfo key)
    {
        try
        {
            var bytes = await _inner.LoadCheckpointAsync(sessionId);
            if (bytes is null || bytes.Length == 0)
            {
                _logger.LogWarning(
                    "[FrameworkCheckpointStoreAdapter] Nenhum checkpoint para sessão '{SessionId}'.",
                    sessionId);
                return default;
            }

            var envelope = JsonSerializer.Deserialize<Envelope>(bytes);
            return envelope?.Data ?? default;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[FrameworkCheckpointStoreAdapter] Falha ao ler checkpoint da sessão '{SessionId}'.",
                sessionId);
            throw;
        }
    }

    public ValueTask<IEnumerable<CheckpointInfo>> RetrieveIndexAsync(string sessionId, CheckpointInfo? withParent)
    {
        try
        {
            if (_index.TryGetValue(sessionId, out var list))
            {
                List<CheckpointInfo> snapshot;
                lock (list) { snapshot = new List<CheckpointInfo>(list); }
                return new ValueTask<IEnumerable<CheckpointInfo>>(snapshot);
            }

            // Sessão não conhecida em memória (ex: após restart) — reconstrói um
            // índice single-entry se existir dado persistido.
            return BuildFallbackIndexAsync(sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[FrameworkCheckpointStoreAdapter] Falha ao listar checkpoints da sessão '{SessionId}'.",
                sessionId);
            return new ValueTask<IEnumerable<CheckpointInfo>>(Array.Empty<CheckpointInfo>());
        }
    }

    private async ValueTask<IEnumerable<CheckpointInfo>> BuildFallbackIndexAsync(string sessionId)
    {
        var bytes = await _inner.LoadCheckpointAsync(sessionId);
        if (bytes is null || bytes.Length == 0)
            return Array.Empty<CheckpointInfo>();

        try
        {
            var envelope = JsonSerializer.Deserialize<Envelope>(bytes);
            if (envelope is null || string.IsNullOrEmpty(envelope.CheckpointId))
                return Array.Empty<CheckpointInfo>();

            var info = new CheckpointInfo(sessionId, envelope.CheckpointId);
            _index[sessionId] = new List<CheckpointInfo> { info };
            return new[] { info };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[FrameworkCheckpointStoreAdapter] Envelope corrompido para sessão '{SessionId}'.",
                sessionId);
            return Array.Empty<CheckpointInfo>();
        }
    }

    /// <summary>
    /// Remove o índice em memória da sessão. Usado em estados terminais pelo WorkflowRunnerService
    /// para eliminar o leak monotônico do _index.
    /// </summary>
    public void EvictSession(string sessionId) => _index.TryRemove(sessionId, out _);

    /// <summary>Retorna o CheckpointInfo mais recente para a sessão, ou null.</summary>
    public async Task<CheckpointInfo?> GetLatestAsync(string sessionId)
    {
        if (_index.TryGetValue(sessionId, out var list))
        {
            lock (list)
            {
                if (list.Count > 0) return list[^1];
            }
        }

        var rebuilt = await BuildFallbackIndexAsync(sessionId);
        return rebuilt.LastOrDefault();
    }

    private sealed class Envelope
    {
        public string CheckpointId { get; set; } = string.Empty;
        public JsonElement Data { get; set; }
    }
}
