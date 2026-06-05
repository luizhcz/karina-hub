using System.Globalization;
using System.Text;
using System.Text.Json;

namespace EfsAiHub.Platform.Runtime.Tools.Generic;

/// <summary>
/// Serializa um dict de args como query string pra GET com input estruturado.
/// Estratégia V1: primitives viram <c>key=value</c>, arrays/objects viram
/// <c>key=&lt;JSON encoded&gt;</c>. Nulls são pulados — sem isso o LLM acaba
/// emitindo <c>?x=null</c> em campos opcionais.
/// </summary>
public static class QueryStringFlattener
{
    public static IReadOnlyList<KeyValuePair<string, string>> Flatten(
        IReadOnlyDictionary<string, object?> args)
    {
        var pairs = new List<KeyValuePair<string, string>>();
        foreach (var (key, value) in args)
        {
            var serialized = SerializeValue(value);
            if (serialized is null) continue;
            pairs.Add(new KeyValuePair<string, string>(key, serialized));
        }
        return pairs;
    }

    private static string? SerializeValue(object? value)
    {
        if (value is null) return null;

        switch (value)
        {
            case string s:
                return s;
            case bool b:
                return b ? "true" : "false";
            case JsonElement je:
                return SerializeJsonElement(je);
            case IFormattable f:
                return f.ToString(null, CultureInfo.InvariantCulture);
            default:
                return Convert.ToString(value, CultureInfo.InvariantCulture);
        }
    }

    private static string? SerializeJsonElement(JsonElement je) => je.ValueKind switch
    {
        JsonValueKind.String => je.GetString(),
        JsonValueKind.Number => je.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Null => null,
        JsonValueKind.Object or JsonValueKind.Array => je.GetRawText(),
        _ => je.GetRawText(),
    };

    /// <summary>
    /// Concatena pairs já produzidos pelo <see cref="Flatten"/> num query
    /// string pronto pra anexar à URL. Faz URL-encode em ambos lados.
    /// </summary>
    public static string Build(IReadOnlyList<KeyValuePair<string, string>> pairs)
    {
        if (pairs.Count == 0) return string.Empty;
        var sb = new StringBuilder();
        for (var i = 0; i < pairs.Count; i++)
        {
            if (i > 0) sb.Append('&');
            sb.Append(Uri.EscapeDataString(pairs[i].Key))
              .Append('=')
              .Append(Uri.EscapeDataString(pairs[i].Value));
        }
        return sb.ToString();
    }
}
