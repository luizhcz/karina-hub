using System.Text.Json;
using EfsAiHub.Core.Agents.GenericTools;
using EfsAiHub.Core.Abstractions.Persistence;

namespace EfsAiHub.Platform.Runtime.Tools.Generic;

/// <summary>
/// Monta o JSON Schema único exposto ao LLM como descritor da Generic Tool.
/// Combina path params + query params + body (quando Json/FormUrlEncoded/Text)
/// em um schema flat — o LLM passa todos os args num dict só, e o
/// <see cref="GenericRequestBuilder"/> separa por contexto na hora do request.
/// </summary>
public static class GenericToolSchemaBuilder
{
    public static JsonElement Build(GenericTool tool)
    {
        var properties = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var required = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (name, def) in tool.PathParams)
        {
            properties[name] = MakePropertySchema(def.Type, $"[Path] {def.Description}");
            required.Add(name);
        }

        foreach (var (name, def) in tool.QueryParams)
        {
            if (properties.ContainsKey(name)) continue;
            properties[name] = MakePropertySchema(def.Type, $"[Query] {def.Description}");
            if (def.Required) required.Add(name);
        }

        // GET + InputContentType=Json: properties do schema viram query params
        // adicionais (flattened pela QueryStringFlattener no executor). Pro LLM,
        // o prefix muda pra [Query] em vez de [Body] pra refletir o destino real.
        var bodyPrefix = tool.HttpMethod == HttpMethodType.GET ? "[Query] " : "[Body] ";

        switch (tool.InputContentType)
        {
            case InputContentType.Json:
            case InputContentType.FormUrlEncoded:
                MergeBodySchema(tool.InputSchema, properties, required, prefix: bodyPrefix);
                break;
            case InputContentType.Text:
                AddTextBodyProperty(tool.InputSchema, properties, required);
                break;
            case InputContentType.None:
                break;
        }

        var rootObject = new
        {
            type = "object",
            properties = properties.ToDictionary(kv => kv.Key, kv => kv.Value),
            required = required.ToArray(),
            additionalProperties = false,
        };
        var json = JsonSerializer.Serialize(rootObject, JsonDefaults.Domain);
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    private static JsonElement MakePropertySchema(string type, string description)
    {
        var json = JsonSerializer.Serialize(new
        {
            type = NormalizeType(type),
            description,
        }, JsonDefaults.Domain);
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    private static string NormalizeType(string type) => type?.ToLowerInvariant() switch
    {
        "string" => "string",
        "number" => "number",
        "integer" => "integer",
        "boolean" => "boolean",
        _ => "string",
    };

    private static void MergeBodySchema(
        string? bodySchema,
        Dictionary<string, JsonElement> target,
        HashSet<string> required,
        string prefix)
    {
        if (string.IsNullOrWhiteSpace(bodySchema)) return;

        JsonDocument doc;
        try { doc = JsonDocument.Parse(bodySchema); }
        catch (JsonException) { return; }

        using (doc)
        {
            if (!doc.RootElement.TryGetProperty("properties", out var props)
                || props.ValueKind != JsonValueKind.Object) return;

            foreach (var prop in props.EnumerateObject())
            {
                if (target.ContainsKey(prop.Name)) continue;
                target[prop.Name] = AnnotatePropertyDescription(prop.Value, prefix);
            }

            if (doc.RootElement.TryGetProperty("required", out var req)
                && req.ValueKind == JsonValueKind.Array)
            {
                foreach (var name in req.EnumerateArray())
                {
                    if (name.ValueKind == JsonValueKind.String)
                        required.Add(name.GetString()!);
                }
            }
        }
    }

    private static void AddTextBodyProperty(
        string? inputSchema,
        Dictionary<string, JsonElement> target,
        HashSet<string> required)
    {
        var fieldName = ExtractFirstPropertyName(inputSchema) ?? "body";
        if (target.ContainsKey(fieldName)) return;
        target[fieldName] = MakePropertySchema("string", "[Body] Conteúdo enviado como texto puro.");
        required.Add(fieldName);
    }

    private static string? ExtractFirstPropertyName(string? schema)
    {
        if (string.IsNullOrWhiteSpace(schema)) return null;
        try
        {
            using var doc = JsonDocument.Parse(schema);
            if (!doc.RootElement.TryGetProperty("properties", out var props)
                || props.ValueKind != JsonValueKind.Object) return null;
            foreach (var prop in props.EnumerateObject()) return prop.Name;
        }
        catch (JsonException) { return null; }
        return null;
    }

    private static JsonElement AnnotatePropertyDescription(JsonElement original, string prefix)
    {
        if (original.ValueKind != JsonValueKind.Object) return original.Clone();
        var dict = new Dictionary<string, object?>();
        foreach (var prop in original.EnumerateObject())
        {
            if (prop.NameEquals("description") && prop.Value.ValueKind == JsonValueKind.String)
                dict[prop.Name] = prefix + prop.Value.GetString();
            else
                dict[prop.Name] = JsonSerializer.Deserialize<object?>(prop.Value.GetRawText());
        }
        if (!dict.ContainsKey("description"))
            dict["description"] = prefix.TrimEnd();
        var json = JsonSerializer.Serialize(dict, JsonDefaults.Domain);
        return JsonDocument.Parse(json).RootElement.Clone();
    }
}
