using System.IO;
using FolderThemeStudio.App.Services;
using FolderThemeStudio.Core.Tests.TestSupport;
using Xunit;

namespace FolderThemeStudio.Core.Tests.Application;

public sealed class FolderLiveContentServiceTests
{
    [Fact]
    public void Info_IncludesDatesDirectCountsAndNestedSize()
    {
        using var temp = new TemporaryDirectory();
        File.WriteAllBytes(Path.Combine(temp.Path, "root.bin"), new byte[20]);
        var child = Directory.CreateDirectory(Path.Combine(temp.Path, "Child"));
        File.WriteAllBytes(Path.Combine(child.FullName, "nested.bin"), new byte[30]);
        var info = Assert.IsType<FolderInfoSummary>(new FolderLiveContentService().Read(temp.Path).Info);
        Assert.Equal(1, info.FileCount);
        Assert.Equal(1, info.FolderCount);
        Assert.Equal(50, info.SizeBytes);
        Assert.False(info.SizePartial);
        Assert.False(info.ContentsPartial);
        Assert.Equal(Directory.GetCreationTimeUtc(temp.Path), info.CreatedUtc);
        Assert.Equal(Directory.GetLastWriteTimeUtc(temp.Path), info.ModifiedUtc);
    }

    [Fact]
    public void Info_LimitsTraversalAndLabelsIncompleteStatistics()
    {
        using var temp = new TemporaryDirectory();
        for (var i = 0; i < 5; i++) File.WriteAllBytes(Path.Combine(temp.Path, $"{i}.bin"), new byte[10]);
        var info = Assert.IsType<FolderInfoSummary>(new FolderInfoSummaryService(maxEntries: 2, timeBudget: TimeSpan.FromSeconds(10)).Read(temp.Path));
        Assert.Equal(20, info.SizeBytes);
        Assert.Equal(2, info.FileCount);
        Assert.True(info.SizePartial);
        Assert.True(info.ContentsPartial);
        Assert.Null(new FolderInfoSummaryService().Read(Path.Combine(temp.Path, "missing")));
    }

    [Fact]
    public void Info_LabelsEmptyFolderAsExactlyZeroBytes()
    {
        using var temp = new TemporaryDirectory();
        var info = Assert.IsType<FolderInfoSummary>(new FolderInfoSummaryService().Read(temp.Path));
        Assert.Equal("0 B", FolderInfoSummary.FormatSize(info.SizeBytes));
        Assert.False(info.SizePartial);
    }

    [Fact]
    public void Read_ShowsNewestDirectFilesAndChildFolders()
    {
        using var temp = new TemporaryDirectory();
        var older = Path.Combine(temp.Path, "older.txt");
        var newer = Path.Combine(temp.Path, "newer.txt");
        File.WriteAllText(older, "older");
        File.WriteAllText(newer, "newer");
        File.SetLastWriteTimeUtc(older, DateTime.UtcNow.AddDays(-2));
        File.SetLastWriteTimeUtc(newer, DateTime.UtcNow.AddDays(-1));
        Directory.CreateDirectory(Path.Combine(temp.Path, "Child"));

        var content = new FolderLiveContentService().Read(temp.Path);

        Assert.Equal(["newer.txt", "older.txt"], content.RecentFiles.Select(file => file.Name));
        Assert.Contains(content.DropTargets, target => target.Name == "Child" && target.Path == Path.Combine(temp.Path, "Child"));
        Assert.Equal(temp.Path, content.DropTargets[0].Path);
    }

    [Fact]
    public void Read_DoesNotShowHiddenFiles()
    {
        using var temp = new TemporaryDirectory();
        var hidden = Path.Combine(temp.Path, "hidden.txt");
        File.WriteAllText(hidden, "secret");
        File.SetAttributes(hidden, FileAttributes.Hidden);

        var content = new FolderLiveContentService().Read(temp.Path);

        Assert.Empty(content.RecentFiles);
    }

    [Fact]
    public void Move_MovesFileIntoChosenDirectChildWithoutOverwriting()
    {
        using var temp = new TemporaryDirectory();
        var root = Path.Combine(temp.Path, "Target");
        var child = Directory.CreateDirectory(Path.Combine(root, "Child")).FullName;
        var source = Path.Combine(temp.Path, "report.txt");
        File.WriteAllText(source, "new");

        var result = new FolderDropService().Move([source], root, child);

        Assert.Equal(1, result.MovedCount);
        Assert.Empty(result.Errors);
        Assert.False(File.Exists(source));
        Assert.Equal("new", File.ReadAllText(Path.Combine(child, "report.txt")));

        File.WriteAllText(source, "later");
        var conflict = new FolderDropService().Move([source], root, child);
        Assert.Equal(0, conflict.MovedCount);
        Assert.NotEmpty(conflict.Errors);
        Assert.Equal("new", File.ReadAllText(Path.Combine(child, "report.txt")));
        Assert.Equal("later", File.ReadAllText(source));
    }

    [Fact]
    public void Move_RejectsDestinationOutsideRoot()
    {
        using var temp = new TemporaryDirectory();
        var root = Directory.CreateDirectory(Path.Combine(temp.Path, "Target")).FullName;
        var outside = Directory.CreateDirectory(Path.Combine(temp.Path, "Other")).FullName;
        var source = Path.Combine(temp.Path, "report.txt");
        File.WriteAllText(source, "keep");

        var result = new FolderDropService().Move([source], root, outside);

        Assert.Equal(0, result.MovedCount);
        Assert.NotEmpty(result.Errors);
        Assert.True(File.Exists(source));
    }
}
