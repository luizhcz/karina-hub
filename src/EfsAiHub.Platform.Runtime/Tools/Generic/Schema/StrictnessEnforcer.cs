using System.Text.Json.Nodes;

namespace EfsAiHub.Platform.Runtime.Tools.Generic.Schema;

/// <summary>
/// Aplica as regras do OpenAI <c>strict: true</c> em schemas de INPUT (que vão
/// pro LLM como tool params): <c>additionalProperties: false</c> em todo
/// objeto, <c>required</c> com TODAS as properties, <c>items</c> default em
/// array sem items.
///
/// Pra schemas de OUTPUT (validar response da API), strict é semanticamente
/// errado: response com campos extras viraria erro de validação em vez de
/// passar pelo projector (que drop os extras silenciosamente); response sem
/// um campo declarado opcional viraria erro também. Output role só aplica o
/// fix de items default — required e additionalProperties são preservados
/// como o autor declarou (ou ausentes).
/// </summary>
internal static class StrictnessEnforcer
{
    public static JsonNode? Enforce(JsonNode? root, IList<NormalizationWarning> warnings, SchemaRole role)
    {
        if (root is not JsonObject obj) return root;
        EnforceSchemaNode(obj, warnings, role, path: "");
        return obj;
    }

    private static JsonNode EnforceSchemaNode(
        JsonObject obj,
        IList<NormalizationWarning> warnings,
        SchemaRole role,
        string path)
    {
        var type = ReadType(obj);

        if (IsObjectType(type))
        {
            if (role == SchemaRole.Input)
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
            // Output: respeita o `required` do autor e não força
            // additionalProperties:false. Validation acontece "loose" e o
            // projector drop campos extras durante a iteração de properties.
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
                return EnforceSchemaNode(childObj, warnings, role, childPath);
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
