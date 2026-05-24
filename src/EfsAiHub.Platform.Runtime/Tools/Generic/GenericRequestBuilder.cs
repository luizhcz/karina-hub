using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using EfsAiHub.Core.Agents.GenericTools;
using EfsAiHub.Core.Abstractions.Persistence;

namespace EfsAiHub.Platform.Runtime.Tools.Generic;

/// <summary>
/// Helper puro para construir <see cref="HttpRequestMessage"/> a partir de um
/// <see cref="GenericTool"/> e os argumentos vindos do LLM. Sem efeitos colaterais —
/// fácil de testar isolado.
/// </summary>
public static class GenericRequestBuilder
{
    public static HttpRequestMessage Build(
        GenericTool tool,
        IReadOnlyDictionary<string, object?> args)
    {
        var url = BuildUrl(tool, args);
        var method = tool.HttpMethod == HttpMethodType.POST ? HttpMethod.Post : HttpMethod.Get;
        var request = new HttpRequestMessage(method, url);

        request.Headers.Accept.Clear();
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(MediaTypeFor(tool.OutputContentType)));

        foreach (var (key, value) in tool.CustomHeaders)
            request.Headers.TryAddWithoutValidation(key, value);

        if (tool.HttpMethod == HttpMethodType.POST)
        {
            var body = BuildBody(tool, args);
            if (body is not null)
                request.Content = body;
        }

        return request;
    }

    public static string BuildUrl(GenericTool tool, IReadOnlyDictionary<string, object?> args)
    {
        var url = GenericTool.PlaceholderRegex.Replace(tool.UrlTemplate, match =>
        {
            var name = match.Groups[1].Value;
            args.TryGetValue(name, out var value);
            return Uri.EscapeDataString(Stringify(value) ?? string.Empty);
        });

        if (tool.QueryParams.Count == 0) return url;

        var pairs = new List<string>();
        foreach (var (name, _) in tool.QueryParams)
        {
            if (!args.TryGetValue(name, out var value) || value is null) continue;
            pairs.Add($"{Uri.EscapeDataString(name)}={Uri.EscapeDataString(Stringify(value) ?? string.Empty)}");
        }

        if (pairs.Count == 0) return url;
        var separator = url.Contains('?') ? "&" : "?";
        return url + separator + string.Join("&", pairs);
    }

    private static HttpContent? BuildBody(GenericTool tool, IReadOnlyDictionary<string, object?> args)
    {
        switch (tool.InputContentType)
        {
            case InputContentType.None:
                return null;

            case InputContentType.Json:
            {
                var bodyArgs = ProjectBodyArgs(tool, args);
                var json = JsonSerializer.Serialize(bodyArgs, JsonDefaults.Domain);
                return new StringContent(json, Encoding.UTF8, "application/json");
            }

            case InputContentType.Text:
            {
                // Schema do body em modo Text é sempre 1 campo string — primeiro
                // property root vira o body cru. Sem property → body vazio.
                var fieldName = ResolveTextBodyFieldName(tool.InputSchema);
                var raw = fieldName is not null && args.TryGetValue(fieldName, out var v)
                    ? Stringify(v) ?? string.Empty
                    : string.Empty;
                return new StringContent(raw, Encoding.UTF8, "text/plain");
            }

            case InputContentType.FormUrlEncoded:
            {
                var bodyArgs = ProjectBodyArgs(tool, args);
                var pairs = bodyArgs
                    .Where(kv => kv.Value is not null)
                    .Select(kv => new KeyValuePair<string, string>(kv.Key, Stringify(kv.Value) ?? string.Empty));
                return new FormUrlEncodedContent(pairs);
            }
        }

        return null;
    }

    /// <summary>
    /// Filtra os args do agente removendo chaves que correspondem a path/query
    /// params — o que sobra é considerado payload de body. Garante que o LLM
    /// pode enviar tudo num dict único sem duplicar dados.
    /// </summary>
    private static Dictionary<string, object?> ProjectBodyArgs(
        GenericTool tool,
        IReadOnlyDictionary<string, object?> args)
    {
        var pathOrQuery = new HashSet<string>(StringComparer.Ordinal);
        foreach (var k in tool.PathParams.Keys) pathOrQuery.Add(k);
        foreach (var k in tool.QueryParams.Keys) pathOrQuery.Add(k);

        var body = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (k, v) in args)
        {
            if (pathOrQuery.Contains(k)) continue;
            body[k] = v;
        }
        return body;
    }

    private static string? ResolveTextBodyFieldName(string? inputSchema)
    {
        if (string.IsNullOrWhiteSpace(inputSchema)) return null;
        try
        {
            using var doc = JsonDocument.Parse(inputSchema);
            if (!doc.RootElement.TryGetProperty("properties", out var props)
                || props.ValueKind != JsonValueKind.Object) return null;
            foreach (var prop in props.EnumerateObject()) return prop.Name;
        }
        catch (JsonException)
        {
            return null;
        }
        return null;
    }

    private static string? Stringify(object? value)
    {
        if (value is null) return null;
        if (value is string s) return s;
        if (value is bool b) return b ? "true" : "false";
        if (value is JsonElement je)
        {
            return je.ValueKind switch
            {
                JsonValueKind.String => je.GetString(),
                JsonValueKind.Number => je.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Null => null,
                _ => je.GetRawText(),
            };
        }
        return Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string MediaTypeFor(OutputContentType type) => type switch
    {
        OutputContentType.Json => "application/json",
        OutputContentType.Text => "text/plain",
        OutputContentType.Csv => "text/csv",
        _ => "application/json",
    };
}
