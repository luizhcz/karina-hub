using EfsAiHub.Host.Api.Configuration;
using EfsAiHub.Host.Api.Services;
using Microsoft.Extensions.Options;

namespace EfsAiHub.Tests.Unit.Identity;

[Trait("Category", "Unit")]
public class AdminPermissionEvaluatorTests
{
    private static AdminPermissionEvaluator Build(params string[] adminPermissions)
    {
        var options = new AdminOptions { AdminPermissions = [..adminPermissions] };
        var monitor = Substitute.For<IOptionsMonitor<AdminOptions>>();
        monitor.CurrentValue.Returns(options);
        return new AdminPermissionEvaluator(monitor);
    }

    [Fact]
    public void Permissions_Vazias_NuncaAdmin()
    {
        var evaluator = Build("efs.admin");

        evaluator.IsAdmin(Array.Empty<string>()).Should().BeFalse();
        evaluator.IsAdmin(null).Should().BeFalse();
    }

    [Fact]
    public void Config_Vazia_NuncaAdmin()
    {
        var evaluator = Build();

        evaluator.IsAdmin(new[] { "efs.admin" }).Should().BeFalse();
    }

    [Fact]
    public void QualquerMatch_EhAdmin()
    {
        var evaluator = Build("efs.admin", "efs.platform.admin");

        evaluator.IsAdmin(new[] { "efs.viewer", "efs.admin" }).Should().BeTrue();
    }

    [Fact]
    public void Match_CaseInsensitive()
    {
        var evaluator = Build("EFS.Admin");

        evaluator.IsAdmin(new[] { "efs.admin" }).Should().BeTrue();
    }

    [Fact]
    public void SemInterseccao_NaoEhAdmin()
    {
        var evaluator = Build("efs.admin");

        evaluator.IsAdmin(new[] { "efs.viewer", "efs.editor" }).Should().BeFalse();
    }
}
