using System.Text.Json.Serialization;

namespace EfsAiHub.Core.Agents.GenericTools;

/// <summary>
/// Define como o response da tool é projetado contra o
/// <see cref="GenericTool.OutputSchema"/> antes de chegar ao LLM e ao
/// tester. Persiste como string no Postgres.
///
/// <list type="bullet">
///   <item><b>Off</b>: bypass total. Usado apenas quando
///   <c>OutputContentType=Text</c> — texto puro não tem shape pra projetar.</item>
///   <item><b>Project</b>: drop silencioso de campos extras (não declarados
///   no schema). Fail-loud em <c>required</c> ausente ou type mismatch. O LLM
///   só vê o shape declarado. Obrigatório quando
///   <c>OutputContentType in {Json, Csv}</c>.</item>
/// </list>
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum OutputProjectionMode
{
    Off = 0,
    Project = 1,
}
