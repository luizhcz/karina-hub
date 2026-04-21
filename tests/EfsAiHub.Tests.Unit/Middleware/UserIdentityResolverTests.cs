using EfsAiHub.Host.Api.Services;
using Microsoft.AspNetCore.Http;

namespace EfsAiHub.Tests.Unit.Middleware;

[Trait("Category", "Unit")]
public class UserIdentityResolverTests
{
    private readonly UserIdentityResolver _resolver = new();

    private static IHeaderDictionary Headers(string? account = null, string? profileId = null)
    {
        var headers = new HeaderDictionary();
        if (account is not null) headers["x-efs-account"] = account;
        if (profileId is not null) headers["x-efs-user-profile-id"] = profileId;
        return headers;
    }

    [Fact]
    public void Account_ResolveCliente()
    {
        var identity = _resolver.TryResolve(Headers(account: "12345"), out var error);

        identity.Should().NotBeNull();
        identity!.UserId.Should().Be("12345");
        identity.UserType.Should().Be("cliente");
        error.Should().BeNull();
    }

    [Fact]
    public void ProfileId_ResolveAssessor()
    {
        var identity = _resolver.TryResolve(Headers(profileId: "p-9999"), out var error);

        identity.Should().NotBeNull();
        identity!.UserId.Should().Be("p-9999");
        identity.UserType.Should().Be("assessor");
        error.Should().BeNull();
    }

    [Fact]
    public void SemHeaders_RetornaNull_ComErro()
    {
        var identity = _resolver.TryResolve(Headers(), out var error);

        identity.Should().BeNull();
        error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void AmbosHeaders_RetornaNull_ComErro()
    {
        var identity = _resolver.TryResolve(Headers(account: "12345", profileId: "p-9999"), out var error);

        identity.Should().BeNull();
        error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void AccountVazio_TratadoComoAusente()
    {
        var identity = _resolver.TryResolve(Headers(account: "   "), out var error);

        identity.Should().BeNull();
        error.Should().NotBeNullOrEmpty();
    }
}
