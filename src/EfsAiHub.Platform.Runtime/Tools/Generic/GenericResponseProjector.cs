using System.Text.Json;
using System.Text.Json.Nodes;
using EfsAiHub.Core.Agents.GenericTools;
using Json.Schema;

namespace EfsAiHub.Platform.Runtime.Tools.Generic;

/// <summary>
/// Valida + projeta o response da tool contra o <see cref="GenericTool.OutputSchema"/>
/// declarado pelo admin. Pipeline: <c>Executor → Parser → Projector</c>.
///
/// Modos (vindos de <see cref="OutputProjectionMode"/>):
/// <list type="bullet">
///   <item><b>Off</b>: bypass total — devolve o input intacto.</item>
///   <item><b>Project</b>: drop silencioso de campos extras + fail-loud em
///   required ausente ou type mismatch.</item>
///   <item><b>Strict</b>: igual ao Project + fail-loud em qualquer campo
///   extra (independente de <c>additionalProperties</c> no schema).</item>
/// </list>
///
/// Cap de 5 erros (+ "...and N more"); arrays grandes truncados em 500
/// items na projeção (validação ainda roda no array original pra detectar
/// erros estruturais em qualquer linha).
/// </summary>
public sealed class GenericResponseProjector
{
    public const int MaxErrorsReported = 5;
    public const int MaxArrayItemsProjected = 500;

    private readonly SchemaCache _cache;

    public GenericResponseProjector(SchemaCache cache)
    {
        _cache = cache;
    }

    /// <summary>
    /// Projeta o input contra o schema. Quando <paramref name="mode"/> é
    /// <c>Off</c> ou schema é null/vazio, devolve <see cref="ProjectionResult.AsBypass"/>.
    /// </summary>
    public ProjectionResult Project(
        object? parsed,
        string? schemaJson,
        OutputProjectionMode mode,
        string toolName)
    {
        if (mode == OutputProjectionMode.Off || string.IsNullOrWhiteSpace(schemaJson))
            return ProjectionResult.AsBypass(parsed);
        if (parsed is null)
            return ProjectionResult.AsBypass(null);

        JsonSchema schema;
        try
        {
            schema = _cache.GetOrAdd(schemaJson);
        }
        catch (Exception ex)
        {
            // Schema malformado é falha de config — não derrubamos o caller,
            // marcamos como bypass com log. EnsureInvariants no save já evita
            // isso pra tools novas; aqui cobrimos data legacy/corrompida.
            return ProjectionResult.AsFailure(new[]
            {
                $"OutputSchema malformado: {ex.Message}",
            });
        }

        // Normaliza pra JsonNode pra projeção uniforme. CSV vira array de
        // objects; Json mantém shape; string (Text) é bypass por contrato
        // (EnsureInvariants rejeita Text + Mode != Off).
        var node = ToJsonNode(parsed);
        if (node is null)
        {
            return ProjectionResult.AsFailure(new[]
            {
                $"Response da tool '{toolName}' não pôde ser convertido pra JSON.",
            });
        }

        var evaluation = schema.Evaluate(node, new EvaluationOptions
        {
            OutputFormat = OutputFormat.List,
            RequireFormatValidation = false,
        });

        var errors = ExtractErrors(evaluation);
        if (errors.Count > 0)
        {
            return ProjectionResult.AsFailure(CapErrors(errors));
        }

        TruncationInfo? truncation = null;
        var projected = ProjectNode(node, schema, mode, ref truncation);

        // Em modo Strict, drop ainda acontece mas qualquer extra detectado
        // já viraria erro acima quando o schema tem additionalProperties
        // resolvível. Pra schemas sem flag explícita, DetectExtras varre
        // por extras não declarados.
        if (mode == OutputProjectionMode.Strict)
        {
            var strictErrors = DetectExtras(node, schema);
            if (strictErrors.Count > 0)
                return ProjectionResult.AsFailure(CapErrors(strictErrors));
        }

        return ProjectionResult.AsSuccess(projected, truncation);
    }

    private static JsonNode? ToJsonNode(object? parsed)
    {
        if (parsed is null) return null;
        if (parsed is JsonNode existing) return existing;
        if (parsed is JsonElement el)
        {
            // JsonNode.Parse direto do RawText é ~10x mais barato que round-trip
            // via SerializeToNode pra payloads grandes (evita serialize→parse).
            return JsonNode.Parse(el.GetRawText());
        }
        return JsonSerializer.SerializeToNode(parsed);
    }

    private static IReadOnlyList<string> ExtractErrors(EvaluationResults evaluation)
    {
        if (evaluation.IsValid) return Array.Empty<string>();
        var list = new List<string>();
        foreach (var detail in evaluation.Details)
        {
            if (detail.IsValid) continue;
            if (detail.Errors is null) continue;
            foreach (var (keyword, message) in detail.Errors)
            {
                var location = detail.InstanceLocation.ToString();
                var loc = string.IsNullOrEmpty(location) ? "(root)" : location;
                list.Add($"{loc}: {keyword} — {message}");
            }
        }
        // Caso o evaluation não retorne errors em Details (formato basic vs list),
        // fallback pra mensagem genérica.
        if (list.Count == 0)
            list.Add("Schema validation falhou — response não casa com OutputSchema.");
        return list;
    }

    private static IReadOnlyList<string> CapErrors(IReadOnlyList<string> errors)
    {
        if (errors.Count <= MaxErrorsReported) return errors;
        var capped = new List<string>(MaxErrorsReported + 1);
        for (var i = 0; i < MaxErrorsReported; i++) capped.Add(errors[i]);
        capped.Add($"...and {errors.Count - MaxErrorsReported} more.");
        return capped;
    }

    /// <summary>
    /// Projeta o node mantendo apenas campos declarados no schema. Recursão
    /// percorre <c>properties</c> em objetos e <c>items</c> em arrays. Tipos
    /// primitivos passam direto. <c>Strict</c> não influencia projeção (drop
    /// é universal); só muda o conjunto de erros pré-projeção.
    ///
    /// Truncamento de arrays grandes (acima de <see cref="MaxArrayItemsProjected"/>)
    /// preserva o shape do schema do <c>items</c> — o marker fica fora, em
    /// <paramref name="truncation"/>, pra que o LLM receba só dados válidos
    /// segundo o schema. Tester (UI) exibe o aviso de truncamento.
    /// </summary>
    private static JsonNode? ProjectNode(
        JsonNode? node,
        JsonSchema schema,
        OutputProjectionMode mode,
        ref TruncationInfo? truncation)
    {
        if (node is null) return null;

        var properties = schema.GetProperties();
        var items = schema.GetItems();

        if (node is JsonObject obj && properties is { Count: > 0 })
        {
            var projected = new JsonObject();
            foreach (var (key, subSchema) in properties)
            {
                if (!obj.TryGetPropertyValue(key, out var sub) || sub is null) continue;
                // Reparenting requer detach (sub ainda pertence ao obj original).
                // Como obj é descartado, é seguro: sub passa a viver na nova árvore.
                obj.Remove(key);
                projected[key] = ProjectNode(sub, subSchema, mode, ref truncation);
            }
            return projected;
        }

        if (node is JsonArray arr && items is not null)
        {
            var projectedArr = new JsonArray();
            var limit = Math.Min(arr.Count, MaxArrayItemsProjected);
            // Detach each element from source array antes de reparentear.
            // Iterar do fim pro começo evita shift de indices em Remove.
            var originalCount = arr.Count;
            for (var i = 0; i < limit; i++)
            {
                var element = arr[0];
                arr.RemoveAt(0);
                projectedArr.Add(element is null ? null : ProjectNode(element, items, mode, ref truncation));
            }
            if (originalCount > limit && truncation is null)
            {
                truncation = new TruncationInfo(originalCount, limit);
            }
            return projectedArr;
        }

        // Sem properties/items declarados no schema: detach do parent (caller
        // já o removeu da árvore original) e devolve.
        return node;
    }

    /// <summary>
    /// Para modo Strict: detecta propriedades presentes no response que NÃO
    /// estão declaradas no schema (em qualquer nível). Erro estruturado pro
    /// LLM ajustar a chamada. Respeita <c>additionalProperties: true</c> ou
    /// sub-schema explícito — admin opta out da rigidez Strict pra subtree.
    /// </summary>
    private static IReadOnlyList<string> DetectExtras(JsonNode? node, JsonSchema schema)
    {
        var extras = new List<string>();
        DetectExtrasInternal(node, schema, "(root)", extras);
        return extras;
    }

    private static void DetectExtrasInternal(JsonNode? node, JsonSchema schema, string path, List<string> extras)
    {
        if (node is JsonObject obj)
        {
            // additionalProperties=true ou sub-schema explícito: schema declara
            // extras permitidos, Strict não deve falhar nesse subtree.
            if (AllowsAdditionalProperties(schema)) return;

            var props = schema.GetProperties();
            var declared = props is null
                ? new HashSet<string>(StringComparer.Ordinal)
                : new HashSet<string>(props.Keys, StringComparer.Ordinal);
            foreach (var prop in obj)
            {
                if (!declared.Contains(prop.Key))
                {
                    extras.Add($"{path}: campo '{prop.Key}' não declarado em OutputSchema (modo Strict).");
                    if (extras.Count >= MaxErrorsReported + 1) return;
                    continue;
                }
                if (props is not null && props.TryGetValue(prop.Key, out var subSchema))
                {
                    DetectExtrasInternal(prop.Value, subSchema, $"{path}/{prop.Key}", extras);
                    if (extras.Count >= MaxErrorsReported + 1) return;
                }
            }
        }
        else if (node is JsonArray arr)
        {
            var items = schema.GetItems();
            if (items is null) return;
            for (var i = 0; i < arr.Count; i++)
            {
                DetectExtrasInternal(arr[i], items, $"{path}[{i}]", extras);
                if (extras.Count >= MaxErrorsReported + 1) return;
            }
        }
    }

    /// <summary>
    /// Examina o keyword <c>additionalProperties</c> do schema. Retorna true
    /// quando ausente E default permissivo (sem flag), true quando explícito
    /// <c>true</c>, true quando sub-schema (admin permite extras desde que
    /// respeitem o tipo). Retorna false APENAS quando explícito <c>false</c>
    /// — Strict reforça esse contrato verificando extras manualmente.
    /// </summary>
    private static bool AllowsAdditionalProperties(JsonSchema schema)
    {
        var keyword = schema.Keywords?.OfType<AdditionalPropertiesKeyword>().FirstOrDefault();
        if (keyword is null) return false; // Strict trata ausência como "false implícito".
        var sub = keyword.Schema;
        if (sub.BoolValue is bool b) return b;
        return true; // sub-schema declarado → admin permite extras tipados.
    }
}
