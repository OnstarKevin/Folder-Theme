namespace FolderThemeStudio.App.Settings;

public sealed record AppleColorPreset(string Name, string Hex, string GradientEnd)
{
    public FolderPalette Palette => new(Hex, GradientEnd, "#FFFFFF", Hex);
}

public static class AppleColors
{
    public static IReadOnlyList<AppleColorPreset> All { get; } =
    [
        new("蓝 · Blue", "#007AFF", "#0055D4"),
        new("绿 · Green", "#34C759", "#209C43"),
        new("靛蓝 · Indigo", "#5856D6", "#3D3BAC"),
        new("紫 · Purple", "#AF52DE", "#8935BB"),
        new("粉 · Pink", "#FF2D55", "#CF2047"),
        new("红 · Red", "#FF3B30", "#D42B25"),
        new("橙 · Orange", "#FF9500", "#DD7600"),
        new("黄 · Yellow", "#FFCC00", "#D9A500"),
        new("青 · Teal", "#30B0C7", "#238CA2"),
        new("薄荷 · Mint", "#00C7BE", "#009F99"),
        new("银 · Silver", "#C7C7CC", "#98989D"),
        new("石墨 · Graphite", "#8E8E93", "#636366")
    ];
}
