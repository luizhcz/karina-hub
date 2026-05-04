namespace EfsAiHub.Core.Agents.GenericTools;

/// <summary>
/// Envelope retornado pelo executor pra cada chamada de Generic Tool. Em qualquer
/// falha (rede, timeout, status≥400, parse) <see cref="Success"/> é false e
/// <see cref="Error"/> traz mensagem genérica — detalhe técnico fica no log Warning.
/// </summary>
public sealed record ToolExecutionResult(bool Success, object? Data, string? Error)
{
    public static ToolExecutionResult Ok(object? data) => new(true, data, null);

    public static ToolExecutionResult Fail(string message) => new(false, null, message);
}
