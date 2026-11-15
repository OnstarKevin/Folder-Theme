using System.IO;

namespace FolderThemeStudio.App.Services;

public sealed record RecentFolderFile(string Name, string Path, DateTime LastWriteTimeUtc);
public sealed record FolderDropTarget(string Name, string Path);
public sealed record FolderLiveContent(IReadOnlyList<RecentFolderFile> RecentFiles, IReadOnlyList<FolderDropTarget> DropTargets,
    FolderInfoSummary? Info = null);
public sealed record FolderDropResult(int MovedCount, IReadOnlyList<string> Errors);

public sealed class FolderLiveContentService
{
    public FolderLiveContent Read(string folderPath)
    {
        var root = Path.GetFullPath(folderPath);
        if (!Directory.Exists(root)) return new([], []);
        try
        {
            var directory = new DirectoryInfo(root);
            var files = directory.EnumerateFiles()
                .Where(file => (file.Attributes & (FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint)) == 0)
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Take(5)
                .Select(file => new RecentFolderFile(file.Name, file.FullName, file.LastWriteTimeUtc))
                .ToArray();
            var children = directory.EnumerateDirectories()
                .Where(child => (child.Attributes & (FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint)) == 0)
                .OrderBy(child => child.Name, StringComparer.CurrentCultureIgnoreCase)
                .Take(8)
                .Select(child => new FolderDropTarget(child.Name, child.FullName));
            var targets = new[] { new FolderDropTarget("此文件夹", root) }.Concat(children).ToArray();
            return new(files, targets, new FolderInfoSummaryService().Read(root));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return new([], [new FolderDropTarget("此文件夹", root)]);
        }
    }
}

public sealed class FolderDropService
{
    public FolderDropResult Move(IReadOnlyList<string> sourcePaths, string rootPath, string destinationPath)
    {
        ArgumentNullException.ThrowIfNull(sourcePaths);
        var errors = new List<string>();
        int moved = 0;
        string root;
        string destination;
        try
        {
            root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
            destination = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destinationPath));
            var isRoot = string.Equals(root, destination, StringComparison.OrdinalIgnoreCase);
            var isDirectChild = string.Equals(Path.GetDirectoryName(destination), root, StringComparison.OrdinalIgnoreCase);
            if ((!isRoot && !isDirectChild) || !Directory.Exists(root) || !Directory.Exists(destination) ||
                (new DirectoryInfo(root).Attributes & FileAttributes.ReparsePoint) != 0 ||
                (new DirectoryInfo(destination).Attributes & FileAttributes.ReparsePoint) != 0)
                return new(0, ["目标必须是当前文件夹或其直属子文件夹。"]);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return new(0, [exception.Message]);
        }

        foreach (var sourcePath in sourcePaths)
        {
            try
            {
                var source = Path.GetFullPath(sourcePath);
                if (!File.Exists(source) || (new FileInfo(source).Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    errors.Add($"仅支持移动普通文件：{Path.GetFileName(source)}");
                    continue;
                }
                var target = Path.Combine(destination, Path.GetFileName(source));
                if (File.Exists(target) || Directory.Exists(target))
                {
                    errors.Add($"目标已有同名项目：{Path.GetFileName(source)}");
                    continue;
                }
                File.Move(source, target);
                moved++;
            }
            catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException or System.Security.SecurityException)
            {
                errors.Add($"{Path.GetFileName(sourcePath)}：{exception.Message}");
            }
        }
        return new(moved, errors);
    }
}
