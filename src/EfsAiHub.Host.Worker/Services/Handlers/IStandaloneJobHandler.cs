using EfsAiHub.Core.Agents.Responses;

namespace EfsAiHub.Host.Worker.Services.Handlers;

/// <summary>
/// Handler de jobs standalone. O <c>StandaloneJobDispatcherService</c> mantém
/// o lease + heartbeat + cancellation cooperativo; handlers implementam só a
/// lógica do "que fazer com o job leased".
///
/// O dispatcher itera pelos handlers registrados e usa o primeiro que retorna
/// <see cref="CanHandle"/>=<c>true</c>. <c>WorkflowStandaloneJobHandler</c>
/// (default) deve ser o último — aceita qualquer job que ninguém mais quis.
/// </summary>
public interface IStandaloneJobHandler
{
    /// <summary>Decide se este handler é o responsável por processar o job.</summary>
    bool CanHandle(BackgroundResponseJob job);

    /// <summary>
    /// Processa o job. Deve usar <paramref name="ctx"/> pra escritas
    /// ownership-aware (Complete/Fail/UpdateStep). Retornar normalmente quando
    /// o job atinge estado terminal ou for abortado por <c>jobCt</c>; lançar
    /// exception em caso de erro inesperado (dispatcher captura e marca Failed
    /// permanente).
    /// </summary>
    Task ProcessAsync(BackgroundResponseJob job, IStandaloneJobContext ctx, CancellationToken ct);
}

/// <summary>
/// Capa de escrita ownership-aware exposta aos handlers. Encapsula
/// <c>IBackgroundResponseRepository</c> + <c>podId</c> pra que os handlers
/// não precisem repetir o podId em toda chamada (e não tenham como esquecê-lo).
/// </summary>
public interface IStandaloneJobContext
{
    /// <summary>PodId estável por processo. Visível pros handlers só pra logging.</summary>
    string PodId { get; }

    /// <summary>
    /// Estado terminal final do job, registrado quando o handler chama
    /// <see cref="CompleteAsync"/> (=Completed) ou <see cref="FailAsync"/>
    /// com <c>permanent=true</c> / <c>nextAttemptAt=null</c> (=Failed).
    /// <c>null</c> quando o job ainda não atingiu terminal (retry agendado
    /// ou ainda processando). Usado pelo dispatcher pra emitir métricas
    /// sem re-ler o job do DB.
    /// </summary>
    BackgroundResponseStatus? LastTerminalStatus { get; }

    /// <summary>Quando o terminal foi escrito. <c>null</c> se ainda não terminal.</summary>
    DateTime? LastTerminalAt { get; }

    /// <summary>
    /// Marca o job como Completed. Retorna false quando o lease já foi
    /// roubado (caller deve abortar sem reescrever estado).
    /// </summary>
    Task<bool> CompleteAsync(string? output, CancellationToken ct);

    /// <summary>
    /// Marca falha com retry agendado ou permanente. Retorna false quando o
    /// lease já foi roubado.
    /// </summary>
    Task<bool> FailAsync(string lastError, DateTime? nextAttemptAt, bool permanent, CancellationToken ct);

    /// <summary>
    /// Re-enfileira por backpressure de capacidade (um gate de concorrência
    /// externo estava cheio e o job não chegou a processar) SEM consumir
    /// tentativa nem marcar terminal. Use no lugar de <see cref="FailAsync"/>
    /// quando o motivo é "sem capacidade agora", não "o processamento falhou" —
    /// assim a saturação não caminha pro teto de tentativas. Retorna false
    /// quando o lease já foi roubado.
    /// </summary>
    Task<bool> DeferAsync(string reason, DateTime nextAttemptAt, CancellationToken ct);

    Task UpdateStepAsync(string? step, CancellationToken ct);
    Task UpdateIngestionContextAsync(string? ingestionContextJson, CancellationToken ct);
    Task SetExecutionIdAsync(string executionId, CancellationToken ct);
}
