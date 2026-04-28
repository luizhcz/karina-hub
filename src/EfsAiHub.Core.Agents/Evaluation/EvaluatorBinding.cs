using System.Text.Json;

namespace EfsAiHub.Core.Agents.Evaluation;

// BindingIndex permite o mesmo evaluator declarado 2x na config com
// parâmetros diferentes — entra na PK de EvaluationResult sem colisão.
public sealed record EvaluatorBinding(
    EvaluatorKind Kind,
    string Name,
    JsonDocument? Params,
    bool Enabled,
    double Weight,
    int BindingIndex);
