using System.Text.Json;
using EfsAiHub.Core.Abstractions.Exceptions;

namespace EfsAiHub.Core.Agents.GenericTools;

/// <summary>
/// Assertions estruturais baratas que comprovam que um schema persistido já
/// foi canonicalizado pelo <c>SchemaNormalizer</c>. Roda em <c>EnsureInvariants</c>
/// como defesa em profundidade — se alguém escrever no banco contornando o
/// save (script SQL manual, restore de backup velho), o invariant falha alto
/// em vez de o LLM falhar silencioso.
///
/// Foco nos keywords forbidden (<c>$ref</c>, <c>oneOf</c>, <c>anyOf</c>,
/// <c>allOf</c>) — strictness (<c>additionalProperties: false</c>) só vale pra
/// Input schemas e o provider LLM rejeita se ausente, então não precisa de
/// check aqui. Output schemas legitimamente não têm strict.
/// </summary>
public static class NormalizationGuard
{
    private static readonly string[] ForbiddenKeywords = { "$ref", "oneOf", "anyOf", "allOf" };

    /// <summary>
    /// Confirma que o JSON satisfaz as invariantes do schema canônico: parse
    /// OK, objeto na raiz, sem <c>$ref</c>/<c>oneOf</c>/<c>anyOf</c>/<c>allOf</c>
    /// em nenhum nível. Walk fundida — uma única passada checa todas as
    /// invariantes por nó.
    /// </summary>
    public static void AssertCanonical(string? schemaJson, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(schemaJson)) return;

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(schemaJson);
        }
        catch (JsonException ex)
        {
            throw new DomainException(
                $"GenericTool.{fieldName} não é JSON válido: {ex.Message}");
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new DomainException(
                    $"GenericTool.{fieldName} canônico precisa ser objeto na raiz.");

            AssertNode(root, fieldName);
        }
    }

    private static void AssertNode(JsonElement node, string fieldName)
    {
        if (node.ValueKind != JsonValueKind.Object) return;

        foreach (var keyword in ForbiddenKeywords)
        {
            if (node.TryGetProperty(keyword, out _))
                throw new DomainException(
                    $"GenericTool.{fieldName} canônico contém '{keyword}' — schema não foi normalizado corretamente.");
        }

        // Recurse — uma única passada visita cada nó uma vez.
        foreach (var prop in node.EnumerateObject())
        {
            if (prop.Value.ValueKind == JsonValueKind.Object)
            {
                AssertNode(prop.Value, fieldName);
            }
            else if (prop.Value.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in prop.Value.EnumerateArray())
                    if (item.ValueKind == JsonValueKind.Object)
                        AssertNode(item, fieldName);
            }
        }
    }
}
