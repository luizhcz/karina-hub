namespace EfsAiHub.Platform.Runtime.Executors;

/// <summary>
/// Wrappers que consomem o <see cref="PostProcessorResult"/> tipado e emitem strings
/// raw para os destinos downstream:
/// <list type="bullet">
///   <item><c>unwrap_errors_to_text</c> — feedback humano para o agente de boleta no loop de erro.</item>
///   <item><c>unwrap_post_processor_output</c> — envelope/legacy JSON validado para o consumidor terminal.</item>
/// </list>
/// </summary>
public static class PostProcessorUnwrappers
{
    public static string FormatErrorsForAgent(PostProcessorResult result)
    {
        var errors = result.Errors.Count == 0 ? new List<string> { "(sem detalhes)" } : result.Errors;
        var original = result.OriginalOutput ?? "(input vazio)";
        return
            "Sua resposta anterior falhou na validação. Corrija e responda novamente seguindo exatamente o schema.\n" +
            "Erros:\n- " + string.Join("\n- ", errors) + "\n\n" +
            "Output original:\n" + original;
    }

    public static string ExtractValidatedOutput(PostProcessorResult result)
        => ServicePostProcessor.SerializeOutput(result);
}
