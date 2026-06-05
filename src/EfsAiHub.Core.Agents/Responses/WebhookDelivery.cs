namespace EfsAiHub.Core.Agents.Responses;

/// <summary>
/// Entrega de webhook do pool standalone. Criada quando um
/// <see cref="BackgroundResponseJob"/> entra em estado terminal
/// (<see cref="BackgroundResponseStatus.Completed"/> ou
/// <see cref="BackgroundResponseStatus.Failed"/>) E carrega
/// <see cref="BackgroundResponseJob.CallbackTarget"/>.
///
/// Sem retry no design atual — um único POST por delivery. Status terminal
/// já no primeiro response do destino — <see cref="Delivered"/> pra 2xx ou
/// <see cref="Failed"/> pra qualquer outra resposta (timeouts, network
/// errors). <see cref="Delivering"/> é um marker interno: indica que algum
/// pod fez lease da row e está rodando o POST. Lease órfão (pod morreu) é
/// promovido pra <see cref="Failed"/> via sweep no startup do worker.
/// </summary>
public enum WebhookDeliveryStatus
{
    Pending,
    Delivering,
    Delivered,
    Failed
}

public sealed class WebhookDelivery
{
    public string DeliveryId { get; set; } = Guid.NewGuid().ToString("N");
    public string JobId { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// Segredo usado pra assinar o payload (HMAC-SHA256, header
    /// <c>X-EfsAiHub-Signature</c>). Armazenado em TEXTO CLARO no design atual.
    /// Operadores com SELECT na tabela enxergam o segredo — encriptar via
    /// Data Protection é tech debt rastreado pra antes de prod externa.
    /// </summary>
    public string? HmacSecret { get; set; }

    /// <summary>Headers adicionais propagados pro POST de entrega (JSONB).</summary>
    public IReadOnlyDictionary<string, string>? Headers { get; set; }

    public WebhookDeliveryStatus Status { get; set; } = WebhookDeliveryStatus.Pending;
    public int? LastResponseCode { get; set; }
    public string? LastError { get; set; }
    public DateTime? DeliveredAt { get; set; }

    public string ProjectId { get; set; } = "default";
    public string TenantId { get; set; } = "default";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public interface IWebhookDeliveryRepository
{
    Task<WebhookDelivery?> GetAsync(string deliveryId, CancellationToken ct = default);
    Task<IReadOnlyList<WebhookDelivery>> ListByJobAsync(string jobId, CancellationToken ct = default);

    /// <summary>
    /// Lê N entregas <c>Status='Pending'</c> e atualiza-as pra <c>Delivering</c>
    /// num único UPDATE atômico (subquery com <c>FOR UPDATE SKIP LOCKED</c>).
    /// O update persiste o lease — N réplicas pegam rows disjuntas sem dupla
    /// entrega. Worker finaliza cada delivery via <see cref="MarkDeliveredAsync"/>
    /// ou <see cref="MarkFailedAsync"/>.
    /// </summary>
    Task<IReadOnlyList<WebhookDelivery>> LeasePendingAsync(int batchSize, CancellationToken ct = default);

    Task MarkDeliveredAsync(string deliveryId, int statusCode, CancellationToken ct = default);
    Task MarkFailedAsync(string deliveryId, string error, int? statusCode, CancellationToken ct = default);

    /// <summary>
    /// Promove rows em <c>Delivering</c> com <c>UpdatedAt</c> mais antigo que
    /// <paramref name="stuckThreshold"/> pra <c>Failed</c>. Roda no startup do
    /// worker pra resgatar leases de pods que morreram no meio do POST.
    /// Retorna o número de rows promovidas.
    /// </summary>
    Task<int> ReclaimStuckDeliveringAsync(TimeSpan stuckThreshold, CancellationToken ct = default);
}
