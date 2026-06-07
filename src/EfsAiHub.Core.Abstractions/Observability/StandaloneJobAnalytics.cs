namespace EfsAiHub.Core.Abstractions.Observability;

/// <summary>
/// Agregados de uso/saúde da fila de standalone jobs (
/// <c>aihub.background_response_jobs</c>) por projeto. Mostra como a fila tá
/// indo: throughput, taxa de sucesso, retries, jobs travados, p95 fila→running
/// e workflow→completed.
/// </summary>
public sealed class StandaloneJobOverview
{
    public required string ProjectId { get; init; }
    public DateTime PeriodFrom { get; init; }
    public DateTime PeriodTo { get; init; }

    /// <summary>Total de jobs criados no período (todos os Status).</summary>
    public long TotalJobs { get; init; }

    public long Completed { get; init; }
    public long Failed { get; init; }
    public long Cancelled { get; init; }
    public long Queued { get; init; }
    public long Running { get; init; }

    /// <summary>Concluídos / (Concluídos + Falhos). 0..1. 0 quando nenhum resolvido.</summary>
    public double SuccessRate { get; init; }

    /// <summary>Soma de <c>Attempt</c> em todos os jobs do período. Útil pra detectar workflows tóxicos.</summary>
    public long TotalAttempts { get; init; }

    /// <summary>Jobs com Attempt > 1 / total. Reflete pressão de retry.</summary>
    public double RetryRate { get; init; }

    /// <summary>Latência fila → running (CreatedAt → StartedAt) em ms. Backlog signal.</summary>
    public double P95QueueMs { get; init; }

    /// <summary>Latência ponta a ponta (CreatedAt → CompletedAt) em ms.</summary>
    public double P95TotalMs { get; init; }

    public required IReadOnlyList<StandaloneJobByWorkflow> Workflows { get; init; }
}

/// <summary>Linha agregada por <c>WorkflowId</c>. Ordenada por total DESC.</summary>
public sealed class StandaloneJobByWorkflow
{
    public required string WorkflowId { get; init; }
    public long Total { get; init; }
    public long Completed { get; init; }
    public long Failed { get; init; }
    public double SuccessRate { get; init; }
    public long TotalAttempts { get; init; }
    public double AvgTotalMs { get; init; }
    public double P95TotalMs { get; init; }
}

/// <summary>Bucket temporal de criação de jobs + sucesso/falha por bucket.</summary>
public sealed class StandaloneJobTimeseriesBucket
{
    public DateTime Bucket { get; init; }
    public long Created { get; init; }
    public long Completed { get; init; }
    public long Failed { get; init; }
    /// <summary>p95 da latência queue → running (StartedAt - CreatedAt) nesse bucket, em ms.</summary>
    public double P95QueueMs { get; init; }
}
