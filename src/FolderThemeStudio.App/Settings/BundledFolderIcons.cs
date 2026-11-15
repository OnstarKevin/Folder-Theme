using System.IO;
using System.Security.Cryptography;

namespace FolderThemeStudio.App.Settings;

public sealed record BundledFolderIcon(string Id, string Name)
{
    public string PreviewUri => $"pack://application:,,,/FolderThemeStudio.App;component/Assets/BundledIcons/{Id}.png";
}
public static class BundledFolderIcons
{
    public static IReadOnlyList<BundledFolderIcon> All { get; } = Array.AsReadOnly(new[] {
        new BundledFolderIcon("design", "设计紫"), new("music", "唱片蓝"), new("downloads", "下载青"), new("cloud", "云端蓝"), new("photos", "相册橙") });
    public static string Materialize(string id, string? directory = null)
    {
        if (!All.Any(icon => icon.Id == id)) throw new ArgumentException("未知内置图标。");
        using var input = typeof(BundledFolderIcons).Assembly.GetManifestResourceStream("fts.icon." + id) ?? throw new IOException("内置图标缺失。");
        using var bytes = new MemoryStream(); input.CopyTo(bytes); var content = bytes.ToArray();
        var hash = Convert.ToHexString(SHA256.HashData(content));
        var root = directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FolderThemeStudio", "bundled-icons");
        Directory.CreateDirectory(root);
        if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0) throw new IOException("图标目录不能是链接。");
        var path = Path.Combine(root, hash + ".ico");
        if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("图标文件不能是链接。");
        if (!File.Exists(path) || !SHA256.HashData(File.ReadAllBytes(path)).SequenceEqual(SHA256.HashData(content)))
        {
            var temporary = Path.Combine(root, Guid.NewGuid().ToString("N") + ".tmp");
            try { File.WriteAllBytes(temporary, content); File.Move(temporary, path, true); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        return path;
    }
}
