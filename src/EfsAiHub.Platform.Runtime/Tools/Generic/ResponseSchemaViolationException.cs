using System.Text.Json;
using EfsAiHub.Core.Abstractions.Persistence;

namespace EfsAiHub.Platform.Runtime.Tools.Generic;

/// <summary>
/// Lançada pelo <see cref="GenericToolExecutor"/> quando o response do
/// endpoint não casa com o <c>OutputSchema</c> declarado e o modo da tool
/// é <c>Project</c> ou <c>Strict</c>. O framework de tool-calling
/// (Microsoft.Agents.AI) captura e serializa pra o LLM — o LLM vê o
/// <see cref="ToJson"/> payload e decide próximo passo (retry, fallback,
/// reportar ao user).
/// </summary>
public sealed class ResponseSchemaViolationException : Exception
{
    public string ToolName { get; }
    public IReadOnlyList<string> Details { get; }

    public ResponseSchemaViolationException(string toolName, IReadOnlyList<string> details)
        : base(BuildMessage(toolName, details))
    {
        ToolName = toolName;
        Details = details;
    }

    /// <summary>
    /// Serializa em JSON estruturado pra o LLM consumir. Mantém formato
    /// previsível mesmo se a exception virar string ao trafegar pelo
    /// framework de tool-calling.
    /// </summary>
    public string ToJson() =>
        JsonSerializer.Serialize(new
        {
            error = "response_schema_violation",
            tool = ToolName,
            details = Details,
            hint = "Endpoint retornou shape inesperado. Não invente dados; tente reformular a chamada ou reporte ao usuário.",
        }, JsonDefaults.Domain);

    private static string BuildMessage(string toolName, IReadOnlyList<string> details)
    {
        var preview = details.Count > 0 ? string.Join("; ", details) : "(sem detalhes)";
        return $"Response da tool '{toolName}' não casa com o OutputSchema declarado: {preview}";
    }
}
