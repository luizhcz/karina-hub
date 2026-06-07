namespace EfsAiHub.Core.Abstractions.Observability;

/// <summary>
/// Agregados de entrega de webhooks (<c>aihub.webhook_deliveries</c>) por
/// projeto. Mostra taxa de entrega, latência (CreatedAt → DeliveredAt),
/// breakdown por status HTTP e top hosts.
/// </summary>
public sealed class WebhookDeliveryOverview
{
    public required string ProjectId { get; init; }
    public DateTime PeriodFrom { get; init; }
    public DateTime PeriodTo { get; init; }

    public long TotalDeliveries { get; init; }
    public long Delivered { get; init; }
    public long Failed { get; init; }
    public long Pending { get; init; }
    public long Delivering { get; init; }

    /// <summary>Entregues / (Entregues + Falhos). 0..1.</summary>
    public double DeliveryRate { get; init; }

    /// <summary>p95 da latência CreatedAt → DeliveredAt em ms, sobre entregas resolvidas.</summary>
    public double P95DeliveryMs { get; init; }

    /// <summary>Breakdown por status HTTP do receiver. NULL colapsa em "no-response" (timeout, DNS, etc.).</summary>
    public required IReadOnlyList<WebhookStatusCodeRow> StatusCodes { get; init; }

    /// <summary>Top hosts por número de tentativas. Hostname extraído de Url via regex no SQL.</summary>
    public required IReadOnlyList<WebhookHostRow> Hosts { get; init; }
}

public sealed class WebhookStatusCodeRow
{
    /// <summary>"2xx", "4xx", "5xx" ou "no-response" (NULL LastResponseCode).</summary>
    public required string Bucket { get; init; }
    public long Count { get; init; }
}

public sealed class WebhookHostRow
{
    public required string Host { get; init; }
    public long Total { get; init; }
    public long Delivered { get; init; }
    public long Failed { get; init; }
    public double DeliveryRate { get; init; }
}

public sealed class WebhookDeliveryTimeseriesBucket
{
    public DateTime Bucket { get; init; }
    public long Created { get; init; }
    public long Delivered { get; init; }
    public long Failed { get; init; }
    public double P95DeliveryMs { get; init; }
}
