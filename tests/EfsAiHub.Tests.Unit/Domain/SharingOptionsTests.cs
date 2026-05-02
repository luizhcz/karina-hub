using EfsAiHub.Core.Abstractions.Sharing;

namespace EfsAiHub.Tests.Unit.Domain;

[Trait("Category", "Unit")]
public class SharingOptionsTests
{
    [Fact]
    public void Defaults_MasterFlagsAtivos()
    {
        var opts = new SharingOptions();

        opts.Enabled.Should().BeTrue();
        opts.CrossProjectEnabled.Should().BeTrue();
        opts.WhitelistEnabled.Should().BeTrue();
        opts.AuditCrossInvoke.Should().BeTrue();
        opts.CrossInvokeAuditThrottleSeconds.Should().Be(60);
    }

    [Fact]
    public void Toggling_PreservaCadaFlagIndependentemente()
    {
        var opts = new SharingOptions
        {
            Enabled = false,
            CrossProjectEnabled = false,
            WhitelistEnabled = false,
            AuditCrossInvoke = false,
            CrossInvokeAuditThrottleSeconds = 30,
        };

        opts.Enabled.Should().BeFalse();
        opts.CrossProjectEnabled.Should().BeFalse();
        opts.WhitelistEnabled.Should().BeFalse();
        opts.AuditCrossInvoke.Should().BeFalse();
        opts.CrossInvokeAuditThrottleSeconds.Should().Be(30);
    }
}
