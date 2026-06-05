namespace EfsAiHub.Platform.Runtime.Configuration;

/// <summary>
/// Configuração do worker de entrega de webhooks de jobs standalone.
///
/// Decisão v1: SEM retry — uma única tentativa POST. Falha de rede ou status
/// não-2xx vira <c>Failed</c> imediato. Cliente que perdeu o webhook precisa
/// pollar <c>GET /responses/{jobId}</c> pra reconciliar.
/// </summary>
public sealed class WebhookDeliveryOptions
{
    public const string SectionName = "WebhookDelivery";

    /// <summary>
    /// Liga/desliga o worker. Default <c>true</c> — o worker é gratuito
    /// quando não há rows em <c>webhook_deliveries</c> (índice partial filtrado).
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Tamanho do lote por iteração. Default 25.</summary>
    public int BatchSize { get; init; } = 25;

    /// <summary>Timeout do POST de delivery, em segundos. Default 10.</summary>
    public int DeliveryTimeoutSeconds { get; init; } = 10;

    /// <summary>Intervalo entre ciclos quando a fila está vazia. Default 5s.</summary>
    public int PollIdleSeconds { get; init; } = 5;

    /// <summary>
    /// Máximo de POSTs concorrentes por pod. Protege thread pool / connections
    /// quando a fila enche (operadores configurando 500 deliveries de uma vez).
    /// Default 10 — alinhado com volume típico, não compete com Chat path.
    /// </summary>
    public int MaxConcurrentDeliveries { get; init; } = 10;

    /// <summary>
    /// Idade máxima de uma row <c>Status='Delivering'</c> antes de virar
    /// <c>Failed</c> automaticamente. Cobre o caso de pod morto no meio do
    /// POST — sem isso, o lease fica preso pra sempre. Default 5min = 6×
    /// <see cref="DeliveryTimeoutSeconds"/> default, dá margem pra GC pauses
    /// e redes lentas.
    /// </summary>
    public int DeliveringTimeoutSeconds { get; init; } = 300;
}
