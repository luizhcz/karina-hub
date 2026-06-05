using System.Text.Json;
using EfsAiHub.Platform.Runtime.Tools.Generic;

namespace EfsAiHub.Tests.Unit.Domain;

[Trait("Category", "Unit")]
public class QueryStringFlattenerTests
{
    [Fact]
    public void Flatten_Primitives_RetornaParesSimples()
    {
        var args = new Dictionary<string, object?>
        {
            ["name"] = "John",
            ["age"] = 30,
            ["active"] = true,
        };

        var pairs = QueryStringFlattener.Flatten(args);

        pairs.Should().Contain(kv => kv.Key == "name" && kv.Value == "John");
        pairs.Should().Contain(kv => kv.Key == "age" && kv.Value == "30");
        pairs.Should().Contain(kv => kv.Key == "active" && kv.Value == "true");
    }

    [Fact]
    public void Flatten_Null_PulaCampo()
    {
        // LLM emite null pra opcionais; flattener não emite ?x=null (lixo).
        var args = new Dictionary<string, object?>
        {
            ["ticker"] = "PETR4",
            ["limit"] = null,
        };

        var pairs = QueryStringFlattener.Flatten(args);

        pairs.Should().HaveCount(1);
        pairs[0].Key.Should().Be("ticker");
    }

    [Fact]
    public void Flatten_NestedObject_SerializaComoJsonInline()
    {
        var obj = JsonDocument.Parse("""{"city":"Sao Paulo","zip":"01310"}""").RootElement;
        var args = new Dictionary<string, object?>
        {
            ["address"] = obj,
        };

        var pairs = QueryStringFlattener.Flatten(args);

        pairs.Should().HaveCount(1);
        pairs[0].Key.Should().Be("address");
        pairs[0].Value.Should().Contain("Sao Paulo").And.Contain("01310");
    }

    [Fact]
    public void Flatten_Array_SerializaComoJsonInline()
    {
        var arr = JsonDocument.Parse("[\"PETR4\",\"VALE3\"]").RootElement;
        var args = new Dictionary<string, object?>
        {
            ["ids"] = arr,
        };

        var pairs = QueryStringFlattener.Flatten(args);

        pairs[0].Key.Should().Be("ids");
        pairs[0].Value.Should().Be("[\"PETR4\",\"VALE3\"]");
    }

    [Fact]
    public void Build_PrefereParesEscapados()
    {
        var pairs = new List<KeyValuePair<string, string>>
        {
            new("q", "hello world"),
            new("emoji", "🚀"),
        };

        var qs = QueryStringFlattener.Build(pairs);

        qs.Should().Contain("q=hello%20world");
        qs.Should().Contain("emoji=%F0%9F%9A%80");
        qs.Should().Contain("&");
    }

    [Fact]
    public void Build_VazioRetornaStringVazia()
    {
        var qs = QueryStringFlattener.Build(Array.Empty<KeyValuePair<string, string>>());
        qs.Should().BeEmpty();
    }
}
