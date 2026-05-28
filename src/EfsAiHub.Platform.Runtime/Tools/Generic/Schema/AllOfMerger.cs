using System.Text.Json.Nodes;

namespace EfsAiHub.Platform.Runtime.Tools.Generic.Schema;

/// <summary>
/// Faz merge de <c>allOf</c> nas branches em um único schema deep-merged.
/// OpenAI strict mode não aceita <c>allOf</c> — composição precisa estar
/// inlined. Estratégia: union de <c>properties</c>, union de <c>required</c>
/// (dedup), tipo da primeira branch vence em conflito (warning emitido).
/// </summary>
internal static class AllOfMerger
{
    public static JsonNode? Merge(JsonNode? root, IList<NormalizationWarning> warnings)
    {
        return MergeNode(root, warnings, path: "");
    }

    private static JsonNode? MergeNode(JsonNode? node, IList<NormalizationWarning> warnings, string path)
    {
        if (node is JsonObject obj)
        {
            if (obj["allOf"] is JsonArray branches && branches.Count > 0)
            {
                // Detach branches do allOf antes de mergear (cada item deixa
                // de pertencer à array original — necessário pra reparenting).
                var branchList = new List<JsonNode?>(branches.Count);
                for (var i = branches.Count - 1; i >= 0; i--)
                {
                    var b = branches[i];
                    branches.RemoveAt(i);
                    branchList.Insert(0, b);
                }
                obj.Remove("allOf");

                foreach (var branch in branchList)
                {
                    if (branch is JsonObject branchObj)
                        MergeInto(obj, branchObj, warnings, $"{path}/allOf");
                }
            }

            // Recurse depois de mergear o allOf deste nível.
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

    /// <summary>
    /// Merge deep de <paramref name="src"/> em <paramref name="dst"/>.
    /// Properties: union (dst preserva, src adiciona novas chaves e merge
    /// recursivo nas colidentes). Required: union dedupada. Type: dst vence;
    /// conflito → warning. Reusado pelo <see cref="DisjunctionMerger"/> quando
    /// duas branches têm property colidente — mesma semântica de merge.
    /// </summary>
    internal static void MergeInto(
        JsonObject dst,
        JsonObject src,
        IList<NormalizationWarning> warnings,
        string path)
    {
        foreach (var (key, value) in src)
        {
            switch (key)
            {
                case "properties":
                    MergeProperties(dst, value as JsonObject, warnings, path);
                    break;
                case "required":
                    MergeRequired(dst, value as JsonArray);
                    break;
                case "type":
                    MergeType(dst, value, warnings, path);
                    break;
                default:
                    // Demais keywords: dst preserva o que tem; só copia se ausente.
                    if (!dst.ContainsKey(key) && value is not null)
                        dst[key] = value.DeepClone();
                    break;
            }
        }
    }

    private static void MergeProperties(
        JsonObject dst,
        JsonObject? srcProps,
        IList<NormalizationWarning> warnings,
        string path)
    {
        if (srcProps is null) return;
        if (dst["properties"] is not JsonObject dstProps)
        {
            dstProps = new JsonObject();
            dst["properties"] = dstProps;
        }

        foreach (var (propKey, propVal) in srcProps)
        {
            if (propVal is null) continue;
            if (dstProps[propKey] is JsonObject existing && propVal is JsonObject incoming)
            {
                MergeInto(existing, incoming, warnings, $"{path}/properties/{propKey}");
            }
            else if (!dstProps.ContainsKey(propKey))
            {
                dstProps[propKey] = propVal.DeepClone();
            }
        }
    }

    private static void MergeRequired(JsonObject dst, JsonArray? srcReq)
    {
        if (srcReq is null) return;
        if (dst["required"] is not JsonArray dstReq)
        {
            dstReq = new JsonArray();
            dst["required"] = dstReq;
        }

        var existing = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in dstReq)
            if (item is JsonValue v && v.TryGetValue<string>(out var s)) existing.Add(s);

        foreach (var item in srcReq)
        {
            if (item is JsonValue v && v.TryGetValue<string>(out var s) && existing.Add(s))
                dstReq.Add(s);
        }
    }

    private static void MergeType(
        JsonObject dst,
        JsonNode? srcType,
        IList<NormalizationWarning> warnings,
        string path)
    {
        if (srcType is null) return;
        if (!dst.ContainsKey("type"))
        {
            dst["type"] = srcType.DeepClone();
            return;
        }

        var dstType = dst["type"]?.ToJsonString();
        var srcTypeStr = srcType.ToJsonString();
        if (!string.Equals(dstType, srcTypeStr, StringComparison.Ordinal))
        {
            warnings.Add(new NormalizationWarning(
                "allof.type_conflict",
                path,
                $"allOf com types divergentes ({dstType} vs {srcTypeStr}) — mantido {dstType}."));
        }
    }
}
