using System.Text.Json.Nodes;

namespace EfsAiHub.Platform.Runtime.Tools.Generic.Schema;

/// <summary>
/// Detecta o dialect do JSON Schema input pra fins de auditoria. A
/// transformação subsequente é dialect-agnostic (sempre converte pro 2020-12
/// strict) — esse campo só vai pro <see cref="CanonicalSchema.SourceDialect"/>
/// pra debug. Heurística usada quando o autor não declara <c>$schema</c>.
/// </summary>
internal static class DialectDetector
{
    public const string Unknown = "unknown";
    public const string Draft04 = "draft-04";
    public const string Draft06 = "draft-06";
    public const string Draft07 = "draft-07";
    public const string Draft201909 = "2019-09";
    public const string Draft202012 = "2020-12";

    public static string Detect(JsonNode? root)
    {
        if (root is not JsonObject obj) return Unknown;

        if (obj["$schema"] is JsonValue schemaVal
            && schemaVal.TryGetValue<string>(out var schemaUri)
            && !string.IsNullOrWhiteSpace(schemaUri))
        {
            return MapByUri(schemaUri);
        }

        // Heurística por keywords presentes (apenas quando o autor omitiu $schema).
        // 2020-12: usa $defs (renomeado de definitions). Pre-2019-09 usa definitions.
        if (ContainsKeywordAnywhere(obj, "$defs")) return Draft202012;
        if (ContainsKeywordAnywhere(obj, "unevaluatedProperties")
            || ContainsKeywordAnywhere(obj, "unevaluatedItems"))
        {
            return Draft201909;
        }
        if (ContainsKeywordAnywhere(obj, "dependentSchemas")
            || ContainsKeywordAnywhere(obj, "dependentRequired"))
        {
            return Draft201909;
        }
        if (ContainsKeywordAnywhere(obj, "definitions")) return Draft07;

        return Unknown;
    }

    private static string MapByUri(string uri)
    {
        var lower = uri.ToLowerInvariant();
        if (lower.Contains("draft-04")) return Draft04;
        if (lower.Contains("draft-06")) return Draft06;
        if (lower.Contains("draft-07")) return Draft07;
        if (lower.Contains("2019-09")) return Draft201909;
        if (lower.Contains("2020-12")) return Draft202012;
        return Unknown;
    }

    private static bool ContainsKeywordAnywhere(JsonNode? node, string keyword)
    {
        if (node is null) return false;
        if (node is JsonObject obj)
        {
            if (obj.ContainsKey(keyword)) return true;
            foreach (var prop in obj)
                if (ContainsKeywordAnywhere(prop.Value, keyword)) return true;
            return false;
        }
        if (node is JsonArray arr)
        {
            foreach (var item in arr)
                if (ContainsKeywordAnywhere(item, keyword)) return true;
        }
        return false;
    }
}
