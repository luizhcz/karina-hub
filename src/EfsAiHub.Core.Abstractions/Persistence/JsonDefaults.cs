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
    /// </summary>
    public static readonly JsonSerializerOptions Domain = new()
    {
        Converters = { new JsonStringEnumConverter() },
        PropertyNamingPolicy = null
    };

    /// <summary>
    /// Options for deserializing external/HTTP input where property casing may vary.
    /// </summary>
    public static readonly JsonSerializerOptions CaseInsensitive = new()
    {
        PropertyNameCaseInsensitive = true
    };
}
