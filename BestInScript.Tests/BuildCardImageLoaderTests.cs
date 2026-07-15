using System.Windows.Media;
using System.Windows.Media.Imaging;
using BestInScript.API.Overlay;

namespace BestInScript.Tests;

/// <summary>
/// Pins the build panel's image decode. This needs no window or dispatcher, so unlike the
/// rest of the overlay it is testable — and it earns the coverage: the WPF imaging API
/// silently rejects a valid PNG if the BitmapImage properties are set in the wrong order
/// or IgnoreImageCache is combined with a StreamSource (v1.14.0 shipped exactly that bug,
/// and the panel rendered a title with no picture).
/// </summary>
public sealed class BuildCardImageLoaderTests : IDisposable
{
    private readonly string _dir;

    public BuildCardImageLoaderTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "bis-img-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    /// <summary>Writes a real PNG of the given size, so the decode under test is the genuine article.</summary>
    private string WritePng(int width, int height, string name = "card.png")
    {
        var stride = width * 4;
        var pixels = new byte[stride * height];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = 0x20; pixels[i + 1] = 0x60; pixels[i + 2] = 0xC0; pixels[i + 3] = 0xFF;
        }

        var source = BitmapSource.Create(
            width, height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);

        var path = Path.Combine(_dir, name);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var fs = File.Create(path);
        encoder.Save(fs);
        return path;
    }

    [Fact]
    public void Load_ValidPng_ReturnsAFrozenImage()
    {
        // The v1.14.0 regression: this threw ArgumentNullException("key") and the caller's
        // catch turned it into a silently image-less panel.
        var image = BuildCardImageLoader.Load(WritePng(765, 689), maxWidth: 900);

        Assert.NotNull(image);
        Assert.True(image.IsFrozen, "must be frozen to be safe to hold and hand around");
    }

    [Fact]
    public void Load_SourceWiderThanMax_DecodesDownscaled()
    {
        var image = BuildCardImageLoader.Load(WritePng(1800, 900), maxWidth: 900);

        // Downscaled at decode time, not just at render time — that's the point.
        Assert.Equal(900, ((BitmapSource)image).PixelWidth);
        Assert.Equal(450, ((BitmapSource)image).PixelHeight);  // aspect preserved
    }

    [Fact]
    public void Load_SourceNarrowerThanMax_IsNotUpscaled()
    {
        // A small gear table must stay crisp at its own size rather than being blown up
        // to the panel max and rendering soft.
        var image = BuildCardImageLoader.Load(WritePng(300, 200), maxWidth: 900);

        Assert.Equal(300, ((BitmapSource)image).PixelWidth);
        Assert.Equal(200, ((BitmapSource)image).PixelHeight);
    }

    [Fact]
    public void Load_SourceExactlyMax_IsNotResampled()
    {
        var image = BuildCardImageLoader.Load(WritePng(900, 400), maxWidth: 900);

        Assert.Equal(900, ((BitmapSource)image).PixelWidth);
    }

    [Fact]
    public void Load_DoesNotHoldTheFileOpen()
    {
        // The web UI replaces a card's image while the panel may be holding it, so the
        // decode must not leave a lock behind.
        var path = WritePng(400, 300);
        BuildCardImageLoader.Load(path, maxWidth: 900);

        var ex = Record.Exception(() => File.Delete(path));

        Assert.Null(ex);
    }

    [Fact]
    public void Load_CorruptFile_Throws_SoTheCallerCanSurfaceIt()
    {
        var path = Path.Combine(_dir, "broken.png");
        File.WriteAllBytes(path, [0x89, 0x50, 0x4E, 0x47, 0x00, 0x01, 0x02, 0x03]);

        Assert.ThrowsAny<Exception>(() => BuildCardImageLoader.Load(path, maxWidth: 900));
    }

    [Fact]
    public void Load_MissingFile_Throws()
        => Assert.Throws<FileNotFoundException>(
            () => BuildCardImageLoader.Load(Path.Combine(_dir, "nope.png"), maxWidth: 900));

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    public void Load_NonPositiveMaxWidth_StillDecodes(int maxWidth)
    {
        // Config is clamped upstream, but a zero DecodePixelWidth would throw — the loader
        // floors it rather than trusting the caller.
        var image = BuildCardImageLoader.Load(WritePng(400, 300), maxWidth);

        Assert.NotNull(image);
        Assert.True(image.IsFrozen);
    }
}
