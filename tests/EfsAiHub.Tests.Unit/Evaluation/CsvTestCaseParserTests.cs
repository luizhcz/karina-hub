using System.Text;
using EfsAiHub.Host.Api.Services.Evaluation;

namespace EfsAiHub.Tests.Unit.Evaluation;

/// <summary>
/// CSV import edge cases (PR 3 review do tech lead — 6 fixtures obrigatórias).
/// </summary>
public sealed class CsvTestCaseParserTests
{
    private static Stream Bytes(string s, Encoding? enc = null)
        => new MemoryStream((enc ?? Encoding.UTF8).GetBytes(s));

    [Fact]
    public void Parse_HappyPath_Headers_E_3_Cases()
    {
        var csv = "input,expectedOutput,tags,weight\n"
                + "\"What's the weather?\",Sunny,weather|easy,1.0\n"
                + "\"Calc 2+2\",\"4\",math,1.5\n"
                + "\"Hello\",,greeting,0.5\n";

        var cases = CsvTestCaseParser.Parse("tsv-1", Bytes(csv));

        cases.Should().HaveCount(3);
        cases[0].Input.Should().Be("What's the weather?");
        cases[0].ExpectedOutput.Should().Be("Sunny");
        cases[0].Tags.Should().BeEquivalentTo(new[] { "weather", "easy" });
        cases[0].Weight.Should().Be(1.0);
        cases[1].Weight.Should().Be(1.5);
        // Campo vazio → string vazia (Trim de "" retorna ""). Mantém compat com
        // exports que distinguem "expected vazio" de "coluna ausente".
        cases[2].ExpectedOutput.Should().Be(string.Empty);
    }

    [Fact]
    public void Parse_UTF8_BOM_Detectado_Sem_Garbage()
    {
        var bom = new byte[] { 0xEF, 0xBB, 0xBF };
        var bodyBytes = Encoding.UTF8.GetBytes("input\nFoo\n");
        var stream = new MemoryStream(bom.Concat(bodyBytes).ToArray());

        var cases = CsvTestCaseParser.Parse("tsv-1", stream);

        cases.Should().HaveCount(1);
        cases[0].Input.Should().Be("Foo"); // sem BOM no input
    }

    [Fact]
    public void Parse_QuotesEscapadas_Preserva_Aspas()
    {
        var csv = "input\n\"He said \"\"hello\"\"\"\n";

        var cases = CsvTestCaseParser.Parse("tsv-1", Bytes(csv));

        cases.Should().HaveCount(1);
        cases[0].Input.Should().Be("He said \"hello\"");
    }

    [Fact]
    public void Parse_Newline_Dentro_De_Quotes_Preserva()
    {
        var csv = "input\n\"line1\nline2\"\n";

        var cases = CsvTestCaseParser.Parse("tsv-1", Bytes(csv));

        cases.Should().HaveCount(1);
        cases[0].Input.Should().Be("line1\nline2");
    }

    [Fact]
    public void Parse_Linha_Vazia_Ignorada()
    {
        const string csv = """
input
Foo

Bar
""";

        var cases = CsvTestCaseParser.Parse("tsv-1", Bytes(csv));

        cases.Should().HaveCount(2);
        cases[0].Input.Should().Be("Foo");
        cases[1].Input.Should().Be("Bar");
    }

    [Fact]
    public void Parse_Header_Sem_Coluna_Input_Lanca_Excecao()
    {
        const string csv = """
expectedOutput,tags
foo,bar
""";

        var act = () => CsvTestCaseParser.Parse("tsv-1", Bytes(csv));

        act.Should().Throw<CsvTestCaseParser.CsvParseException>()
            .WithMessage("*'input'*ausente*");
    }

    [Fact]
    public void Parse_Sem_Body_Lanca_Excecao()
    {
        const string csv = "input,expectedOutput\n";

        var act = () => CsvTestCaseParser.Parse("tsv-1", Bytes(csv));

        act.Should().Throw<CsvTestCaseParser.CsvParseException>()
            .WithMessage("*nenhuma linha de dados*");
    }

    [Fact]
    public void Parse_Weight_Invalido_Lanca_Excecao()
    {
        const string csv = """
input,weight
Foo,not-a-number
""";

        var act = () => CsvTestCaseParser.Parse("tsv-1", Bytes(csv));

        act.Should().Throw<CsvTestCaseParser.CsvParseException>()
            .WithMessage("*'weight'*não é número válido*");
    }

    [Fact]
    public void Parse_ExpectedToolCalls_JSON_Valido()
    {
        var csv = "input,expectedToolCalls\n"
                + "\"Get weather\",\"[{\"\"name\"\":\"\"get_weather\"\"}]\"\n";

        var cases = CsvTestCaseParser.Parse("tsv-1", Bytes(csv));

        cases.Should().HaveCount(1);
        cases[0].ExpectedToolCalls.Should().NotBeNull();
        cases[0].ExpectedToolCalls!.RootElement.GetArrayLength().Should().Be(1);
    }

    [Fact]
    public void Parse_ExpectedToolCalls_JSON_Invalido_Lanca_Excecao()
    {
        const string csv = """
input,expectedToolCalls
Foo,"not valid json"
""";

        var act = () => CsvTestCaseParser.Parse("tsv-1", Bytes(csv));

        act.Should().Throw<CsvTestCaseParser.CsvParseException>()
            .WithMessage("*JSON válido*");
    }

    [Fact]
    public void Parse_Index_Sequencial_Por_Ordem_De_Linha()
    {
        const string csv = """
input
A
B
C
""";

        var cases = CsvTestCaseParser.Parse("tsv-1", Bytes(csv));

        cases[0].Index.Should().Be(0);
        cases[1].Index.Should().Be(1);
        cases[2].Index.Should().Be(2);
    }

    [Fact]
    public void Parse_Headers_CaseInsensitive()
    {
        const string csv = """
INPUT,ExpectedOutput,TAGS,Weight
Foo,Bar,baz,1.0
""";

        var cases = CsvTestCaseParser.Parse("tsv-1", Bytes(csv));

        cases.Should().HaveCount(1);
        cases[0].Input.Should().Be("Foo");
        cases[0].ExpectedOutput.Should().Be("Bar");
        cases[0].Tags.Should().BeEquivalentTo(new[] { "baz" });
    }
}
