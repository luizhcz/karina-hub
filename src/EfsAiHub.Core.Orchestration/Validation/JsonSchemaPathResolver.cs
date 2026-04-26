using System.Globalization;
using System.Text.Json;

namespace EfsAiHub.Core.Orchestration.Validation;

/// <summary>
/// Verifica se um JSONPath subset (consistente com <see cref="EdgeInvariantsValidator"/>)
/// resolve algum campo num JSON Schema (draft 7+). Suporta navegação via <c>properties</c>
/// (objetos) e <c>items</c> (arrays). Schemas com <c>oneOf</c>/<c>anyOf</c>/<c>allOf</c> e
/// <c>additionalProperties</c> dinâmicos são tratados de forma permissiva — assume-se que
/// o campo pode existir e o validator não rejeita o predicate (degrade graceful: validação
/// runtime pega o erro real se houver mismatch).
/// </summary>
public static class JsonSchemaPathResolver
{
    /// <summary>
    /// Tenta resolver o <paramref name="path"/> dentro do <paramref name="schema"/> recebido.
    /// Retorna true se o caminho é resolvível ou indeterminado (schema permissivo);
    /// false só quando há prova de que o campo não existe.
    /// </summary>
    public static bool PathExistsInSchema(JsonElement schema, string path)
    {
        if (string.IsNullOrEmpty(path) || path[0] != '$') return false;
        if (path == "$") return true;

        return TryResolve(schema, path, 1);
    }

    private static bool TryResolve(JsonElement current, string path, int i)
    {
        while (i < path.Length)
        {
            // Schema permissivo: oneOf/anyOf/allOf — não conseguimos provar ausência sem
            // resolver cada branch; aceitamos como "pode existir" pra não bloquear no save.
            if (HasComposition(current)) return true;

            // additionalProperties: true ou objeto — aceita qualquer campo extra.
            if (HasOpenAdditionalProperties(current)) return true;

            var c = path[i];
            if (c == '.')
            {
                i++;
                var start = i;
                while (i < path.Length && path[i] != '.' && path[i] != '[') i++;
                if (i == start) return false;
                var name = path[start..i];

                if (!current.TryGetProperty("properties", out var props)) return false;
                if (!props.TryGetProperty(name, out var next)) return false;
                current = next;
            }
            else if (c == '[')
            {
                i++;
                var start = i;
                while (i < path.Length && path[i] != ']') i++;
                if (i == path.Length) return false;
                var idxText = path[start..i];
                i++; // consome ']'
                if (!int.TryParse(idxText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var idx) || idx < 0)
                    return false;

                if (!current.TryGetProperty("items", out var items)) return false;
                current = items;
            }
            else
            {
                return false;
            }
        }
        return true;
    }

    private static bool HasComposition(JsonElement schema)
        => schema.ValueKind == JsonValueKind.Object
           && (schema.TryGetProperty("oneOf", out _)
               || schema.TryGetProperty("anyOf", out _)
               || schema.TryGetProperty("allOf", out _));

    private static bool HasOpenAdditionalProperties(JsonElement schema)
    {
        if (schema.ValueKind != JsonValueKind.Object) return false;
        if (!schema.TryGetProperty("additionalProperties", out var ap)) return false;
        return ap.ValueKind == JsonValueKind.True
            || ap.ValueKind == JsonValueKind.Object;
    }
}
