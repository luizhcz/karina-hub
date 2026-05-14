namespace EfsAiHub.Platform.Runtime.Options;

/// <summary>
/// Opções do subsistema Agent Sandbox. Bind em appsettings via seção
/// <c>AgentSandbox</c> (legado <c>ChatSandbox</c> também aceito enquanto a
/// rename está em curso — config bind é case-insensitive por key, não por
/// seção). Vive em <c>Platform.Runtime/Options</c> pra acesso cross-host
/// (Host.Api consome o AgentSandboxService, Host.Worker o cleanup).
/// </summary>
public sealed class AgentSandboxOptions
{
    /// <summary>TTL de session ativa antes de virar candidata a Expired pelo cleanup.</summary>
    public int SessionTtlDays { get; set; } = 7;

    /// <summary>Periodicidade do cleanup em segundos. 0 desabilita o background service.</summary>
    public int CleanupIntervalSeconds { get; set; } = 3600;

    /// <summary>Limite de sessions cleanadas por ciclo (paginação).</summary>
    public int CleanupBatchSize { get; set; } = 100;
}
