using System.Net;
using OutfitFinder.Core.Abstractions;
using OutfitFinder.Core.Ai;
using OutfitFinder.Infrastructure.Imaging;
using OutfitFinder.Infrastructure.Safety;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;

namespace OutfitFinder.Tests.Unit;

public class ImagingTests
{
    private static byte[] MakeJpeg(int w, int h, bool withGps = false)
    {
        // Random noise: blurring it changes pixels a lot (a smooth gradient would barely change).
        var rng = new Random(42);
        using var img = new Image<Rgb24>(w, h);
        for (var y = 0; y < h; y++) for (var x = 0; x < w; x++) img[x, y] = new Rgb24((byte)rng.Next(256), (byte)rng.Next(256), (byte)rng.Next(256));
        if (withGps)
        {
            var exif = new ExifProfile();
            exif.SetValue(ExifTag.Artist, "Someone");
            exif.SetValue(ExifTag.Copyright, "Someone");
            img.Metadata.ExifProfile = exif;
        }
        using var ms = new MemoryStream();
        img.SaveAsJpeg(ms);
        return ms.ToArray();
    }

    [Fact]
    public void Rejects_files_that_are_not_jpg_png_or_webp_by_signature()
    {
        var p = new ImageProcessor();
        Assert.Throws<InvalidImageException>(() => p.Normalise("GIF89a......"u8.ToArray()));
        Assert.Throws<InvalidImageException>(() => p.Normalise("<svg></svg>"u8.ToArray()));
        Assert.Throws<InvalidImageException>(() => p.Normalise([]));
    }

    [Fact]
    public void Rejects_files_over_10_MB()
    {
        var big = new byte[ImageProcessor.MaxBytes + 1];
        big[0] = 0xFF; big[1] = 0xD8; big[2] = 0xFF;
        Assert.Throws<InvalidImageException>(() => new ImageProcessor().Normalise(big));
    }

    [Fact]
    public void Re_encodes_strips_exif_and_resizes_to_2048()
    {
        var result = new ImageProcessor().Normalise(MakeJpeg(3000, 1500, withGps: true));
        Assert.Equal(2048, result.Width);
        Assert.Equal(1024, result.Height);
        using var img = Image.Load(result.Jpeg);
        Assert.Null(img.Metadata.ExifProfile);
        Assert.Equal(64, result.Sha256.Length);
    }

    [Fact]
    public void Blurs_only_the_padded_face_region()
    {
        var p = new ImageProcessor();
        var src = p.Normalise(MakeJpeg(400, 400)).Jpeg;
        var baseline = p.BlurRegions(src, []);   // same re-encode, no faces
        var blurred = p.BlurRegions(src, [new NormalizedBox { X = 0.4, Y = 0.1, W = 0.2, H = 0.2 }]);
        using var a = Image.Load<Rgb24>(baseline);
        using var b = Image.Load<Rgb24>(blurred);
        // Inside the face box the pixels change a lot; far away they are untouched.
        Assert.True(MeanDiff(a, b, 190, 70) > 30, "face region was not blurred");
        Assert.True(MeanDiff(a, b, 10, 370) < 2, "pixels far from the face changed");
    }

    private static double MeanDiff(Image<Rgb24> a, Image<Rgb24> b, int x0, int y0)
    {
        double sum = 0;
        for (var y = y0; y < y0 + 20; y++)
            for (var x = x0; x < x0 + 20; x++)
                sum += Math.Abs(a[x, y].R - b[x, y].R) + Math.Abs(a[x, y].G - b[x, y].G) + Math.Abs(a[x, y].B - b[x, y].B);
        return sum / 400 / 3;
    }
}

public class SafetyTests
{
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("10.1.2.3")]
    [InlineData("172.16.0.1")]
    [InlineData("192.168.1.1")]
    [InlineData("169.254.169.254")]
    [InlineData("100.64.0.1")]
    [InlineData("::1")]
    [InlineData("fd00::1")]
    public void Private_and_metadata_addresses_are_blocked(string ip) => Assert.True(UrlGuard.IsPrivate(IPAddress.Parse(ip)));

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("151.101.1.1")]
    public void Public_addresses_are_allowed(string ip) => Assert.False(UrlGuard.IsPrivate(IPAddress.Parse(ip)));

    [Theory]
    [InlineData("http://example.com/a.jpg")]          // https only
    [InlineData("https://169.254.169.254/latest")]   // metadata endpoint
    [InlineData("https://localhost/a.jpg")]
    [InlineData("not a url")]
    public async Task Image_fetcher_refuses_unsafe_urls(string url)
    {
        var fetcher = new SafeImageFetcher(new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }));
        await Assert.ThrowsAsync<ImageFetchException>(() => fetcher.FetchAsync(url, default));
    }
}
