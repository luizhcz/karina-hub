using System.Text;
using System.Text.Json;
using EfsAiHub.Core.Agents.GenericTools;

namespace EfsAiHub.Platform.Runtime.Tools.Generic;

/// <summary>
/// Parser puro do response HTTP em formato compatível com o envelope retornado
/// ao agente. Json vira <see cref="JsonElement"/>, Text vira string crua, CSV
/// vira lista de dicionários (primeira linha é header).
/// </summary>
public static class GenericResponseParser
{
    public static async Task<object?> ParseAsync(
        HttpContent content,
        OutputContentType outputContentType,
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
                return ParseCsv(raw);
            }
        }

        return null;
    }

    /// <summary>
    /// Parser RFC 4180 minimalista: primeira linha é header, valores em aspas
    /// suportam vírgula, quebra de linha e aspas escapadas (""). Retorna lista
    /// vazia em CSV vazio. Não faz coercion de tipos — todos os valores ficam
    /// como string (caller pode tipar depois usando OutputSchema se quiser).
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
