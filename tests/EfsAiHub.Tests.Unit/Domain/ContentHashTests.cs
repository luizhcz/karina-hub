using EfsAiHub.Core.Abstractions.Hashing;

namespace EfsAiHub.Tests.Unit.Domain;

[Trait("Category", "Unit")]
public class ContentHashTests
{
    [Fact]
    public void MesmoConteudo_MesmoHash()
    {
        var h1 = ContentHashCalculator.ComputeFromString("conteudo identico");
        var h2 = ContentHashCalculator.ComputeFromString("conteudo identico");

        h1.Should().Be(h2);
    }

    [Fact]
    public void ConteudoDiferente_HashDiferente()
    {
        var h1 = ContentHashCalculator.ComputeFromString("conteudo A");
        var h2 = ContentHashCalculator.ComputeFromString("conteudo B");

        h1.Should().NotBe(h2);
    }

    [Fact]
    public void Compute_MesmoObjeto_HashIdentico()
    {
        var obj = new { Name = "Test", Value = 42 };
        var h1 = ContentHashCalculator.Compute(obj);
        var h2 = ContentHashCalculator.Compute(obj);

        h1.Should().Be(h2);
    }

    [Fact]
    public void Compute_CamposDiferentes_HashDiferente()
    {
        var obj1 = new { Name = "Agent A", Value = 1 };
        var obj2 = new { Name = "Agent B", Value = 1 };

        var h1 = ContentHashCalculator.Compute(obj1);
        var h2 = ContentHashCalculator.Compute(obj2);

        h1.Should().NotBe(h2);
    }

    [Fact]
    public void HashResulta_EmHexadecimalMinusculo()
    {
        var hash = ContentHashCalculator.ComputeFromString("qualquer coisa");

        hash.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public void StringVazia_ProduziHashValido()
    {
        var hash = ContentHashCalculator.ComputeFromString(string.Empty);

        hash.Should().NotBeNullOrEmpty();
        hash.Should().HaveLength(64);
    }
}
