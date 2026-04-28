namespace EfsAiHub.Platform.Runtime.Evaluation;

/// <summary>Configuração do subsistema de avaliação. Bind em <c>appsettings</c> sob <c>"Evaluation"</c> (ADR 0015).</summary>
public sealed class EvaluationOptions
{
    public const string SectionName = "Evaluation";

    /// <summary>Kill-switch SRE: false faz runner/reaper/autotrigger virarem no-op e endpoints API retornarem 503.</summary>
    public bool Enabled { get; set; } = true;

    public int MaxParallelCases { get; set; } = 4;

    /// <summary>Evaluators paralelos por case. MaxParallelCases × MaxConcurrentEvaluators deve ficar abaixo de HttpClient.MaxConnectionsPerServer (default 16).</summary>
    public int MaxConcurrentEvaluators { get; set; } = 3;

    /// <summary>Tempo máximo sem heartbeat antes do reaper marcar a run como Failed.</summary>
    public int HeartbeatTimeoutSeconds { get; set; } = 300;

    public int HeartbeatIntervalSeconds { get; set; } = 30;

    /// <summary>Polling fallback quando NOTIFY <c>eval_run_cancelled</c> está indisponível.</summary>
    public int CancelPollFallbackSeconds { get; set; } = 5;

    public int SchedulerPollSeconds { get; set; } = 5;

    public int ReaperIntervalSeconds { get; set; } = 60;

    /// <summary>Truncamento do EvaluationResult.OutputContent; integral fica em llm_token_usage.OutputContent correlacionado por ExecutionId='eval:{RunId}'.</summary>
    public int OutputContentMaxChars { get; set; } = 8 * 1024;
}
