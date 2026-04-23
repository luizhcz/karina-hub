using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EfsAiHub.Core.Abstractions.Persistence;

/// <summary>
/// Centralized JSON serialization options used by all persistence backends.
/// Avoids duplicated JsonSerializerOptions instances across repositories.
/// </summary>
public static class JsonDefaults
{
    /// <summary>
    /// Options for serializing/deserializing domain entities stored as JSON.
    /// Includes JsonStringEnumConverter and preserves property casing.
    ///
    /// Encoder UnsafeRelaxedJsonEscaping: evita escape de caracteres não-ASCII
    /// (ex: "ã" não vira "ã"). Os textos em português permanecem legíveis
    /// nos payloads persistidos (workflow_event_audit, etc.) e nas respostas
    /// da API. "Unsafe" refere-se apenas a output direto em HTML sem escape —
    /// JSON puro é seguro.
    /// </summary>
    public static readonly JsonSerializerOptions Domain = new()
    {
        Converters = { new JsonStringEnumConverter() },
        PropertyNamingPolicy = null,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// Options for deserializing external/HTTP input where property casing may vary.
    /// </summary>
    public static readonly JsonSerializerOptions CaseInsensitive = new()
    {
        PropertyNameCaseInsensitive = true
    };
}
