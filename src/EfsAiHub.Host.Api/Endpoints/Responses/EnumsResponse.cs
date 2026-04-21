namespace EfsAiHub.Host.Api.Models.Responses;

public class EnumsResponse
{
    public List<string> OrchestrationModes     { get; init; } = [];
    public List<string> EdgeTypes              { get; init; } = [];
    public List<string> ExecutionStatuses      { get; init; } = [];
    public List<string> HitlStatuses           { get; init; } = [];
    public List<string> MiddlewarePhases       { get; init; } = [];
}
