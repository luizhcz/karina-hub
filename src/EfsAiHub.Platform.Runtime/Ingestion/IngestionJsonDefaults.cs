using System.Text.Json;
using System.Text.Json.Serialization;

namespace EfsAiHub.Platform.Runtime.Ingestion;

/// <summary>
/// Opções JSON compartilhadas entre o controller de ingestão e o handler.
/// Ambos precisam ler/escrever o mesmo shape (IngestionContext + envelope do
/// workflow) — manter dois <c>JsonSerializerOptions</c> separados leva a drift
/// silencioso quando alguém ajusta um lado.
/// </summary>
public static class IngestionJsonDefaults
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
