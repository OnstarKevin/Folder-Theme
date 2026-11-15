using System.IO;
using System.Text.Json.Nodes;
using FolderThemeStudio.App.Services;
using FolderThemeStudio.Core.Tests.TestSupport;
using Xunit;

namespace FolderThemeStudio.Core.Tests.Application;

public sealed class DesktopRecoveryTests
{
    [Fact]
    public void StartupWithoutLegacyState_PreservesNativeVisibility()
    {
        using var directory = new TemporaryDirectory();
        var platform = new Platform();
        new DesktopRecovery(platform, Path.Combine(directory.Path, "missing.json")).Restore();
        Assert.Empty(platform.Actions);
    }

    [Fact]
    public void EnabledLegacyState_RestoresAndDisablesOnce_PreservingUnknownFieldsAndUserFiles()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "desktop-organizer.json");
        File.WriteAllText(path, """{"Enabled":true,"ApplicationPositions":{"app":{"X":10,"Y":20}},"FutureOption":"keep"}""");
        var userFile = Path.Combine(directory.Path, "report.txt");
        File.WriteAllText(userFile, "untouched");
        var platform = new Platform();
        var recovery = new DesktopRecovery(platform, path);
        recovery.Restore();
        recovery.Restore();
        Assert.Equal(["show:1"], platform.Actions);
        var migrated = JsonNode.Parse(File.ReadAllText(path))!;
        Assert.False(migrated["Enabled"]!.GetValue<bool>());
        Assert.Equal("keep", migrated["FutureOption"]!.GetValue<string>());
        Assert.Equal(10, migrated["ApplicationPositions"]!["app"]!["X"]!.GetValue<int>());
        Assert.Equal("untouched", File.ReadAllText(userFile));
    }

    [Fact]
    public void LiveLegacyHost_IsNotifiedBeforeNativeIconsAreRestored()
    {
        using var directory = new TemporaryDirectory();
        var platform = new Platform { Views = [new(1, [2, 3])] };
        new DesktopRecovery(platform, Path.Combine(directory.Path, "missing.json")).Restore();
        Assert.Equal(["stop:2", "stop:3", "show:1"], platform.Actions);
    }

    [Fact]
    public void FailedNativeRestore_KeepsEnabledStateForRetry()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "desktop-organizer.json");
        const string original = "{\"Enabled\":true}";
        File.WriteAllText(path, original);
        var platform = new Platform { ShowSucceeds = false };
        new DesktopRecovery(platform, path).Restore();
        Assert.Equal(original, File.ReadAllText(path));
    }

    [Fact]
    public void ExplorerUnavailable_KeepsEnabledStateForNextStartup()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "desktop-organizer.json");
        const string original = "{\"Enabled\":true}";
        File.WriteAllText(path, original);
        var platform = new Platform { Views = [] };
        new DesktopRecovery(platform, path).Restore();
        Assert.Empty(platform.Actions);
        Assert.Equal(original, File.ReadAllText(path));
    }

    [Fact]
    public void FailedHostNotification_StillShowsIconsAndKeepsMigrationPending()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "desktop-organizer.json");
        const string original = "{\"Enabled\":true}";
        File.WriteAllText(path, original);
        var platform = new Platform { Views = [new(1, [2])], StopSucceeds = false };
        new DesktopRecovery(platform, path).Restore();
        Assert.Equal(["stop:2", "show:1"], platform.Actions);
        Assert.Equal(original, File.ReadAllText(path));
    }

    [Fact]
    public void CorruptLegacyState_DoesNotChangeVisibility_ExplicitCommandStillRestores()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "desktop-organizer.json");
        File.WriteAllText(path, "broken json");
        var platform = new Platform();
        var recovery = new DesktopRecovery(platform, path);
        recovery.Restore();
        Assert.Empty(platform.Actions);
        recovery.Restore(force: true);
        Assert.Equal(["show:1"], platform.Actions);
        Assert.Equal("broken json", File.ReadAllText(path));
    }

    private sealed class Platform : IDesktopRecoveryPlatform
    {
        public IReadOnlyList<DesktopRecoveryView> Views { get; init; } = [new(1, [])];
        public bool ShowSucceeds { get; init; } = true;
        public bool StopSucceeds { get; init; } = true;
        public List<string> Actions { get; } = [];
        public IReadOnlyList<DesktopRecoveryView> FindDesktopViews() => Views;
        public bool RequestLegacyHostStop(nint host) { Actions.Add($"stop:{host}"); return StopSucceeds; }
        public bool ShowNativeIcons(nint list) { Actions.Add($"show:{list}"); return ShowSucceeds; }
    }
}
