using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace EfsAiHub.Platform.Runtime.Sanitization;

/// <summary>
/// Implementação default do <see cref="ILlmPayloadSanitizer"/>. Padrões
/// compilados singleton — custo de aplicação por payload é proporcional ao
/// número de bytes, não ao número de regexes. Aplica em ordem do mais
/// específico (chave do OpenAI <c>sk-...</c>) pro mais genérico (campos
/// <c>"api_key": "..."</c>) — primeiro match ganha pra não substituir 2x.
/// </summary>
public sealed class RegexLlmPayloadSanitizer : ILlmPayloadSanitizer
{
    private readonly IReadOnlyList<SanitizationPattern> _patterns;

    public RegexLlmPayloadSanitizer(IOptions<LlmPayloadSanitizerOptions>? options = null)
    {
        var configured = options?.Value.Patterns;
        _patterns = configured is { Count: > 0 } ? configured : DefaultPatterns;
    }

    public string Sanitize(string payload)
    {
        if (string.IsNullOrEmpty(payload)) return payload;

        var result = payload;
        foreach (var pattern in _patterns)
        {
            result = pattern.Regex.Replace(result, m => pattern.Replace(m));
        }
        return result;
    }

    /// <summary>
    /// Defaults cobrem secrets mais comuns. Lista é determinística e pode
    /// ser sobrescrita via DI (testes / configs específicas de cliente).
    /// </summary>
    internal static readonly IReadOnlyList<SanitizationPattern> DefaultPatterns = new[]
    {
        // OpenAI: sk-..., sk-proj-..., sk-svcacct-...
        new SanitizationPattern(
            "openai-key",
            new Regex(@"sk-(?:proj-|svcacct-|None-)?[A-Za-z0-9_-]{20,}", RegexOptions.Compiled | RegexOptions.CultureInvariant),
            _ => "***REDACTED:openai-key***"),

        // JWT (3 segmentos base64url separados por ponto)
        new SanitizationPattern(
            "jwt",
            new Regex(@"eyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}", RegexOptions.Compiled | RegexOptions.CultureInvariant),
            _ => "***REDACTED:jwt***"),

        // Slack tokens
        new SanitizationPattern(
            "slack-token",
            new Regex(@"xox[baprs]-[A-Za-z0-9-]{20,}", RegexOptions.Compiled | RegexOptions.CultureInvariant),
            _ => "***REDACTED:slack-token***"),

        // GitHub PAT (clássico ghp_ e fine-grained github_pat_)
        new SanitizationPattern(
            "github-pat",
            new Regex(@"(?:ghp_|github_pat_)[A-Za-z0-9_]{20,}", RegexOptions.Compiled | RegexOptions.CultureInvariant),
            _ => "***REDACTED:github-pat***"),

        // Genérico: "api_key"|"apiKey"|"authorization"|"bearer"|"x-api-key"|"secret"|"token"|"password"
        // seguido de ":" e o valor entre aspas (escapadas ou não) com tamanho >= 8.
        // Preserva o nome do campo + aspas + substitui só o VALOR.
        new SanitizationPattern(
            "credential-field",
            new Regex(
                @"(\\?""(?:api[_-]?key|authorization|bearer|x-api-key|secret|token|password)\\?""\s*:\s*\\?"")([^""\\]{8,})(\\?"")",
                RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase),
            m => $"{m.Groups[1].Value}***REDACTED:credential***{m.Groups[3].Value}"),

        // Authorization: Bearer <token> em headers HTTP serializados como string
        new SanitizationPattern(
            "bearer-header",
            new Regex(@"(?i)(authorization\s*:\s*bearer\s+)([A-Za-z0-9._\-+/=]{20,})", RegexOptions.Compiled | RegexOptions.CultureInvariant),
            m => $"{m.Groups[1].Value}***REDACTED:bearer***"),
    };
}

/// <summary>
/// Padrão único de sanitização — par <c>(regex, replacer)</c> com um
/// <c>kind</c> textual usado no token de redação.
/// </summary>
public sealed record SanitizationPattern(string Kind, Regex Regex, Func<Match, string> Replace);

public sealed class LlmPayloadSanitizerOptions
{
    public IReadOnlyList<SanitizationPattern>? Patterns { get; init; }
}
