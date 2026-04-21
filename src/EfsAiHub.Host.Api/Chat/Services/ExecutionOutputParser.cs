using System.Text.Json;

namespace EfsAiHub.Host.Api.Services;

/// <summary>
/// Resultado do parse de um output terminal de workflow: separa o texto exibível
/// do payload estruturado (quando o agente produz algo do tipo
/// { "message": "...", "output": {...} } ou um JSON genérico de structured output).
/// </summary>
public readonly record struct ParsedExecutionOutput(string TextContent, JsonDocument? StructuredOutput);

/// <summary>
/// Centraliza a lógica de interpretar o <c>finalOutput</c> de um workflow.
/// Extraído de <see cref="ConversationService"/> para isolar a parsing strategy
/// e permitir testes unitários diretos.
///
/// Estratégia de parse (em ordem de prioridade):
/// 1. Pattern { "message": "...", "output": {...} } → separa texto e payload estruturado
/// 2. JSON genérico (objeto ou array sem "message") → structured output do agente
/// 3. Texto puro → retorna como está, sem StructuredOutput
/// </summary>
public static class ExecutionOutputParser
{
    public static ParsedExecutionOutput Parse(string finalOutput)
    {
        if (string.IsNullOrWhiteSpace(finalOutput))
            return new ParsedExecutionOutput(finalOutput, null);

        try
        {
            using var doc = JsonDocument.Parse(finalOutput);

            // 1. Pattern explícito: { "message": "...", "output": {...} }
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("message", out var msgEl))
            {
                var textContent = msgEl.GetString() ?? finalOutput;

                JsonDocument structured;
                if (doc.RootElement.TryGetProperty("output", out var outEl) &&
                    outEl.ValueKind != JsonValueKind.Null)
                {
                    structured = JsonDocument.Parse(outEl.GetRawText());
                }
                else
                {
                    structured = JsonDocument.Parse(finalOutput);
                }

                return new ParsedExecutionOutput(textContent, structured);
            }

            // 2. JSON genérico (structured output do agente — objeto ou array)
            if (doc.RootElement.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
            {
                var structured = JsonDocument.Parse(finalOutput);
                return new ParsedExecutionOutput(finalOutput, structured);
            }

            // JSON primitivo (string, number, etc.) — trata como texto
            return new ParsedExecutionOutput(finalOutput, null);
        }
        catch (JsonException)
        {
            return new ParsedExecutionOutput(finalOutput, null);
        }
    }
}
