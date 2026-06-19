namespace EfsAiHub.Infra.Storage;

/// <summary>
/// Configuração do object store S3 (seção <c>Storage:S3</c> do appsettings).
/// Credenciais NÃO vivem aqui — usam a default credential chain do AWS SDK
/// (IAM role em prod, AWS_PROFILE/AWS_REGION + mount ~/.aws em dev), a mesma
/// já usada pelo AWS Secrets Manager. Zero segredo no código/config.
/// </summary>
public sealed class S3ObjectStoreOptions
{
    public const string SectionName = "Storage:S3";

    /// <summary>Feature-flag. <c>false</c> (default) registra um no-op — nada vai pro S3.</summary>
    public bool Enabled { get; init; } = false;

    /// <summary>Bucket alvo. Obrigatório quando <see cref="Enabled"/> é true.</summary>
    public string? BucketName { get; init; }

    /// <summary>Região (ex.: us-east-1). Se vazia, o SDK resolve via env/profile.</summary>
    public string? Region { get; init; }

    /// <summary>Prefixo de path aplicado a toda key (espelha o prefixo do Redis). Default "efs-ai-hub/".</summary>
    public string KeyPrefix { get; init; } = "efs-ai-hub/";

    /// <summary>Liga Server-Side Encryption no PUT. Default true.</summary>
    public bool UseServerSideEncryption { get; init; } = true;

    /// <summary>Vazio = SSE-S3 (AES256). Preenchido = SSE-KMS com esta chave.</summary>
    public string? KmsKeyId { get; init; }

    /// <summary>Endpoint custom para MinIO/LocalStack em dev. Vazio = AWS real.</summary>
    public string? ServiceUrl { get; init; }

    /// <summary>Path-style addressing (necessário em MinIO/LocalStack). Default false.</summary>
    public bool ForcePathStyle { get; init; } = false;

    /// <summary>Timeout por request ao S3 (s). Curto pra não pendurar o fluxo. Default 30.</summary>
    public int TimeoutSeconds { get; init; } = 30;
}
