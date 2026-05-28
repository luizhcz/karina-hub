using System.Text.Json;
using EfsAiHub.Core.Abstractions.Exceptions;
using EfsAiHub.Platform.Runtime.Tools.Generic.Schema;

namespace EfsAiHub.Tests.Unit.Tools.Generic.Schema;

[Trait("Category", "Unit")]
public class SchemaNormalizerTests
{
    private readonly SchemaNormalizer _normalizer = new();

    private CanonicalSchema Normalize(string raw, SchemaRole role = SchemaRole.Output)
        => _normalizer.Normalize(raw, role);

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    // ──────────────────────────────────────────────────────────────────────
    // Strictness — additionalProperties:false + required:[all keys]
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void Normalize_ObjectSchema_ForcaAdditionalPropertiesFalse()
    {
        var raw = """
        {
          "type": "object",
          "properties": {
            "name": {"type": "string"}
          },
          "additionalProperties": true
        }
        """;

        var canon = Parse(Normalize(raw).CanonicalJson);

        canon.GetProperty("additionalProperties").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public void Normalize_ObjectSchema_RequiredVemPreenchidoComTodasAsProperties()
    {
        var raw = """
        {
          "type": "object",
          "properties": {
            "a": {"type": "string"},
            "b": {"type": "number"}
          },
          "required": ["a"]
        }
        """;

        var canon = Parse(Normalize(raw).CanonicalJson);
        var required = canon.GetProperty("required").EnumerateArray().Select(e => e.GetString()).ToArray();

        required.Should().BeEquivalentTo(new[] { "a", "b" });
    }

    [Fact]
    public void Normalize_NestedObject_AplicaStrictnessEmTodosOsNiveis()
    {
        var raw = """
        {
          "type": "object",
          "properties": {
            "user": {
              "type": "object",
              "properties": {
                "name": {"type": "string"}
              }
            }
          }
        }
        """;

        var canon = Parse(Normalize(raw).CanonicalJson);

        canon.GetProperty("additionalProperties").GetBoolean().Should().BeFalse();
        var user = canon.GetProperty("properties").GetProperty("user");
        user.GetProperty("additionalProperties").GetBoolean().Should().BeFalse();
        user.GetProperty("required")
            .EnumerateArray()
            .Select(e => e.GetString())
            .Should().BeEquivalentTo(new[] { "name" });
    }

    [Fact]
    public void Normalize_ArraySemItems_GanhaDefaultStringComWarning()
    {
        var raw = """{"type": "array"}""";

        var result = Normalize(raw);
        var canon = Parse(result.CanonicalJson);

        canon.GetProperty("items").GetProperty("type").GetString().Should().Be("string");
        result.Warnings.Should().Contain(w => w.Code == "array.items_default");
    }

    // ──────────────────────────────────────────────────────────────────────
    // $ref inlining
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void Normalize_Draft7ComDefinitionsERef_FazInlineEDroppaDefinitions()
    {
        var raw = """
        {
          "$schema": "http://json-schema.org/draft-07/schema#",
          "type": "object",
          "properties": {
            "address": {"$ref": "#/definitions/Address"}
          },
          "definitions": {
            "Address": {
              "type": "object",
              "properties": {
                "city": {"type": "string"}
              }
            }
          }
        }
        """;

        var result = Normalize(raw);
        var canon = Parse(result.CanonicalJson);

        canon.TryGetProperty("definitions", out _).Should().BeFalse();
        canon.TryGetProperty("$schema", out _).Should().BeFalse();
        canon.GetProperty("properties").GetProperty("address")
             .GetProperty("properties").GetProperty("city").GetProperty("type").GetString()
             .Should().Be("string");
        result.SourceDialect.Should().Be("draft-07");
    }

    [Fact]
    public void Normalize_2020_12ComDefsRecursivo_QuebraCicloComWarning()
    {
        var raw = """
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "type": "object",
          "properties": {
            "node": {"$ref": "#/$defs/TreeNode"}
          },
          "$defs": {
            "TreeNode": {
              "type": "object",
              "properties": {
                "child": {"$ref": "#/$defs/TreeNode"}
              }
            }
          }
        }
        """;

        var result = Normalize(raw);

        result.Warnings.Should().Contain(w => w.Code == "ref.cycle");
        Action act = () => Parse(result.CanonicalJson);
        act.Should().NotThrow(); // canônico continua parseável
    }

    [Fact]
    public void Normalize_RefExterna_ViraEmptyComWarning()
    {
        var raw = """
        {
          "type": "object",
          "properties": {
            "x": {"$ref": "https://outro.host/schema.json"}
          }
        }
        """;

        var result = Normalize(raw);
        result.Warnings.Should().Contain(w => w.Code == "ref.external");
    }

    [Fact]
    public void Normalize_RefNaoResolvido_ViraEmptyComWarning()
    {
        var raw = """
        {
          "type": "object",
          "properties": {
            "x": {"$ref": "#/definitions/NotThere"}
          }
        }
        """;

        var result = Normalize(raw);
        result.Warnings.Should().Contain(w => w.Code == "ref.unresolved");
    }

    // ──────────────────────────────────────────────────────────────────────
    // oneOf/anyOf merge em superset
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void Normalize_OneOfComBranches_FazUnionDePropertiesERequiredIntersection()
    {
        var raw = """
        {
          "oneOf": [
            {
              "type": "object",
              "properties": {"a": {"type": "string"}, "b": {"type": "string"}},
              "required": ["a", "b"]
            },
            {
              "type": "object",
              "properties": {"a": {"type": "string"}, "c": {"type": "string"}},
              "required": ["a", "c"]
            }
          ]
        }
        """;

        var result = Normalize(raw);
        var canon = Parse(result.CanonicalJson);

        // Properties: union {a, b, c}
        var props = canon.GetProperty("properties").EnumerateObject().Select(p => p.Name).ToHashSet();
        props.Should().BeEquivalentTo(new[] { "a", "b", "c" });

        // O canônico após StrictnessEnforcer força required = todas as properties.
        // Mas a intersection só importa pra LEITURA semântica do canônico — o que
        // garantimos no warning. Aqui só asseguramos que o merge aconteceu sem
        // sobrar oneOf no schema final.
        canon.TryGetProperty("oneOf", out _).Should().BeFalse();
        result.Warnings.Should().Contain(w => w.Code == "oneof.merged");
    }

    [Fact]
    public void Normalize_AnyOfComBranches_TambemMesclaEmSuperset()
    {
        var raw = """
        {
          "anyOf": [
            {"type": "object", "properties": {"x": {"type": "string"}}},
            {"type": "object", "properties": {"y": {"type": "number"}}}
          ]
        }
        """;

        var result = Normalize(raw);
        var canon = Parse(result.CanonicalJson);

        canon.TryGetProperty("anyOf", out _).Should().BeFalse();
        var props = canon.GetProperty("properties").EnumerateObject().Select(p => p.Name).ToHashSet();
        props.Should().BeEquivalentTo(new[] { "x", "y" });
        result.Warnings.Should().Contain(w => w.Code == "anyof.merged");
    }

    [Fact]
    public void Normalize_Not_RemovidoComWarning()
    {
        var raw = """
        {
          "type": "object",
          "properties": {
            "name": {"type": "string"}
          },
          "not": {"type": "string"}
        }
        """;

        var result = Normalize(raw);
        var canon = Parse(result.CanonicalJson);

        canon.TryGetProperty("not", out _).Should().BeFalse();
        result.Warnings.Should().Contain(w => w.Code == "not.dropped");
    }

    // ──────────────────────────────────────────────────────────────────────
    // allOf merge
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void Normalize_AllOf_MesclaPropertiesERequired()
    {
        var raw = """
        {
          "allOf": [
            {
              "type": "object",
              "properties": {"a": {"type": "string"}},
              "required": ["a"]
            },
            {
              "type": "object",
              "properties": {"b": {"type": "number"}},
              "required": ["b"]
            }
          ]
        }
        """;

        var canon = Parse(Normalize(raw).CanonicalJson);

        canon.TryGetProperty("allOf", out _).Should().BeFalse();
        var props = canon.GetProperty("properties").EnumerateObject().Select(p => p.Name).ToHashSet();
        props.Should().BeEquivalentTo(new[] { "a", "b" });
    }

    // ──────────────────────────────────────────────────────────────────────
    // Keyword sanitization
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void Normalize_PatternComLookahead_RemovidoComWarning()
    {
        var raw = """
        {
          "type": "object",
          "properties": {
            "pwd": {"type": "string", "pattern": "(?=.*[A-Z]).*"}
          }
        }
        """;

        var result = Normalize(raw);
        var canon = Parse(result.CanonicalJson);

        canon.GetProperty("properties").GetProperty("pwd")
             .TryGetProperty("pattern", out _).Should().BeFalse();
        result.Warnings.Should().Contain(w => w.Code == "pattern.unsupported_regex");
    }

    [Fact]
    public void Normalize_PatternSimples_Mantido()
    {
        var raw = """
        {
          "type": "object",
          "properties": {
            "code": {"type": "string", "pattern": "^[A-Z0-9]+$"}
          }
        }
        """;

        var canon = Parse(Normalize(raw).CanonicalJson);
        canon.GetProperty("properties").GetProperty("code")
             .GetProperty("pattern").GetString().Should().Be("^[A-Z0-9]+$");
    }

    [Fact]
    public void Normalize_FormatForaDaAllowlist_Removido()
    {
        var raw = """
        {
          "type": "object",
          "properties": {
            "color": {"type": "string", "format": "color-hex"}
          }
        }
        """;

        var result = Normalize(raw);
        var canon = Parse(result.CanonicalJson);

        canon.GetProperty("properties").GetProperty("color")
             .TryGetProperty("format", out _).Should().BeFalse();
        result.Warnings.Should().Contain(w => w.Code == "format.dropped");
    }

    [Fact]
    public void Normalize_FormatDaAllowlist_Mantido()
    {
        var raw = """
        {
          "type": "object",
          "properties": {
            "email": {"type": "string", "format": "email"}
          }
        }
        """;

        var canon = Parse(Normalize(raw).CanonicalJson);
        canon.GetProperty("properties").GetProperty("email")
             .GetProperty("format").GetString().Should().Be("email");
    }

    [Fact]
    public void Normalize_UnevaluatedProperties_RemovidoComWarning()
    {
        var raw = """
        {
          "type": "object",
          "properties": {"x": {"type": "string"}},
          "unevaluatedProperties": false
        }
        """;

        var result = Normalize(raw);
        var canon = Parse(result.CanonicalJson);

        canon.TryGetProperty("unevaluatedProperties", out _).Should().BeFalse();
        result.Warnings.Should().Contain(w =>
            w.Code == "keyword.dropped" && w.Message.Contains("unevaluatedProperties"));
    }

    // ──────────────────────────────────────────────────────────────────────
    // Type inference / nullability
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void Normalize_SemType_InfereObjectQuandoTemProperties()
    {
        var raw = """{"properties": {"x": {"type": "string"}}}""";

        var canon = Parse(Normalize(raw).CanonicalJson);
        canon.GetProperty("type").GetString().Should().Be("object");
    }

    [Fact]
    public void Normalize_TypeArrayComStringENull_Preservado()
    {
        var raw = """
        {
          "type": "object",
          "properties": {
            "n": {"type": ["string", "null"]}
          }
        }
        """;

        var canon = Parse(Normalize(raw).CanonicalJson);
        var typeArr = canon.GetProperty("properties").GetProperty("n").GetProperty("type")
            .EnumerateArray().Select(e => e.GetString()).ToArray();
        typeArr.Should().BeEquivalentTo(new[] { "string", "null" });
    }

    [Fact]
    public void Normalize_TypeArrayComMultiplosNaoNull_ColapsaProPrimeiro()
    {
        var raw = """
        {
          "type": "object",
          "properties": {
            "n": {"type": ["string", "number"]}
          }
        }
        """;

        var result = Normalize(raw);
        var canon = Parse(result.CanonicalJson);

        canon.GetProperty("properties").GetProperty("n").GetProperty("type").GetString()
             .Should().Be("string");
        result.Warnings.Should().Contain(w => w.Code == "type.union_collapsed");
    }

    // ──────────────────────────────────────────────────────────────────────
    // Edge cases
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void Normalize_SchemaCompletamenteVazio_EmiteWarning()
    {
        var raw = "{}";
        var result = Normalize(raw);

        result.Warnings.Should().Contain(w => w.Code == "schema.empty");
        Parse(result.CanonicalJson).GetProperty("type").GetString().Should().Be("object");
    }

    [Fact]
    public void Normalize_JsonInvalido_LancaDomainException()
    {
        Action act = () => Normalize("{ broken ");
        act.Should().Throw<DomainException>().WithMessage("*não é JSON válido*");
    }

    [Fact]
    public void Normalize_NaoObjetoNaRaiz_LancaDomainException()
    {
        Action act = () => Normalize("[1,2,3]");
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Normalize_EhIdempotente()
    {
        // Normalize(Normalize(x)) == Normalize(x) — propriedade fundamental.
        // Sem isso, backfill repetido produziria deriva.
        var raw = """
        {
          "type": "object",
          "properties": {
            "user": {
              "type": "object",
              "properties": {
                "name": {"type": "string"},
                "age": {"type": "integer"}
              }
            },
            "tags": {"type": "array", "items": {"type": "string"}}
          }
        }
        """;

        var first = Normalize(raw);
        var second = Normalize(first.CanonicalJson);

        second.CanonicalJson.Should().Be(first.CanonicalJson);
        second.CanonicalHash.Should().Be(first.CanonicalHash);
    }

    [Fact]
    public void Normalize_HashEDeterministico()
    {
        var raw = """{"type":"object","properties":{"x":{"type":"string"}}}""";

        var a = Normalize(raw);
        var b = Normalize(raw);

        a.CanonicalHash.Should().Be(b.CanonicalHash);
        a.CanonicalJson.Should().Be(b.CanonicalJson);
    }

    [Fact]
    public void Normalize_DialectDetectadoPorHeuristica_Quando_SchemaAusente()
    {
        var raw = """
        {
          "type": "object",
          "properties": {
            "x": {"$ref": "#/$defs/Foo"}
          },
          "$defs": {
            "Foo": {"type": "string"}
          }
        }
        """;

        var result = Normalize(raw);
        result.SourceDialect.Should().Be("2020-12");
    }
}
