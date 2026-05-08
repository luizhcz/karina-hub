using EfsAiHub.Platform.Runtime.Tools.Generic;

namespace EfsAiHub.Host.Api.Models.Responses;

/// <summary>
/// DTO de resposta do POST /api/aihub/generic-tools/{id}/execute. Espelha 1:1
/// <see cref="GenericToolTestResult"/>. Mantido como tipo separado pra estabilidade
/// do contrato HTTP — o tester é runtime, este é o shape exposto pra UI.
/// </summary>
public sealed class GenericToolTestResponse
{
    public bool Success { get; init; }
    public int? StatusCode { get; init; }
    public long DurationMs { get; init; }
    public string Url { get; init; } = string.Empty;
    public string Method { get; init; } = string.Empty;
    public string? RequestBody { get; init; }
    public IReadOnlyDictionary<string, string> RequestHeaders { get; init; } = new Dictionary<string, string>();
    public string? ResponseBody { get; init; }
    public bool ResponseTruncated { get; init; }
    public IReadOnlyDictionary<string, string> ResponseHeaders { get; init; } = new Dictionary<string, string>();
    public object? ParsedData { get; init; }
    public string? Error { get; init; }

    public static GenericToolTestResponse FromResult(GenericToolTestResult r) => new()
    {
        Success = r.Success,
        StatusCode = r.StatusCode,
        DurationMs = r.DurationMs,
        Url = r.Url,
        Method = r.Method,
        RequestBody = r.RequestBody,
        RequestHeaders = r.RequestHeaders,
        ResponseBody = r.ResponseBody,
        ResponseTruncated = r.ResponseTruncated,
        ResponseHeaders = r.ResponseHeaders,
        ParsedData = r.ParsedData,
        Error = r.Error,
    };
}
