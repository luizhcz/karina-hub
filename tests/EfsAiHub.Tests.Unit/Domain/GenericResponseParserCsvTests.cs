using EfsAiHub.Platform.Runtime.Tools.Generic;

namespace EfsAiHub.Tests.Unit.Domain;

[Trait("Category", "Unit")]
public class GenericResponseParserCsvTests
{
    private const string ItemsSchema = """
        {
          "type": "array",
          "items": {
            "type": "object",
            "properties": {
              "id": {"type": "string"},
              "qty": {"type": "integer"},
              "price": {"type": "number"},
              "active": {"type": "boolean"},
              "tags": {"type": "array"}
            },
            "required": ["id"],
            "additionalProperties": false
          }
        }
        """;

    [Fact]
    public void ParseCsv_SemSchema_RetornaStringValues()
    {
        var csv = "id,qty\nABC,100\nDEF,200\n";

        var rows = GenericResponseParser.ParseCsv(csv);

        rows.Should().HaveCount(2);
        rows[0]["id"].Should().Be("ABC");
        rows[0]["qty"].Should().Be("100"); // ainda string sem coerção
    }

    [Fact]
    public void CoerceCsvRows_NumberETypeInteger_CoerceParaTipoCerto()
    {
        var raw = GenericResponseParser.ParseCsv("id,qty,price\nABC,100,12.5\n");

        var coerced = GenericResponseParser.CoerceCsvRows(raw, ItemsSchema);

        coerced.Should().HaveCount(1);
        coerced[0]["id"].Should().Be("ABC");
        coerced[0]["qty"].Should().Be(100L);
        coerced[0]["price"].Should().Be(12.5m);
    }

    [Fact]
    public void CoerceCsvRows_BooleanVariations_CoerceParaBool()
    {
        var raw = GenericResponseParser.ParseCsv("id,active\nA,true\nB,1\nC,yes\nD,false\nE,no\nF,0\n");

        var coerced = GenericResponseParser.CoerceCsvRows(raw, ItemsSchema);

        coerced[0]["active"].Should().Be(true);
        coerced[1]["active"].Should().Be(true);
        coerced[2]["active"].Should().Be(true);
        coerced[3]["active"].Should().Be(false);
        coerced[4]["active"].Should().Be(false);
        coerced[5]["active"].Should().Be(false);
    }

    [Fact]
    public void CoerceCsvRows_NumberMalformado_RetornaNull()
    {
        var raw = GenericResponseParser.ParseCsv("id,qty\nA,not-a-number\n");

        var coerced = GenericResponseParser.CoerceCsvRows(raw, ItemsSchema);

        coerced[0]["qty"].Should().BeNull();
    }

    [Fact]
    public void CoerceCsvRows_EmptyCell_ViraNull()
    {
        var raw = GenericResponseParser.ParseCsv("id,qty\nA,\n");

        var coerced = GenericResponseParser.CoerceCsvRows(raw, ItemsSchema);

        coerced[0]["qty"].Should().BeNull();
    }

    [Fact]
    public void CoerceCsvRows_ArrayInlineJson_ParseaParaJsonNode()
    {
        // CSV banking típico: cell com JSON serializado pra array/object.
        var raw = GenericResponseParser.ParseCsv("id,tags\nA,\"[\"\"x\"\",\"\"y\"\"]\"\n");

        var coerced = GenericResponseParser.CoerceCsvRows(raw, ItemsSchema);

        coerced[0]["tags"].Should().NotBeNull();
        coerced[0]["tags"]!.ToString().Should().Contain("x");
    }

    [Fact]
    public void CoerceCsvRows_ColunaSemTipoNoSchema_FicaComoString()
    {
        var raw = GenericResponseParser.ParseCsv("id,extra\nA,xyz\n");

        var coerced = GenericResponseParser.CoerceCsvRows(raw, ItemsSchema);

        // 'extra' não está em items.properties → mantém string crua.
        coerced[0]["extra"].Should().Be("xyz");
    }

    [Fact]
    public void CoerceCsvRows_SchemaMalformado_RetornaSemCoercao()
    {
        var raw = GenericResponseParser.ParseCsv("id,qty\nA,100\n");

        var coerced = GenericResponseParser.CoerceCsvRows(raw, "{ broken");

        // Schema unparseable → no type map → todos viram string raw (Object).
        coerced[0]["qty"].Should().Be("100");
    }

    [Fact]
    public void CoerceCsvRows_LocaleBR_NaoCoerceNumberSem_Invariant()
    {
        // "1,234.56" não é parseável em invariant culture (vírgula = separador
        // de fields no CSV; valor real em PT-BR seria escrito como "1234,56"
        // ou "1.234,56"). Documenta o comportamento — locale fica como
        // problema do upstream emitir CSV invariant.
        var raw = GenericResponseParser.ParseCsv("id,price\nA,\"1234,56\"\n");

        var coerced = GenericResponseParser.CoerceCsvRows(raw, ItemsSchema);

        coerced[0]["price"].Should().BeNull();
    }
}
