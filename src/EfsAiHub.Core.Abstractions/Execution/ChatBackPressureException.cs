namespace EfsAiHub.Core.Abstractions.Execution;

/// <summary>
/// Lançada quando o Chat Path atinge o limite global de execuções simultâneas
/// (<see cref="Infrastructure.Configuration.WorkflowEngineOptions.ChatMaxConcurrentExecutions"/>).
/// O controller deve mapear para HTTP 429.
/// </summary>
public sealed class ChatBackPressureException : Exception
{
    public ChatBackPressureException(string message) : base(message) { }
}
