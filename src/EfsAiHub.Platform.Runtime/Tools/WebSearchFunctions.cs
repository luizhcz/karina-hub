using System.ComponentModel;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EfsAiHub.Platform.Runtime.Tools;

/// <summary>
/// Funções para pesquisa de conteúdo na internet.
/// Estratégia em cascata: Wikipedia → DuckDuckGo API → StatusInvest/Fundamentus scraping.
/// </summary>
public static class WebSearchFunctions
{
    private static readonly HttpClient _client = new()
    {
        Timeout = TimeSpan.FromSeconds(15),
        DefaultRequestHeaders =
        {
            { "User-Agent", "Mozilla/5.0 (compatible; EfsAiHub.Api/1.0)" },
            { "Accept-Language", "pt-BR,pt;q=0.9,en;q=0.5" }
        }
    };

    private const int MinUsefulLength = 80;

    /// <summary>
    /// Pesquisa informações na internet sobre um termo.
    /// Tenta Wikipedia PT → Wikipedia EN → DuckDuckGo API → Sites financeiros (StatusInvest/Fundamentus).
    /// </summary>
    [Description("Pesquisa informações na internet. Retorna resumo e descrição.")]
    public static async Task<string> SearchWeb(
        [Description("Termo de busca, ex: 'Petrobras'")] string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return "Termo de busca não pode ser vazio.";

        // 1) Wikipedia PT — busca com nome da empresa
        var result = await TryWikipediaAsync(query, "pt");
        if (result is { Length: > MinUsefulLength })
            return result;

        // 2) Wikipedia EN — fallback para empresas estrangeiras
        result = await TryWikipediaAsync(query, "en");
        if (result is { Length: > MinUsefulLength })
            return result;

        // 3) DuckDuckGo Instant Answer API
        result = await TryDuckDuckGoApiAsync(query);
        if (result is { Length: > MinUsefulLength })
            return result;

        // 4) Tentar DuckDuckGo com query mais ampla
        result = await TryDuckDuckGoApiAsync($"{query} empresa");
        if (result is { Length: > MinUsefulLength })
            return result;

        // 5) StatusInvest — scraping de página do ativo (precisa do ticker)
        var ticker = ExtractTicker(query);
        if (ticker is not null)
        {
            result = await TryStatusInvestAsync(ticker);
            if (result is { Length: > MinUsefulLength })
                return result;

            // 6) Fundamentus — fallback final
            result = await TryFundamentusAsync(ticker);
            if (result is { Length: > MinUsefulLength })
                return result;
        }

        return $"Nenhuma informação detalhada encontrada para '{query}'.";
    }

    private static string? ExtractTicker(string query)
    {
        // Tickers B3: PETR4, VALE3, BKNG34, A1ES34, M1TA34, XPLG11, 2ANC7L, etc.
        // Formato: 4-6 chars alfanuméricos seguidos de 1-2 dígitos e opcionalmente uma letra (F/L/B)
        var match = Regex.Match(query, @"\b([A-Z0-9]{4,6}\d{1,2}[FLB]?)\b", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.ToUpperInvariant() : null;
    }

    // ── Wikipedia ───────────────────────────────────────────────────────────────

    private static async Task<string?> TryWikipediaAsync(string query, string lang)
    {
        try
        {
            // Primeiro faz search para achar o título correto
            var searchUrl = $"https://{lang}.wikipedia.org/w/api.php?action=query&list=search" +
                            $"&srsearch={Uri.EscapeDataString(query)}&srnamespace=0&srlimit=3&format=json";

            var searchJson = await _client.GetStringAsync(searchUrl);
            using var searchDoc = JsonDocument.Parse(searchJson);

            var searchResults = searchDoc.RootElement
                .GetProperty("query")
                .GetProperty("search");

            if (searchResults.GetArrayLength() == 0)
                return null;

            var title = searchResults[0].GetProperty("title").GetString()!;

            // Buscar extrato da página
            var extractUrl = $"https://{lang}.wikipedia.org/w/api.php?action=query&prop=extracts" +
                             $"&exintro=1&explaintext=1&redirects=1&titles={Uri.EscapeDataString(title)}&format=json";

            var extractJson = await _client.GetStringAsync(extractUrl);
            using var extractDoc = JsonDocument.Parse(extractJson);

            var pages = extractDoc.RootElement.GetProperty("query").GetProperty("pages");
            foreach (var page in pages.EnumerateObject())
            {
                if (page.Name == "-1") continue;
                if (page.Value.TryGetProperty("extract", out var extract) &&
                    extract.GetString() is { Length: > 50 } text)
                {
                    // Truncar se muito longo
                    var trimmed = text.Length > 1500 ? text[..1500] + "..." : text;
                    var sb = new StringBuilder();
                    sb.AppendLine($"[Wikipedia {lang.ToUpper()}] {title}");
                    sb.AppendLine(trimmed);
                    return sb.ToString();
                }
            }
        }
        catch
        {
            // Silenciar — tentar próxima estratégia
        }

        return null;
    }

    // ── DuckDuckGo Instant Answer API ───────────────────────────────────────────

    private static async Task<string?> TryDuckDuckGoApiAsync(string query)
    {
        try
        {
            var url = $"https://api.duckduckgo.com/?q={Uri.EscapeDataString(query)}&format=json&no_html=1&skip_disambig=1";
            var json = await _client.GetStringAsync(url);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var sb = new StringBuilder();

            if (root.TryGetProperty("AbstractText", out var abstractText) &&
                abstractText.GetString() is { Length: > 0 } text)
            {
                sb.AppendLine($"[DuckDuckGo] Resumo: {text}");
                if (root.TryGetProperty("AbstractSource", out var source))
                    sb.AppendLine($"Fonte: {source.GetString()}");
            }

            if (root.TryGetProperty("Definition", out var def) &&
                def.GetString() is { Length: > 0 } defText)
            {
                sb.AppendLine($"Definição: {defText}");
            }

            if (root.TryGetProperty("RelatedTopics", out var topics) &&
                topics.ValueKind == JsonValueKind.Array)
            {
                var count = 0;
                foreach (var topic in topics.EnumerateArray())
                {
                    if (count >= 5) break;
                    if (topic.TryGetProperty("Text", out var topicText) &&
                        topicText.GetString() is { Length: > 0 } topicStr)
                    {
                        sb.AppendLine($"- {topicStr}");
                        count++;
                    }
                }
            }

            var result = sb.ToString().Trim();
            return result.Length > MinUsefulLength ? result : null;
        }
        catch
        {
            return null;
        }
    }

    // ── StatusInvest Scraping ─────────────────────────────────────────────────────

    private static readonly Regex _htmlTagRegex = new(@"<[^>]+>", RegexOptions.Compiled);

    private static async Task<string?> TryStatusInvestAsync(string ticker)
    {
        try
        {
            // StatusInvest usa paths diferentes por tipo de ativo
            var paths = new[] { "acoes", "bdrs", "fiis", "etfs" };

            foreach (var path in paths)
            {
                try
                {
                    var url = $"https://statusinvest.com.br/{path}/{ticker.ToLowerInvariant()}";
                    using var request = new HttpRequestMessage(HttpMethod.Get, url);
                    request.Headers.Add("Accept", "text/html");

                    var response = await _client.SendAsync(request);
                    if (!response.IsSuccessStatusCode) continue;

                    var html = await response.Content.ReadAsStringAsync();

                    // Detectar página "não encontrado" (retorna 200 com título OPS)
                    if (html.Contains("OPS. . .N", StringComparison.OrdinalIgnoreCase) ||
                        html.Contains("não encontramos", StringComparison.OrdinalIgnoreCase))
                        continue;

                    // Extrair nome da empresa do título da página
                    var titleMatch = Regex.Match(html, @"<title>([^<]+)</title>", RegexOptions.IgnoreCase);
                    var pageTitle = titleMatch.Success
                        ? System.Net.WebUtility.HtmlDecode(titleMatch.Groups[1].Value.Trim())
                        : ticker;

                    var sb = new StringBuilder();
                    sb.AppendLine($"[StatusInvest] {pageTitle}");

                    // Extrair descrição da empresa (div com about-company ou company-description)
                    var descMatch = Regex.Match(html,
                        @"<div[^>]*class=""[^""]*(?:about-company|company-description)[^""]*""[^>]*>([\s\S]*?)</div>",
                        RegexOptions.IgnoreCase);

                    if (descMatch.Success)
                    {
                        var desc = CleanHtml(descMatch.Groups[1].Value);
                        if (desc.Length > 30)
                            sb.AppendLine($"Descrição: {desc}");
                    }

                    // Extrair setor
                    var sectorMatch = Regex.Match(html,
                        @"(?:Setor|Segmento)[^<]*</[^>]+>\s*<[^>]+>([^<]+)",
                        RegexOptions.IgnoreCase);

                    if (sectorMatch.Success)
                    {
                        var setor = System.Net.WebUtility.HtmlDecode(sectorMatch.Groups[1].Value.Trim());
                        if (setor.Length > 2)
                            sb.AppendLine($"Setor: {setor}");
                    }

                    // Extrair subsetor
                    var subMatch = Regex.Match(html,
                        @"Subsetor[^<]*</[^>]+>\s*<[^>]+>([^<]+)",
                        RegexOptions.IgnoreCase);

                    if (subMatch.Success)
                    {
                        var sub = System.Net.WebUtility.HtmlDecode(subMatch.Groups[1].Value.Trim());
                        if (sub.Length > 2)
                            sb.AppendLine($"Subsetor: {sub}");
                    }

                    // Extrair dados financeiros básicos (cotação, P/L, DY etc.)
                    var indicators = ExtractStatusInvestIndicators(html);
                    if (indicators.Length > 0)
                        sb.AppendLine(indicators);

                    var result = sb.ToString().Trim();
                    if (result.Length > MinUsefulLength)
                        return result;
                }
                catch
                {
                    // Tentar próximo path
                }
            }
        }
        catch
        {
            // Silenciar
        }

        return null;
    }

    private static string ExtractStatusInvestIndicators(string html)
    {
        var sb = new StringBuilder();
        var patterns = new Dictionary<string, string>
        {
            { "Cotação", @"<strong[^>]*class=""[^""]*value[^""]*""[^>]*>([^<]+)</strong>" },
            { "P/L", @"title=""P/L""[^>]*>[\s\S]*?<strong[^>]*>([^<]+)</strong>" },
            { "Dividend Yield", @"title=""D(?:ividend\s*)?Y(?:ield)?""[^>]*>[\s\S]*?<strong[^>]*>([^<]+)</strong>" },
        };

        foreach (var (name, pattern) in patterns)
        {
            var match = Regex.Match(html, pattern, RegexOptions.IgnoreCase);
            if (match.Success)
            {
                var value = match.Groups[1].Value.Trim();
                if (value.Length > 0 && value != "-")
                    sb.AppendLine($"{name}: {value}");
            }
        }

        return sb.ToString().Trim();
    }

    // ── Fundamentus Scraping ──────────────────────────────────────────────────────

    private static async Task<string?> TryFundamentusAsync(string ticker)
    {
        try
        {
            var url = $"https://www.fundamentus.com.br/detalhes.php?papel={Uri.EscapeDataString(ticker)}";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("Accept", "text/html");
            request.Headers.Add("Referer", "https://www.fundamentus.com.br/");

            var response = await _client.SendAsync(request);
            if (!response.IsSuccessStatusCode) return null;

            var html = await response.Content.ReadAsStringAsync();

            // Verificar se a página tem dados (Fundamentus retorna 200 mesmo sem dados)
            if (html.Contains("Nenhum papel encontrado") || html.Length < 500)
                return null;

            var sb = new StringBuilder();
            sb.AppendLine($"[Fundamentus] {ticker}");

            // Extrair nome/razão social da tabela
            var nomeMatch = Regex.Match(html,
                @"Empresa[^<]*</span[^>]*>\s*</td>\s*<td[^>]*>\s*<span[^>]*>([^<]+)",
                RegexOptions.IgnoreCase);

            if (nomeMatch.Success)
                sb.AppendLine($"Empresa: {nomeMatch.Groups[1].Value.Trim()}");

            // Extrair setor
            var setorMatch = Regex.Match(html,
                @"Setor[^<]*</span[^>]*>\s*</td>\s*<td[^>]*>\s*<a[^>]*>([^<]+)",
                RegexOptions.IgnoreCase);

            if (setorMatch.Success)
                sb.AppendLine($"Setor: {setorMatch.Groups[1].Value.Trim()}");

            // Extrair subsetor
            var subsetorMatch = Regex.Match(html,
                @"Subsetor[^<]*</span[^>]*>\s*</td>\s*<td[^>]*>\s*<a[^>]*>([^<]+)",
                RegexOptions.IgnoreCase);

            if (subsetorMatch.Success)
                sb.AppendLine($"Subsetor: {subsetorMatch.Groups[1].Value.Trim()}");

            // Extrair indicadores da tabela principal
            var indicatorPatterns = new (string Label, string Pattern)[]
            {
                ("Cotação", @"Cotação[^<]*</span[^>]*>\s*</td>\s*<td[^>]*>\s*<span[^>]*>([^<]+)"),
                ("P/L", @"P/L[^<]*</span[^>]*>\s*</td>\s*<td[^>]*>\s*<span[^>]*>([^<]+)"),
                ("P/VP", @"P/VP[^<]*</span[^>]*>\s*</td>\s*<td[^>]*>\s*<span[^>]*>([^<]+)"),
                ("Div. Yield", @"Div\.?\s*Yield[^<]*</span[^>]*>\s*</td>\s*<td[^>]*>\s*<span[^>]*>([^<]+)"),
                ("Valor de Mercado", @"Valor\s*de\s*[Mm]ercado[^<]*</span[^>]*>\s*</td>\s*<td[^>]*>\s*<span[^>]*>([^<]+)"),
            };

            foreach (var (label, pattern) in indicatorPatterns)
            {
                var match = Regex.Match(html, pattern, RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    var value = match.Groups[1].Value.Trim();
                    if (value.Length > 0 && value != "-")
                        sb.AppendLine($"{label}: {value}");
                }
            }

            var result = sb.ToString().Trim();
            return result.Length > MinUsefulLength ? result : null;
        }
        catch
        {
            return null;
        }
    }

    private static string CleanHtml(string html)
    {
        var text = _htmlTagRegex.Replace(html, " ");
        text = System.Net.WebUtility.HtmlDecode(text);
        text = Regex.Replace(text, @"\s+", " ");
        return text.Trim();
    }
}
