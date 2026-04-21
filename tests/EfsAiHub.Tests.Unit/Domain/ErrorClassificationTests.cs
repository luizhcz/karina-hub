using System.Net;
using System.Net.Http;
using EfsAiHub.Core.Orchestration.Enums;

namespace EfsAiHub.Tests.Unit.Domain;

/// <summary>
/// Testa a lógica de classificação de erros da mesma forma que WorkflowRunnerService.ClassifyError.
/// A lógica é extraída aqui em forma de método estático para validar isoladamente.
/// </summary>
[Trait("Category", "Unit")]
public class ErrorClassificationTests
{
    // Reproduz a lógica de ClassifyError de WorkflowRunnerService
    private static ErrorCategory Classify(Exception ex)
    {
        if (ex is HttpRequestException httpEx)
        {
            if (httpEx.StatusCode == HttpStatusCode.TooManyRequests)
                return ErrorCategory.LlmRateLimit;
            return ErrorCategory.LlmError;
        }

        if (ex.Message.Contains("content filter", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("content_filter", StringComparison.OrdinalIgnoreCase))
            return ErrorCategory.LlmContentFilter;

        if (ex.Message.Contains("rate limit", StringComparison.OrdinalIgnoreCase))
            return ErrorCategory.LlmRateLimit;

        return ErrorCategory.Unknown;
    }

    [Fact]
    public void Http429_ClassificaComoLlmRateLimit()
    {
        var ex = new HttpRequestException("Too Many Requests", null, HttpStatusCode.TooManyRequests);

        Classify(ex).Should().Be(ErrorCategory.LlmRateLimit);
    }

    [Fact]
    public void Http500_ClassificaComoLlmError()
    {
        var ex = new HttpRequestException("Internal Server Error", null, HttpStatusCode.InternalServerError);

        Classify(ex).Should().Be(ErrorCategory.LlmError);
    }

    [Fact]
    public void MensagemContentFilter_ClassificaComoLlmContentFilter()
    {
        var ex = new InvalidOperationException("Blocked by content filter policy.");

        Classify(ex).Should().Be(ErrorCategory.LlmContentFilter);
    }

    [Fact]
    public void MensagemContentFilter_Underscore_ClassificaCorreto()
    {
        var ex = new Exception("Error: content_filter triggered");

        Classify(ex).Should().Be(ErrorCategory.LlmContentFilter);
    }

    [Fact]
    public void MensagemRateLimit_ClassificaComoLlmRateLimit()
    {
        var ex = new Exception("OpenAI rate limit exceeded.");

        Classify(ex).Should().Be(ErrorCategory.LlmRateLimit);
    }

    [Fact]
    public void ExcecaoGenerica_ClassificaComoUnknown()
    {
        var ex = new InvalidOperationException("Something unexpected happened.");

        Classify(ex).Should().Be(ErrorCategory.Unknown);
    }
}
