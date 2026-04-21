namespace EfsAiHub.Host.Api.Models.Requests;

public class TriggerWorkflowRequest
{
    public string? Input { get; init; }
    public Dictionary<string, string> Metadata { get; init; } = [];
}
