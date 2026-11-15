using System.Text.RegularExpressions;
using FolderThemeStudio.App.Services;

namespace FolderThemeStudio.App.Settings;

public sealed record FolderPalette(string GradientStart, string GradientEnd, string Stroke, string Glow)
{
    public static FolderPalette Default { get; } = new("#007AFF", "#0055D4", "#FFFFFF", "#007AFF");
}

public sealed record AppSettings(
    string Language,
    bool TutorialCompleted,
    FolderPalette Palette,
    IReadOnlyList<string> RecentColors,
    bool StartWithWindows = true,
    bool CloseToTray = true,
    bool MonitoringPaused = false,
    double CardOpacity = 1)
{
    public bool ThemeEditingEnabled { get; init; } = true;
    public bool AutomaticRulesEnabled { get; init; } = true;
    public bool HoverCardsEnabled { get; init; } = true;
    public bool QuickNotesEnabled { get; init; } = true;
    public bool DynamicIslandEnabled { get; init; } = true;
    public bool ApplicationBackgroundEnabled { get; init; } = true;
    public FolderEditChord EditChord { get; init; } = FolderEditChord.Default;
    private static readonly Regex HexColor = new("^#[0-9A-Fa-f]{6}$", RegexOptions.CultureInvariant);

    public static AppSettings Default { get; } = new("zh-CN", false, FolderPalette.Default, Array.Empty<string>());

    public static bool TryNormalize(AppSettings? value, out AppSettings normalized)
    {
        normalized = Default;
        if (value is null || value.Language is not ("zh-CN" or "en-US") || value.Palette is null ||
            !double.IsFinite(value.CardOpacity) || value.CardOpacity is < 0 or > 1 ||
            !FolderEditChord.TryNormalize(value.EditChord, out var normalizedChord))
        {
            return false;
        }

        var paletteColors = new[]
        {
            value.Palette.GradientStart,
            value.Palette.GradientEnd,
            value.Palette.Stroke,
            value.Palette.Glow,
        };
        if (paletteColors.Any(color => color is null || !HexColor.IsMatch(color)))
        {
            return false;
        }

        var recent = (value.RecentColors ?? Array.Empty<string>())
            .Where(color => color is not null && HexColor.IsMatch(color))
            .Select(color => color.ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToArray();
        normalized = new AppSettings(
            value.Language,
            value.TutorialCompleted,
            new FolderPalette(
                value.Palette.GradientStart.ToUpperInvariant(),
                value.Palette.GradientEnd.ToUpperInvariant(),
                value.Palette.Stroke.ToUpperInvariant(),
                value.Palette.Glow.ToUpperInvariant()),
            recent,
            value.StartWithWindows,
            value.CloseToTray,
            value.MonitoringPaused,
            value.CardOpacity) { EditChord = normalizedChord,
            ThemeEditingEnabled = value.ThemeEditingEnabled, AutomaticRulesEnabled = value.AutomaticRulesEnabled,
            HoverCardsEnabled = value.HoverCardsEnabled, QuickNotesEnabled = value.QuickNotesEnabled,
            DynamicIslandEnabled = value.DynamicIslandEnabled, ApplicationBackgroundEnabled = value.ApplicationBackgroundEnabled };
        return true;
    }
}
