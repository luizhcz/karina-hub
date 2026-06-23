using EfsAiHub.Platform.Runtime.Ingestion;

namespace EfsAiHub.Tests.Unit.Ingestion;

/// <summary>
/// Cobre o suporte a PNG/JPEG na ingestão: detecção por magic bytes e leitura de
/// dimensão pelo header (validação do limite 50×50–10.000×10.000 do Azure DI).
/// </summary>
public sealed class ImageSupportTests
{
    [Fact]
    public void Detecta_PNG_por_magic_bytes()
    {
        FileTypeDetector.Detect(Png(100, 100)).Should().Be(DetectedFileType.Png);
    }

    [Fact]
    public void Detecta_JPEG_por_magic_bytes()
    {
        FileTypeDetector.Detect(Jpeg(800, 600)).Should().Be(DetectedFileType.Jpeg);
    }

    [Fact]
    public void Le_dimensao_de_PNG_pelo_header()
    {
        var size = ImageDimensions.TryRead(DetectedFileType.Png, Png(640, 480));
        size.Should().NotBeNull();
        size!.Value.Width.Should().Be(640);
        size.Value.Height.Should().Be(480);
    }

    [Fact]
    public void Le_dimensao_de_JPEG_pelo_header()
    {
        var size = ImageDimensions.TryRead(DetectedFileType.Jpeg, Jpeg(1024, 768));
        size.Should().NotBeNull();
        size!.Value.Width.Should().Be(1024);
        size.Value.Height.Should().Be(768);
    }

    [Theory]
    [InlineData(50, 50, true)]      // limite inferior — ok
    [InlineData(10000, 10000, true)] // limite superior — ok
    [InlineData(49, 100, false)]    // abaixo do mínimo
    [InlineData(100, 10001, false)] // acima do máximo
    public void Valida_limites_de_dimensao(int w, int h, bool esperado)
    {
        var size = ImageDimensions.TryRead(DetectedFileType.Png, Png(w, h));
        size.Should().NotBeNull();
        ImageDimensions.IsWithinLimits(size!.Value).Should().Be(esperado);
    }

    [Fact]
    public void Header_corrompido_ou_curto_retorna_null()
    {
        ImageDimensions.TryRead(DetectedFileType.Png, new byte[] { 0x89, 0x50, 0x4E, 0x47 }).Should().BeNull();
        ImageDimensions.TryRead(DetectedFileType.Jpeg, new byte[] { 0xFF, 0xD8 }).Should().BeNull();
    }

    // ── Builders de header mínimo (suficiente pra detecção + leitura de dimensão) ──
    internal static byte[] Png(int w, int h)
    {
        var b = new byte[24];
        byte[] sig = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        Array.Copy(sig, b, 8);
        b[11] = 13;                                   // IHDR chunk length
        b[12] = (byte)'I'; b[13] = (byte)'H'; b[14] = (byte)'D'; b[15] = (byte)'R';
        b[16] = (byte)(w >> 24); b[17] = (byte)(w >> 16); b[18] = (byte)(w >> 8); b[19] = (byte)w;
        b[20] = (byte)(h >> 24); b[21] = (byte)(h >> 16); b[22] = (byte)(h >> 8); b[23] = (byte)h;
        return b;
    }

    internal static byte[] Jpeg(int w, int h) => new byte[]
    {
        0xFF, 0xD8,             // SOI
        0xFF, 0xC0, 0x00, 0x11, // SOF0, length=17
        0x08,                   // precision
        (byte)(h >> 8), (byte)h,
        (byte)(w >> 8), (byte)w,
        0x03, 0x01, 0x22, 0x00, // component data (filler)
    };
}
