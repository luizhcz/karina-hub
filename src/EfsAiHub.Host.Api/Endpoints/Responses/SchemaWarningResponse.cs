using EfsAiHub.Platform.Runtime.Tools.Generic.Schema;

namespace EfsAiHub.Host.Api.Models.Responses;

/// <summary>
/// Forma HTTP estável da <see cref="NormalizationWarning"/>. Mantida como DTO
/// separado pra que mudanças no record interno não quebrem clientes.
/// </summary>
public sealed class SchemaWarningResponse
{
    public required string Code { get; init; }
    public required string Path { get; init; }
    public required string Message { get; init; }

    public static SchemaWarningResponse FromDomain(NormalizationWarning w) => new()
    {
        Code = w.Code,
        Path = w.Path,
        Message = w.Message,
    };
}
