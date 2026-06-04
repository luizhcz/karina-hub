using System.Text.Json;
using System.Text.Json.Nodes;
using EfsAiHub.Core.Agents.Services;

namespace EfsAiHub.Tests.Unit.Services;

/// <summary>
/// Cobre o gerador de exemplo a partir de JSON Schema usado pelo
/// <see cref="PromptRenderer"/>. Garante que strings viram placeholders
/// (<c>"&lt;string&gt;"</c>), enums viram alternativas (<c>"&lt;v1 | v2&gt;"</c>),
/// arrays viram <c>[&lt;exemplo&gt;]</c>, objects recursam — semântica que
/// reduz o risco do LLM emitir a forma literal do schema em vez da instância.
/// </summary>
[Trait("Category", "Unit")]
public class JsonSchemaExampleGeneratorTests
{
    private static JsonNode? GenerateFromJson(string schemaJson)
    {
        using var doc = JsonDocument.Parse(schemaJson);
        return JsonSchemaExampleGenerator.Generate(doc.RootElement);
    }

    [Fact]
    public void Generate_StringSemEnum_RetornaPlaceholder()
    {
        var result = GenerateFromJson("""{"type":"string"}""");
        result!.GetValue<string>().Should().Be("<string>");
    }

    [Fact]
    public void Generate_StringComEnumPequeno_RetornaListaPipeSeparated()
    {
        var result = GenerateFromJson("""{"type":"string","enum":["B","S"]}""");
        result!.GetValue<string>().Should().Be("<B | S>");
    }

    [Fact]
    public void Generate_StringComEnumUnico_RetornaValorPuro()
    {
        var result = GenerateFromJson("""{"type":"string","enum":["text"]}""");
        result!.GetValue<string>().Should().Be("text");
    }

    [Fact]
    public void Generate_EnumGrande_TruncaCom5ElementosMaisReticencias()
    {
        var result = GenerateFromJson(
            """{"type":"string","enum":["a","b","c","d","e","f","g"]}""");
        result!.GetValue<string>().Should().Be("<a | b | c | d | e | ...>");
    }

    [Fact]
    public void Generate_Number_RetornaZero()
    {
        var result = GenerateFromJson("""{"type":"number"}""");
        result!.GetValue<double>().Should().Be(0);
    }

    [Fact]
    public void Generate_Integer_RetornaZero()
    {
        var result = GenerateFromJson("""{"type":"integer"}""");
        result!.GetValue<int>().Should().Be(0);
    }

    [Fact]
    public void Generate_Boolean_RetornaFalse()
    {
        var result = GenerateFromJson("""{"type":"boolean"}""");
        result!.GetValue<bool>().Should().BeFalse();
    }

    [Fact]
    public void Generate_TypeArrayComStringENull_RetornaPlaceholderDeString()
    {
        // OpenAI strict mode aceita arrays de tipo tipo ["string","null"].
        // Pegamos o primeiro non-null como exemplo principal.
        var result = GenerateFromJson("""{"type":["string","null"]}""");
        result!.GetValue<string>().Should().Be("<string>");
    }

    [Fact]
    public void Generate_TypeArrayInvertido_NumberPrimeiro()
    {
        var result = GenerateFromJson("""{"type":["number","null"]}""");
        result!.GetValue<double>().Should().Be(0);
    }

    [Fact]
    public void Generate_Array_RetornaListaCom1Exemplo()
    {
        var result = GenerateFromJson(
            """{"type":"array","items":{"type":"string"}}""");
        var arr = result.Should().BeOfType<JsonArray>().Subject;
        arr.Should().HaveCount(1);
        arr[0]!.GetValue<string>().Should().Be("<string>");
    }

    [Fact]
    public void Generate_ArrayDeObjects_RecurseNosItems()
    {
        // Caso clássico do boleta: array de objects com properties.
        var schema = """
        {
          "type": "array",
          "items": {
            "type": "object",
            "properties": {
              "Symbol": {"type":"string"},
              "Side": {"type":"string","enum":["B","S"]},
              "Quantity": {"type":["number","null"]}
            }
          }
        }
        """;
        var result = GenerateFromJson(schema);
        var arr = result.Should().BeOfType<JsonArray>().Subject;
        arr.Should().HaveCount(1);

        var obj = arr[0]!.AsObject();
        obj["Symbol"]!.GetValue<string>().Should().Be("<string>");
        obj["Side"]!.GetValue<string>().Should().Be("<B | S>");
        obj["Quantity"]!.GetValue<double>().Should().Be(0);
    }

    [Fact]
    public void Generate_Object_RetornaObjectComCadaProperty()
    {
        var schema = """
        {
          "type": "object",
          "properties": {
            "foo": {"type":"string"},
            "bar": {"type":"number"}
          }
        }
        """;
        var result = GenerateFromJson(schema);
        var obj = result.Should().BeOfType<JsonObject>().Subject;
        obj["foo"]!.GetValue<string>().Should().Be("<string>");
        obj["bar"]!.GetValue<double>().Should().Be(0);
    }

    [Fact]
    public void Generate_SchemaSemType_InferePorShape()
    {
        // properties → object inferido
        var asObject = GenerateFromJson("""{"properties":{"x":{"type":"string"}}}""");
        asObject.Should().BeOfType<JsonObject>();

        // items → array inferido
        var asArray = GenerateFromJson("""{"items":{"type":"number"}}""");
        asArray.Should().BeOfType<JsonArray>();

        // Sem properties nem items → string fallback
        var asString = GenerateFromJson("""{}""");
        asString!.GetValue<string>().Should().Be("<string>");
    }

    [Fact]
    public void Generate_SchemaNull_RetornaNull()
    {
        JsonSchemaExampleGenerator.Generate((JsonDocument?)null).Should().BeNull();
        JsonSchemaExampleGenerator.Generate((JsonElement?)null).Should().BeNull();
    }
}
