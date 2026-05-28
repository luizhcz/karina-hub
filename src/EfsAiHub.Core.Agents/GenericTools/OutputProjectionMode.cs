using System.Text.Json.Serialization;

namespace EfsAiHub.Core.Agents.GenericTools;

/// <summary>
/// Modo legado de projeção do response. Derivado do
/// <see cref="OutputContentType"/>: Text vira Off (texto puro não tem shape),
/// Json/Csv vira Project. Hoje o projector sempre projeta quando há schema —
/// o enum sobrevive só pra compat de serialização do Postgres.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum OutputProjectionMode
{
    [Obsolete("Projection é sempre on quando há OutputSchema. Off só sobra como flag interna pra Text content type.")]
    Off = 0,
    Project = 1,
}
