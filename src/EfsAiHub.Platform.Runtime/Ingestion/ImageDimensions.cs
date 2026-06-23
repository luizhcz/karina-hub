namespace EfsAiHub.Platform.Runtime.Ingestion;

/// <summary>
/// Lê a dimensão (largura×altura em pixels) de PNG/JPEG direto do header, sem
/// decodificar a imagem nem depender de <c>System.Drawing</c> (não roda no Alpine)
/// ou de libs externas. Cobre só o necessário pra validar o limite do Azure
/// Document Intelligence (50×50 a 10.000×10.000 px) antes de gastar uma chamada ao DI.
/// </summary>
public static class ImageDimensions
{
    /// <summary>Limites do Azure DI para imagens (px). Fora disso, o DI rejeita.</summary>
    public const int MinDimension = 50;
    public const int MaxDimension = 10_000;

    public readonly record struct ImageSize(int Width, int Height);

    /// <summary>
    /// Lê a dimensão pelo header. Retorna <c>null</c> se o tipo não for imagem ou
    /// o header for inválido/incompleto (imagem corrompida).
    /// </summary>
    public static ImageSize? TryRead(DetectedFileType type, ReadOnlySpan<byte> bytes) => type switch
    {
        DetectedFileType.Png => ReadPng(bytes),
        DetectedFileType.Jpeg => ReadJpeg(bytes),
        _ => null,
    };

    /// <summary>Ambas as dimensões dentro de [MinDimension, MaxDimension].</summary>
    public static bool IsWithinLimits(ImageSize size) =>
        size.Width >= MinDimension && size.Width <= MaxDimension
        && size.Height >= MinDimension && size.Height <= MaxDimension;

    // PNG: assinatura (8 bytes) + chunk IHDR (len 4, "IHDR" 4, width 4, height 4 — BE).
    // width nos bytes 16..19, height nos bytes 20..23.
    private static ImageSize? ReadPng(ReadOnlySpan<byte> b)
    {
        if (b.Length < 24) return null;
        if (b[0] != 0x89 || b[1] != 0x50 || b[2] != 0x4E || b[3] != 0x47
            || b[4] != 0x0D || b[5] != 0x0A || b[6] != 0x1A || b[7] != 0x0A) return null;
        if (b[12] != 0x49 || b[13] != 0x48 || b[14] != 0x44 || b[15] != 0x52) return null; // "IHDR"

        // BE como long pra não dar overflow/negativo com dimensões absurdas declaradas;
        // se passar de int, está fora do limite de qualquer forma.
        long w = ((long)b[16] << 24) | ((long)b[17] << 16) | ((long)b[18] << 8) | b[19];
        long h = ((long)b[20] << 24) | ((long)b[21] << 16) | ((long)b[22] << 8) | b[23];
        return ToSize(w, h);
    }

    // JPEG: percorre os markers até achar um SOF (Start Of Frame), que carrega a
    // dimensão. Pula segmentos com length; ignora markers standalone (RST/SOI/EOI/TEM).
    private static ImageSize? ReadJpeg(ReadOnlySpan<byte> b)
    {
        if (b.Length < 4 || b[0] != 0xFF || b[1] != 0xD8) return null;

        var i = 2;
        while (i + 1 < b.Length)
        {
            if (b[i] != 0xFF) { i++; continue; }      // resync até o próximo marker
            while (i < b.Length && b[i] == 0xFF) i++;  // pula bytes de fill 0xFF
            if (i >= b.Length) break;

            var marker = b[i++];

            // Markers standalone (sem campo de length): SOI/EOI, RST0-7, TEM.
            if (marker == 0xD8 || marker == 0xD9 || (marker >= 0xD0 && marker <= 0xD7) || marker == 0x01)
                continue;

            if (i + 1 >= b.Length) break;
            var len = (b[i] << 8) | b[i + 1];          // length inclui os 2 bytes do próprio length

            if (IsStartOfFrame(marker))
            {
                // Segmento SOF: [len(2)][precision(1)][height(2 BE)][width(2 BE)]…
                if (i + 6 >= b.Length) return null;
                int h = (b[i + 3] << 8) | b[i + 4];
                int w = (b[i + 5] << 8) | b[i + 6];
                return ToSize(w, h);
            }

            if (len < 2) return null;                  // segmento malformado
            i += len;
        }
        return null;
    }

    // SOF0..SOF15 carregam dimensão, EXCETO 0xC4 (DHT), 0xC8 (JPG) e 0xCC (DAC).
    private static bool IsStartOfFrame(byte m) =>
        m is 0xC0 or 0xC1 or 0xC2 or 0xC3 or 0xC5 or 0xC6 or 0xC7
          or 0xC9 or 0xCA or 0xCB or 0xCD or 0xCE or 0xCF;

    private static ImageSize? ToSize(long w, long h)
    {
        if (w is <= 0 or > int.MaxValue || h is <= 0 or > int.MaxValue) return null;
        return new ImageSize((int)w, (int)h);
    }
}
