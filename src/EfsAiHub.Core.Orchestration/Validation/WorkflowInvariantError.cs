namespace EfsAiHub.Core.Orchestration.Validation;

/// <summary>
/// Envelope estruturado de erro para violações de invariantes do workflow.
/// Códigos identificam a regra; <c>EdgeIndex</c> permite o frontend renderizar inline
/// no edge correto. <c>Hint</c> orienta o usuário a destravar.
/// </summary>
/// <param name="ErrorCode">Identificador estável da regra violada (ex: <c>EdgeNotAllowedFromTextSource</c>).</param>
/// <param name="Message">Mensagem humana explicando o que aconteceu.</param>
/// <param name="Hint">Sugestão concreta de como resolver (ex: "Torne o agente structured ou troque para Direct").</param>
/// <param name="EdgeIndex">Índice do edge afetado em <c>WorkflowDefinition.Edges</c>; null para erros não vinculados a edge específico.</param>
public sealed record WorkflowInvariantError(
    string ErrorCode,
    string Message,
    string? Hint = null,
    int? EdgeIndex = null);

/// <summary>
/// Códigos canônicos de violações de invariantes. Centralizar aqui evita typos
/// entre validator, controller e tests.
/// </summary>
public static class WorkflowErrorCodes
{
    /// <summary>Conditional/Switch saindo de origem sem schema declarado (regra de negócio absoluta).</summary>
    public const string EdgeNotAllowedFromTextSource = "EdgeNotAllowedFromTextSource";

    /// <summary>Conditional sem Predicate, ou Switch case não-default sem Predicate.</summary>
    public const string EdgePredicateRequired = "EdgePredicateRequired";

    /// <summary>JSONPath fora do subset suportado ($, $.field, $.a.b, $.list[N]).</summary>
    public const string InvalidJsonPath = "InvalidJsonPath";

    /// <summary>Combinação (EdgePredicateValueType, EdgeOperator) inválida (ex: Gt em string).</summary>
    public const string InvalidOperatorForType = "InvalidOperatorForType";

    /// <summary>Switch vazio (sem case nem default).</summary>
    public const string SwitchHasNoCaseOrDefault = "SwitchHasNoCaseOrDefault";

    /// <summary>
    /// <c>Path</c> resolve campo inexistente no schema atual do produtor.
    /// <b>Reservado para PR 3</b> — neste PR só validamos sintaxe do JSONPath; cross-check
    /// contra o schema atual do agente/executor produtor depende dos schemas estarem
    /// disponíveis via API (PR 3 expõe <c>OutputSchema</c> em <c>GET /api/functions</c>).
    /// </summary>
    public const string PathNotFoundInSchema = "PathNotFoundInSchema";
}

/// <summary>
/// Lançada por <c>WorkflowService.ValidateAsync</c> quando há violações de invariantes.
/// Carrega a lista completa pra que o controller monte response 400 com array de erros.
/// </summary>
public sealed class WorkflowInvariantViolationException : Exception
{
    public IReadOnlyList<WorkflowInvariantError> Errors { get; }

    public WorkflowInvariantViolationException(IReadOnlyList<WorkflowInvariantError> errors)
        : base($"Workflow inválido — {errors.Count} violação(ões) de invariante.")
    {
        Errors = errors;
    }
}
