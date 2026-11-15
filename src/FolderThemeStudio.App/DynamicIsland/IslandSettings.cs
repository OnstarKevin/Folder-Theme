using System.IO;
using System.Text.Json;

namespace FolderThemeStudio.App.DynamicIsland;

/// <summary>灵动岛使用独立配置，避免和文件夹主题的设置互相覆盖。位置是 WPF 的逻辑像素。</summary>
public sealed record IslandSettings
{
    public bool Enabled { get; init; }
    public bool ShowTime { get; init; } = true;
    public bool ShowCpu { get; init; } = true;
    public bool ShowMemory { get; init; } = true;
    public bool ShowClipboard { get; init; }
    public bool ShowQuotes { get; init; } = true;
    public bool AutoRefreshQuotes { get; init; } = true;
    public bool StretchMotion { get; init; } = true;
    public bool ImageBackground { get; init; } = true;
    public double BackgroundOpacity { get; init; } = .85;
    public double BackgroundLighten { get; init; } = .55;
    public double BackgroundFadeStrength { get; init; } = 1;
    public double BackgroundFadeStart { get; init; } = .1;
    public string BackgroundFadeDirection { get; init; } = "Down";
    public string BackgroundFadeProfile { get; init; } = "Smooth";
    public string BackgroundAlignment { get; init; } = "Center";
    public bool FlowBorder { get; init; } = true;
    public bool AnimateBorder { get; init; } = true;
    public int AppearanceVersion { get; init; } = 1;
    public string CustomFontPath { get; init; } = string.Empty;
    public string CustomFontFamily { get; init; } = string.Empty;
    public bool ClickThrough { get; init; } = true;
    public bool HoverDetails { get; init; } = true;
    public bool LightText { get; init; }
    public string BackgroundImagePath { get; init; } = string.Empty;
    public bool ShowMusic { get; init; } = true;
    public bool ShowLyrics { get; init; } = true;
    public bool ShowNotifications { get; init; }
    public double Opacity { get; init; } = .94;
    public int RefreshSeconds { get; init; } = 1;
    public double? Left { get; init; }
    public double WindowWidth { get; init; } = 220;
    public double? Top { get; init; }
    public string LyricsDirectory { get; init; } = string.Empty;
    public bool OnlineLyrics { get; init; }
    public bool LocalNotifications { get; init; }
    public bool DragMotion { get; init; } = true;
    public bool SnapToEdges { get; init; } = true;
    public string BlockedNotificationSources { get; init; } = string.Empty;
    public int NotificationPort { get; init; } = 47301;
    public string NotificationToken { get; init; } = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24));

    public IslandSettings Normalize() => this with
    {
        AppearanceVersion = 1,
        BackgroundImagePath = BackgroundImagePath is { Length: <= 4096 } ? BackgroundImagePath.Trim() : string.Empty,
        BackgroundOpacity = Unit(BackgroundOpacity, .85), BackgroundLighten = Unit(BackgroundLighten, .55),
        BackgroundFadeStrength = Unit(BackgroundFadeStrength, 1), BackgroundFadeStart = Math.Min(.95, Unit(BackgroundFadeStart, .1)),
        BackgroundFadeDirection = BackgroundFadeDirection is "Down" or "Up" or "Left" or "Right" or "Edges" or "None" ? BackgroundFadeDirection : "Down",
        BackgroundFadeProfile = BackgroundFadeProfile is "Linear" or "Smooth" ? BackgroundFadeProfile : "Smooth",
        BackgroundAlignment = BackgroundAlignment is "Top" or "Center" or "Bottom" ? BackgroundAlignment : "Center",
        CustomFontPath = CustomFontPath is { Length: <= 4096 } ? CustomFontPath.Trim() : string.Empty,
        CustomFontFamily = CustomFontFamily is { Length: <= 256 } && !CustomFontFamily.Any(char.IsControl) ? CustomFontFamily.Trim() : string.Empty,
        Opacity = double.IsFinite(Opacity) ? Math.Clamp(Opacity, .2, 1) : .94,
        RefreshSeconds = Math.Clamp(RefreshSeconds, 1, 60),
        Left = Left is { } x && double.IsFinite(x) ? x : null,
        WindowWidth = double.IsFinite(WindowWidth) ? Math.Clamp(WindowWidth, IslandWidthSpring.Minimum, IslandWidthSpring.Maximum) : 220,
        Top = Top is { } y && double.IsFinite(y) ? y : null,
        LyricsDirectory = LyricsDirectory?.Trim() ?? string.Empty,
        BlockedNotificationSources = (BlockedNotificationSources ?? string.Empty).Trim()[..Math.Min((BlockedNotificationSources ?? string.Empty).Trim().Length, 2048)],
        NotificationPort = Math.Clamp(NotificationPort, 1024, 65535),
        NotificationToken = NotificationToken is { Length: >= 32 and <= 128 } && NotificationToken.All(char.IsAsciiLetterOrDigit)
            ? NotificationToken : Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24)),
    };
    private static double Unit(double value, double fallback) => double.IsFinite(value) ? Math.Clamp(value, 0, 1) : fallback;
}

public interface IIslandSettingsStore
{
    Task<IslandSettings> LoadAsync();
    Task SaveAsync(IslandSettings settings);
}

/// <summary>先写临时文件再原子替换；串行化保存，拖动和滚轮事件不会争抢同一个文件。</summary>
public sealed class IslandSettingsStore : IIslandSettingsStore
{
    private readonly string path;
    private readonly SemaphoreSlim gate = new(1, 1);
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };
    public IslandSettingsStore(string? path = null) => this.path = path ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FolderThemeStudio", "dynamic-island.json");

    public async Task<IslandSettings> LoadAsync()
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!File.Exists(path)) return new();
            var text = await File.ReadAllTextAsync(path).ConfigureAwait(false);
            using var document = JsonDocument.Parse(text);
            var settings = JsonSerializer.Deserialize<IslandSettings>(text, Json) ?? new();
            // Beta 23 forcibly wrote a transparent appearance. Restore the approved
            // original background once; subsequent explicit user choices survive.
            if (document.RootElement.ValueKind == JsonValueKind.Object && !document.RootElement.EnumerateObject().Any(p => p.Name.Equals("appearanceVersion", StringComparison.OrdinalIgnoreCase)))
                settings = settings with { ImageBackground = true };
            return settings.Normalize();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return new(); }
        finally { gate.Release(); }
    }

    public async Task SaveAsync(IslandSettings settings)
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            await File.WriteAllTextAsync(path + ".tmp", JsonSerializer.Serialize(settings.Normalize(), Json)).ConfigureAwait(false);
            File.Move(path + ".tmp", path, overwrite: true);
        }
        finally { gate.Release(); }
    }
}

