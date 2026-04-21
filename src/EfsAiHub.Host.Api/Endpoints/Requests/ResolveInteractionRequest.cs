namespace EfsAiHub.Host.Api.Models.Requests;

public class ResolveInteractionRequest
{
    public required string Resolution { get; init; }
    public bool Approved { get; init; } = true;
}
