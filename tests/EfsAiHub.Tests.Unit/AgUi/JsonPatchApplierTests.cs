using System.Text.Json;
using EfsAiHub.Host.Api.Chat.AgUi.State;

namespace EfsAiHub.Tests.Unit.AgUi;

[Trait("Category", "Unit")]
public class JsonPatchApplierTests
{
    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    // ── replace ────────────────────────────────────────────────────────────────

    [Fact]
    public void Apply_Replace_AtualizaValor()
    {
        var state = Parse("""{"etapa":"inicio","progresso":0}""");
        var patch = Parse("""[{"op":"replace","path":"/progresso","value":50}]""");

        var result = JsonPatchApplier.Apply(state, patch);

        result.GetProperty("progresso").GetInt32().Should().Be(50);
        result.GetProperty("etapa").GetString().Should().Be("inicio"); // outros campos preservados
    }

    [Fact]
    public void Apply_Replace_ValorAninhado_AtualizaValor()
    {
        var state = Parse("""{"info":{"nome":"teste","status":"ok"}}""");
        var patch = Parse("""[{"op":"replace","path":"/info/status","value":"done"}]""");

        var result = JsonPatchApplier.Apply(state, patch);

        result.GetProperty("info").GetProperty("status").GetString().Should().Be("done");
        result.GetProperty("info").GetProperty("nome").GetString().Should().Be("teste");
    }

    // ── add ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Apply_Add_InsereNovoCampo()
    {
        var state = Parse("""{"etapa":"inicio"}""");
        var patch = Parse("""[{"op":"add","path":"/novo","value":"campo"}""" + "]");

        var result = JsonPatchApplier.Apply(state, patch);

        result.GetProperty("novo").GetString().Should().Be("campo");
        result.GetProperty("etapa").GetString().Should().Be("inicio");
    }

    // ── remove ────────────────────────────────────────────────────────────────

    [Fact]
    public void Apply_Remove_RemoveCampo()
    {
        var state = Parse("""{"etapa":"inicio","temp":"removivel"}""");
        var patch = Parse("""[{"op":"remove","path":"/temp"}]""");

        var result = JsonPatchApplier.Apply(state, patch);

        result.TryGetProperty("temp", out _).Should().BeFalse();
        result.GetProperty("etapa").GetString().Should().Be("inicio");
    }

    // ── múltiplas operações ────────────────────────────────────────────────────

    [Fact]
    public void Apply_MultiplasOperacoes_TodasAplicadas()
    {
        var state = Parse("""{"a":1,"b":2,"c":3}""");
        var patch = Parse("""[{"op":"replace","path":"/a","value":10},{"op":"remove","path":"/b"},{"op":"add","path":"/d","value":4}]""");

        var result = JsonPatchApplier.Apply(state, patch);

        result.GetProperty("a").GetInt32().Should().Be(10);
        result.TryGetProperty("b", out _).Should().BeFalse();
        result.GetProperty("c").GetInt32().Should().Be(3);
        result.GetProperty("d").GetInt32().Should().Be(4);
    }

    // ── GenerateDiff ──────────────────────────────────────────────────────────

    [Fact]
    public void GenerateDiff_ValorAlterado_RetornaPatchReplace()
    {
        var old = Parse("""{"progresso":0}""");
        var updated = Parse("""{"progresso":100}""");

        var patch = JsonPatchApplier.GenerateDiff(old, updated);

        patch.GetArrayLength().Should().BeGreaterThan(0);
        var op = patch[0];
        op.GetProperty("op").GetString().Should().Be("replace");
        op.GetProperty("path").GetString().Should().Be("/progresso");
    }

    [Fact]
    public void GenerateDiff_SemAlteracao_RetornaPatchVazio()
    {
        var state = Parse("""{"etapa":"inicio"}""");

        var patch = JsonPatchApplier.GenerateDiff(state, state);

        patch.GetArrayLength().Should().Be(0);
    }
}
