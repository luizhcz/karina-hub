using EfsAiHub.Host.Api.Services;
using Microsoft.AspNetCore.Http;

namespace EfsAiHub.Tests.Unit.Middleware;

[Trait("Category", "Unit")]
public class UserIdentityResolverTests
{
    private readonly UserIdentityResolver _resolver = new();

    private static IHeaderDictionary Headers(
        string? account = null,
        string? profileId = null,
        string? permissions = "")
    {
        var headers = new HeaderDictionary();
        if (account is not null) headers["x-efs-account"] = account;
        if (profileId is not null) headers["x-efs-user-profile-id"] = profileId;
        if (permissions is not null) headers["x-efs-permissions"] = permissions;
        return headers;
    }

    [Fact]
    public void Account_ResolveCliente()
    {
        var identity = _resolver.TryResolve(Headers(account: "12345", permissions: "efs.cliente"), out var error);

        identity.Should().NotBeNull();
        identity!.UserId.Should().Be("12345");
        identity.UserType.Should().Be("cliente");
        identity.Permissions.Should().BeEquivalentTo(new[] { "efs.cliente" });
        error.Should().BeNull();
    }

    [Fact]
    public void ProfileId_ResolveAdmin()
    {
        var identity = _resolver.TryResolve(Headers(profileId: "p-9999", permissions: "efs.admin"), out var error);

        identity.Should().NotBeNull();
        identity!.UserId.Should().Be("p-9999");
        identity.UserType.Should().Be("admin");
        identity.Permissions.Should().BeEquivalentTo(new[] { "efs.admin" });
        error.Should().BeNull();
    }

    [Fact]
    public void SemHeaders_RetornaNull_ComErro()
    {
        var identity = _resolver.TryResolve(Headers(permissions: null), out var error);

        identity.Should().BeNull();
        error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void AmbosHeaders_RetornaNull_ComErro()
    {
        var identity = _resolver.TryResolve(
            Headers(account: "12345", profileId: "p-9999", permissions: "efs.admin"),
            out var error);

        identity.Should().BeNull();
        error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void AccountVazio_TratadoComoAusente()
    {
        var identity = _resolver.TryResolve(Headers(account: "   ", permissions: "efs.admin"), out var error);

        identity.Should().BeNull();
        error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void ComIdentidade_SemHeaderPermissions_RetornaNullComErro()
    {
        // Identidade presente mas permissions header não enviado: backend
        // exige x-efs-permissions sempre que identidade é fornecida.
        var identity = _resolver.TryResolve(Headers(account: "12345", permissions: null), out var error);

        identity.Should().BeNull();
        error.Should().NotBeNullOrEmpty();
        error.Should().Contain("x-efs-permissions");
    }

    [Fact]
    public void ComIdentidade_PermissionsVazias_Aceito()
    {
        // String vazia é válida — autenticado sem nenhuma permission.
        var identity = _resolver.TryResolve(Headers(account: "12345", permissions: ""), out var error);

        identity.Should().NotBeNull();
        identity!.Permissions.Should().BeEmpty();
        error.Should().BeNull();
    }

    [Fact]
    public void Permissions_NormalizadasLowercaseTrimDedupe()
    {
        var identity = _resolver.TryResolve(
            Headers(account: "12345", permissions: " EFS.Admin , efs.admin, , Efs.Workflows "),
            out var error);

        identity.Should().NotBeNull();
        identity!.Permissions.Should().BeEquivalentTo(new[] { "efs.admin", "efs.workflows" });
        error.Should().BeNull();
    }
}
