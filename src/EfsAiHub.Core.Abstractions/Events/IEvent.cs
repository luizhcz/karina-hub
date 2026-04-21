namespace EfsAiHub.Core.Abstractions.Events;

/// <summary>
/// Marcador de evento publicado no event bus. Todo evento expõe um tipo estável
/// (usado pelo serializer discriminator) e um timestamp monotônico.
/// </summary>
public interface IEvent
{
    string EventType { get; }
    DateTimeOffset OccurredAt { get; }
}
