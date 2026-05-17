using System.Text.Json.Serialization;

namespace EfsAiHub.Core.Agents.GenericTools;

/// <summary>
/// Define como o response da tool é validado e projetado contra o
/// <see cref="GenericTool.OutputSchema"/> antes de chegar ao LLM e ao
/// tester. Persiste como string no Postgres (`Off`, `Project`, `Strict`).
///
/// <list type="bullet">
///   <item><b>Off</b>: bypass total. Comportamento legacy — response cru vai
///   pro LLM. Default pra tools cadastradas antes da feature de projeção.</item>
///   <item><b>Project</b>: drop silencioso de campos extras (não declarados
///   no schema). Fail-loud em <c>required</c> ausente ou type mismatch. O LLM
///   só vê o shape declarado pelo admin.</item>
///   <item><b>Strict</b>: igual ao Project + fail-loud também em qualquer
///   campo extra. Equivalente a injetar <c>additionalProperties: false</c>
///   no schema declarado. Usar quando o endpoint tem contrato estável e
///   campos novos indicam mudança que precisa de revisão.</item>
/// </list>
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum OutputProjectionMode
{
    Off = 0,
    Project = 1,
    Strict = 2,
}
