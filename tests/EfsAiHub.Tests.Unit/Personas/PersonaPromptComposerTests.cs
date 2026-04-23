using EfsAiHub.Core.Abstractions.Identity.Persona;
using EfsAiHub.Platform.Runtime.Personalization;
using FluentAssertions;
using Xunit;

namespace EfsAiHub.Tests.Unit.Personas;

[Trait("Category", "Unit")]
public class PersonaPromptComposerTests
{
    private readonly PersonaPromptComposer _composer = new();

    [Fact]
    public void Compose_NullPersona_ReturnsEmpty()
    {
        var result = _composer.Compose(null);

        result.SystemSection.Should().BeNull();
        result.UserReinforcement.Should().BeNull();
        result.HasAnyContent.Should().BeFalse();
    }

    [Fact]
    public void Compose_AnonymousPersona_ReturnsEmpty()
    {
        var persona = UserPersona.Anonymous("u1", "cliente");

        var result = _composer.Compose(persona);

        result.SystemSection.Should().BeNull();
        result.UserReinforcement.Should().BeNull();
    }

    [Fact]
    public void Compose_FullPersona_IncludesAllFieldsAndTonePolicy()
    {
        var persona = new UserPersona(
            UserId: "u1",
            UserType: "cliente",
            DisplayName: "João Silva",
            Segment: "private",
            RiskProfile: "conservador",
            AdvisorId: "A123");

        var result = _composer.Compose(persona);

        result.HasAnyContent.Should().BeTrue();
        result.SystemSection.Should().Contain("## Persona do cliente");
        result.SystemSection.Should().Contain("Segment: private");
        result.SystemSection.Should().Contain("Risk profile: conservador");
        result.SystemSection.Should().Contain("Display name: João Silva");
        result.SystemSection.Should().Contain("Advisor: A123");
        result.SystemSection.Should().Contain("## Tone Policy");
        // Tone policy específica de (private, conservador) bate com tabela
        result.SystemSection.Should().Contain("renda fixa grau de investimento");
    }

    [Fact]
    public void Compose_ReinforcementIsShort_AndIncludesKeyFields()
    {
        var persona = new UserPersona(
            UserId: "u1",
            UserType: "cliente",
            DisplayName: "João",
            Segment: "private",
            RiskProfile: "conservador",
            AdvisorId: null);

        var result = _composer.Compose(persona);

        result.UserReinforcement.Should().NotBeNullOrEmpty();
        result.UserReinforcement.Should().StartWith("[").And.EndWith("]");
        result.UserReinforcement.Should().Contain("persona.segment=private");
        result.UserReinforcement.Should().Contain("persona.risk=conservador");
        // Não deve ultrapassar ~60 chars (~15 tokens)
        result.UserReinforcement!.Length.Should().BeLessThan(80);
    }

    [Fact]
    public void Compose_OnlyDisplayName_SkipsTonePolicy()
    {
        // sem segment + risk, não há match na TonePolicyTable — mas o bloco persona
        // ainda é emitido com o campo display_name. Garante que a composição é "parcial"
        // sem crash quando campos centrais faltam.
        var persona = new UserPersona(
            UserId: "u1",
            UserType: "cliente",
            DisplayName: "Maria",
            Segment: null,
            RiskProfile: null,
            AdvisorId: null);

        var result = _composer.Compose(persona);

        result.SystemSection.Should().Contain("Display name: Maria");
        result.SystemSection.Should().NotContain("## Tone Policy");
        result.UserReinforcement.Should().BeNull(); // sem segment/risk = sem reforço
    }

    [Fact]
    public void Compose_UnmappedSegment_EmitsPersonaButNoTonePolicy()
    {
        var persona = new UserPersona(
            UserId: "u1",
            UserType: "cliente",
            DisplayName: null,
            Segment: "segmento-novo-nao-cadastrado",
            RiskProfile: "conservador",
            AdvisorId: null);

        var result = _composer.Compose(persona);

        result.SystemSection.Should().Contain("Segment: segmento-novo-nao-cadastrado");
        result.SystemSection.Should().NotContain("## Tone Policy");
    }
}
