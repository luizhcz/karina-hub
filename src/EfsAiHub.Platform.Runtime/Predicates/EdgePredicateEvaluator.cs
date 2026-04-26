using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using EfsAiHub.Core.Orchestration.Workflows;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EfsAiHub.Platform.Runtime.Predicates;

/// <summary>
/// Avaliador de <see cref="EdgePredicate"/> com JSONPath subset enxuto e operadores tipados.
/// Determinístico: JSON inválido / Path inexistente → false (sem derrubar execução).
/// Regex tem timeout hard de 250ms e é cacheada por pattern (singleton lifetime no DI).
/// </summary>
public sealed class EdgePredicateEvaluator : IEdgePredicateEvaluator
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(250);
    private const int RegexCacheCap = 256;

    /// <summary>
    /// Cache de Regex compiladas por pattern. Singleton scope (DI registra como Singleton)
    /// → cache compartilhado entre threads. ConcurrentDictionary cobre concurrent gets.
    /// Cap soft: quando estoura, novos patterns simplesmente não são cacheados — evita unbounded growth.
    /// </summary>
    private readonly ConcurrentDictionary<string, Regex> _regexCache =
        new(StringComparer.Ordinal);

    private readonly ILogger<EdgePredicateEvaluator> _logger;

    public EdgePredicateEvaluator(ILogger<EdgePredicateEvaluator>? logger = null)
    {
        _logger = logger ?? NullLogger<EdgePredicateEvaluator>.Instance;
    }

    public bool Evaluate(EdgePredicate predicate, string? output, JsonDocument? parsedDocument = null)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        if (string.IsNullOrWhiteSpace(output))
        {
            _logger.LogWarning("[EdgePredicate] Output vazio — predicate retornando false (Path={Path}, Operator={Operator}).",
                predicate.Path, predicate.Operator);
            return false;
        }

        JsonDocument? localDoc = null;
        try
        {
            JsonDocument doc;
            if (parsedDocument is not null)
            {
                doc = parsedDocument;
            }
            else
            {
                try
                {
                    localDoc = JsonDocument.Parse(output);
                    doc = localDoc;
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex,
                        "[EdgePredicate] JSON inválido no output do nó produtor — predicate retornando false. Output[:100]={Snippet}",
                        output[..Math.Min(100, output.Length)]);
                    return false;
                }
            }

            if (!TryResolvePath(doc.RootElement, predicate.Path, out var element))
            {
                // Path missing tem semântica especial pros operadores unários
                return predicate.Operator switch
                {
                    EdgeOperator.IsNull => true,
                    EdgeOperator.IsNotNull => false,
                    _ => false
                };
            }

            return ApplyOperator(predicate, element);
        }
        finally
        {
            localDoc?.Dispose();
        }
    }

    private bool ApplyOperator(EdgePredicate predicate, JsonElement field)
    {
        return predicate.Operator switch
        {
            EdgeOperator.IsNull => field.ValueKind == JsonValueKind.Null,
            EdgeOperator.IsNotNull => field.ValueKind != JsonValueKind.Null,

            EdgeOperator.Eq => CompareEq(field, predicate.Value, predicate.ValueType),
            EdgeOperator.NotEq => !CompareEq(field, predicate.Value, predicate.ValueType),

            EdgeOperator.Gt => CompareNumeric(field, predicate.Value, predicate.ValueType, NumericOp.Gt),
            EdgeOperator.Gte => CompareNumeric(field, predicate.Value, predicate.ValueType, NumericOp.Gte),
            EdgeOperator.Lt => CompareNumeric(field, predicate.Value, predicate.ValueType, NumericOp.Lt),
            EdgeOperator.Lte => CompareNumeric(field, predicate.Value, predicate.ValueType, NumericOp.Lte),

            EdgeOperator.Contains => CompareString(field, predicate.Value, (s, v) => s.Contains(v, StringComparison.OrdinalIgnoreCase)),
            EdgeOperator.StartsWith => CompareString(field, predicate.Value, (s, v) => s.StartsWith(v, StringComparison.OrdinalIgnoreCase)),
            EdgeOperator.EndsWith => CompareString(field, predicate.Value, (s, v) => s.EndsWith(v, StringComparison.OrdinalIgnoreCase)),
            EdgeOperator.MatchesRegex => CompareRegex(field, predicate.Value),

            EdgeOperator.In => CompareIn(field, predicate.Value, predicate.ValueType),
            EdgeOperator.NotIn => !CompareIn(field, predicate.Value, predicate.ValueType),

            _ => false
        };
    }

    private enum NumericOp { Gt, Gte, Lt, Lte }

    private bool CompareEq(JsonElement field, JsonElement? value, EdgePredicateValueType valueType)
    {
        if (value is null) return false;
        var v = value.Value;

        // Numérico: bifurca por hint de tipo pra preservar precisão de Int64
        // (GetDouble() perde precisão a partir de 2^53). Auto recai em decimal
        // que cobre até 28-29 dígitos significativos com fallback pra double.
        if (field.ValueKind == JsonValueKind.Number && v.ValueKind == JsonValueKind.Number)
            return CompareNumericEq(field, v, valueType);

        return (field.ValueKind, v.ValueKind) switch
        {
            (JsonValueKind.String, JsonValueKind.String) =>
                string.Equals(field.GetString(), v.GetString(), StringComparison.Ordinal),
            (JsonValueKind.True, JsonValueKind.True) or (JsonValueKind.False, JsonValueKind.False) => true,
            (JsonValueKind.True, JsonValueKind.False) or (JsonValueKind.False, JsonValueKind.True) => false,
            (JsonValueKind.Null, JsonValueKind.Null) => true,
            // Strict typing: number ≠ string-de-number; mismatch logado pra debug
            _ => LogTypeMismatch(field.ValueKind, v.ValueKind)
        };
    }

    private bool LogTypeMismatch(JsonValueKind fieldKind, JsonValueKind valueKind)
    {
        // LogDebug em vez de LogWarning: type mismatch é o comportamento esperado de
        // strict typing (decisão consciente do plano), e este path é hot — Switch com
        // 5 cases × 100 execs/s pode floodar logs sem trazer sinal acionável. Se
        // operadores precisarem investigar, ativam Debug temporariamente.
        _logger.LogDebug(
            "[EdgePredicate] Type mismatch — field={FieldKind} value={ValueKind}. Strict typing → false.",
            fieldKind, valueKind);
        return false;
    }

    /// <summary>
    /// Compara dois <see cref="JsonValueKind.Number"/> respeitando o hint de tipo declarado
    /// no predicate. Integer/Auto-com-int cabíveis usam Int64 (sem perda de precisão).
    /// Number usa Decimal. Fallback pra Double só em ULTIMO caso (overflow do decimal).
    /// </summary>
    private static bool CompareNumericEq(JsonElement field, JsonElement value, EdgePredicateValueType valueType)
    {
        if (valueType == EdgePredicateValueType.Integer
            && field.TryGetInt64(out var fi) && value.TryGetInt64(out var vi))
            return fi == vi;

        if (field.TryGetDecimal(out var fd) && value.TryGetDecimal(out var vd))
            return fd == vd;

        // Auto/Number quando valores estouram decimal (raríssimo): última escolha.
        return field.GetDouble() == value.GetDouble();
    }

    private bool CompareNumeric(JsonElement field, JsonElement? value, EdgePredicateValueType valueType, NumericOp op)
    {
        if (value is null) return false;
        if (field.ValueKind != JsonValueKind.Number || value.Value.ValueKind != JsonValueKind.Number)
            return false;

        // Mesma estratégia do Eq: Int64 quando declarado/cabível, Decimal default, Double fallback.
        if (valueType == EdgePredicateValueType.Integer
            && field.TryGetInt64(out var fi) && value.Value.TryGetInt64(out var vi))
            return op switch
            {
                NumericOp.Gt => fi > vi,
                NumericOp.Gte => fi >= vi,
                NumericOp.Lt => fi < vi,
                NumericOp.Lte => fi <= vi,
                _ => false
            };

        if (field.TryGetDecimal(out var fd) && value.Value.TryGetDecimal(out var vd))
            return op switch
            {
                NumericOp.Gt => fd > vd,
                NumericOp.Gte => fd >= vd,
                NumericOp.Lt => fd < vd,
                NumericOp.Lte => fd <= vd,
                _ => false
            };

        var fdb = field.GetDouble();
        var vdb = value.Value.GetDouble();
        return op switch
        {
            NumericOp.Gt => fdb > vdb,
            NumericOp.Gte => fdb >= vdb,
            NumericOp.Lt => fdb < vdb,
            NumericOp.Lte => fdb <= vdb,
            _ => false
        };
    }

    private bool CompareString(JsonElement field, JsonElement? value, Func<string, string, bool> op)
    {
        if (value is null) return false;
        if (field.ValueKind != JsonValueKind.String || value.Value.ValueKind != JsonValueKind.String)
            return false;
        var s = field.GetString();
        var v = value.Value.GetString();
        if (s is null || v is null) return false;
        return op(s, v);
    }

    private bool CompareRegex(JsonElement field, JsonElement? value)
    {
        if (value is null) return false;
        if (field.ValueKind != JsonValueKind.String || value.Value.ValueKind != JsonValueKind.String)
            return false;
        var s = field.GetString();
        var pattern = value.Value.GetString();
        if (s is null || pattern is null) return false;

        var regex = GetOrCreateRegex(pattern);
        if (regex is null) return false; // pattern inválido: já logado em GetOrCreateRegex

        try
        {
            return regex.IsMatch(s);
        }
        catch (RegexMatchTimeoutException ex)
        {
            _logger.LogWarning(ex,
                "[EdgePredicate] Regex timeout ({Timeout}ms) — predicate retornando false. Pattern={Pattern}",
                RegexTimeout.TotalMilliseconds, pattern);
            return false;
        }
    }

    /// <summary>
    /// Cache de regex por pattern. Compilação é custosa (10-100ms primeira vez); sem cache, Switch
    /// com 5 cases × 1000 execuções = 5000 compilações de patterns idênticos. Cap soft: estourou,
    /// novos patterns simplesmente não cacheiam — evita unbounded growth via patterns adversariais.
    /// </summary>
    private Regex? GetOrCreateRegex(string pattern)
    {
        if (_regexCache.TryGetValue(pattern, out var cached)) return cached;

        try
        {
            var regex = new Regex(pattern, RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);
            if (_regexCache.Count < RegexCacheCap)
                _regexCache.TryAdd(pattern, regex);
            return regex;
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex,
                "[EdgePredicate] Pattern regex inválido — predicate retornando false. Pattern={Pattern}",
                pattern);
            return null;
        }
    }

    private bool CompareIn(JsonElement field, JsonElement? value, EdgePredicateValueType valueType)
    {
        if (value is null || value.Value.ValueKind != JsonValueKind.Array) return false;
        foreach (var candidate in value.Value.EnumerateArray())
        {
            if (CompareEq(field, candidate, valueType)) return true;
        }
        return false;
    }

    /// <summary>
    /// JSONPath subset: $, $.field, $.a.b, $.list[N], $.list[N].field.
    /// Sem wildcards, filtros, slicing, índice negativo. Tudo fora do subset → false.
    /// </summary>
    internal static bool TryResolvePath(JsonElement root, string path, out JsonElement result)
    {
        result = default;
        if (string.IsNullOrEmpty(path) || path[0] != '$') return false;
        if (path == "$") { result = root; return true; }

        var current = root;
        var i = 1; // pula '$'
        while (i < path.Length)
        {
            var c = path[i];
            if (c == '.')
            {
                i++;
                var start = i;
                while (i < path.Length && path[i] != '.' && path[i] != '[') i++;
                if (i == start) return false;
                var name = path[start..i];
                if (current.ValueKind != JsonValueKind.Object) return false;
                if (!current.TryGetProperty(name, out current)) return false;
            }
            else if (c == '[')
            {
                i++;
                var start = i;
                while (i < path.Length && path[i] != ']') i++;
                if (i == path.Length) return false;
                var idxText = path[start..i];
                i++; // consome ']'
                if (!int.TryParse(idxText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var idx)) return false;
                if (idx < 0) return false;
                if (current.ValueKind != JsonValueKind.Array) return false;
                if (idx >= current.GetArrayLength()) return false;
                current = current[idx];
            }
            else
            {
                return false;
            }
        }

        result = current;
        return true;
    }
}
