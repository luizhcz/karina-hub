using System.Text.Json;

namespace EfsAiHub.Core.Orchestration.Workflows;

/// <summary>
/// Predicado tipado avaliado sobre o output JSON do nó produtor.
/// Substitui o substring match legado: predicate é estruturado (Path → Operator → Value)
/// e avaliado em runtime parseando o output do nó como JsonDocument.
///
/// Usado em edges <c>Conditional</c> (1 predicate por edge) e em cada case de <c>Switch</c>.
/// Origem precisa expor schema JSON declarado (<c>Agent.StructuredOutput.ResponseFormat == "json_schema"</c>
/// ou <c>Executor.Register&lt;TIn,TOut&gt;</c>) — ausência de schema bloqueia Conditional/Switch
/// na validação (regra de negócio absoluta, ver <c>EnsureInvariants</c>).
/// </summary>
/// <param name="Path">
/// JSONPath subset suportado: <c>$.field</c>, <c>$.a.b</c>, <c>$.list[N]</c>, <c>$.results[0].status</c>.
/// Sem wildcards, filtros, slicing ou índice negativo. Validado no save.
/// </param>
/// <param name="Operator">Operador a aplicar entre o valor resolvido pelo Path e o Value.</param>
/// <param name="Value">
/// Valor de comparação como JsonElement preservando tipo (number ≠ string-de-number).
/// Null quando Operator é unário (<see cref="EdgeOperator.IsNull"/>/<see cref="EdgeOperator.IsNotNull"/>).
/// Array para <see cref="EdgeOperator.In"/>/<see cref="EdgeOperator.NotIn"/>.
/// </param>
/// <param name="ValueType">
/// Hint do tipo do campo no schema. <c>Auto</c> = detecta via <c>JsonElement.ValueKind</c> em runtime.
/// Validado no save: combinações inválidas (ex: Gt com String) → 400 InvalidOperatorForType.
/// </param>
/// <param name="SourceSchemaVersion">
/// Hash sha256 (12 hex) do schema do produtor no momento da criação do edge. Usado pra
/// detectar schema drift e invalidar cache de definição. Null em edges criados antes de PR 3.
/// </param>
public sealed record EdgePredicate(
    string Path,
    EdgeOperator Operator,
    JsonElement? Value = null,
    EdgePredicateValueType ValueType = EdgePredicateValueType.Auto,
    string? SourceSchemaVersion = null);

/// <summary>
/// Operadores aplicáveis a um campo do schema (resolvido via Path).
/// Validados no save contra o tipo do campo — combinações inválidas retornam 400.
/// </summary>
public enum EdgeOperator
{
    Eq,
    NotEq,
    Gt,
    Gte,
    Lt,
    Lte,
    Contains,
    StartsWith,
    EndsWith,
    MatchesRegex,
    In,
    NotIn,
    IsNull,
    IsNotNull
}

/// <summary>
/// Hint do tipo esperado do campo. <c>Auto</c> deixa runtime detectar via JsonValueKind;
/// valores explícitos permitem validação cruzada (Operator vs Type) já no save do workflow.
/// </summary>
public enum EdgePredicateValueType
{
    Auto,
    String,
    Number,
    Integer,
    Boolean,
    Enum
}
