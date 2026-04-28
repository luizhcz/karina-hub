using EfsAiHub.Core.Abstractions.Secrets;

namespace EfsAiHub.Tests.Unit.Secrets;

[Trait("Category", "Unit")]
public class SecretReferenceTests
{
    [Fact]
    public void Parse_NullOrEmpty_ReturnsEmpty()
    {
        SecretReference.Parse(null).Should().BeOfType<EmptySecretReference>();
        SecretReference.Parse("").Should().BeOfType<EmptySecretReference>();
        SecretReference.Parse("   ").Should().BeOfType<EmptySecretReference>();
    }

    [Fact]
    public void Parse_AwsPrefix_ReturnsAwsReference()
    {
        var result = SecretReference.Parse("secret://aws/efs-ai-hub-openai-default");

        result.Should().BeOfType<AwsSecretReference>();
        ((AwsSecretReference)result).Identifier.Should().Be("efs-ai-hub-openai-default");
    }

    [Fact]
    public void Parse_AwsPrefixCaseInsensitive()
    {
        var result = SecretReference.Parse("SECRET://AWS/my-key");

        result.Should().BeOfType<AwsSecretReference>();
        ((AwsSecretReference)result).Identifier.Should().Be("my-key");
    }

    [Fact]
    public void Parse_AwsPrefixWithEmptyId_ReturnsEmpty()
    {
        SecretReference.Parse("secret://aws/").Should().BeOfType<EmptySecretReference>();
        SecretReference.Parse("secret://aws/   ").Should().BeOfType<EmptySecretReference>();
    }

    [Fact]
    public void Parse_PlainValue_ReturnsLiteral()
    {
        var result = SecretReference.Parse("sk-proj-abc123");

        result.Should().BeOfType<LiteralSecretReference>();
        ((LiteralSecretReference)result).Value.Should().Be("sk-proj-abc123");
    }

    [Fact]
    public void Parse_LiteralIsTrimmed()
    {
        var result = SecretReference.Parse("  sk-key  ");

        result.Should().BeOfType<LiteralSecretReference>();
        ((LiteralSecretReference)result).Value.Should().Be("sk-key");
    }

    [Fact]
    public void IsAwsReference_DetectsAwsPrefix()
    {
        SecretReference.IsAwsReference("secret://aws/foo").Should().BeTrue();
        SecretReference.IsAwsReference("SECRET://AWS/foo").Should().BeTrue();
        SecretReference.IsAwsReference("sk-foo").Should().BeFalse();
        SecretReference.IsAwsReference(null).Should().BeFalse();
        SecretReference.IsAwsReference("").Should().BeFalse();
    }

    [Fact]
    public void AwsReference_ToString_RestoresOriginalFormat()
    {
        var aws = new AwsSecretReference("efs-ai-hub-postgres");

        aws.ToString().Should().Be("secret://aws/efs-ai-hub-postgres");
    }
}
