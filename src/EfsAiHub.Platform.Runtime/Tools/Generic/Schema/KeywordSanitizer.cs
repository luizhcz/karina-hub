using System.Text.Json.Nodes;

namespace EfsAiHub.Platform.Runtime.Tools.Generic.Schema;

/// <summary>
/// Remove ou reescreve keywords que OpenAI strict mode rejeita. Foco: garantir
/// que o schema final só usa subset que provedores aceitam universalmente
/// (type/properties/required/items/enum/description/format-allowlist).
///
/// Format e pattern são os 2 mais arriscados — pattern com lookahead vira
/// regex error no serializer do OpenAI; format esotérico vira validation
/// error. Allowlist conservadora.
///
/// Recursão usa <see cref="SchemaTreeWalker"/> pra descer só em posições
/// que JSON Schema legitimamente coloca sub-schemas — sem isso, containers
/// como <c>properties</c> (que é um JsonObject mas NÃO é um schema) ganham
/// um <c>"type":"string"</c> espúrio.
/// </summary>
internal static class KeywordSanitizer
{
    private static readonly HashSet<string> DropOnSight =
        new(StringComparer.Ordinal)
        {
            "multipleOf",
            "contentEncoding",
            "contentMediaType",
            "dependentSchemas",
            "dependentRequired",
            "if", "then", "else",
            "propertyNames",
            "patternProperties",
            "unevaluatedProperties",
            "unevaluatedItems",
            "contains",
            "minContains", "maxContains",
            "$comment",
            "readOnly", "writeOnly",
            "deprecated",
            "examples", "example",
            "default",
        };

    // Subset de format que OpenAI strict + Anthropic aceitam. Outros viram drop.
    private static readonly HashSet<string> FormatAllowlist =
        new(StringComparer.Ordinal)
        {
            "date-time", "date", "time",
            "email",
            "uri",
            "uuid",
            "ipv4", "ipv6",
        };

    public static JsonNode? Sanitize(JsonNode? root, IList<NormalizationWarning> warnings)
    {
        if (root is not JsonObject obj) return root;
        SanitizeSchemaNode(obj, warnings, path: "");
        return obj;
    }

    private static JsonNode SanitizeSchemaNode(JsonObject obj, IList<NormalizationWarning> warnings, string path)
    {
        DropUnsupportedKeywords(obj, warnings, path);
        SanitizePattern(obj, warnings, path);
        SanitizeFormat(obj, warnings, path);
        SanitizeType(obj, warnings, path);

        SchemaTreeWalker.RecurseSchemaChildren(obj, path, (child, childPath) =>
        {
            if (child is JsonObject childObj)
                return SanitizeSchemaNode(childObj, warnings, childPath);
            return child;
        });

        return obj;
    }

    private static void DropUnsupportedKeywords(JsonObject obj, IList<NormalizationWarning> warnings, string path)
    {
        foreach (var keyword in DropOnSight)
        {
            if (obj.ContainsKey(keyword))
            {
                obj.Remove(keyword);
                warnings.Add(new NormalizationWarning(
                    "keyword.dropped",
                    path,
                    $"Keyword '{keyword}' removida — não suportada no canônico."));
            }
        }
    }

    private static void SanitizePattern(JsonObject obj, IList<NormalizationWarning> warnings, string path)
    {
        if (obj["pattern"] is not JsonValue patternVal) return;
        if (!patternVal.TryGetValue<string>(out var pattern) || string.IsNullOrEmpty(pattern)) return;

        // Lookahead/lookbehind quebram o validator do OpenAI. Resto da regex
        // (anchors, char classes, quantifiers) passa.
        if (pattern.Contains("(?=", StringComparison.Ordinal)
            || pattern.Contains("(?!", StringComparison.Ordinal)
            || pattern.Contains("(?<=", StringComparison.Ordinal)
            || pattern.Contains("(?<!", StringComparison.Ordinal))
        {
            obj.Remove("pattern");
            warnings.Add(new NormalizationWarning(
                "pattern.unsupported_regex",
                path,
                "Pattern com lookahead/lookbehind removido — não suportado."));
        }
    }

    private static void SanitizeFormat(JsonObject obj, IList<NormalizationWarning> warnings, string path)
    {
        if (obj["format"] is not JsonValue formatVal) return;
        if (!formatVal.TryGetValue<string>(out var format) || string.IsNullOrEmpty(format)) return;

        if (!FormatAllowlist.Contains(format))
        {
            obj.Remove("format");
            warnings.Add(new NormalizationWarning(
                "format.dropped",
                path,
                $"Format '{format}' fora da allowlist removido — type preservado."));
        }
    }

    private static void SanitizeType(JsonObject obj, IList<NormalizationWarning> warnings, string path)
    {
        if (!obj.ContainsKey("type"))
        {
            // Tenta inferir do shape do PRÓPRIO node (não dos containers
            // descendentes). Como a recursão usa SchemaTreeWalker, esse método
            // só é chamado em schemas reais.
            var inferred = InferType(obj);
            if (inferred is not null)
            {
                obj["type"] = inferred;
            }
            else
            {
                obj["type"] = "string";
                warnings.Add(new NormalizationWarning(
                    "type.inferred_fallback",
                    path,
                    "Type ausente e indeterminável — assumido 'string'."));
            }
            return;
        }

        // type pode ser array (["string","null"]); aceitamos só {X, null}.
        if (obj["type"] is JsonArray typeArr)
        {
            var nonNullTypes = new List<string>();
            var hasNull = false;
            foreach (var item in typeArr)
            {
                if (item is JsonValue v && v.TryGetValue<string>(out var s))
                {
                    if (s == "null") hasNull = true;
                    else nonNullTypes.Add(s);
                }
            }

            if (nonNullTypes.Count == 0)
            {
                obj["type"] = hasNull ? "null" : "string";
                return;
            }

            var primary = nonNullTypes[0];
            if (nonNullTypes.Count > 1)
            {
                warnings.Add(new NormalizationWarning(
                    "type.union_collapsed",
                    path,
                    $"Type array com múltiplos tipos não-null colapsado pro primeiro: '{primary}'."));
            }

            if (hasNull)
            {
                var newArr = new JsonArray { primary, "null" };
                obj["type"] = newArr;
            }
            else
            {
                obj["type"] = primary;
            }
        }
    }

    private static string? InferType(JsonObject obj)
    {
        if (obj["properties"] is JsonObject) return "object";
        if (obj["items"] is not null) return "array";
        if (obj["enum"] is JsonArray enumArr && enumArr.Count > 0)
        {
            var first = enumArr[0];
            if (first is JsonValue v)
            {
                if (v.TryGetValue<bool>(out _)) return "boolean";
                if (v.TryGetValue<double>(out _)) return "number";
                if (v.TryGetValue<string>(out _)) return "string";
            }
        }
        return null;
    }
}
