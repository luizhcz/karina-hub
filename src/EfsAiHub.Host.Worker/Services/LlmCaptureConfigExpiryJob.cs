using EfsAiHub.Core.Agents.Capture;
using EfsAiHub.Platform.Runtime.Services;

namespace EfsAiHub.Host.Worker.Services;

/// <summary>
/// Job que zera <c>Enabled=true</c> quando <c>ExpiresAt</c> venceu — auto-off
/// da captura LLM. Admin liga via UI com TTL ("4h"); esse job garante que
/// captura para sozinha mesmo se admin esquecer ou perder acesso.
///
/// Roda a cada 5min (granularidade fina o suficiente pra TTLs curtos como
/// 1h sem martelar o DB).
/// </summary>
public sealed class LlmCaptureConfigExpiryJob : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<LlmCaptureConfigExpiryJob> _logger;

    public LlmCaptureConfigExpiryJob(
        IServiceScopeFactory scopeFactory,
        ILogger<LlmCaptureConfigExpiryJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[LlmCaptureConfigExpiry] Started — poll {Interval}.", PollInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            await RunOnceSafe(stoppingToken);
            try
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (TaskCanceledException) { break; }
        }

        _logger.LogInformation("[LlmCaptureConfigExpiry] Stopped.");
    }

    private async Task RunOnceSafe(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<ILlmCaptureConfigRepository>();
            var service = scope.ServiceProvider.GetRequiredService<LlmCaptureConfigService>();

            var current = await repo.GetAsync(ct);
            if (!current.Enabled) return;
            if (current.ExpiresAt is null) return;
            if (current.ExpiresAt.Value > DateTime.UtcNow) return;

            var expired = current with
            {
                Enabled = false,
                ExpiresAt = null,
                EnabledBy = null,
                EnabledAt = null,
                UpdatedAt = DateTime.UtcNow,
            };
            await service.UpdateAsync(expired, ct);

            _logger.LogInformation(
                "[LlmCaptureConfigExpiry] Captura LLM expirou (era ON desde {EnabledAt}). Auto-off aplicado.",
                current.EnabledAt);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[LlmCaptureConfigExpiry] Falha no sweep — tentará novamente em {Interval}.", PollInterval);
        }
    }
}
