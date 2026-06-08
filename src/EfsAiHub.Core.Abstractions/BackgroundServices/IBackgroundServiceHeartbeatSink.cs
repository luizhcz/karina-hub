namespace EfsAiHub.Core.Abstractions.BackgroundServices;

/// <summary>
/// Coletor de heartbeats de hosted services. Cada serviço chama
/// <see cref="Started"/> ao entrar em ExecuteAsync e
/// <see cref="RecordSuccess"/> / <see cref="RecordError"/> em cada iteração
/// do loop (ou em cada evento processado, para serviços event-driven).
///
/// Contrato:
/// - Chamadas são hot path: implementação DEVE ser O(1) e lock-free.
/// - <see cref="Started"/> é idempotente — chamar 2x não cria entradas duplicadas.
/// - <see cref="GetAll"/> retorna snapshot imutável seguro pra iteração concorrente.
/// </summary>
public interface IBackgroundServiceHeartbeatSink
{
    /// <summary>
    /// Marca o início da execução do serviço (StartedAtUtc). Idempotente: subsequentes
    /// chamadas com o mesmo nome NÃO sobrescrevem StartedAtUtc — assim ficamos sabendo
    /// "rodando desde X" mesmo se o serviço faz init parcial entre ticks.
    /// </summary>
    void Started(string name, DateTimeOffset utcNow);

    /// <summary>Registra tick bem-sucedido. Atualiza LastTickAtUtc + LastSuccessAtUtc + TickCount.</summary>
    void RecordSuccess(string name, DateTimeOffset utcNow);

    /// <summary>
    /// Registra tick com erro. Atualiza LastTickAtUtc + LastErrorAtUtc + LastErrorMessage +
    /// TickCount + ErrorCount.
    /// </summary>
    void RecordError(string name, DateTimeOffset utcNow, Exception error);

    BackgroundServiceHeartbeat? Get(string name);
    IReadOnlyDictionary<string, BackgroundServiceHeartbeat> GetAll();
}
