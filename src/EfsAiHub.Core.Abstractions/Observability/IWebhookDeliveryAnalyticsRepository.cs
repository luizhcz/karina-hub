namespace EfsAiHub.Core.Abstractions.Observability;

/// <summary>
/// Agregações de <c>aihub.webhook_deliveries</c> por projeto. Não há matview;
/// query é direta sobre a tabela com índice em (Status, CreatedAt). Webhook
/// delivery service hoje emite 1 row por tentativa (sem retry implícito) —
/// agregar por JobId pra distinct deliveries seria diferente, mas a UI quer
/// taxa de tentativa, então o agregado simples já reflete realidade.
/// </summary>
public interface IWebhookDeliveryAnalyticsRepository
{
    Task<WebhookDeliveryOverview> GetOverviewAsync(
        string projectId, DateTime from, DateTime to, CancellationToken ct = default);

    Task<IReadOnlyList<WebhookDeliveryTimeseriesBucket>> GetTimeseriesAsync(
        string projectId, DateTime from, DateTime to, string groupBy, CancellationToken ct = default);
}
