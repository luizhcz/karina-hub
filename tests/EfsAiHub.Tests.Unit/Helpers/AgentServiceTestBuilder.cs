namespace EfsAiHub.Tests.Unit.Helpers;

/// <summary>
/// Fábricas pra <see cref="IAgentDefinitionComposer"/>/<see cref="IAgentDefinitionDecomposer"/>
/// em testes do AgentService que não exercitam a composição/decomposição —
/// devolve o input cru e mantém o foco do teste no comportamento testado.
/// </summary>
internal static class AgentServiceTestBuilder
{
    public static IAgentDefinitionComposer IdentityComposer()
    {
        var composer = Substitute.For<IAgentDefinitionComposer>();
        composer
            .ComposeAsync(Arg.Any<AgentDefinition>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult(callInfo.ArgAt<AgentDefinition>(0)));
        return composer;
    }

    public static IAgentDefinitionDecomposer IdentityDecomposer()
    {
        var decomposer = Substitute.For<IAgentDefinitionDecomposer>();
        decomposer.Decompose(Arg.Any<AgentDefinition>())
            .Returns(callInfo => callInfo.ArgAt<AgentDefinition>(0));
        return decomposer;
    }
}
