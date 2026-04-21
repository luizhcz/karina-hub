namespace EfsAiHub.Tests.Unit.Hitl;

[Trait("Category", "Unit")]
public class HitlResolutionClassifierTests
{
    [Theory]
    [InlineData("Confirmar")]
    [InlineData("confirmar")]
    [InlineData("approved")]
    [InlineData("sim")]
    [InlineData("yes")]
    [InlineData("ok")]
    [InlineData("qualquer texto livre")]
    public void IsApproved_ApprovalTerms_ReturnsTrue(string content)
    {
        HitlResolutionClassifier.IsApproved(content).Should().BeTrue();
    }

    [Theory]
    [InlineData("rejected")]
    [InlineData("Rejected")]
    [InlineData("REJECTED")]
    [InlineData("cancelar")]
    [InlineData("Cancelar")]
    [InlineData("CANCELAR")]
    [InlineData("cancelled")]
    [InlineData("cancel")]
    [InlineData("Cancel")]
    [InlineData("rejeitar")]
    [InlineData("rejeitado")]
    [InlineData("cancelado")]
    [InlineData("não")]
    [InlineData("nao")]
    [InlineData("no")]
    [InlineData("No")]
    [InlineData("timeout")]
    [InlineData("expired")]
    public void IsApproved_RejectionTerms_ReturnsFalse(string content)
    {
        HitlResolutionClassifier.IsApproved(content).Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsApproved_NullOrEmpty_ReturnsFalse(string? content)
    {
        HitlResolutionClassifier.IsApproved(content).Should().BeFalse();
    }

    [Fact]
    public void IsApproved_JsonApproved_ReturnsTrue()
    {
        var json = """{"approved": true, "reason": "Confirmar"}""";
        HitlResolutionClassifier.IsApproved(json).Should().BeTrue();
    }

    [Fact]
    public void IsApproved_JsonRejected_ReturnsFalse()
    {
        var json = """{"approved": false, "reason": "Cancelar"}""";
        HitlResolutionClassifier.IsApproved(json).Should().BeFalse();
    }

    [Fact]
    public void IsApproved_MalformedJson_FallsBackToStringMatching()
    {
        // Starts with '{' but is not valid JSON — should fallback to string matching
        // Since "{broken" is not in the rejection set, it should be treated as approval
        HitlResolutionClassifier.IsApproved("{broken").Should().BeTrue();
    }

    [Fact]
    public void IsRejected_IsMirrorOfIsApproved()
    {
        HitlResolutionClassifier.IsRejected("Cancelar").Should().BeTrue();
        HitlResolutionClassifier.IsRejected("Confirmar").Should().BeFalse();
    }
}
