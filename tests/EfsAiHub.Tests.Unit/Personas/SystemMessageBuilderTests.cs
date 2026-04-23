using EfsAiHub.Core.Abstractions.Identity.Persona;
using EfsAiHub.Platform.Runtime.Factories;
using FluentAssertions;
using Xunit;

namespace EfsAiHub.Tests.Unit.Personas;

[Trait("Category", "Unit")]
public class SystemMessageBuilderTests
{
    private readonly SystemMessageBuilder _builder = new();

    [Fact]
    public void Build_EmptyPersona_ReturnsOnlyInstructions()
    {
        var result = _builder.Build("Você é um assistente.", ComposedPersonaPrompt.Empty);

        result.Should().Be("Você é um assistente.");
    }

    [Fact]
    public void Build_WithPersona_AppendsSectionAfterInstructions()
    {
        var persona = new ComposedPersonaPrompt(
            SystemSection: "## Persona\n- Segment: private",
            UserReinforcement: null);

        var result = _builder.Build("Você é um assistente.", persona);

        // Ordem crítica para prompt caching: instructions vem PRIMEIRO (prefix invariante).
        result.Should().StartWith("Você é um assistente.");
        result.Should().Contain("## Persona");
        // Deve ter pelo menos uma linha em branco separando
        result.Should().Contain("\n\n## Persona");
    }

    [Fact]
    public void Build_NullInstructions_StillIncludesPersona()
    {
        var persona = new ComposedPersonaPrompt("## Persona\n- Segment: private", null);

        var result = _builder.Build(null!, persona);

        result.Should().Contain("## Persona");
    }

    [Fact]
    public void Build_PersonaSectionMustComeAfterInstructionsByte()
    {
        // Invariante de prompt caching: o byte offset do prefixo cacheável não pode mudar
        // quando persona muda. Teste: 2 personas diferentes devem produzir strings que
        // COMPARTILHAM o mesmo prefixo (instructions).
        var instructions = "Você é um assistente financeiro.";
        var a = new ComposedPersonaPrompt("## Persona\n- Segment: private", null);
        var b = new ComposedPersonaPrompt("## Persona\n- Segment: varejo", null);

        var ra = _builder.Build(instructions, a);
        var rb = _builder.Build(instructions, b);

        ra.Should().StartWith(instructions);
        rb.Should().StartWith(instructions);
        // Os primeiros N chars (= instructions + separador) devem ser idênticos.
        var commonPrefixLength = instructions.Length;
        ra.Substring(0, commonPrefixLength).Should().Be(rb.Substring(0, commonPrefixLength));
    }
}
