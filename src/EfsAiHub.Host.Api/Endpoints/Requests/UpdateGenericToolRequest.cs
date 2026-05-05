using System.ComponentModel.DataAnnotations;
using EfsAiHub.Core.Agents.GenericTools;

namespace EfsAiHub.Host.Api.Models.Requests;

/// <summary>
/// Body do PUT /api/aihub/generic-tools/{id}. <see cref="ExpectedUpdatedAt"/> vem do
/// GET anterior — divergência → 412. Id da rota prevalece sobre qualquer Id no
/// body.
/// </summary>
public sealed class UpdateGenericToolRequest
{
    [Required, MaxLength(256)]
    public string Name { get; init; } = string.Empty;

    [MaxLength(4096)]
    public string Description { get; init; } = string.Empty;

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

    [MaxLength(2048)]
    public string? WhenToUse { get; init; }

    [Required]
    public DateTime ExpectedUpdatedAt { get; init; }

    /// <summary>
    /// Materializa o patch como <see cref="GenericTool"/>. Id/ProjectId/TenantId
    /// ficam vazios propositalmente — o service substitui pelos valores do row
    /// existente (não confia no client pra mudar identidade ou ownership).
    /// CreatedAt também é preservado pelo service.
    /// </summary>
    public GenericTool ToDomainPatch() => new()
    {
        Id = string.Empty,
        ProjectId = string.Empty,
        TenantId = string.Empty,
        Name = Name.Trim(),
        Description = Description ?? string.Empty,
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
        WhenToUse = string.IsNullOrWhiteSpace(WhenToUse) ? null : WhenToUse.Trim(),
    };
}
