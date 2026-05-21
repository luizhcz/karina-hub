using System.Text.Json;
using EfsAiHub.Core.Agents.GenericTools;
using EfsAiHub.Platform.Runtime.Tools.Generic;

namespace EfsAiHub.Tests.Unit.Domain;

[Trait("Category", "Unit")]
public class GenericResponseProjectorTests
{
    private static GenericResponseProjector BuildProjector() =>
        new(new SchemaCache());

    private static JsonElement ParseJson(string raw) =>
        JsonDocument.Parse(raw).RootElement.Clone();

    [Fact]
    public void ModeOff_BypassesAnyPayload()
    {
        var projector = BuildProjector();
        var input = ParseJson("""{"foo":"bar","extra":[1,2,3]}""");

        var result = projector.Project(input, "{}", OutputProjectionMode.Off, "test");

        result.Bypassed.Should().BeTrue();
        result.Projected.Should().Be(input);
        result.HasErrors.Should().BeFalse();
    }

    [Fact]
    public void ModeOff_NullSchema_AlsoBypasses()
    {
        var projector = BuildProjector();
        var input = ParseJson("""{"foo":"bar"}""");

        var result = projector.Project(input, null, OutputProjectionMode.Off, "test");

        result.Bypassed.Should().BeTrue();
        result.Projected.Should().Be(input);
    }

    [Fact]
    public void Project_RequiredPresent_ReturnsProjectedShape()
    {
        var projector = BuildProjector();
        const string schema = """
            {"type":"object","required":["id"],"properties":{"id":{"type":"string"}}}
            """;
        var input = ParseJson("""{"id":"abc"}""");

        var result = projector.Project(input, schema, OutputProjectionMode.Project, "test");

        result.Success.Should().BeTrue();
        result.Bypassed.Should().BeFalse();
        result.Projected.Should().NotBeNull();
    }

    [Fact]
    public void Project_RequiredMissing_ReturnsStructuredErrors()
    {
        var projector = BuildProjector();
        const string schema = """
            {"type":"object","required":["id","name"],"properties":{"id":{"type":"string"},"name":{"type":"string"}}}
            """;
        var input = ParseJson("""{"id":"abc"}""");

        var result = projector.Project(input, schema, OutputProjectionMode.Project, "test");

        result.HasErrors.Should().BeTrue();
        result.Errors.Should().NotBeEmpty();
        string.Join("\n", result.Errors).Should().Contain("required");
    }

    [Fact]
    public void Project_TypeMismatch_ReturnsErrors()
    {
        var projector = BuildProjector();
        const string schema = """
            {"type":"object","required":["count"],"properties":{"count":{"type":"integer"}}}
            """;
        var input = ParseJson("""{"count":"not-a-number"}""");

        var result = projector.Project(input, schema, OutputProjectionMode.Project, "test");

        result.HasErrors.Should().BeTrue();
    }

    [Fact]
    public void Project_ExtraProperty_SilentlyDropped()
    {
        var projector = BuildProjector();
        const string schema = """
            {"type":"object","properties":{"id":{"type":"string"}}}
            """;
        var input = ParseJson("""{"id":"abc","ghost":"removeMe","more":42}""");

        var result = projector.Project(input, schema, OutputProjectionMode.Project, "test");

        result.Success.Should().BeTrue();
        var json = JsonSerializer.Serialize(result.Projected);
        json.Should().Contain("\"id\":\"abc\"");
        json.Should().NotContain("ghost");
        json.Should().NotContain("more");
    }

    [Fact]
    public void Project_ArrayItems_Validated()
    {
        var projector = BuildProjector();
        const string schema = """
            {"type":"array","items":{"type":"object","required":["id"],"properties":{"id":{"type":"string"}}}}
            """;
        var validInput = ParseJson("""[{"id":"a"},{"id":"b","extra":"x"}]""");
        var invalidInput = ParseJson("""[{"id":"a"},{"name":"semId"}]""");

        var ok = projector.Project(validInput, schema, OutputProjectionMode.Project, "test");
        var fail = projector.Project(invalidInput, schema, OutputProjectionMode.Project, "test");

        ok.Success.Should().BeTrue();
        JsonSerializer.Serialize(ok.Projected).Should().NotContain("extra");
        fail.HasErrors.Should().BeTrue();
    }

    [Fact]
    public void SchemaCache_SameSchemaJson_ReturnsSameInstance()
    {
        var cache = new SchemaCache();
        const string schema = """
            {"type":"object","properties":{"id":{"type":"string"}}}
            """;

        var first = cache.GetOrAdd(schema);
        var second = cache.GetOrAdd(schema);

        ReferenceEquals(first, second).Should().BeTrue("cache deve devolver mesma instância pra schema idêntico");
        cache.Count.Should().Be(1);
    }
}
