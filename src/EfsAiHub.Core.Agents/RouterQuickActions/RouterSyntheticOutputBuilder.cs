using System.Text.Json;
using EfsAiHub.Core.Abstractions.Persistence;

namespace EfsAiHub.Core.Agents.RouterQuickActions;

/// <summary>
/// Constrói o JSON sintético que o Router emite quando o bypass é acionado.
/// Schema idêntico ao que o LLM produziria — assim os middlewares downstream
/// (RouterDecisionTelemetry, predicados de Edge, etc) operam sem distinguir
/// fluxo bypass vs LLM real.
/// </summary>
public static class RouterSyntheticOutputBuilder
{
    /// <summary>
    /// Schema produzido (compatível com RouterDefaults.OutputSchemaJson):
    /// <code>
    /// {
    ///   "intent": "X",
    ///   "confidence": 1.0,
    ///   "reason": "Quick action: pattern '...'",
    ///   "candidate_intents": [],
    ///   "operationalMemory": {
    ///     "last_intent": "X",
    ///     "last_reason": "Quick action: pattern '...'",
    ///     "clarification_depth": 0,
    ///     "last_candidate_intents": []
    ///   }
    /// }
    /// </code>
    /// </summary>
    public static string Build(string intent, string pattern)
    {
        var reason = $"Quick action: pattern '{pattern}'";
        var payload = new
        {
            intent,
            confidence = 1.0,
            reason,
            candidate_intents = Array.Empty<object>(),
            operationalMemory = new
            {
                last_intent = intent,
                last_reason = reason,
                clarification_depth = 0,
                last_candidate_intents = Array.Empty<string>()
            }
        };
        return JsonSerializer.Serialize(payload, JsonDefaults.Domain);
    }
}
