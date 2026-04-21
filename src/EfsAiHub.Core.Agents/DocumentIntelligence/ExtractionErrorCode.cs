namespace EfsAiHub.Core.Agents.DocumentIntelligence;

/// <summary>
/// Códigos de erro padronizados para o executor Document Intelligence.
/// </summary>
public static class ExtractionErrorCode
{
    public const string FileSizeExceeded  = "FILE_SIZE_EXCEEDED";
    public const string UnreadablePdf     = "UNREADABLE_PDF";
    public const string SourceUnavailable = "SOURCE_UNAVAILABLE";
    public const string AzureDiFailure    = "AZURE_DI_FAILURE";
    public const string Timeout           = "TIMEOUT";
    public const string GateTimeout       = "GATE_TIMEOUT";
    public const string Cancelled         = "CANCELLED";
}
