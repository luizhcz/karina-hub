namespace EfsAiHub.Platform.Runtime.Tools.Generic;

/// <summary>
/// Resultado do <see cref="GenericResponseProjector"/>. Quando o modo da
/// tool é <c>Off</c>, <see cref="Bypassed"/>=true e <see cref="Projected"/>
/// recebe o input intacto. Em <c>Project</c>/<c>Strict</c>, sucesso devolve
/// o response filtrado; violações populam <see cref="Errors"/> (cap de 5
/// + sufixo "...and N more" quando excede).
/// </summary>
public sealed record ProjectionResult(
    object? Projected,
    IReadOnlyList<string> Errors,
    bool Bypassed,
    TruncationInfo? Truncation = null)
{
    public bool HasErrors => Errors.Count > 0;
    public bool Success => !HasErrors;

    public static ProjectionResult AsBypass(object? parsed) =>
        new(parsed, Array.Empty<string>(), Bypassed: true);

    public static ProjectionResult AsSuccess(object? projected, TruncationInfo? truncation = null) =>
        new(projected, Array.Empty<string>(), Bypassed: false, Truncation: truncation);

    public static ProjectionResult AsFailure(IReadOnlyList<string> errors) =>
        new(Projected: null, errors, Bypassed: false);
}

/// <summary>
/// Metadados de truncamento da projeção quando um array excede
/// <see cref="GenericResponseProjector.MaxArrayItemsProjected"/>. Fica FORA
/// do <see cref="ProjectionResult.Projected"/> pra preservar o shape declarado
/// no schema do array.items — o tester usa pra renderizar aviso; em runtime
/// o LLM recebe o array projetado limpo + métrica/log do executor reporta
/// o truncamento separadamente.
/// </summary>
public sealed record TruncationInfo(int OriginalCount, int ProjectedCount);
