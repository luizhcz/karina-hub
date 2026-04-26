using System.Text;
using System.Text.RegularExpressions;
using EfsAiHub.Core.Abstractions.Blocklist;
using EfsAiHub.Platform.Runtime.Guards.BuiltIns;
using EfsAiHub.Platform.Runtime.Guards.Validators;
using Microsoft.Extensions.Logging;

namespace EfsAiHub.Platform.Runtime.Guards;

/// <summary>
/// Resultado de um match de blocklist. <c>MatchedText</c> é o conteúdo cru que bateu —
/// nunca persistido em audit log (vai obfuscado via <see cref="BlocklistMatcher.ObfuscateContext"/>).
/// </summary>
public sealed record BlocklistMatchResult(
    string PatternId,
    string Category,
    BlocklistAction Action,
    int StartIndex,
    int Length,
    string MatchedText);

/// <summary>
/// Pré-compila uma lista de <see cref="EffectivePattern"/> em regexes com MatchTimeout
/// (anti-ReDoS) e expõe scan/redact otimizados. Imutável — engine reconstrói quando
/// catálogo ou config do projeto muda.
/// </summary>
public sealed class BlocklistMatcher
{
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(100);

    private readonly CompiledPattern[] _patterns;
    private readonly string _replacement;

    /// <summary>Matcher que nunca casa nada — usado quando blocklist está desabilitado no projeto.</summary>
    public static BlocklistMatcher Empty { get; } = new(Array.Empty<CompiledPattern>(), "[REDACTED]");

    public bool HasPatterns => _patterns.Length > 0;

    private BlocklistMatcher(CompiledPattern[] patterns, string replacement)
    {
        _patterns = patterns;
        _replacement = replacement;
    }

    /// <summary>
    /// Constrói um matcher a partir dos patterns efetivos resolvidos pelo engine
    /// (catálogo + override do projeto). <paramref name="builtInHandlers"/> mapeia
    /// IDs de patterns <c>builtin</c> (ex: "internal_tools") para suas implementações.
    /// <para>
    /// <paramref name="logger"/> é opcional pra simplificar tests; em produção o engine
    /// passa o logger pra que falhas de compilação (regex inválido, builtin sem literals)
    /// virem warnings — caso contrário a proteção fica silenciosamente desligada.
    /// </para>
    /// </summary>
    public static BlocklistMatcher Build(
        IReadOnlyCollection<EffectivePattern> patterns,
        IReadOnlyDictionary<string, IBuiltInPatternHandler> builtInHandlers,
        string replacement,
        ILogger? logger = null)
    {
        var compiled = new List<CompiledPattern>(patterns.Count);
        foreach (var p in patterns)
        {
            var regex = TryCompile(p, builtInHandlers, logger);
            if (regex is null) continue;
            compiled.Add(new CompiledPattern(p, regex));
        }
        return new BlocklistMatcher(compiled.ToArray(), replacement);
    }

    /// <summary>
    /// Procura o primeiro match em <paramref name="text"/>. Aplica validators (Mod11/Luhn)
    /// pós-match — se falhar a validação, ignora e continua. Retorna null se nada bate.
    /// </summary>
    public BlocklistMatchResult? FirstMatch(string? text)
    {
        if (string.IsNullOrEmpty(text) || _patterns.Length == 0) return null;

        BlocklistMatchResult? earliest = null;
        foreach (var cp in _patterns)
        {
            var match = FindFirstValid(cp, text);
            if (match is null) continue;
            if (earliest is null || match.StartIndex < earliest.StartIndex)
                earliest = match;
        }
        return earliest;
    }

    /// <summary>
    /// Aplica redact em todos os matches válidos do texto. Retorna o novo texto
    /// e a lista de hits encontrados (ordenada por StartIndex no texto original).
    /// </summary>
    public (string RedactedText, IReadOnlyList<BlocklistMatchResult> Hits) Redact(string? text)
    {
        if (string.IsNullOrEmpty(text) || _patterns.Length == 0)
            return (text ?? string.Empty, Array.Empty<BlocklistMatchResult>());

        var hits = new List<BlocklistMatchResult>();
        foreach (var cp in _patterns)
            hits.AddRange(FindAllValid(cp, text));

        if (hits.Count == 0) return (text, Array.Empty<BlocklistMatchResult>());

        // Substitui da direita pra esquerda preservando offsets dos matches anteriores.
        hits.Sort((a, b) => a.StartIndex.CompareTo(b.StartIndex));
        var sb = new StringBuilder(text);
        for (var i = hits.Count - 1; i >= 0; i--)
        {
            var hit = hits[i];
            sb.Remove(hit.StartIndex, hit.Length);
            sb.Insert(hit.StartIndex, _replacement);
        }
        return (sb.ToString(), hits);
    }

    /// <summary>
    /// Produz snippet de ~<paramref name="contextSize"/> chars antes/depois do match,
    /// com o conteúdo violador substituído por <c>[REDACTED-len:N]</c>. Vai pro audit log.
    /// </summary>
    public static string ObfuscateContext(string text, int startIndex, int length, int contextSize = 50)
    {
        var preStart = Math.Max(0, startIndex - contextSize);
        var preLen = startIndex - preStart;
        var postStart = startIndex + length;
        var postLen = Math.Min(contextSize, text.Length - postStart);

        var pre = text.Substring(preStart, preLen);
        var post = text.Substring(postStart, postLen);
        return $"{pre}[REDACTED-len:{length}]{post}";
    }

    private BlocklistMatchResult? FindFirstValid(CompiledPattern cp, string text)
    {
        Match m;
        try { m = cp.Regex.Match(text); }
        catch (RegexMatchTimeoutException) { return null; }

        while (m.Success)
        {
            if (PassesValidator(cp.Source.Source.Validator, m.Value))
                return ToResult(cp, m);
            try { m = m.NextMatch(); }
            catch (RegexMatchTimeoutException) { return null; }
        }
        return null;
    }

    private IEnumerable<BlocklistMatchResult> FindAllValid(CompiledPattern cp, string text)
    {
        Match m;
        try { m = cp.Regex.Match(text); }
        catch (RegexMatchTimeoutException) { yield break; }

        while (m.Success)
        {
            if (PassesValidator(cp.Source.Source.Validator, m.Value))
                yield return ToResult(cp, m);

            try { m = m.NextMatch(); }
            catch (RegexMatchTimeoutException) { yield break; }
        }
    }

    private static BlocklistMatchResult ToResult(CompiledPattern cp, Match m)
        => new(
            PatternId: cp.Source.Source.Id,
            Category: cp.Source.Category,
            Action: cp.Source.EffectiveAction,
            StartIndex: m.Index,
            Length: m.Length,
            MatchedText: m.Value);

    private static bool PassesValidator(BlocklistValidator v, string matched) => v switch
    {
        BlocklistValidator.None => true,
        BlocklistValidator.Mod11 => Mod11Validator.IsValid(matched),
        BlocklistValidator.Luhn => LuhnValidator.IsValid(matched),
        _ => true
    };

    private static Regex? TryCompile(
        EffectivePattern p,
        IReadOnlyDictionary<string, IBuiltInPatternHandler> builtIns,
        ILogger? logger)
    {
        var src = p.Source;
        var options = RegexOptions.Compiled;
        if (!src.CaseSensitive) options |= RegexOptions.IgnoreCase;

        string pattern;
        switch (src.Type)
        {
            case BlocklistPatternType.Literal:
                pattern = src.WholeWord
                    ? $@"\b{Regex.Escape(src.Pattern)}\b"
                    : Regex.Escape(src.Pattern);
                break;

            case BlocklistPatternType.Regex:
                // WholeWord ignorado em regex puro — caller compõe boundaries dentro do pattern se quiser.
                pattern = src.Pattern;
                break;

            case BlocklistPatternType.BuiltIn:
                if (!builtIns.TryGetValue(src.Pattern, out var handler))
                {
                    logger?.LogWarning(
                        "[BlocklistMatcher] Pattern '{PatternId}' tipo=builtin referencia handler '{HandlerId}' não registrado. Pattern desligado.",
                        src.Id, src.Pattern);
                    return null;
                }
                var literals = handler.Literals;
                if (literals.Count == 0)
                {
                    // Race condition possível se IFunctionToolRegistry ainda não foi populado
                    // no startup. Próximo NOTIFY/TTL recompila com a lista atualizada.
                    logger?.LogWarning(
                        "[BlocklistMatcher] Pattern '{PatternId}' tipo=builtin id='{HandlerId}' retornou Literals vazio. Pattern desligado nesse build.",
                        src.Id, src.Pattern);
                    return null;
                }
                var alternation = string.Join("|", literals.Select(Regex.Escape));
                pattern = src.WholeWord
                    ? $@"\b(?:{alternation})\b"
                    : $"(?:{alternation})";
                break;

            default:
                logger?.LogWarning(
                    "[BlocklistMatcher] Pattern '{PatternId}' com Type desconhecido '{Type}'. Pattern desligado.",
                    src.Id, src.Type);
                return null;
        }

        try
        {
            return new Regex(pattern, options, MatchTimeout);
        }
        catch (ArgumentException ex)
        {
            // Pattern regex malformado — proteção fica silenciosamente desligada se não logarmos.
            logger?.LogWarning(ex,
                "[BlocklistMatcher] Pattern '{PatternId}' falhou ao compilar regex. Pattern desligado.",
                src.Id);
            return null;
        }
    }

    /// <summary>Pattern compilado: a regex pronta + a fonte original (pra hidratar o MatchResult).</summary>
    public sealed record CompiledPattern(EffectivePattern Source, Regex Regex);
}
