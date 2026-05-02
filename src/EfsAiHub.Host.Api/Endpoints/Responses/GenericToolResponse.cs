using EfsAiHub.Core.Agents.GenericTools;

namespace EfsAiHub.Host.Api.Models.Responses;

public sealed class GenericToolResponse
{
    public required string Id { get; init; }
    public required string ProjectId { get; init; }
    public required string TenantId { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string HttpMethod { get; init; }
    public required string UrlTemplate { get; init; }
    public required IReadOnlyDictionary<string, ParamDefinition> PathParams { get; init; }
    public required IReadOnlyDictionary<string, ParamDefinition> QueryParams { get; init; }
    public required IReadOnlyDictionary<string, string> CustomHeaders { get; init; }
    public required string InputContentType { get; init; }
    public string? InputSchema { get; init; }
    public required string OutputContentType { get; init; }
    public string? OutputSchema { get; init; }
    public int? TimeoutSecondsOverride { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }

    public static GenericToolResponse FromDomain(GenericTool tool) => new()
    {
        Id = tool.Id,
        ProjectId = tool.ProjectId,
        TenantId = tool.TenantId,
        Name = tool.Name,
        Description = tool.Description,
        HttpMethod = tool.HttpMethod.ToString(),
        UrlTemplate = tool.UrlTemplate,
        PathParams = tool.PathParams,
        QueryParams = tool.QueryParams,
        CustomHeaders = tool.CustomHeaders,
        InputContentType = tool.InputContentType.ToString(),
        InputSchema = tool.InputSchema,
        OutputContentType = tool.OutputContentType.ToString(),
        OutputSchema = tool.OutputSchema,
        TimeoutSecondsOverride = tool.TimeoutSecondsOverride,
        CreatedAt = tool.CreatedAt,
        UpdatedAt = tool.UpdatedAt,
    };
}
