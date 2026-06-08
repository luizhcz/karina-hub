namespace EfsAiHub.Core.Abstractions.BackgroundServices;

/// <summary>
/// Categorias de hosted services. Usadas pra agrupar a tela de admin sem
/// fazer string-matching em cima do Name. Adicionar entrada nova: estender
/// o enum, propagar pro frontend (Workers.tsx → CATEGORY_LABELS).
/// </summary>
public enum BackgroundServiceCategory
{
    /// <summary>Roda uma vez no boot e termina (DatabaseBootstrap, permission validators).</summary>
    Bootstrap,
    /// <summary>Drena Channel/fila de eventos e persiste em batch (TokenUsage, ToolInvocation, etc.).</summary>
    Persistence,
    /// <summary>Dispatcher de fila de jobs ou entrega de webhooks.</summary>
    Dispatcher,
    /// <summary>Reaper que retoma estado pendurado (StuckExecution, StuckLease, HitlRecovery).</summary>
    Recovery,
    /// <summary>Roda avaliações agendadas.</summary>
    Evaluation,
    /// <summary>TTLs e housekeeping (cleanups, retention).</summary>
    Cleanup,
    /// <summary>LISTEN/NOTIFY entre pods.</summary>
    Messaging,
    /// <summary>Hot-reload de configs de runtime (Blocklist, permissions).</summary>
    Guards,
}

public class BackgroundServiceDescriptor
{
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required string Lifecycle { get; init; }
    public TimeSpan? Interval { get; init; }
    public required Type ServiceType { get; init; }
    public required BackgroundServiceCategory Category { get; init; }
}
