namespace EfsAiHub.Core.Abstractions.Identity.Persona;

/// <summary>
/// Dados de personalização resolvidos a partir do UserId. Fonte de verdade é
/// uma API externa; este record é a projeção usada pelo runtime (composer
/// de prompt, observabilidade, policies). Mantido enxuto por decisão de MVP
/// — campos extensíveis entrariam como futura feature, não como JSONB aberto.
/// </summary>
public sealed record UserPersona(
    string UserId,
    string UserType,
    string? DisplayName,
    string? Segment,
    string? RiskProfile,
    string? AdvisorId)
{
    /// <summary>
    /// Constante sentinel reutilizável para fluxos sem identidade. Evita
    /// alocação por request quando nenhuma personalização se aplica.
    /// </summary>
    public static readonly UserPersona AnonymousInstance =
        new("", "", null, null, null, null);

    public static UserPersona Anonymous(string userId, string userType)
        => new(userId, userType, null, null, null, null);

    /// <summary>
    /// true quando nenhum campo personalizável foi resolvido — composer
    /// não emite seção de persona no prompt.
    /// </summary>
    public bool IsAnonymous =>
        DisplayName is null && Segment is null && RiskProfile is null;
}
