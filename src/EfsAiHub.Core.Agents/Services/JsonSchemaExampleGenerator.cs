using System.Text.Json;
using System.Text.Json.Nodes;

namespace EfsAiHub.Core.Agents.Services;

/// <summary>
/// Gera uma INSTÂNCIA exemplo a partir de um JSON Schema. Saída é um
/// <see cref="JsonNode"/> pronto pra serialização — usado pelo
/// <see cref="PromptRenderer"/> pra mostrar ao LLM a FORMA estrutural concreta
/// do output esperado, em vez de só descrição textual.
///
/// <para>
/// Razão de existir: LLMs em strict mode tendem a copiar a forma literal do
/// schema (ex.: emitir <c>{"items":[...]}</c> quando o schema declara
/// <c>{"type":"array","items":{...}}</c>) em vez de instanciar. Mostrar
/// exemplo concreto ao lado da regra "isto é INSTÂNCIA, não o schema" reduz
/// drasticamente esse erro.
/// </para>
///
/// <para>
/// Valores gerados são PLACEHOLDERS (ex.: <c>"&lt;string&gt;"</c>,
/// <c>"&lt;v1 | v2&gt;"</c>) — o caller deve adicionar instrução clara
/// pedindo pro LLM substituir pelos valores reais do turno.
/// </para>
/// </summary>
internal static class JsonSchemaExampleGenerator
{
    // Cap defensivo contra schemas recursivos (não esperado em strict mode
    // mas o gerador precisa ser robusto). Em depth excedido, retorna marker.
    private const int MaxDepth = 16;

    // Pra enums longos, mostra os 5 primeiros pra preservar legibilidade.
    private const int MaxEnumValuesShown = 5;

    /// <summary>
    /// Gera o exemplo. Retorna null quando schema é null/inválido OU quando
    /// o tipo do schema é literalmente <c>"null"</c>.
    /// </summary>
    public static JsonNode? Generate(JsonElement? schema)
        => schema is { } el ? GenerateRecursive(el, depth: 0) : null;

    /// <summary>
    /// Conveniência sobre <see cref="JsonDocument"/>. Retorna null quando doc
    /// é null/inválido.
    /// </summary>
    public static JsonNode? Generate(JsonDocument? schemaDoc)
        => schemaDoc is null ? null : GenerateRecursive(schemaDoc.RootElement, depth: 0);

    private static JsonNode? GenerateRecursive(JsonElement schema, int depth)
    {
        if (depth > MaxDepth)
            return JsonValue.Create("<...>");

        if (schema.ValueKind != JsonValueKind.Object)
            return JsonValue.Create("<value>");

        // Enum dominante: independente do type, o valor REAL precisa estar no
        // enum. Mostra a lista (truncada) como placeholder.
        if (schema.TryGetProperty("enum", out var enumEl)
            && enumEl.ValueKind == JsonValueKind.Array)
        {
            return BuildEnumExample(enumEl);
        }

        var type = ReadType(schema);
        type ??= InferTypeFromShape(schema);

        return type switch
        {
            "string" => JsonValue.Create("<string>"),
            // number preserva tipo double; integer mantém int — diferenciação
            // importa pro JSON Schema strict mode (number aceita decimal,
            // integer só inteiro).
            "number" => JsonValue.Create(0.0),
            "integer" => JsonValue.Create(0),
            "boolean" => JsonValue.Create(false),
            "null" => null,
            "array" => BuildArrayExample(schema, depth),
            "object" => BuildObjectExample(schema, depth),
            _ => JsonValue.Create("<value>"),
        };
    }

    /// <summary>
    /// Lê o campo <c>type</c>. Suporta string (<c>"string"</c>) e array
    /// (<c>["string","null"]</c>) — neste último caso retorna o primeiro
    /// tipo non-null, alinhado com OpenAI strict mode.
    /// </summary>
    private static string? ReadType(JsonElement schema)
    {
        if (!schema.TryGetProperty("type", out var t)) return null;
        if (t.ValueKind == JsonValueKind.String) return t.GetString();
        if (t.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in t.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String) continue;
                var s = item.GetString();
                if (!string.IsNullOrEmpty(s)
                    && !string.Equals(s, "null", StringComparison.Ordinal))
                {
                    return s;
                }
            }
        }
        return null;
    }

    /// <summary>
    /// Inferência defensiva quando o schema omite <c>type</c>:
    ///   - tem <c>properties</c> → object
    ///   - tem <c>items</c> → array
    ///   - caso contrário → string (placeholder mais útil)
    /// </summary>
    private static string InferTypeFromShape(JsonElement schema)
    {
        if (schema.TryGetProperty("properties", out _)) return "object";
        if (schema.TryGetProperty("items", out _)) return "array";
        return "string";
    }

    private static JsonNode? BuildEnumExample(JsonElement enumArr)
    {
        var values = new List<string>();
        foreach (var v in enumArr.EnumerateArray())
        {
            // Preserva tipo de valor: string → texto puro, outros tipos →
            // raw JSON pro placeholder mostrar o valor real (ex.: 0, true).
            values.Add(v.ValueKind == JsonValueKind.String
                ? (v.GetString() ?? string.Empty)
                : v.GetRawText());
        }

        if (values.Count == 0) return JsonValue.Create("<value>");
        if (values.Count == 1) return JsonValue.Create(values[0]);

        var shown = values.Take(MaxEnumValuesShown).ToList();
        var suffix = values.Count > MaxEnumValuesShown ? " | ..." : string.Empty;
        return JsonValue.Create($"<{string.Join(" | ", shown)}{suffix}>");
    }

    private static JsonArray BuildArrayExample(JsonElement schema, int depth)
    {
        // Sem `items`: array vazio com placeholder de item escalar.
        if (!schema.TryGetProperty("items", out var items))
            return new JsonArray { JsonValue.Create("<item>") };

        var inner = GenerateRecursive(items, depth + 1);
        return new JsonArray { inner };
    }

    private static JsonObject BuildObjectExample(JsonElement schema, int depth)
    {
        var result = new JsonObject();
        if (!schema.TryGetProperty("properties", out var props)
            || props.ValueKind != JsonValueKind.Object)
        {
            // Object sem properties declaradas — mantém vazio. Strict mode
            // tipicamente rejeita esse shape no save, então é cenário raro.
            return result;
        }

        foreach (var prop in props.EnumerateObject())
        {
            result[prop.Name] = GenerateRecursive(prop.Value, depth + 1);
        }
        return result;
    }
}
