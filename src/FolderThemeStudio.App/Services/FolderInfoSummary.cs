using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace FolderThemeStudio.App.Services;

public sealed record FolderInfoSummary(DateTime CreatedUtc, DateTime ModifiedUtc,
    int FileCount, int FolderCount, long SizeBytes, bool ContentsPartial, bool SizePartial)
{
    public static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var size = (double)bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1) { size /= 1024; unit++; }
        return $"{size.ToString(unit == 0 ? "0" : "0.#", CultureInfo.CurrentUICulture)} {units[unit]}";
    }
}

public sealed class FolderInfoSummaryService(int maxEntries = 4096, TimeSpan? timeBudget = null)
{
    public FolderInfoSummary? Read(string folderPath, CancellationToken token = default)
    {
        var root = new DirectoryInfo(folderPath);
        if (!root.Exists) return null;
        var created = root.CreationTimeUtc;
        var modified = root.LastWriteTimeUtc;
        if ((root.Attributes & FileAttributes.ReparsePoint) != 0)
            return new(created, modified, 0, 0, 0, true, true);

        var timer = Stopwatch.StartNew();
        var pending = new Queue<DirectoryInfo>();
        pending.Enqueue(root);
        int files = 0, folders = 0, visited = 0;
        long size = 0;
        bool contentsPartial = false, sizePartial = false;
        while (pending.TryDequeue(out var directory))
        {
            var isRoot = string.Equals(directory.FullName, root.FullName, StringComparison.OrdinalIgnoreCase);
            try
            {
                foreach (var item in directory.EnumerateFileSystemInfos())
                {
                    token.ThrowIfCancellationRequested();
                    if (visited++ >= maxEntries || timer.Elapsed >= (timeBudget ?? TimeSpan.FromMilliseconds(200)))
                        return new(created, modified, files, folders, size, contentsPartial || isRoot, true);
                    if (isRoot) { if (item is DirectoryInfo) folders++; else files++; }
                    if ((item.Attributes & FileAttributes.ReparsePoint) != 0) { sizePartial = true; continue; }
                    if (item is DirectoryInfo child) pending.Enqueue(child);
                    else if (item is FileInfo file) size += file.Length;
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                contentsPartial |= isRoot;
                sizePartial = true;
            }
        }
        return new(created, modified, files, folders, size, contentsPartial, sizePartial);
    }
}
