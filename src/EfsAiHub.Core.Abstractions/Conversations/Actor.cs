namespace EfsAiHub.Core.Abstractions.Conversations;

/// <summary>
/// Proveniência da mensagem — dimensão ortogonal à <see cref="ChatMessage.Role"/>.
/// Role discrimina conteúdo semântico (system/user/assistant/tool, padrão AG-UI);
/// Actor discrimina origem do envio (humano vs automação).
///
/// Trust model: o backend confia no valor declarado pelo cliente porque o trust
/// boundary fica na camada de proxy upstream. Ver ADR 0014.
/// </summary>
public enum Actor
{
    Human = 0,
    Robot = 1
}
