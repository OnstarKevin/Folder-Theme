using System.IO;
using FolderThemeStudio.App.DynamicIsland;
using Xunit;

namespace FolderThemeStudio.Core.Tests.Application;

public sealed class IslandFontStoreTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "island-font-tests-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData("segoeui.ttf")]
    [InlineData("msyh.ttc")]
    public void ImportedFontSurvivesSourceDeletionAndRestart(string name)
    {
        var systemFont = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), name);
        if (!File.Exists(systemFont)) return;
        Directory.CreateDirectory(root);
        var source = Path.Combine(root, name);
        File.Copy(systemFont, source);
        var store = new IslandFontStore(Path.Combine(root, "managed"));
        var imported = store.Import(source);
        File.Delete(source);
        Assert.True(File.Exists(imported.FilePath));
        Assert.NotEmpty(imported.Families);
        Assert.Contains(imported.FamilyName, imported.Families);
        var resolved = new IslandFontStore(Path.Combine(root, "managed")).Resolve(imported.FilePath, imported.FamilyName);
        Assert.NotEqual(IslandFontStore.DefaultFamily, resolved.Source);
        Assert.Contains(resolved.GetTypefaces(), face => face.TryGetGlyphTypeface(out var glyph)
            && string.Equals(imported.FilePath, glyph.FontUri.LocalPath, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RepeatedImportDeduplicatesAndRejectsReplacedManagedFile()
    {
        var source = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "segoeui.ttf");
        var store = new IslandFontStore(root);
        var first = store.Import(source);
        Assert.Equal(first.FilePath, store.Import(source).FilePath);
        Assert.Single(Directory.GetDirectories(root));
        File.Delete(first.FilePath);
        Assert.Equal(IslandFontStore.DefaultFamily, store.Resolve(first.FilePath, first.FamilyName).Source);
        File.WriteAllText(first.FilePath, "corrupt replacement");
        Assert.Equal(IslandFontStore.DefaultFamily, store.Resolve(first.FilePath, first.FamilyName).Source);
    }

    [Fact]
    public void MissingUnmanagedAndUnknownFamilyFallBackToSystemYaHei()
    {
        var store = new IslandFontStore(root);
        Assert.Equal(IslandFontStore.DefaultFamily, store.Resolve(null, null).Source);
        Assert.Equal(IslandFontStore.DefaultFamily, store.Resolve(Path.Combine(root, "missing.ttf"), "Missing").Source);
        var source = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "segoeui.ttf");
        Assert.Equal(IslandFontStore.DefaultFamily, store.Resolve(source, "Segoe UI").Source);
        var imported = store.Import(source);
        Assert.Equal(IslandFontStore.DefaultFamily, store.Resolve(imported.FilePath, "Unknown family").Source);
    }
    [Fact]
    public void ReimportRepairsMissingAndCorruptPrivateCopies()
    {
        var source = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "segoeui.ttf");
        var store = new IslandFontStore(root); var first = store.Import(source);
        File.Delete(first.FilePath);
        Assert.Equal(first.FilePath, store.Import(source).FilePath);
        File.Delete(first.FilePath); File.WriteAllText(first.FilePath, "corrupt");
        var repaired = store.Import(source);
        Assert.Equal(first.FilePath, repaired.FilePath);
        Assert.NotEqual(IslandFontStore.DefaultFamily, store.Resolve(repaired.FilePath, repaired.FamilyName).Source);
    }
    [Fact]
    public void ReopeningCollectionRestoresAllAvailableFamilies()
    {
        var source = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "msyh.ttc");
        var imported = new IslandFontStore(root).Import(source);
        var loaded = new IslandFontStore(root).GetFamilies(imported.FilePath);
        Assert.Equal(imported.Families, loaded);
        Assert.NotEmpty(loaded);
    }

    [Theory]
    [InlineData("invalid.ttf", false)]
    [InlineData("invalid.otf", false)]
    [InlineData("invalid.exe", false)]
    [InlineData("huge.ttf", true)]
    public void InvalidImportsLeaveNoManagedDataOrOpenHandles(string name, bool oversized)
    {
        Directory.CreateDirectory(root);
        var source = Path.Combine(root, name);
        using (var file = File.Create(source))
        {
            file.Write(new byte[] { 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 });
            if (oversized) file.SetLength(32L * 1024 * 1024 + 1);
        }
        var managed = Path.Combine(root, "managed");
        Assert.ThrowsAny<Exception>(() => new IslandFontStore(managed).Import(source));
        File.Delete(source);
        Assert.True(!Directory.Exists(managed) || !Directory.EnumerateFileSystemEntries(managed).Any());
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
