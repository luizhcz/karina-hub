namespace EfsAiHub.Core.Abstractions.Secrets;

public interface ISecretResolver
{
    Task<string?> ResolveAsync(string? referenceOrLiteral, SecretContext context, CancellationToken ct = default);
}
