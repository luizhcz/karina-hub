using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using EfsAiHub.Core.Agents.GenericTools;

namespace EfsAiHub.Platform.Runtime.Tools.Generic;

/// <summary>
/// Parser puro do response HTTP em formato compatível com o envelope retornado
/// ao agente. Json vira <see cref="JsonElement"/>, Text vira string crua, CSV
/// vira array de objects com tipos coercidos pelo <c>OutputSchema</c> quando
/// fornecido — sem schema, valores ficam como string crua.
/// </summary>
public static class GenericResponseParser
{
    public static async Task<object?> ParseAsync(
        HttpContent content,
        OutputContentType outputContentType,
        string? outputSchema = null,
        CancellationToken ct = default)
    {
        switch (outputContentType)
        {
            case OutputContentType.Text:
            {
                return await content.ReadAsStringAsync(ct).ConfigureAwait(false);
            }

            case OutputContentType.Json:
            {
                var raw = await content.ReadAsStringAsync(ct).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(raw)) return null;
                using var doc = JsonDocument.Parse(raw);
                return doc.RootElement.Clone();
            }

            case OutputContentType.Csv:
            {
                var raw = await content.ReadAsStringAsync(ct).ConfigureAwait(false);
                var rows = ParseCsv(raw);
                return string.IsNullOrWhiteSpace(outputSchema)
                    ? (object)rows
                    : CoerceCsvRows(rows, outputSchema!);
            }
        }

        return null;
    }

    /// <summary>
    /// Parser RFC 4180 minimalista: primeira linha é header, valores em aspas
    /// suportam vírgula, quebra de linha e aspas escapadas (""). Retorna lista
    /// vazia em CSV vazio. Valores ficam como string — coerção de tipos via
    /// <see cref="CoerceCsvRows"/> usando <c>OutputSchema</c>.
    /// </summary>
    public static List<Dictionary<string, string>> ParseCsv(string csv)
    {
        var rows = new List<Dictionary<string, string>>();
        if (string.IsNullOrWhiteSpace(csv)) return rows;

        var lines = SplitLines(csv);
        if (lines.Count == 0) return rows;

        var header = ParseLine(lines[0]);
        for (var i = 1; i < lines.Count; i++)
        {
            if (string.IsNullOrEmpty(lines[i])) continue;
            var values = ParseLine(lines[i]);
            var row = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var c = 0; c < header.Count && c < values.Count; c++)
                row[header[c]] = values[c];
            rows.Add(row);
        }

        return rows;
    }

    /// <summary>
    /// Tipa as células do CSV usando <c>items.properties.&lt;col&gt;.type</c> do
    /// OutputSchema canônico. Falhas de parse (ex.: "abc" pra number) viram
    /// <c>null</c>; o projector valida e levanta SchemaErrors no caller. Sem
    /// isso, schema declara <c>quantity: number</c> mas projector vê string
    /// "100" e falha — exatamente a dor que o autor reporta.
    ///
    /// Invariant culture pra decimais — "1.234,56" não é parseável, "1234.56"
    /// é. Locale-dependent vira problema upstream (Transform step future).
    /// </summary>
    public static List<Dictionary<string, object?>> CoerceCsvRows(
        List<Dictionary<string, string>> rows,
        string outputSchema)
    {
        var typeByColumn = ExtractColumnTypes(outputSchema);
        var coerced = new List<Dictionary<string, object?>>(rows.Count);

        foreach (var row in rows)
        {
            var typedRow = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var (col, raw) in row)
            {
                typedRow[col] = typeByColumn.TryGetValue(col, out var type)
                    ? CoerceValue(raw, type)
                    : raw;
            }
            coerced.Add(typedRow);
        }

        return coerced;
    }

    // OutputSchema é imutável por GenericTool (regravado no save). Cachear o
    // type-map evita JsonDocument.Parse por response — economia de ~50-200µs
    // por chamada em tools CSV-only com volume alto. Capped só pelo número
    // de schemas únicos do sistema (~dezenas a baixas centenas).
    private static readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, string>> _typeMapCache
        = new(StringComparer.Ordinal);

    private static IReadOnlyDictionary<string, string> ExtractColumnTypes(string outputSchema)
        => _typeMapCache.GetOrAdd(outputSchema, ParseColumnTypes);

    private static IReadOnlyDictionary<string, string> ParseColumnTypes(string outputSchema)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            using var doc = JsonDocument.Parse(outputSchema);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return map;
            if (!doc.RootElement.TryGetProperty("items", out var items)) return map;
            if (items.ValueKind != JsonValueKind.Object) return map;
            if (!items.TryGetProperty("properties", out var props)) return map;
            if (props.ValueKind != JsonValueKind.Object) return map;

            foreach (var prop in props.EnumerateObject())
            {
                if (prop.Value.ValueKind != JsonValueKind.Object) continue;
                if (!prop.Value.TryGetProperty("type", out var typeNode)) continue;
                if (typeNode.ValueKind != JsonValueKind.String) continue;
                map[prop.Name] = typeNode.GetString() ?? "string";
            }
        }
        catch (JsonException)
        {
            // Schema corrompido — caller decide se rejeita ou bypassa.
        }
        return map;
    }

    private static object? CoerceValue(string raw, string targetType)
    {
        if (string.IsNullOrEmpty(raw)) return null;

        switch (targetType)
        {
            case "integer":
                return long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l)
                    ? (object?)l
                    : null;
            case "number":
                return decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
                    ? (object?)d
                    : null;
            case "boolean":
                return raw.ToLowerInvariant() switch
                {
                    "true" or "1" or "yes" or "y" => true,
                    "false" or "0" or "no" or "n" => false,
                    _ => null,
                };
            case "object":
            case "array":
                try { return JsonNode.Parse(raw); }
                catch (JsonException) { return null; }
            case "null":
                return null;
            default:
                return raw;
        }
    }

    private static List<string> SplitLines(string csv)
    {
        var lines = new List<string>();
        var sb = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < csv.Length; i++)
        {
            var ch = csv[i];

            if (ch == '"')
            {
                if (inQuotes && i + 1 < csv.Length && csv[i + 1] == '"')
                {
                    sb.Append('"').Append('"');
                    i++;
                    continue;
                }
                inQuotes = !inQuotes;
                sb.Append(ch);
                continue;
            }

            if (!inQuotes && (ch == '\n' || ch == '\r'))
            {
                if (ch == '\r' && i + 1 < csv.Length && csv[i + 1] == '\n') i++;
                lines.Add(sb.ToString());
                sb.Clear();
                continue;
            }

            sb.Append(ch);
        }

        if (sb.Length > 0) lines.Add(sb.ToString());
        return lines;
    }

    private static List<string> ParseLine(string line)
    {
        var fields = new List<string>();
        var sb = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];

            if (ch == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    sb.Append('"');
                    i++;
                    continue;
                }
                inQuotes = !inQuotes;
                continue;
            }

            if (ch == ',' && !inQuotes)
            {
                fields.Add(sb.ToString());
                sb.Clear();
                continue;
            }

            sb.Append(ch);
        }

        fields.Add(sb.ToString());
        return fields;
    }
}
