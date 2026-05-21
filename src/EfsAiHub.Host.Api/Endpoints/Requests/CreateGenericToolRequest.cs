using System.ComponentModel.DataAnnotations;
using EfsAiHub.Core.Agents.GenericTools;

namespace EfsAiHub.Host.Api.Models.Requests;

/// <summary>
/// Body do POST /api/aihub/generic-tools. Id opcional — quando ausente o service
/// gera um GUID e devolve no response. Validação semântica (placeholders ↔
/// path params, schema FormUrl plano, headers reservados) roda no domain
/// <see cref="GenericTool.EnsureInvariants"/> e devolve 400.
/// </summary>
public sealed class CreateGenericToolRequest
{
    [MaxLength(64)]
    public string? Id { get; init; }

    [Required, MaxLength(256)]
    public string Name { get; init; } = string.Empty;

    [Required]
    public HttpMethodType HttpMethod { get; init; }

    [Required]
    public string UrlTemplate { get; init; } = string.Empty;

    public Dictionary<string, ParamDefinition> PathParams { get; init; } = new();
    public Dictionary<string, ParamDefinition> QueryParams { get; init; } = new();
    public Dictionary<string, string> CustomHeaders { get; init; } = new();

    public InputContentType InputContentType { get; init; } = InputContentType.None;

    public string? InputSchema { get; init; }

    public OutputContentType OutputContentType { get; init; } = OutputContentType.Json;

    public string? OutputSchema { get; init; }

    [Range(1, int.MaxValue)]
    public int? TimeoutSecondsOverride { get; init; }

    /// <summary>
    /// Quando true, executor forwarda <c>app_origin</c>/<c>access_token</c> da
    /// request original. Default false — tool considerada "geral".
    /// </summary>
    public bool IsExclusive { get; init; }

    /// <summary>
    /// Materializa um template de <see cref="GenericTool"/>. Id/ProjectId/TenantId
    /// ficam como string.Empty propositalmente — o service substitui por valores
    /// canônicos (Id gerado/ProjectId/TenantId do contexto da request) antes de
    /// validar invariantes e persistir.
    /// </summary>
    public GenericTool ToDomainTemplate() => new()
    {
        Id = string.Empty,
        ProjectId = string.Empty,
        TenantId = string.Empty,
        Name = Name.Trim(),
        HttpMethod = HttpMethod,
        UrlTemplate = UrlTemplate.Trim(),
        PathParams = PathParams ?? new Dictionary<string, ParamDefinition>(),
        QueryParams = QueryParams ?? new Dictionary<string, ParamDefinition>(),
        CustomHeaders = CustomHeaders ?? new Dictionary<string, string>(),
        InputContentType = InputContentType,
        InputSchema = InputSchema,
        OutputContentType = OutputContentType,
        OutputSchema = OutputSchema,
        TimeoutSecondsOverride = TimeoutSecondsOverride,
        IsExclusive = IsExclusive,
    };
}
