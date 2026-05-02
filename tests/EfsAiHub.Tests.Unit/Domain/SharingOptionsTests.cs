using EfsAiHub.Core.Abstractions.Sharing;

namespace EfsAiHub.Tests.Unit.Domain;

[Trait("Category", "Unit")]
public class SharingOptionsTests
{
    [Fact]
    public void IsMandatoryPinFor_FlagOff_RetornaFalseSemImporto()
    {
        var opts = new SharingOptions { MandatoryPin = false };

        opts.IsMandatoryPinFor("tenant-x").Should().BeFalse();
        opts.IsMandatoryPinFor(null).Should().BeFalse();
    }

    [Fact]
    public void IsMandatoryPinFor_FlagOnSemWhitelist_RetornaTrueGlobal()
    {
        var opts = new SharingOptions
        {
            MandatoryPin = true,
            MandatoryPinTenants = null,
        };

        opts.IsMandatoryPinFor("tenant-x").Should().BeTrue();
        opts.IsMandatoryPinFor("any-tenant").Should().BeTrue();
    }

    [Fact]
    public void IsMandatoryPinFor_FlagOnComWhitelistVazia_RetornaTrueGlobal()
    {
        var opts = new SharingOptions
        {
            MandatoryPin = true,
            MandatoryPinTenants = Array.Empty<string>(),
        };

        opts.IsMandatoryPinFor("tenant-x").Should().BeTrue();
    }

    [Fact]
    public void IsMandatoryPinFor_FlagOnComWhitelist_TenantMatchRetornaTrue()
    {
        var opts = new SharingOptions
        {
            MandatoryPin = true,
            MandatoryPinTenants = new[] { "tenant-A", "tenant-B" },
        };

        opts.IsMandatoryPinFor("tenant-A").Should().BeTrue();
        opts.IsMandatoryPinFor("tenant-B").Should().BeTrue();
    }

    [Fact]
    public void IsMandatoryPinFor_FlagOnComWhitelist_TenantForaRetornaFalse()
    {
        var opts = new SharingOptions
        {
            MandatoryPin = true,
            MandatoryPinTenants = new[] { "tenant-A" },
        };

        opts.IsMandatoryPinFor("tenant-B").Should().BeFalse();
        opts.IsMandatoryPinFor("tenant-Z").Should().BeFalse();
    }

    [Fact]
    public void IsMandatoryPinFor_TenantIdNull_ComWhitelistRetornaFalse()
    {
        var opts = new SharingOptions
        {
            MandatoryPin = true,
            MandatoryPinTenants = new[] { "tenant-A" },
        };

        // Sem tenant context, não enforce mesmo com flag ON.
        opts.IsMandatoryPinFor(null).Should().BeFalse();
        opts.IsMandatoryPinFor("").Should().BeFalse();
    }

    [Fact]
    public void IsMandatoryPinFor_MatchCaseInsensitive()
    {
        var opts = new SharingOptions
        {
            MandatoryPin = true,
            MandatoryPinTenants = new[] { "Tenant-A" },
        };

        opts.IsMandatoryPinFor("tenant-a").Should().BeTrue();
        opts.IsMandatoryPinFor("TENANT-A").Should().BeTrue();
    }

    [Fact]
    public void LosslessAgentVersion_DefaultTrue()
    {
        var opts = new SharingOptions();
        opts.LosslessAgentVersion.Should().BeTrue();
    }

    [Fact]
    public void MandatoryPin_DefaultFalse()
    {
        var opts = new SharingOptions();
        opts.MandatoryPin.Should().BeFalse();
    }

    [Fact]
    public void MandatoryPinTenants_DefaultNull()
    {
        var opts = new SharingOptions();
        opts.MandatoryPinTenants.Should().BeNull();
    }
}
