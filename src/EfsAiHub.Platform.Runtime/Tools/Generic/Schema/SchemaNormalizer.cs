using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using EfsAiHub.Core.Abstractions.Exceptions;

namespace EfsAiHub.Platform.Runtime.Tools.Generic.Schema;

/// <summary>
/// Orquestra a pipeline de normalização: parse → strip metadata → inline $ref
/// → merge allOf → merge oneOf/anyOf (superset) → sanitize keywords → enforce
/// strictness. Save-time only. O JSON canônico produzido roda em OpenAI strict
/// 2020-12 e Anthropic tool-use sem ajustes adicionais.
///
/// Determinístico: ordering de keys consistente (alphabetic via JsonSerializer),
/// hash SHA256 estável.
/// </summary>
public sealed class SchemaNormalizer : ISchemaNormalizer
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false,
    };

    public CanonicalSchema Normalize(string rawJson, SchemaRole role)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
            throw new DomainException("Schema vazio — forneça um JSON Schema.");

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(rawJson);
        }
        catch (JsonException ex)
        {
            throw new DomainException($"Schema não é JSON válido: {ex.Message}");
        }

        if (root is null)
            throw new DomainException("Schema parse retornou null.");

        if (root is not JsonObject rootObj)
            throw new DomainException(
                $"Schema deve ser um objeto JSON na raiz (recebeu {root.GetValueKind()}).");

        var dialect = DialectDetector.Detect(rootObj);
        var warnings = new List<NormalizationWarning>();

        // Strip metadata do dialect — não tem semântica no canônico.
        rootObj.Remove("$schema");
        rootObj.Remove("$id");
        rootObj.Remove("$anchor");

        // Root "raiz vazia" ({} ou só metadata) default pra object — sub-schemas
        // sem clues caem em "string" via SanitizeType, mas a raiz de um tool
        // schema é virtualmente sempre object.
        if (rootObj.Count == 0)
            rootObj["type"] = "object";

        JsonNode? working = rootObj;
        working = RefInliner.Inline(working, warnings);
        working = AllOfMerger.Merge(working, warnings);
        working = DisjunctionMerger.Merge(working, warnings);
        working = KeywordSanitizer.Sanitize(working, warnings);
        working = StrictnessEnforcer.Enforce(working, warnings, role);

        if (working is not JsonObject finalObj)
            throw new DomainException(
                "Normalização produziu um nó raiz que não é objeto — schema inválido.");

        // Caso degenerado: schema 100% vazio depois de toda transformação.
        // Mantemos o canônico válido (com strictness aplicada) mas alertamos.
        if (IsEmptyObjectSchema(finalObj))
        {
            warnings.Add(new NormalizationWarning(
                "schema.empty",
                "",
                "Schema final é vazio (sem properties). Tool retornará {} pro LLM — declare propriedades para que algo seja exposto."));
        }

        var canonical = finalObj.ToJsonString(SerializerOptions);
        var hash = ComputeHash(canonical);

        return new CanonicalSchema(canonical, dialect, warnings, hash);
    }

    private static bool IsEmptyObjectSchema(JsonObject obj)
    {
        if (obj["type"] is not JsonValue typeVal) return false;
        if (!typeVal.TryGetValue<string>(out var typeStr) || typeStr != "object") return false;
        if (obj["properties"] is not JsonObject props) return true;
        return props.Count == 0;
    }

    private static string ComputeHash(string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
