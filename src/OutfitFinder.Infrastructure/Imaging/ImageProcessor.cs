using System.Security.Cryptography;
using OutfitFinder.Core.Abstractions;
using OutfitFinder.Core.Ai;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace OutfitFinder.Infrastructure.Imaging;

public sealed class ImageProcessor : IImageProcessor
{
    public const int MaxBytes = 10 * 1024 * 1024;
    public const int MaxSide = 2048;
    private static readonly JpegEncoder Encoder = new() { Quality = 88 };

    /// <summary>Identify the type by file signature (magic bytes), never by extension or content-type.</summary>
    public static string? DetectType(ReadOnlySpan<byte> b)
    {
        if (b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return "jpeg";
        if (b.Length >= 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47 && b[4] == 0x0D && b[5] == 0x0A && b[6] == 0x1A && b[7] == 0x0A) return "png";
        if (b.Length >= 12 && b[0] == 'R' && b[1] == 'I' && b[2] == 'F' && b[3] == 'F' && b[8] == 'W' && b[9] == 'E' && b[10] == 'B' && b[11] == 'P') return "webp";
        return null;
    }

    public ProcessedImage Normalise(byte[] upload)
    {
        if (upload.Length == 0) throw new InvalidImageException("The file is empty.");
        if (upload.Length > MaxBytes) throw new InvalidImageException("Photos must be 10 MB or smaller.");
        if (DetectType(upload) is null) throw new InvalidImageException("Please upload a JPG, PNG or WebP photo.");

        Image<Rgb24> image;
        try { image = Image.Load<Rgb24>(upload); }
        catch (Exception) { throw new InvalidImageException("This photo could not be read. Try another file."); }

        using (image)
        {
            image.Mutate(x => x.AutoOrient());
            if (image.Width > MaxSide || image.Height > MaxSide)
                image.Mutate(x => x.Resize(new ResizeOptions { Mode = ResizeMode.Max, Size = new Size(MaxSide, MaxSide) }));
            // Re-encoding from pixels drops EXIF, GPS, XMP and ICC metadata.
            image.Metadata.ExifProfile = null;
            image.Metadata.XmpProfile = null;
            image.Metadata.IptcProfile = null;
            using var ms = new MemoryStream();
            image.SaveAsJpeg(ms, Encoder);
            var jpeg = ms.ToArray();
            return new ProcessedImage(jpeg, image.Width, image.Height, Sha256(jpeg));
        }
    }

    public byte[] BlurRegions(byte[] jpeg, IEnumerable<NormalizedBox> faces, double padding = 0.25)
    {
        using var image = Image.Load<Rgb24>(jpeg);
        foreach (var f in faces)
        {
            var rect = ToPixels(f, image.Width, image.Height, padding);
            if (rect.Width < 2 || rect.Height < 2) continue;
            var sigma = Math.Max(12f, Math.Max(rect.Width, rect.Height) / 6f);
            // Pixelate then blur: heavy enough that features are not recoverable.
            image.Mutate(x => x.Pixelate(Math.Max(6, rect.Width / 10), rect).GaussianBlur(rect, sigma));
        }
        using var ms = new MemoryStream();
        image.SaveAsJpeg(ms, Encoder);
        return ms.ToArray();
    }

    public byte[] Crop(byte[] jpeg, NormalizedBox box)
    {
        using var image = Image.Load<Rgb24>(jpeg);
        var rect = ToPixels(box, image.Width, image.Height, 0.05);
        if (rect.Width < 8 || rect.Height < 8) return jpeg;
        image.Mutate(x => x.Crop(rect));
        using var ms = new MemoryStream();
        image.SaveAsJpeg(ms, Encoder);
        return ms.ToArray();
    }

    public static Rectangle ToPixels(NormalizedBox b, int width, int height, double padding)
    {
        var padX = b.W * padding; var padY = b.H * padding;
        var x0 = Math.Clamp((b.X - padX) * width, 0, width);
        var y0 = Math.Clamp((b.Y - padY) * height, 0, height);
        var x1 = Math.Clamp((b.X + b.W + padX) * width, 0, width);
        var y1 = Math.Clamp((b.Y + b.H + padY) * height, 0, height);
        return Rectangle.FromLTRB((int)x0, (int)y0, (int)Math.Ceiling(x1), (int)Math.Ceiling(y1));
    }

    public static string Sha256(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data));
}
