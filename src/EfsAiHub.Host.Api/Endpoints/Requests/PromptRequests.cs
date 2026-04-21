namespace EfsAiHub.Host.Api.Models.Requests;

public record SavePromptVersionRequest(string VersionId, string Content);

public record SetMasterRequest(string VersionId);
