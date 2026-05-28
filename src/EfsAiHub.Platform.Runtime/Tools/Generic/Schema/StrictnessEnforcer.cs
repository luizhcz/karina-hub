using System.Text.Json.Nodes;

namespace EfsAiHub.Platform.Runtime.Tools.Generic.Schema;

/// <summary>
/// Aplica as regras do OpenAI <c>strict: true</c>:
/// <list type="bullet">
///   <item>Todo objeto tem <c>additionalProperties: false</c></item>
///   <item>Todo objeto tem <c>required</c> com TODAS as properties</item>
///   <item>Todo array tem <c>items</c> (default <c>type: "string"</c>)</item>
/// </list>
/// Usa <see cref="SchemaTreeWalker"/> pra descer só em posições de schema —
/// containers como <c>properties</c> nunca recebem <c>additionalProperties</c>.
/// </summary>
internal static class StrictnessEnforcer
{
    public static JsonNode? Enforce(JsonNode? root, IList<NormalizationWarning> warnings)
    {
        if (root is not JsonObject obj) return root;
        EnforceSchemaNode(obj, warnings, path: "");
        return obj;
    }

    private static JsonNode EnforceSchemaNode(JsonObject obj, IList<NormalizationWarning> warnings, string path)
    {
        var type = ReadType(obj);

        if (IsObjectType(type))
        {
            obj["additionalProperties"] = false;

            if (obj["properties"] is JsonObject props)
            {
                var required = new JsonArray();
                foreach (var pair in props.OrderBy(p => p.Key, StringComparer.Ordinal))
                    required.Add(pair.Key);
                obj["required"] = required;
            }
            else
            {
                obj["required"] = new JsonArray();
            }
        }
        else if (IsArrayType(type))
        {
            if (obj["items"] is null)
            {
                obj["items"] = new JsonObject { ["type"] = "string" };
                warnings.Add(new NormalizationWarning(
                    "array.items_default",
                    path,
                    "Array sem 'items' ganhou default {type: 'string'}."));
            }
        }

        SchemaTreeWalker.RecurseSchemaChildren(obj, path, (child, childPath) =>
        {
            if (child is JsonObject childObj)
                return EnforceSchemaNode(childObj, warnings, childPath);
            return child;
        });

        return obj;
    }

    private static string? ReadType(JsonObject obj)
    {
        if (obj["type"] is JsonValue v && v.TryGetValue<string>(out var s)) return s;
        if (obj["type"] is JsonArray arr)
        {
            foreach (var item in arr)
                if (item is JsonValue iv && iv.TryGetValue<string>(out var ts) && ts != "null") return ts;
        }
        return null;
    }

    private static bool IsObjectType(string? type) =>
        string.Equals(type, "object", StringComparison.Ordinal);

    private static bool IsArrayType(string? type) =>
        string.Equals(type, "array", StringComparison.Ordinal);
}
