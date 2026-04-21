using System.ComponentModel;
using System.Diagnostics;

namespace EfsAiHub.Host.Api.Services;

/// <summary>
/// Renderiza diagramas de workflow para PNG.
/// Tenta usar Graphviz (comando local 'dot') primeiro; usa a API mermaid.ink como fallback.
/// </summary>
public class DiagramRenderingService
{
    private readonly IHttpClientFactory _httpClientFactory;

    public DiagramRenderingService(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>
    /// Renderiza um diagrama de workflow para bytes PNG.
    /// Usa Graphviz se disponível, caso contrário usa mermaid.ink como fallback.
    /// </summary>
    public async Task<byte[]> RenderToPngAsync(string dotString, string mermaidString, CancellationToken ct)
    {
        try
        {
            return await RenderDotToPngAsync(dotString, ct);
        }
        catch (Exception ex) when (ex is Win32Exception or FileNotFoundException)
        {
            return await RenderMermaidToPngAsync(mermaidString, ct);
        }
    }

    private static async Task<byte[]> RenderDotToPngAsync(string dot, CancellationToken ct)
    {
        var psi = new ProcessStartInfo("dot", "-Tpng")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Falha ao iniciar processo 'dot'.");

        await process.StandardInput.WriteAsync(dot);
        process.StandardInput.Close();

        using var ms = new MemoryStream();
        await process.StandardOutput.BaseStream.CopyToAsync(ms, ct);
        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0)
        {
            var err = await process.StandardError.ReadToEndAsync(ct);
            throw new InvalidOperationException($"dot retornou exit code {process.ExitCode}: {err}");
        }

        return ms.ToArray();
    }

    private async Task<byte[]> RenderMermaidToPngAsync(string mermaid, CancellationToken ct)
    {
        var encoded = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(mermaid))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

        var client = _httpClientFactory.CreateClient("mermaid-ink");
        var response = await client.GetAsync($"/img/{encoded}", ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(ct);
    }
}
