namespace FolderThemeStudio.Core.Themes;

public enum MaterialStyle { Glass, Clay, FrostedGlass, Paper, Satin }
public enum StickerStyle { None, Star, Flower, Heart, Paw }

public static class StickerPlacement
{
    public const double MinimumCenter = 0.15;
    public const double MaximumCenter = 0.85;
    public const double MinimumScale = 0.15;
    public const double MaximumScale = 0.60;
    public const double MinimumRotation = -180;
    public const double MaximumRotation = 180;
}

public sealed record FolderTheme(
    int SchemaVersion,
    Guid Id,
    string Name,
    string GradientStart,
    string GradientEnd,
    double GradientAngle,
    double Opacity,
    double CornerRadius,
    string StrokeColor,
    double StrokeOpacity,
    double StrokeWidth,
    double HighlightStrength,
    string GlowColor,
    double GlowStrength,
    double GlowRadius,
    double ShadowStrength,
    double ShadowOffset,
    MaterialStyle Material = MaterialStyle.Glass,
    StickerStyle Sticker = StickerStyle.None,
    string StickerColor = "#FFFFFF",
    double StickerX = 0.5,
    double StickerY = 0.5,
    double StickerScale = 0.35,
    double StickerRotation = 0)
{
    public const int CurrentSchemaVersion = 2;

    public FolderTheme UpgradeV1() => SchemaVersion == 1
        ? this with
        {
            SchemaVersion = CurrentSchemaVersion,
            Material = MaterialStyle.Glass,
            Sticker = StickerStyle.None,
            StickerColor = "#FFFFFF",
            StickerX = 0.5,
            StickerY = 0.5,
            StickerScale = 0.35,
            StickerRotation = 0
        }
        : this;

    public static FolderTheme IceBlue { get; } = new(
        CurrentSchemaVersion, Guid.Parse("7fcb41ba-7c24-4914-9353-2f90be4bc4f0"), "玻璃霓虹 / 冰川蓝",
        "#65D9FF", "#478CFF", 135, .82, 16, "#B9F5FF", .90, 2,
        .58, "#43BFFF", .45, 8, .18, 6);
}

public sealed record ThemeValidationResult(IReadOnlyList<string> Errors)
{
    public bool IsValid => Errors.Count == 0;
}
