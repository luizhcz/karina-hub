namespace EfsAiHub.Platform.Runtime.Ingestion;

/// <summary>
/// Erro semântico do pipeline de ingestão — usado quando o download ou a
/// validação rejeitam o input por política (tamanho, redirect chain, tipo de
/// arquivo não suportado, etc.). Distingue rejeição esperada de falha
/// inesperada (que vira <see cref="Exception"/> genérica).
/// </summary>
public sealed class IngestionRejectedException : Exception
{
    public IngestionRejectedException(string message) : base(message) { }
    public IngestionRejectedException(string message, Exception inner) : base(message, inner) { }
}
