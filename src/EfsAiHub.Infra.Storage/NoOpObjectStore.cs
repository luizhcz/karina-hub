using EfsAiHub.Core.Abstractions.Storage;

namespace EfsAiHub.Infra.Storage;

/// <summary>
/// Usado quando <c>Storage:S3:Enabled=false</c>. Mantém o DI sempre satisfeito:
/// o caller injeta <see cref="IObjectStore"/> e chama normalmente, mas nada é
/// persistido — <see cref="PutAsync"/> devolve <c>false</c> (sem durabilidade) e
/// <see cref="GetAsync"/> devolve null, fazendo o pipeline cair no fallback (ex.:
/// re-download). Evita null-check espalhado pelos consumidores.
/// </summary>
public sealed class NoOpObjectStore : IObjectStore
{
    public Task<bool> PutAsync(string key, byte[] content, string? contentType, CancellationToken ct = default)
        => Task.FromResult(false);

    public Task<byte[]?> GetAsync(string key, CancellationToken ct = default)
        => Task.FromResult<byte[]?>(null);

    public Task<bool> ExistsAsync(string key, CancellationToken ct = default)
        => Task.FromResult(false);
}
