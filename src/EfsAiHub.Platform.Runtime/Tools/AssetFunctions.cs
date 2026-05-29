using System.ComponentModel;
using System.Reflection;

namespace EfsAiHub.Platform.Runtime.Tools;

public static class AssetFunctions
{
    private static readonly Lazy<Dictionary<string, string>> Catalog = new(LoadCsv);

    [Description("Consulta um ativo pelo ticker. Retorna {ticker, nome} se existir no catálogo, ou null caso contrário. Match case-insensitive.")]
    public static AssetResult? GetAsset(
        [Description("Ticker do ativo (ex: PETR4, VALE3). Case-insensitive.")] string ticker)
    {
        if (string.IsNullOrWhiteSpace(ticker)) return null;

        var key = ticker.Trim().ToUpperInvariant();
        return Catalog.Value.TryGetValue(key, out var nome)
            ? new AssetResult(key, nome)
            : null;
    }

    private static Dictionary<string, string> LoadCsv()
    {
        var asm = Assembly.GetExecutingAssembly();
        const string resourceName = "EfsAiHub.Platform.Runtime.Tools.assets.csv";

        using var stream = asm.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{resourceName}' não encontrado. Verifique o csproj.");

        using var reader = new StreamReader(stream);
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        string? line;
        var first = true;
        while ((line = reader.ReadLine()) != null)
        {
            if (first) { first = false; continue; }
            if (string.IsNullOrWhiteSpace(line)) continue;

            var parts = line.Split(',', 2);
            if (parts.Length == 2)
                dict[parts[0].Trim()] = parts[1].Trim();
        }

        return dict;
    }

    public record AssetResult(string Ticker, string Nome);
}
