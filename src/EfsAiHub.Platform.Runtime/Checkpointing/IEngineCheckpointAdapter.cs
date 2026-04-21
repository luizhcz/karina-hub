using Microsoft.Agents.AI.Workflows;

namespace EfsAiHub.Platform.Runtime.Checkpointing;

/// <summary>
/// Encapsula TODA interação com o <see cref="CheckpointManager"/> e a API de
/// retomada de workflows do Microsoft.Agents.AI.Workflows (prerelease).
///
/// Objetivo: isolar o blast radius de uma quebra de API upstream em um único
/// ponto do código.
/// </summary>
public interface IEngineCheckpointAdapter
{
    /// <summary>Retorna o <see cref="CheckpointManager"/> do framework já
    /// configurado para gravar no store customizado (Postgres em prod).</summary>
    CheckpointManager CreateManager();

    /// <summary>Retorna o último checkpoint salvo para a execução, ou null.</summary>
    Task<CheckpointInfo?> GetLatestAsync(string executionId, CancellationToken ct = default);

    /// <summary>
    /// Tenta retomar um workflow a partir do checkpoint mais recente.
    /// Retorna null se não há checkpoint ou se houve falha (logada).
    /// </summary>
    Task<StreamingRun?> TryResumeAsync(Workflow workflow, string executionId, CancellationToken ct = default);

    /// <summary>
    /// Remove qualquer estado em memória (índice de checkpoints) e opcionalmente o checkpoint
    /// persistido para a execução. Deve ser chamado em estados terminais para evitar leak do
    /// <see cref="FrameworkCheckpointStoreAdapter"/>._index.
    /// </summary>
    Task EvictSessionAsync(string executionId, bool deletePersistent = true, CancellationToken ct = default);
}
