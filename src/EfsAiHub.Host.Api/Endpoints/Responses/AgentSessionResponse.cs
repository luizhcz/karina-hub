
namespace EfsAiHub.Host.Api.Models.Responses;

public class AgentSessionResponse
{
    public required string SessionId { get; init; }
    public required string AgentId { get; init; }
    public required int TurnCount { get; init; }
    public required DateTime CreatedAt { get; init; }
    public required DateTime LastAccessedAt { get; init; }

    public static AgentSessionResponse FromDomain(AgentSessionRecord record) => new()
    {
        SessionId = record.SessionId,
        AgentId = record.AgentId,
        TurnCount = record.TurnCount,
        CreatedAt = record.CreatedAt,
        LastAccessedAt = record.LastAccessedAt
    };
}

public class SessionRunResponse
{
    public required string SessionId { get; init; }
    public required string Response { get; init; }
    public required int TurnCount { get; init; }
}
