using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using NSubstitute;

namespace EfsAiHub.Tests.Integration.Controllers;

[Collection("Integration")]
[Trait("Category", "Integration")]
public class SecretsControllerTests(IntegrationWebApplicationFactory factory)
{
    private static IAmazonSecretsManager BuildMock()
    {
        var aws = Substitute.For<IAmazonSecretsManager>();
        aws.DescribeSecretAsync(
                Arg.Is<DescribeSecretRequest>(r => r.SecretId == "efs-existing"),
                Arg.Any<CancellationToken>())
            .Returns(new DescribeSecretResponse
            {
                Name = "efs-existing",
                ARN = "arn:aws:secretsmanager:us-east-1:000000000000:secret:efs-existing",
                LastChangedDate = new DateTime(2026, 4, 28, 12, 0, 0, DateTimeKind.Utc),
                Tags = [new Tag { Key = "Environment", Value = "test" }]
            });
        aws.DescribeSecretAsync(
                Arg.Is<DescribeSecretRequest>(r => r.SecretId == "efs-missing"),
                Arg.Any<CancellationToken>())
            .Returns<Task<DescribeSecretResponse>>(_ => throw new ResourceNotFoundException("not found"));
        return aws;
    }

    [Fact]
    public async Task Validate_AwsRefExists_Retorna200_ComExistsTrue()
    {
        var client = factory.CreateClientWithMockedAws(BuildMock());

        var response = await client.PostAsJsonAsync("/api/secrets/validate", new
        {
            reference = "secret://aws/efs-existing"
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("exists").GetBoolean().Should().BeTrue();
        body.GetProperty("lastChanged").GetString().Should().NotBeNull();
        body.GetProperty("tags").GetProperty("Environment").GetString().Should().Be("test");
    }

    [Fact]
    public async Task Validate_AwsRefMissing_Retorna200_ComExistsFalse()
    {
        var client = factory.CreateClientWithMockedAws(BuildMock());

        var response = await client.PostAsJsonAsync("/api/secrets/validate", new
        {
            reference = "secret://aws/efs-missing"
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("exists").GetBoolean().Should().BeFalse();
        body.GetProperty("error").GetString().Should().Contain("not found");
    }

    [Fact]
    public async Task Validate_LiteralReference_Retorna400()
    {
        var client = factory.CreateClientWithMockedAws(BuildMock());

        var response = await client.PostAsJsonAsync("/api/secrets/validate", new
        {
            reference = "sk-some-literal"
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Validate_EmptyReference_Retorna400()
    {
        var client = factory.CreateClientWithMockedAws(BuildMock());

        var response = await client.PostAsJsonAsync("/api/secrets/validate", new { reference = "" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task InvalidateCache_AwsRef_Retorna204()
    {
        var client = factory.CreateClientWithMockedAws(BuildMock());

        var response = await client.DeleteAsync("/api/secrets/cache?reference=secret%3A%2F%2Faws%2Fefs-existing");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task InvalidateCache_LiteralReference_Retorna400()
    {
        var client = factory.CreateClientWithMockedAws(BuildMock());

        var response = await client.DeleteAsync("/api/secrets/cache?reference=sk-literal");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Health_CanaryConfigurado_DescribeSecretSucesso_AwsReachableTrue()
    {
        var client = factory.CreateClientWithMockedAws(BuildMock(), canaryReference: "secret://aws/efs-existing");

        var response = await client.GetAsync("/api/secrets/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("awsReachable").GetBoolean().Should().BeTrue();
        body.GetProperty("canaryReference").GetString().Should().Be("secret://aws/efs-existing");
    }

    [Fact]
    public async Task Health_CanaryNaoConfigurado_AwsReachableFalse()
    {
        var client = factory.CreateClientWithMockedAws(BuildMock());

        var response = await client.GetAsync("/api/secrets/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("awsReachable").GetBoolean().Should().BeFalse();
        body.GetProperty("error").GetString().Should().Contain("canary");
    }

    [Fact]
    public async Task Health_CanaryDescribeFalha_AwsReachableFalse()
    {
        var client = factory.CreateClientWithMockedAws(BuildMock(), canaryReference: "secret://aws/efs-missing");

        var response = await client.GetAsync("/api/secrets/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("awsReachable").GetBoolean().Should().BeFalse();
        body.GetProperty("error").GetString().Should().NotBeNull();
    }
}
