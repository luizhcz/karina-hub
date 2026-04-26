namespace EfsAiHub.Core.Abstractions.Blocklist;

/// <summary>
/// Resultado de uma violação detectada pelo BlocklistChatClient.
/// <para>
/// <c>Category</c> é o nível público — vai pro envelope HTTP 422 e UI.
/// <c>PatternId</c> é interno — só audit log, nunca exposto (evita oracle de timing).
/// <c>ContextObfuscated</c> contém ~50 chars antes/depois com o match já substituído
/// por <c>[REDACTED-len:N]</c> — evidência forense sem persistir conteúdo cru.
/// </para>
/// </summary>
public sealed record BlocklistViolation(
    Guid ViolationId,
    DateTimeOffset DetectedAt,
    string Category,
    string PatternId,
    BlocklistPhase Phase,
    BlocklistAction Action,
    string ContentHash,
    string ContextObfuscated);
