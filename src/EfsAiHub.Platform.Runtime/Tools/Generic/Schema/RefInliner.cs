using System.Text.Json.Nodes;

namespace EfsAiHub.Platform.Runtime.Tools.Generic.Schema;

/// <summary>
/// Inline de <c>$ref</c> contra <c>$defs</c>/<c>definitions</c>. Sem inline,
/// o schema vai pro LLM com referências penduradas — OpenAI strict mode
/// rejeita. Cycles e refs externas (http://, file://) viram <c>{}</c> com
/// warning — runtime nunca faz fetch externo.
/// </summary>
internal static class RefInliner
{
    private const int MaxDepth = 32;

    public static JsonNode? Inline(JsonNode? root, IList<NormalizationWarning> warnings)
    {
        if (root is not JsonObject rootObj) return root;

        var defs = CollectDefs(rootObj);

        // Remove os containers de defs do output — depois do inline ficaram
        // mortos, e dialect 2020-12 strict não aceita keys "extras" no root.
        rootObj.Remove("$defs");
        rootObj.Remove("definitions");

        var visited = new HashSet<string>(StringComparer.Ordinal);
        return InlineNode(rootObj, defs, visited, warnings, depth: 0, path: "");
    }

    private static IReadOnlyDictionary<string, JsonNode> CollectDefs(JsonObject root)
    {
        var map = new Dictionary<string, JsonNode>(StringComparer.Ordinal);

        if (root["$defs"] is JsonObject defs2020)
        {
            foreach (var (key, value) in defs2020)
                if (value is not null) map[$"$defs/{key}"] = value.DeepClone();
        }
        if (root["definitions"] is JsonObject defsLegacy)
        {
            foreach (var (key, value) in defsLegacy)
                if (value is not null) map[$"definitions/{key}"] = value.DeepClone();
        }
        return map;
    }

    private static JsonNode? InlineNode(
        JsonNode? node,
        IReadOnlyDictionary<string, JsonNode> defs,
        HashSet<string> visited,
        IList<NormalizationWarning> warnings,
        int depth,
        string path)
    {
        if (node is null) return null;
        if (depth > MaxDepth)
        {
            warnings.Add(new NormalizationWarning(
                "ref.depth_exceeded",
                path,
                $"Profundidade máxima ({MaxDepth}) excedida ao inlinear $ref — substituído por {{}}."));
            return new JsonObject();
        }

        if (node is JsonObject obj)
        {
            if (obj["$ref"] is JsonValue refVal && refVal.TryGetValue<string>(out var refStr))
            {
                return ResolveRef(refStr, defs, visited, warnings, depth, path);
            }

            // Recurse properties — clone keys list pra permitir mutação durante iteração.
            foreach (var key in obj.Select(p => p.Key).ToList())
            {
                var childPath = $"{path}/{key}";
                var inlined = InlineNode(obj[key], defs, visited, warnings, depth + 1, childPath);
                if (!ReferenceEquals(inlined, obj[key]))
                {
                    obj.Remove(key);
                    obj[key] = inlined;
                }
            }
            return obj;
        }

        if (node is JsonArray arr)
        {
            for (var i = 0; i < arr.Count; i++)
            {
                var childPath = $"{path}/{i}";
                var inlined = InlineNode(arr[i], defs, visited, warnings, depth + 1, childPath);
                if (!ReferenceEquals(inlined, arr[i]))
                {
                    arr[i] = inlined;
                }
            }
            return arr;
        }

        return node;
    }

    private static JsonNode? ResolveRef(
        string refStr,
        IReadOnlyDictionary<string, JsonNode> defs,
        HashSet<string> visited,
        IList<NormalizationWarning> warnings,
        int depth,
        string path)
    {
        // Refs externas (http, file, etc.) — runtime nunca faz fetch.
        if (!refStr.StartsWith("#/", StringComparison.Ordinal))
        {
            warnings.Add(new NormalizationWarning(
                "ref.external",
                path,
                $"$ref externa '{refStr}' não suportada — substituída por {{}}."));
            return new JsonObject();
        }

        var key = refStr[2..]; // remove "#/" prefix
        if (!defs.TryGetValue(key, out var target))
        {
            warnings.Add(new NormalizationWarning(
                "ref.unresolved",
                path,
                $"$ref '{refStr}' não encontrada em $defs/definitions — substituída por {{}}."));
            return new JsonObject();
        }

        if (!visited.Add(refStr))
        {
            // Ciclo detectado — o ref está sendo expandido ainda mais acima.
            warnings.Add(new NormalizationWarning(
                "ref.cycle",
                path,
                $"$ref ciclo em '{refStr}' — substituído por {{}}."));
            return new JsonObject();
        }

        try
        {
            var clone = target.DeepClone();
            return InlineNode(clone, defs, visited, warnings, depth + 1, path);
        }
        finally
        {
            visited.Remove(refStr);
        }
    }
}
