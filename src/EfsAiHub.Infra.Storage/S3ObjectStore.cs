using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using EfsAiHub.Core.Abstractions.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EfsAiHub.Infra.Storage;

/// <summary>
/// Implementação S3 de <see cref="IObjectStore"/>. É a ÚNICA classe que toca o
/// AWS SDK. Aplica o <see cref="S3ObjectStoreOptions.KeyPrefix"/> a toda key
/// (espelhando o BuildKey do Redis) e trata toda falha de infra de forma
/// tolerante: PUT que falha apenas loga (best-effort), GET/HEAD em erro/miss
/// devolve null/false — nunca propaga, pra que o pipeline de ingestão tenha o
/// fallback (re-download) e o S3 indisponível jamais derrube o job.
/// </summary>
public sealed class S3ObjectStore : IObjectStore
{
    private readonly IAmazonS3 _s3;
    private readonly S3ObjectStoreOptions _options;
    private readonly ILogger<S3ObjectStore> _logger;
    private readonly string _bucket;
    private readonly string _keyPrefix;

    public S3ObjectStore(IAmazonS3 s3, IOptions<S3ObjectStoreOptions> options, ILogger<S3ObjectStore> logger)
    {
        _s3 = s3;
        _options = options.Value;
        _logger = logger;
        _bucket = string.IsNullOrWhiteSpace(_options.BucketName)
            ? throw new InvalidOperationException(
                "Storage:S3:BucketName é obrigatório quando Storage:S3:Enabled=true.")
            : _options.BucketName;

        // Garante separador no fim do prefixo: sem isso, um KeyPrefix sem '/'
        // colaria no 1º segmento da key e quebraria a fronteira de prefixo da qual
        // bucket policy / lifecycle / IAM dependem.
        _keyPrefix = string.IsNullOrEmpty(_options.KeyPrefix) || _options.KeyPrefix.EndsWith('/')
            ? _options.KeyPrefix
            : _options.KeyPrefix + "/";

        // O arquivo cru é PII: SSE-S3 (AES256) funciona, mas SSE-KMS dá auditoria
        // (CloudTrail data events) e rotação/segregação de chave melhores. Avisa
        // quando ligado sem KMS pra a escolha ser consciente (enforce real = bucket policy).
        if (_options.UseServerSideEncryption && string.IsNullOrWhiteSpace(_options.KmsKeyId))
            _logger.LogWarning("[S3ObjectStore] SSE-S3 (AES256) em uso sem KmsKeyId — para PII considere SSE-KMS.");
    }

    private string BuildKey(string key) => _keyPrefix + key;

    public async Task<bool> PutAsync(string key, byte[] content, string? contentType, CancellationToken ct = default)
    {
        try
        {
            using var ms = new MemoryStream(content, writable: false);
            var req = new PutObjectRequest
            {
                BucketName = _bucket,
                Key = BuildKey(key),
                InputStream = ms,
                ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType,
                AutoCloseStream = false,
            };

            if (_options.UseServerSideEncryption)
            {
                if (!string.IsNullOrWhiteSpace(_options.KmsKeyId))
                {
                    req.ServerSideEncryptionMethod = ServerSideEncryptionMethod.AWSKMS;
                    req.ServerSideEncryptionKeyManagementServiceKeyId = _options.KmsKeyId;
                }
                else
                {
                    req.ServerSideEncryptionMethod = ServerSideEncryptionMethod.AES256;
                }
            }

            await _s3.PutObjectAsync(req, ct).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            // Best-effort: perde-se durabilidade deste objeto, mas o fluxo segue
            // (caller tem fallback) — devolve false pra que o caller NÃO persista um
            // ponteiro pra objeto inexistente. NUNCA propaga falha de INFRA. O guard
            // deixa o cancelamento cooperativo (ct cancelado pelo worker/lease)
            // propagar — engolir cancelamento mascararia o sinal de "lease perdido".
            // Timeout do SDK (ct NÃO cancelado) cai aqui e é engolido como infra.
            _logger.LogWarning(ex, "[S3ObjectStore] PUT falhou key='{Key}' bucket='{Bucket}'.", key, _bucket);
            return false;
        }
    }

    public async Task<byte[]?> GetAsync(string key, CancellationToken ct = default)
    {
        try
        {
            // Deadline por-operação: o AmazonS3Config.Timeout cobre a request HTTP,
            // mas com ResponseHeadersRead a LEITURA do corpo (PDF de dezenas de MB)
            // não é coberta — sem isto, rede degradada penduraria o step segurando
            // um slot de worker. O CTS linkado limita download de headers + corpo.
            using var opCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            opCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _options.TimeoutSeconds)));
            using var resp = await _s3.GetObjectAsync(_bucket, BuildKey(key), opCts.Token).ConfigureAwait(false);
            using var dst = new MemoryStream();
            await resp.ResponseStream.CopyToAsync(dst, opCts.Token).ConfigureAwait(false);
            return dst.ToArray();
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null; // miss — caller faz fallback
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            // Inclui timeout do opCts (ct do caller NÃO cancelado) e falha de infra —
            // best-effort → null. Cancelamento do worker (ct cancelado) NÃO é pego
            // aqui: propaga, preservando o sinal de lease perdido.
            _logger.LogWarning(ex, "[S3ObjectStore] GET falhou key='{Key}' bucket='{Bucket}'.", key, _bucket);
            return null;
        }
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken ct = default)
    {
        try
        {
            await _s3.GetObjectMetadataAsync(_bucket, BuildKey(key), ct).ConfigureAwait(false);
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "[S3ObjectStore] HEAD falhou key='{Key}' bucket='{Bucket}'.", key, _bucket);
            return false;
        }
    }
}
