using EfsAiHub.Host.Api.Services;
using EfsAiHub.Platform.Runtime.Executors;

namespace EfsAiHub.Host.Api.CodeExecutors;

/// <summary>
/// Registra o executor document_intelligence no CodeExecutorRegistry.
/// Captura o singleton DocumentIntelligenceFunctions via closure.
/// Chamado após app.Build() no Program.cs.
/// </summary>
public static class DocumentIntelligenceExecutorSetup
{
    public static void RegisterDocumentIntelligenceExecutor(this WebApplication app)
    {
        var functions = app.Services.GetRequiredService<DocumentIntelligenceFunctions>();
        var codeRegistry = app.Services.GetRequiredService<ICodeExecutorRegistry>();

        codeRegistry.Register("document_intelligence",
            (input, ct) => functions.ExecuteAsync(input, ct));
    }
}
