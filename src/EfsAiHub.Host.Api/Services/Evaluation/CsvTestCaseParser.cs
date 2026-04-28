using System.Globalization;
using System.Text;
using System.Text.Json;
using EfsAiHub.Core.Agents.Evaluation;

namespace EfsAiHub.Host.Api.Services.Evaluation;

/// <summary>
/// Parser leve de CSV pra import de test cases. Sem dependência externa
/// (projeto não usa CsvHelper). Comportamento alinhado com RFC 4180:
///   - aspas duplas escapam aspas (<c>"He said ""hi"""</c>)
///   - newlines dentro de aspas são preservados
///   - vírgulas dentro de aspas não dividem campos
///   - BOM UTF-8/UTF-16 LE/BE detectado e ignorado
///   - linhas vazias puladas (whitespace-only)
///
/// Formato esperado (header obrigatório, case-insensitive, ordem livre):
///   <c>input,expectedOutput,tags,weight,expectedToolCalls</c>
///
/// Colunas opcionais: <c>expectedOutput</c>, <c>tags</c> (lista separada por
/// <c>|</c>), <c>weight</c> (double, default 1.0), <c>expectedToolCalls</c>
/// (JSON array literal, ex.: <c>[{"name":"get_weather"}]</c>).
/// Coluna obrigatória: <c>input</c>.
/// </summary>
public static class CsvTestCaseParser
{
    public sealed class CsvParseException : Exception
    {
        public CsvParseException(string message) : base(message) { }
    }

    public static IReadOnlyList<EvaluationTestCase> Parse(string testSetVersionId, Stream csvStream)
    {
        using var reader = CreateReader(csvStream);
        var headers = ReadHeaders(reader);

        var inputIdx = headers.IndexOf("input");
        if (inputIdx < 0)
            throw new CsvParseException("Coluna obrigatória 'input' ausente no CSV header.");

        var expectedIdx = headers.IndexOf("expectedoutput");
        var tagsIdx = headers.IndexOf("tags");
        var weightIdx = headers.IndexOf("weight");
        var toolCallsIdx = headers.IndexOf("expectedtoolcalls");

        var cases = new List<EvaluationTestCase>();
        var rowIndex = 0;
        var lineNumber = 1; // header foi 1
        while (TryReadRecord(reader, out var fields, out var lineCount))
        {
            lineNumber += lineCount;
            // Linha vazia (whitespace) — pula sem incrementar Index.
            if (fields.Count == 1 && string.IsNullOrWhiteSpace(fields[0])) continue;

            string input = SafeField(fields, inputIdx) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(input))
                throw new CsvParseException($"Linha {lineNumber}: coluna 'input' vazia.");

            string? expectedOutput = expectedIdx >= 0 ? Trim(SafeField(fields, expectedIdx)) : null;

            IReadOnlyList<string> tags = Array.Empty<string>();
            if (tagsIdx >= 0)
            {
                var tagsRaw = SafeField(fields, tagsIdx);
                if (!string.IsNullOrWhiteSpace(tagsRaw))
                {
                    tags = tagsRaw.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                }
            }

            double weight = 1.0;
            if (weightIdx >= 0)
            {
                var weightRaw = SafeField(fields, weightIdx);
                if (!string.IsNullOrWhiteSpace(weightRaw)
                    && !double.TryParse(weightRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out weight))
                {
                    throw new CsvParseException(
                        $"Linha {lineNumber}: coluna 'weight' não é número válido: '{weightRaw}'.");
                }
                if (weight < 0)
                    throw new CsvParseException($"Linha {lineNumber}: 'weight' deve ser >= 0.");
            }

            JsonDocument? expectedToolCalls = null;
            if (toolCallsIdx >= 0)
            {
                var raw = SafeField(fields, toolCallsIdx);
                if (!string.IsNullOrWhiteSpace(raw))
                {
                    try { expectedToolCalls = JsonDocument.Parse(raw); }
                    catch (JsonException ex)
                    {
                        throw new CsvParseException(
                            $"Linha {lineNumber}: 'expectedToolCalls' não é JSON válido — {ex.Message}");
                    }
                }
            }

            cases.Add(new EvaluationTestCase(
                CaseId: Guid.NewGuid().ToString("N"),
                TestSetVersionId: testSetVersionId,
                Index: rowIndex,
                Input: input,
                ExpectedOutput: expectedOutput,
                ExpectedToolCalls: expectedToolCalls,
                Tags: tags,
                Weight: weight,
                CreatedAt: DateTime.UtcNow));
            rowIndex++;
        }

        if (cases.Count == 0)
            throw new CsvParseException("CSV não contém nenhuma linha de dados.");

        return cases;
    }

    private static StreamReader CreateReader(Stream stream)
    {
        // detectEncodingFromByteOrderMarks=true cobre HttpRequest streams non-seekable
        // em alguns hosts.
        return new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
    }

    private static List<string> ReadHeaders(StreamReader reader)
    {
        if (!TryReadRecord(reader, out var fields, out _))
            throw new CsvParseException("CSV vazio — header obrigatório ausente.");
        return fields.Select(f => f.Trim().ToLowerInvariant()).ToList();
    }

    private static string? SafeField(IReadOnlyList<string> fields, int idx)
        => idx < 0 || idx >= fields.Count ? null : fields[idx];

    private static string? Trim(string? s) => string.IsNullOrEmpty(s) ? s : s.Trim();

    /// <summary>
    /// Lê um record CSV com suporte a campos quoted multi-linha.
    /// Devolve <see langword="false"/> em EOF.
    /// </summary>
    private static bool TryReadRecord(StreamReader reader, out List<string> fields, out int linesConsumed)
    {
        fields = new List<string>();
        linesConsumed = 0;

        if (reader.EndOfStream) return false;

        var current = new StringBuilder();
        bool inQuotes = false;
        bool consumedAny = false;

        while (true)
        {
            int ch = reader.Read();
            if (ch == -1)
            {
                if (!consumedAny) return false;
                // EOF no meio do record — fecha o último campo.
                fields.Add(current.ToString());
                return true;
            }

            consumedAny = true;
            char c = (char)ch;

            if (inQuotes)
            {
                if (c == '"')
                {
                    // Lookahead: aspas duplicadas = literal aspa; senão fecha quotes.
                    int next = reader.Peek();
                    if (next == '"')
                    {
                        reader.Read();
                        current.Append('"');
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    current.Append(c);
                    if (c == '\n') linesConsumed++;
                }
            }
            else
            {
                if (c == ',')
                {
                    fields.Add(current.ToString());
                    current.Clear();
                }
                else if (c == '\r')
                {
                    // Possible \r\n — peek e descarta se for.
                    if (reader.Peek() == '\n') reader.Read();
                    linesConsumed++;
                    fields.Add(current.ToString());
                    return true;
                }
                else if (c == '\n')
                {
                    linesConsumed++;
                    fields.Add(current.ToString());
                    return true;
                }
                else if (c == '"' && current.Length == 0)
                {
                    // Quote no início do campo — entra em modo quoted.
                    inQuotes = true;
                }
                else
                {
                    current.Append(c);
                }
            }
        }
    }
}
