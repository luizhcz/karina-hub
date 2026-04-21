using System.Text.Json;
using EfsAiHub.Host.Api.Services;

namespace EfsAiHub.Tests.Unit.Application;

[Trait("Category", "Unit")]
public class ExecutionOutputParserTests
{
    // ── Pattern { "message": "...", "output": {...} } ───────────────────────

    [Fact]
    public void MessageOutput_SeparaTextoEPayload()
    {
        var json = """{"message": "Boleta criada com sucesso.", "output": {"ticker": "PETR4", "qty": 100}}""";
        var parsed = ExecutionOutputParser.Parse(json);

        parsed.TextContent.Should().Be("Boleta criada com sucesso.");
        parsed.StructuredOutput.Should().NotBeNull();
        parsed.StructuredOutput!.RootElement.GetProperty("ticker").GetString().Should().Be("PETR4");
        parsed.StructuredOutput!.RootElement.GetProperty("qty").GetInt32().Should().Be(100);
    }

    [Fact]
    public void MessageSemOutput_RetornaDocumentoInteiro()
    {
        var json = """{"message": "Feito."}""";
        var parsed = ExecutionOutputParser.Parse(json);

        parsed.TextContent.Should().Be("Feito.");
        parsed.StructuredOutput.Should().NotBeNull();
        parsed.StructuredOutput!.RootElement.GetProperty("message").GetString().Should().Be("Feito.");
    }

    [Fact]
    public void MessageComOutputNull_RetornaDocumentoInteiro()
    {
        var json = """{"message": "Ok", "output": null}""";
        var parsed = ExecutionOutputParser.Parse(json);

        parsed.TextContent.Should().Be("Ok");
        parsed.StructuredOutput.Should().NotBeNull();
        parsed.StructuredOutput!.RootElement.GetProperty("message").GetString().Should().Be("Ok");
    }

    // ── Structured output genérico (JSON sem "message") ─────────────────────

    [Fact]
    public void JsonGenerico_Objeto_ReconheceComoStructuredOutput()
    {
        var json = """{"ticker": "PETR4", "quantidade": 100, "preco": 28.50}""";
        var parsed = ExecutionOutputParser.Parse(json);

        parsed.TextContent.Should().Be(json);
        parsed.StructuredOutput.Should().NotBeNull();
        parsed.StructuredOutput!.RootElement.GetProperty("ticker").GetString().Should().Be("PETR4");
        parsed.StructuredOutput!.RootElement.GetProperty("quantidade").GetInt32().Should().Be(100);
    }

    [Fact]
    public void JsonGenerico_Array_ReconheceComoStructuredOutput()
    {
        var json = """[{"ticker": "PETR4"}, {"ticker": "VALE3"}]""";
        var parsed = ExecutionOutputParser.Parse(json);

        parsed.TextContent.Should().Be(json);
        parsed.StructuredOutput.Should().NotBeNull();
        parsed.StructuredOutput!.RootElement.GetArrayLength().Should().Be(2);
    }

    [Fact]
    public void JsonGenerico_ObjetoComplexo_PreservaEstrutura()
    {
        var json = """{"boletas": [{"ticker": "PETR4", "qty": 100}], "resumo": "1 boleta coletada"}""";
        var parsed = ExecutionOutputParser.Parse(json);

        parsed.StructuredOutput.Should().NotBeNull();
        parsed.StructuredOutput!.RootElement.GetProperty("boletas").GetArrayLength().Should().Be(1);
        parsed.StructuredOutput!.RootElement.GetProperty("resumo").GetString().Should().Be("1 boleta coletada");
    }

    // ── Texto puro ──────────────────────────────────────────────────────────

    [Fact]
    public void TextoPuro_RetornaSemStructuredOutput()
    {
        var text = "A posição do cliente PETR4 é de 500 ações.";
        var parsed = ExecutionOutputParser.Parse(text);

        parsed.TextContent.Should().Be(text);
        parsed.StructuredOutput.Should().BeNull();
    }

    [Fact]
    public void StringVazia_RetornaSemStructuredOutput()
    {
        var parsed = ExecutionOutputParser.Parse("");

        parsed.TextContent.Should().Be("");
        parsed.StructuredOutput.Should().BeNull();
    }

    [Fact]
    public void Null_RetornaSemStructuredOutput()
    {
        var parsed = ExecutionOutputParser.Parse(null!);

        parsed.StructuredOutput.Should().BeNull();
    }

    [Fact]
    public void JsonInvalido_RetornaSemStructuredOutput()
    {
        var broken = """{"ticker": "PETR4", qty: 100}""";
        var parsed = ExecutionOutputParser.Parse(broken);

        parsed.TextContent.Should().Be(broken);
        parsed.StructuredOutput.Should().BeNull();
    }

    // ── JSON primitivo ──────────────────────────────────────────────────────

    [Fact]
    public void JsonPrimitivo_String_RetornaSemStructuredOutput()
    {
        var json = "\"apenas uma string\"";
        var parsed = ExecutionOutputParser.Parse(json);

        parsed.TextContent.Should().Be(json);
        parsed.StructuredOutput.Should().BeNull();
    }

    [Fact]
    public void JsonPrimitivo_Number_RetornaSemStructuredOutput()
    {
        var json = "42";
        var parsed = ExecutionOutputParser.Parse(json);

        parsed.TextContent.Should().Be(json);
        parsed.StructuredOutput.Should().BeNull();
    }
}
