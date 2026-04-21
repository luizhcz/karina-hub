namespace EfsAiHub.Core.Abstractions.Execution;

/// <summary>
/// Origem de uma execução de workflow.
/// </summary>
public enum ExecutionSource
{
    /// <summary>Execução disparada pela API de admin.</summary>
    Api,

    /// <summary>Execução disparada via chat.</summary>
    Chat,

    /// <summary>Execução disparada por webhook externo.</summary>
    Webhook,

    /// <summary>Execução disparada por comunicação Agent-to-Agent.</summary>
    A2A
}
