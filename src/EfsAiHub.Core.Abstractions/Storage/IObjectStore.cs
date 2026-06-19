namespace EfsAiHub.Core.Abstractions.Storage;

/// <summary>
/// Object storage durável (S3 e compatíveis) para blobs da ingestão. Abstração
/// pura, sem dependência de SDK — espelha o precedente <c>IDistributedSlotCounter</c>
/// (interface aqui em Core.Abstractions, implementação em Infra).
///
/// Contrato de tolerância a falha (load-bearing): implementações NÃO propagam
/// erros de infraestrutura. Escrita é best-effort (<see cref="PutAsync"/> engole e
/// loga); leitura devolve <c>null</c> em miss/erro. O caller decide o fallback
/// (ex.: re-download da origem). Alinha com a filosofia do projeto de que infra
/// indisponível é degradação graciosa, nunca erro do job.
///
/// A <c>key</c> é a chave LÓGICA (sem o prefixo do bucket) — a implementação
/// aplica o <c>KeyPrefix</c> configurado, igual ao <c>BuildKey</c> do Redis. A
/// "pasta" no S3 é apenas convenção de prefixo: é criada implicitamente pelo PUT,
/// não existe operação de criar diretório.
/// </summary>
public interface IObjectStore
{
    /// <summary>Grava (overwrite) o objeto. Best-effort — não propaga erro de infra.</summary>
    Task PutAsync(string key, byte[] content, string? contentType, CancellationToken ct = default);

    /// <summary>Lê o objeto. Retorna <c>null</c> em miss ou erro (sem propagar).</summary>
    Task<byte[]?> GetAsync(string key, CancellationToken ct = default);

    /// <summary><c>true</c> se o objeto existe; <c>false</c> em miss ou erro.</summary>
    Task<bool> ExistsAsync(string key, CancellationToken ct = default);
}
