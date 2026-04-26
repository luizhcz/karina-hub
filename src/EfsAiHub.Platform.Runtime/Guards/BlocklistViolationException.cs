using EfsAiHub.Core.Abstractions.Blocklist;

namespace EfsAiHub.Platform.Runtime.Guards;

/// <summary>
/// Lançada pelo BlocklistChatClient quando um pattern com action=block bate.
/// Mapeada pelo GlobalExceptionMiddleware pra HTTP 422 com envelope contendo
/// apenas Category + ViolationId — PatternId nunca é exposto (evita oracle de
/// pattern via HTTP).
/// </summary>
public sealed class BlocklistViolationException : Exception
{
    public BlocklistViolation Violation { get; }

    public BlocklistViolationException(BlocklistViolation violation)
        : base($"Conteúdo violou política do projeto (categoria: {violation.Category}).")
    {
        Violation = violation;
    }
}
