using System.Text.Json;
using System.Text.Json.Nodes;

namespace EfsAiHub.Host.Api.Chat.AgUi.State;

/// <summary>
/// Aplica operações JSON Patch (RFC 6902) a um JsonElement.
/// Suporta add, remove, replace. Operações move/copy/test não são necessárias
/// para o state sync AG-UI e não são implementadas.
/// </summary>
public static class JsonPatchApplier
{
    public static JsonElement Apply(JsonElement document, JsonElement patch)
    {
        var node = JsonNode.Parse(document.GetRawText())
            ?? throw new InvalidOperationException("Invalid document JSON.");

        if (patch.ValueKind != JsonValueKind.Array)
            throw new ArgumentException("JSON Patch must be an array.", nameof(patch));

        foreach (var op in patch.EnumerateArray())
        {
            var opType = op.GetProperty("op").GetString()
                ?? throw new ArgumentException("Patch operation missing 'op'.");
            var path = op.GetProperty("path").GetString()
                ?? throw new ArgumentException("Patch operation missing 'path'.");

            switch (opType)
            {
                case "add":
                case "replace":
                    var value = op.GetProperty("value");
                    SetPath(node, path, JsonNode.Parse(value.GetRawText()));
                    break;

                case "remove":
                    RemovePath(node, path);
                    break;

                default:
                    throw new NotSupportedException($"JSON Patch operation '{opType}' not supported.");
            }
        }

        return JsonDocument.Parse(node.ToJsonString()).RootElement.Clone();
    }

    public static JsonElement SetPath(JsonElement document, string path, JsonElement value)
    {
        var node = JsonNode.Parse(document.GetRawText())
            ?? throw new InvalidOperationException("Invalid document JSON.");

        SetPath(node, path, JsonNode.Parse(value.GetRawText()));

        return JsonDocument.Parse(node.ToJsonString()).RootElement.Clone();
    }

    /// <summary>
    /// Gera um JSON Patch (array de operações) representando a diferença
    /// entre oldState e newState. Implementação simplificada: compara top-level keys.
    /// </summary>
    public static JsonElement GenerateDiff(JsonElement oldState, JsonElement newState)
    {
        var ops = new List<JsonNode>();
        var oldObj = JsonNode.Parse(oldState.GetRawText())?.AsObject();
        var newObj = JsonNode.Parse(newState.GetRawText())?.AsObject();

        if (oldObj is null || newObj is null)
        {
            ops.Add(new JsonObject
            {
                ["op"] = "replace",
                ["path"] = "",
                ["value"] = JsonNode.Parse(newState.GetRawText())
            });
            return JsonDocument.Parse(new JsonArray(ops.ToArray()).ToJsonString())
                .RootElement.Clone();
        }

        // Keys removed
        foreach (var (key, _) in oldObj)
        {
            if (!newObj.ContainsKey(key))
                ops.Add(new JsonObject { ["op"] = "remove", ["path"] = $"/{key}" });
        }

        // Keys added or changed
        foreach (var (key, newVal) in newObj)
        {
            if (!oldObj.ContainsKey(key))
            {
                ops.Add(new JsonObject
                {
                    ["op"] = "add",
                    ["path"] = $"/{key}",
                    ["value"] = newVal is not null ? JsonNode.Parse(newVal.ToJsonString()) : null
                });
            }
            else
            {
                var oldVal = oldObj[key];
                var oldJson = oldVal?.ToJsonString() ?? "null";
                var newJson = newVal?.ToJsonString() ?? "null";
                if (oldJson != newJson)
                {
                    ops.Add(new JsonObject
                    {
                        ["op"] = "replace",
                        ["path"] = $"/{key}",
                        ["value"] = newVal is not null ? JsonNode.Parse(newVal.ToJsonString()) : null
                    });
                }
            }
        }

        return JsonDocument.Parse(new JsonArray(ops.ToArray()).ToJsonString())
            .RootElement.Clone();
    }

    private static void SetPath(JsonNode root, string path, JsonNode? value)
    {
        if (string.IsNullOrEmpty(path) || path == "/")
        {
            // Replace root — not supported for in-place mutation
            throw new ArgumentException("Cannot replace root via SetPath. Use direct assignment.");
        }

        var segments = path.TrimStart('/').Split('/');
        var current = root;

        for (var i = 0; i < segments.Length - 1; i++)
        {
            var segment = segments[i];
            if (current is JsonObject obj)
            {
                if (!obj.ContainsKey(segment))
                    obj[segment] = new JsonObject();
                current = obj[segment]!;
            }
            else
            {
                throw new InvalidOperationException(
                    $"Cannot traverse path '{path}' — segment '{segment}' is not an object.");
            }
        }

        var lastSegment = segments[^1];
        if (current is JsonObject parentObj)
        {
            parentObj[lastSegment] = value;
        }
        else
        {
            throw new InvalidOperationException(
                $"Cannot set value at path '{path}' — parent is not an object.");
        }
    }

    private static void RemovePath(JsonNode root, string path)
    {
        var segments = path.TrimStart('/').Split('/');
        var current = root;

        for (var i = 0; i < segments.Length - 1; i++)
        {
            if (current is JsonObject obj && obj.ContainsKey(segments[i]))
                current = obj[segments[i]]!;
            else
                return; // Path doesn't exist, no-op
        }

        if (current is JsonObject parentObj)
            parentObj.Remove(segments[^1]);
    }
}
