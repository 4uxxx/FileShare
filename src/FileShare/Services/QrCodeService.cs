using System.IO;
using System.Windows.Media.Imaging;
using QRCoder;

namespace FileShare.Services;

/// <summary>Renders a URL as a QR code bitmap for display in the GUI.</summary>
public static class QrCodeService
{
    public static BitmapImage? Generate(string? text, int pixelsPerModule = 8)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.Q);
        var pngQrCode = new PngByteQRCode(data);
        var bytes = pngQrCode.GetGraphic(pixelsPerModule);

        var image = new BitmapImage();
        using var stream = new MemoryStream(bytes);
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }
}
