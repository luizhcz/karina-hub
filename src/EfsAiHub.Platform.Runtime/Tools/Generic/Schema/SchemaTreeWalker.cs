using System.Text.Json.Nodes;

namespace EfsAiHub.Platform.Runtime.Tools.Generic.Schema;

/// <summary>
/// Helper para recursão schema-aware. JSON Schema tem 3 tipos de containers:
/// <list type="bullet">
///   <item>Schema-value (chave aponta direto pra um sub-schema): <c>items</c>, <c>additionalProperties</c>, <c>not</c>, ...</item>
///   <item>Schema-map (chave aponta pra mapa <c>nome → schema</c>): <c>properties</c>, <c>patternProperties</c>, <c>$defs</c>, ...</item>
///   <item>Schema-array (chave aponta pra array de schemas): <c>allOf</c>, <c>oneOf</c>, <c>anyOf</c>, <c>prefixItems</c></item>
/// </list>
/// Recursão "burra" (descer em toda property) faz transformações stupidas como
/// adicionar <c>"type": "string"</c> no container <c>properties</c> — porque
/// ele é um JsonObject mas NÃO É um schema. Esse helper recurse só nas
/// posições onde schemas legitimamente existem.
/// </summary>
internal static class SchemaTreeWalker
{
    private static readonly HashSet<string> SchemaValueKeys = new(StringComparer.Ordinal)
    {
        "items",
        "additionalProperties",
        "contains",
        "not",
        "if", "then", "else",
        "propertyNames",
        "unevaluatedItems",
        "unevaluatedProperties",
        "additionalItems",
    };

    private static readonly HashSet<string> SchemaMapKeys = new(StringComparer.Ordinal)
    {
        "properties",
        "patternProperties",
        "definitions",
        "$defs",
        "dependentSchemas",
    };

    private static readonly HashSet<string> SchemaArrayKeys = new(StringComparer.Ordinal)
    {
        "allOf", "oneOf", "anyOf", "prefixItems",
    };

    /// <summary>
    /// Aplica <paramref name="transformChild"/> em cada sub-schema dentro de
    /// <paramref name="parent"/>. Quando o transform retorna um nó diferente,
    /// substitui no lugar. Quem chama recursivamente passa o próprio handler
    /// como o transform — permitindo deep traversal.
    /// </summary>
    public static void RecurseSchemaChildren(
        JsonObject parent,
        string parentPath,
        Func<JsonNode, string, JsonNode?> transformChild)
    {
        foreach (var key in parent.Select(p => p.Key).ToList())
        {
            if (parent[key] is not { } child) continue;
            var childPath = $"{parentPath}/{key}";

            if (SchemaValueKeys.Contains(key) && child is JsonObject)
            {
                var transformed = transformChild(child, childPath);
                if (!ReferenceEquals(transformed, child))
                {
                    parent.Remove(key);
                    if (transformed is not null) parent[key] = transformed;
                }
            }
            else if (SchemaMapKeys.Contains(key) && child is JsonObject map)
            {
                foreach (var subKey in map.Select(p => p.Key).ToList())
                {
                    if (map[subKey] is not { } subVal) continue;
                    var subSan = transformChild(subVal, $"{childPath}/{subKey}");
                    if (!ReferenceEquals(subSan, subVal))
                    {
                        map.Remove(subKey);
                        if (subSan is not null) map[subKey] = subSan;
                    }
                }
            }
            else if (SchemaArrayKeys.Contains(key) && child is JsonArray arr)
            {
                for (var i = 0; i < arr.Count; i++)
                {
                    if (arr[i] is not { } item) continue;
                    var itemSan = transformChild(item, $"{childPath}/{i}");
                    if (!ReferenceEquals(itemSan, item))
                    {
                        arr[i] = itemSan;
                    }
                }
            }
        }
    }
}
