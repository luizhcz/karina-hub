using System.Text.Json;

namespace EfsAiHub.Core.Orchestration.Workflows;

/// <summary>
/// Avalia um <see cref="EdgePredicate"/> contra o output (string) do nó produtor.
/// Implementação parseia a string como JSON, resolve o <c>Path</c> via JSONPath subset,
/// aplica o <see cref="EdgeOperator"/> e retorna bool.
/// JSON inválido / Path inexistente → false (sem derrubar execução, com warning + métrica).
/// </summary>
public interface IEdgePredicateEvaluator
{
    /// <summary>
    /// Avalia o predicate contra o output. <paramref name="parsedDocument"/> permite cache
    /// por step (Switch com 5 cases parseia 1× só) — passe null pra forçar parse interno.
    /// </summary>
    /// <returns>True se o predicate casa, false caso contrário (incluindo JSON inválido / Path missing).</returns>
    bool Evaluate(EdgePredicate predicate, string? output, JsonDocument? parsedDocument = null);
}
