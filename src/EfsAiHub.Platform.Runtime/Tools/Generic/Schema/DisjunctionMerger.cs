using System.Text.Json.Nodes;

namespace EfsAiHub.Platform.Runtime.Tools.Generic.Schema;

/// <summary>
/// Mescla <c>oneOf</c>/<c>anyOf</c> num superset por <i>union</i> das
/// properties de todas as branches. <c>required</c> vira intersection
/// (campo só é required quando TODAS branches o requerem) — sem isso o
/// schema final pediria todos os campos de todos os branches simultaneamente.
/// Type conflict mantém o primeiro + warning. <c>not</c> é dropado.
///
/// Trade-off explícito: branches contraditórios geram shape que talvez nenhuma
/// response real case. Esse é o preço aceito pra LLM nunca falhar por causa
/// de schema com disjunção.
/// </summary>
internal static class DisjunctionMerger
{
    public static JsonNode? Merge(JsonNode? root, IList<NormalizationWarning> warnings)
    {
        return MergeNode(root, warnings, path: "");
    }

    private static JsonNode? MergeNode(JsonNode? node, IList<NormalizationWarning> warnings, string path)
    {
        if (node is JsonObject obj)
        {
            MergeDisjunctionAt(obj, "oneOf", warnings, path);
            MergeDisjunctionAt(obj, "anyOf", warnings, path);

            if (obj.ContainsKey("not"))
            {
                warnings.Add(new NormalizationWarning(
                    "not.dropped",
                    path,
                    "Keyword 'not' não suportada no canônico — removida."));
                obj.Remove("not");
            }

            foreach (var key in obj.Select(p => p.Key).ToList())
            {
                var merged = MergeNode(obj[key], warnings, $"{path}/{key}");
                if (!ReferenceEquals(merged, obj[key]))
                {
                    obj.Remove(key);
                    obj[key] = merged;
                }
            }
            return obj;
        }

        if (node is JsonArray arr)
        {
            for (var i = 0; i < arr.Count; i++)
            {
                var merged = MergeNode(arr[i], warnings, $"{path}/{i}");
                if (!ReferenceEquals(merged, arr[i]))
                {
                    arr[i] = merged;
                }
            }
            return arr;
        }

        return node;
    }

    private static void MergeDisjunctionAt(
        JsonObject obj,
        string keyword,
        IList<NormalizationWarning> warnings,
        string path)
    {
        if (obj[keyword] is not JsonArray branches || branches.Count == 0) return;

        // Detach branches da array original antes de mergear.
        var branchList = new List<JsonObject>(branches.Count);
        for (var i = branches.Count - 1; i >= 0; i--)
        {
            var b = branches[i];
            branches.RemoveAt(i);
            if (b is JsonObject bo) branchList.Insert(0, bo);
        }
        obj.Remove(keyword);

        if (branchList.Count == 0) return;

        warnings.Add(new NormalizationWarning(
            $"{keyword.ToLowerInvariant()}.merged",
            path,
            $"{keyword} com {branchList.Count} branches mesclado em superset (properties union, required intersection)."));

        // Coleta required de cada branch antes de mesclar properties — precisamos
        // saber qual era required em cada uma pra calcular interseção depois.
        var perBranchRequired = branchList
            .Select(b => CollectRequiredSet(b))
            .ToList();

        foreach (var branch in branchList)
        {
            MergePropertiesUnion(obj, branch, warnings, path);
            MergeTypeFirstWins(obj, branch, warnings, path);
        }

        // Required: intersection — só fica required quem aparece em TODAS branches.
        var intersection = perBranchRequired.Count > 0
            ? perBranchRequired.Aggregate((a, b) => new HashSet<string>(a.Intersect(b), StringComparer.Ordinal))
            : new HashSet<string>(StringComparer.Ordinal);

        // Required do próprio nó-pai também precisa entrar — sobrescrevemos
        // depois das branches porque "obrigatoriedade local" é mais forte.
        if (obj["required"] is JsonArray existingReq)
        {
            foreach (var item in existingReq)
                if (item is JsonValue v && v.TryGetValue<string>(out var s)) intersection.Add(s);
        }

        if (intersection.Count == 0)
        {
            obj.Remove("required");
        }
        else
        {
            var arr = new JsonArray();
            foreach (var name in intersection.OrderBy(s => s, StringComparer.Ordinal))
                arr.Add(name);
            obj["required"] = arr;
        }
    }

    private static HashSet<string> CollectRequiredSet(JsonObject branch)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (branch["required"] is JsonArray arr)
        {
            foreach (var item in arr)
                if (item is JsonValue v && v.TryGetValue<string>(out var s)) set.Add(s);
        }
        return set;
    }

    private static void MergePropertiesUnion(
        JsonObject dst,
        JsonObject branch,
        IList<NormalizationWarning> warnings,
        string path)
    {
        if (branch["properties"] is not JsonObject branchProps) return;

        if (dst["properties"] is not JsonObject dstProps)
        {
            dstProps = new JsonObject();
            dst["properties"] = dstProps;
        }

        foreach (var (key, value) in branchProps)
        {
            if (value is null) continue;
            if (dstProps[key] is JsonObject existing && value is JsonObject incoming)
            {
                // Property colidente entre branches: merge recursivo (union de
                // sub-properties também, mesma lógica).
                AllOfMerger.MergeInto(existing, incoming, warnings, $"{path}/properties/{key}");
            }
            else if (!dstProps.ContainsKey(key))
            {
                dstProps[key] = value.DeepClone();
            }
        }
    }

    private static void MergeTypeFirstWins(
        JsonObject dst,
        JsonObject branch,
        IList<NormalizationWarning> warnings,
        string path)
    {
        if (branch["type"] is not JsonNode srcType) return;
        if (!dst.ContainsKey("type"))
        {
            dst["type"] = srcType.DeepClone();
            return;
        }

        var dstStr = dst["type"]?.ToJsonString();
        var srcStr = srcType.ToJsonString();
        if (!string.Equals(dstStr, srcStr, StringComparison.Ordinal))
        {
            warnings.Add(new NormalizationWarning(
                "disjunction.type_conflict",
                path,
                $"Branches com types divergentes ({dstStr} vs {srcStr}) — mantido o primeiro."));
        }
    }
}
