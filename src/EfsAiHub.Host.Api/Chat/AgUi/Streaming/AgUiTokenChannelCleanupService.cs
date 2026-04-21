namespace EfsAiHub.Host.Api.Chat.AgUi.Streaming;

/// <summary>
/// Remove periodicamente channels SSE órfãos que não foram limpos
/// pelo finally do AgUiSseHandler (ex: desconexão abrupta do cliente).
/// </summary>
public sealed class AgUiTokenChannelCleanupService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(30);

    private readonly AgUiTokenChannel _tokenChannel;
    private readonly ILogger<AgUiTokenChannelCleanupService> _logger;

    public AgUiTokenChannelCleanupService(
        AgUiTokenChannel tokenChannel,
        ILogger<AgUiTokenChannelCleanupService> logger)
    {
        _tokenChannel = tokenChannel;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            var removed = _tokenChannel.RemoveStale(MaxAge);
            if (removed > 0)
                _logger.LogInformation("[AgUiCleanup] Removidos {Count} channels órfãos (>{MaxAge} min). Ativos: {Active}.",
                    removed, (int)MaxAge.TotalMinutes, _tokenChannel.Count);
        }
    }
}
