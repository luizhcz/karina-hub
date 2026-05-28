namespace EfsAiHub.Platform.Runtime.Tools.Generic.Schema;

/// <summary>
/// Aviso emitido durante a normalização. Sai junto na resposta do save pra
/// que a UI mostre um banner — autor saber exatamente o que foi alterado
/// (ex.: <c>oneOf</c> mesclado, <c>$ref</c> ciclo virou <c>{}</c>).
/// </summary>
/// <param name="Code">Identificador estável do tipo de warning (ex.: "ref.cycle"). Usado em métricas.</param>
/// <param name="Path">JSON Pointer-like do nó onde o warning foi emitido (ex.: "/properties/x/items").</param>
/// <param name="Message">Texto humano em PT-BR descrevendo o que aconteceu.</param>
public sealed record NormalizationWarning(string Code, string Path, string Message);
