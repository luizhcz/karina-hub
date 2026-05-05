using System.Text.Json;
using EfsAiHub.Core.Agents.GenericTools;
using EfsAiHub.Platform.Runtime.Tools.Generic;

namespace EfsAiHub.Tests.Unit.Domain;

[Trait("Category", "Unit")]
public class GenericToolSchemaBuilderTests
{
    [Fact]
    public void Build_PathParams_PromoveTodosParaRequired()
    {
        var tool = new GenericTool
        {
            Id = "t",
            ProjectId = "p",
            TenantId = "t",
            Name = "x",
            HttpMethod = HttpMethodType.GET,
            UrlTemplate = "https://api/aihub/{tenant}/users/{id}",
            PathParams = new Dictionary<string, ParamDefinition>
            {
                ["tenant"] = new("string", "tenant slug", true),
                ["id"] = new("integer", "user id", true),
            },
            OutputContentType = OutputContentType.Text,
        };

        var schema = GenericToolSchemaBuilder.Build(tool);
        var doc = JsonSerializer.SerializeToElement(schema);

        var props = doc.GetProperty("properties");
        props.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(new[] { "tenant", "id" });
        props.GetProperty("tenant").GetProperty("type").GetString().Should().Be("string");
        props.GetProperty("id").GetProperty("type").GetString().Should().Be("integer");

        var required = doc.GetProperty("required").EnumerateArray().Select(e => e.GetString()).ToList();
        required.Should().BeEquivalentTo(new[] { "tenant", "id" });
    }

    [Fact]
    public void Build_QueryRequiredFalse_NaoFicaRequired()
    {
        var tool = new GenericTool
        {
            Id = "t",
            ProjectId = "p",
            TenantId = "t",
            Name = "x",
            HttpMethod = HttpMethodType.GET,
            UrlTemplate = "https://api/aihub/search",
            QueryParams = new Dictionary<string, ParamDefinition>
            {
                ["q"] = new("string", "term", true),
                ["limit"] = new("integer", "limit", false),
            },
            OutputContentType = OutputContentType.Text,
        };

        var schema = GenericToolSchemaBuilder.Build(tool);

        var required = schema.GetProperty("required").EnumerateArray()
            .Select(e => e.GetString()).ToList();
        required.Should().Contain("q").And.NotContain("limit");
    }

    [Fact]
    public void Build_JsonBody_MergeComPropertiesDoSchemaERequired()
    {
        var tool = new GenericTool
        {
            Id = "t",
            ProjectId = "p",
            TenantId = "t",
            Name = "x",
            HttpMethod = HttpMethodType.POST,
            UrlTemplate = "https://api/aihub/post",
            InputContentType = InputContentType.Json,
            InputSchema = """
                {
                    "type": "object",
                    "properties": {
                        "name": { "type": "string", "description": "Nome" },
                        "age": { "type": "integer" }
                    },
                    "required": ["name"]
                }
                """,
            OutputContentType = OutputContentType.Json,
            OutputSchema = "{\"type\":\"object\",\"properties\":{}}",
        };

        var schema = GenericToolSchemaBuilder.Build(tool);

        var props = schema.GetProperty("properties");
        props.EnumerateObject().Select(p => p.Name).Should().Contain(new[] { "name", "age" });
        props.GetProperty("name").GetProperty("description").GetString().Should().StartWith("[Body]");

        schema.GetProperty("required").EnumerateArray().Select(e => e.GetString())
            .Should().Contain("name");
    }

    [Fact]
    public void Build_TextBody_AdicionaPropertyStringRequired()
    {
        var tool = new GenericTool
        {
            Id = "t",
            ProjectId = "p",
            TenantId = "t",
            Name = "x",
            HttpMethod = HttpMethodType.POST,
            UrlTemplate = "https://api/aihub/post",
            InputContentType = InputContentType.Text,
            InputSchema = "{\"type\":\"object\",\"properties\":{\"raw\":{\"type\":\"string\"}}}",
            OutputContentType = OutputContentType.Text,
        };

        var schema = GenericToolSchemaBuilder.Build(tool);

        schema.GetProperty("properties").EnumerateObject()
            .Select(p => p.Name).Should().Contain("raw");
        schema.GetProperty("required").EnumerateArray()
            .Select(e => e.GetString()).Should().Contain("raw");
    }

    [Fact]
    public void Build_TextBodyComSchemaNulo_FallbackParaBody()
    {
        var tool = new GenericTool
        {
            Id = "t",
            ProjectId = "p",
            TenantId = "t",
            Name = "x",
            HttpMethod = HttpMethodType.POST,
            UrlTemplate = "https://api/aihub/post",
            InputContentType = InputContentType.Text,
            InputSchema = null,
            OutputContentType = OutputContentType.Text,
        };

        var schema = GenericToolSchemaBuilder.Build(tool);

        schema.GetProperty("properties").EnumerateObject()
            .Select(p => p.Name).Should().Contain("body");
    }

    [Fact]
    public void Build_PathColidiComBody_PathPrevalece()
    {
        var tool = new GenericTool
        {
            Id = "t",
            ProjectId = "p",
            TenantId = "t",
            Name = "x",
            HttpMethod = HttpMethodType.POST,
            UrlTemplate = "https://api/aihub/users/{id}",
            PathParams = new Dictionary<string, ParamDefinition>
            {
                ["id"] = new("string", "user id", true),
            },
            InputContentType = InputContentType.Json,
            InputSchema = "{\"type\":\"object\",\"properties\":{\"id\":{\"type\":\"integer\"}}}",
            OutputContentType = OutputContentType.Json,
            OutputSchema = "{\"type\":\"object\",\"properties\":{}}",
        };

        var schema = GenericToolSchemaBuilder.Build(tool);

        schema.GetProperty("properties").GetProperty("id").GetProperty("description")
            .GetString().Should().StartWith("[Path]");
        schema.GetProperty("properties").GetProperty("id").GetProperty("type")
            .GetString().Should().Be("string");
    }
}
