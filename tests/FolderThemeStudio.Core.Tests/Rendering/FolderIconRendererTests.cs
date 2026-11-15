using System.Windows.Media;
using System.Security.Cryptography;
using FolderThemeStudio.Core.Rendering;
using FolderThemeStudio.Core.Themes;
using Xunit;

namespace FolderThemeStudio.Core.Tests.Rendering;

public sealed class FolderIconRendererTests
{
    [Fact]
    public void SatinFolder_RemainsReadableAtSmallSizesWithTransparentMargins()
    {
        Assert.True(Enum.TryParse<MaterialStyle>("Satin", out var satin));
        foreach (var size in new[] { 16, 32, 256 })
        {
            var pixels = Pixels(FolderTheme.IceBlue with { Material = satin }, size);
            Assert.NotEqual(Pixels(FolderTheme.IceBlue, size), pixels);
            Assert.Contains(Enumerable.Range(0, size * size), index => pixels[index * 4 + 3] > 0);
            Assert.All(Enumerable.Range(0, size), x => Assert.Equal(0, pixels[x * 4 + 3]));
        }
    }

    [Fact]
    public void GlassRendering_MatchesOriginalPixelHashes()
    {
        var hashes = new[] { 16, 32, 64, 256 }
            .Select(size => Convert.ToHexString(SHA256.HashData(Pixels(FolderTheme.IceBlue, size))))
            .ToArray();
        Assert.Equal(new[]
        {
            "38C08CBDE311517A2F793ECD7635BB1A55A93CC2A213ED474C33C329203FE8DC",
            "29C0B51C50B924A6AD5062A9AA4B596BB2F2B000C549E683DB60746C28D69B73",
            "91B17D78C544CDA24B42E68F22536D975A63F8200003A57CAB297D4A83894481",
            "04C9037EA7801390E9674C1872F0F831D8D54A29D734DC948FF34C3155B68DE6"
        }, hashes);
    }

    [Theory]
    [InlineData(MaterialStyle.Clay)]
    [InlineData(MaterialStyle.FrostedGlass)]
    [InlineData(MaterialStyle.Paper)]
    public void MaterialStyle_ChangesVisiblePixelsAndKeepsTransparentEdges(MaterialStyle material)
    {
        var original = Pixels(FolderTheme.IceBlue, 64);
        var styled = Pixels(FolderTheme.IceBlue with { Material = material }, 64);
        Assert.NotEqual(original, styled);

        var small = Pixels(FolderTheme.IceBlue with { Material = material }, 16);
        Assert.Contains(Enumerable.Range(0, 16 * 16), index => small[index * 4 + 3] > 0);
        Assert.All(Enumerable.Range(0, 16), x => Assert.Equal(0, small[x * 4 + 3]));
    }

    private static byte[] Pixels(FolderTheme theme, int size)
    {
        var bitmap = FolderIconRenderer.Render(theme, size);
        var pixels = new byte[size * size * 4];
        bitmap.CopyPixels(pixels, size * 4, 0);
        return pixels;
    }

    [Theory]
    [InlineData(StickerStyle.Star)]
    [InlineData(StickerStyle.Flower)]
    [InlineData(StickerStyle.Heart)]
    [InlineData(StickerStyle.Paw)]
    public void Sticker_ChangesFolderFaceWithoutTouchingTransparentMargins(StickerStyle sticker)
    {
        var decorated = FolderTheme.IceBlue with { Sticker = sticker };
        Assert.NotEqual(Pixels(FolderTheme.IceBlue, 64), Pixels(decorated, 64));
        foreach (var size in new[] { 16, 256 })
        {
            var pixels = Pixels(decorated, size);
            Assert.All(Enumerable.Range(0, size), x => Assert.Equal(0, pixels[x * 4 + 3]));
        }
    }

    [Fact]
    public void StickerPosition_ClampsOversizedDragInsideFolderFace()
    {
        var face = new System.Windows.Rect(.09, .30, .82, .55);
        var position = StickerPainter.ClampPosition(new System.Windows.Point(2, -1), .6, face);
        Assert.InRange(position.X, .6, .85);
        Assert.InRange(position.Y, .15, .4);
    }

    [Fact]
    public void StickerPositionRotationAndColor_ChangeRenderedIcon()
    {
        var baseTheme = FolderTheme.IceBlue with { Sticker = StickerStyle.Heart };
        var original = Pixels(baseTheme, 128);
        Assert.NotEqual(original, Pixels(baseTheme with { StickerX = .7 }, 128));
        Assert.NotEqual(original, Pixels(baseTheme with { StickerRotation = 45 }, 128));
        Assert.NotEqual(original, Pixels(baseTheme with { StickerColor = "#FF0088" }, 128));
    }
    public static IEnumerable<object[]> Sizes() =>
        FolderIconRenderer.RequiredSizes.Select(size => new object[] { size });

    [Theory]
    [MemberData(nameof(Sizes))]
    public void Render_ReturnsRequestedSquareBitmap(int size)
    {
        var bitmap = FolderIconRenderer.Render(FolderTheme.IceBlue, size);

        Assert.Equal(size, bitmap.PixelWidth);
        Assert.Equal(size, bitmap.PixelHeight);
        Assert.True(bitmap.IsFrozen);
    }

    [Theory]
    [MemberData(nameof(Sizes))]
    public void Render_VisiblePixelsHaveATransparentInsetOnEveryEdge(int size)
    {
        var bitmap = FolderIconRenderer.Render(FolderTheme.IceBlue, size);
        var pixels = new byte[size * size * 4];
        bitmap.CopyPixels(pixels, size * 4, 0);

        var opaquePixelIndexes = Enumerable.Range(0, size * size)
            .Where(index => pixels[index * 4 + 3] != 0)
            .ToArray();

        Assert.NotEmpty(opaquePixelIndexes);
        var xs = opaquePixelIndexes.Select(index => index % size).ToArray();
        var ys = opaquePixelIndexes.Select(index => index / size).ToArray();

        Assert.InRange(xs.Min(), 1, size - 2);
        Assert.InRange(xs.Max(), 1, size - 2);
        Assert.InRange(ys.Min(), 1, size - 2);
        Assert.InRange(ys.Max(), 1, size - 2);
    }

    [Fact]
    public void Render_UnsupportedSize_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            FolderIconRenderer.Render(FolderTheme.IceBlue, 40));
    }
}
