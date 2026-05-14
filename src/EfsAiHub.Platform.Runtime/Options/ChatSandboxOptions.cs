namespace EfsAiHub.Platform.Runtime.Options;

/// <summary>
/// Opções de configuração do subsistema Chat Sandbox. Bind em appsettings via
/// seção <c>ChatSandbox</c>. Vive em <c>Platform.Runtime/Options</c> pra ficar
/// acessível tanto a <c>Host.Api</c> (ChatSandboxService consome) quanto a
/// <c>Host.Worker</c> (ChatSandboxCleanupService consome) sem dependência
/// cross-host.
/// </summary>
public sealed class ChatSandboxOptions
{
    /// <summary>TTL de session ativa antes de virar candidata a Expired pelo cleanup.</summary>
    public int SessionTtlDays { get; set; } = 7;

    /// <summary>Periodicidade do cleanup em segundos. 0 desabilita o background service.</summary>
    public int CleanupIntervalSeconds { get; set; } = 3600;

    /// <summary>Limite de sessions cleanadas por ciclo (paginação).</summary>
    public int CleanupBatchSize { get; set; } = 100;
}
