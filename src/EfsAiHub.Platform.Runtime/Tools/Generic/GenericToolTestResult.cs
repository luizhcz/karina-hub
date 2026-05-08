namespace EfsAiHub.Platform.Runtime.Tools.Generic;

/// <summary>
/// Envelope verboso retornado pelo <see cref="IGenericToolTester"/> — diferente
/// do <see cref="EfsAiHub.Core.Agents.GenericTools.ToolExecutionResult"/> usado
/// em runtime de agente, este expõe diagnóstico cru pro user (status, headers,
/// body, latência, erro detalhado) sem mascaramento.
/// </summary>
public sealed record GenericToolTestResult
{
    public required bool Success { get; init; }

    /// <summary>HTTP status code retornado pelo upstream. Null se a request nunca foi enviada.</summary>
    public int? StatusCode { get; init; }

    public required long DurationMs { get; init; }

    /// <summary>URL final (com placeholders substituídos e query string).</summary>
    public required string Url { get; init; }

    public required string Method { get; init; }

    /// <summary>Body que foi enviado (já serializado conforme InputContentType). Null em GET ou InputContentType=None.</summary>
    public string? RequestBody { get; init; }

    public required IReadOnlyDictionary<string, string> RequestHeaders { get; init; }

    /// <summary>Corpo cru da resposta. Truncado em <see cref="GenericToolTester.MaxResponseBytes"/> — se foi truncado, <see cref="ResponseTruncated"/> é true.</summary>
    public string? ResponseBody { get; init; }

    public bool ResponseTruncated { get; init; }

    public required IReadOnlyDictionary<string, string> ResponseHeaders { get; init; }

    /// <summary>
    /// Resultado parseado segundo OutputContentType (Json → JsonElement, Csv → lista
    /// de dicts, Text → string). Null quando upstream retornou erro ou parse falhou.
    /// </summary>
    public object? ParsedData { get; init; }

    /// <summary>Mensagem detalhada de erro pra debug humano (ao contrário do executor de runtime).</summary>
    public string? Error { get; init; }
}
