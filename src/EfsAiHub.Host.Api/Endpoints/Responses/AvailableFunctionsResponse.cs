using System.Text.Json;
using System.Text.Json.Serialization;
using EfsAiHub.Platform.Runtime.Interfaces;

namespace EfsAiHub.Host.Api.Models.Responses;

public class AvailableFunctionsResponse
{
    public List<FunctionToolInfo>   FunctionTools      { get; init; } = [];
    public List<CodeExecutorInfo>   CodeExecutors      { get; init; } = [];
    public List<MiddlewareTypeInfo> MiddlewareTypes    { get; init; } = [];
    public List<LlmProviderInfo>    AvailableProviders { get; init; } = [];
}

public class LlmProviderInfo
{
    public string Type { get; init; } = "";
}

public class FunctionToolInfo
{
    public string      Name        { get; init; } = "";
    public string?     Description { get; init; }
    public JsonElement JsonSchema  { get; init; }
    public string?     Fingerprint { get; init; }
}

public class CodeExecutorInfo
{
    public string  Name       { get; init; } = "";
    public string? InputType  { get; init; }
    public string? OutputType { get; init; }

    /// <summary>JSON Schema do tipo de input (gerado via System.Text.Json.Schema). Null pra executors destipados.</summary>
    public JsonElement? InputSchema { get; init; }

    /// <summary>JSON Schema do tipo de output. Consumido pelo frontend pra construir picker de campos no edge predicate editor (PR 5).</summary>
    public JsonElement? OutputSchema { get; init; }

    /// <summary>
    /// Hash sha256 (12 hex) do output schema serializado. Usado pelo frontend pra detectar schema drift
    /// entre o momento da criação do edge e o estado atual do produtor — quando muda, UI sinaliza
    /// pra revisar predicate (e PR 4+ invalida cache de definição).
    /// </summary>
    public string? OutputSchemaVersion { get; init; }
}

public class MiddlewareTypeInfo
{
    public string Name { get; init; } = "";

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public MiddlewarePhase Phase { get; init; } = MiddlewarePhase.Both;

    public string Label { get; init; } = "";
    public string Description { get; init; } = "";
    public List<MiddlewareSettingInfoDto> Settings { get; init; } = [];
}

public class MiddlewareSettingInfoDto
{
    public string Key { get; init; } = "";
    public string Label { get; init; } = "";
    public string Type { get; init; } = "text";
    public List<MiddlewareSettingOptionDto>? Options { get; init; }
    public string DefaultValue { get; init; } = "";
}

public class MiddlewareSettingOptionDto
{
    public string Value { get; init; } = "";
    public string Label { get; init; } = "";
}
