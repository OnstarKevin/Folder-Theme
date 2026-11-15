using System.IO;
using System.Security.Cryptography;
using System.Windows.Media.Imaging;

namespace FolderThemeStudio.App.DynamicIsland;

/// <summary>Local, bounded image copies. Never install resources or retain a lock on the user's source.</summary>
public sealed class IslandBackgroundStore
{
    private readonly string root;
    public IslandBackgroundStore(string? root = null) => this.root = Path.GetFullPath(root ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FolderThemeStudio", "island-backgrounds"));

    public string Import(string source, string? savedBackground = null)
    {
        using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (input.Length is < 12 or > 16 * 1024 * 1024) throw new InvalidDataException("请选择16 MiB以内的PNG或JPEG图片。");
        // Read metadata before decoding. The actual decoder is explicitly limited to1600px.
        var decoder = BitmapDecoder.Create(input, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
        var frame = decoder.Frames[0];
        var width = frame.PixelWidth; var height = frame.PixelHeight;
        if (width < 1 || height < 1 || (long)width * height > 100_000_000 || Math.Max(width, height) > 32768)
            throw new InvalidDataException("图片像素过大或无效。");
        input.Position = 0;
        var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = input;
        if (width >= height) image.DecodePixelWidth = Math.Min(width, 1600);
        else image.DecodePixelHeight = Math.Min(height, 1600);
        image.EndInit(); image.Freeze();
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var bytes = new MemoryStream(); encoder.Save(bytes);
        var content = bytes.ToArray();
        var hash = Convert.ToHexString(SHA256.HashData(content));
        Directory.CreateDirectory(root); RejectLink(root);
        var destination = Path.Combine(root, hash + ".png");
        if (File.Exists(destination)) RejectLink(destination);
        var temporary = Path.Combine(root, ".import-" + Guid.NewGuid().ToString("N"));
        try { File.WriteAllBytes(temporary, content); File.Move(temporary, destination, true); TrimCache(destination, savedBackground); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return destination;
    }

    private void TrimCache(string latest, string? saved)
    {
        // Keep the persisted image and newest draft; remove old previews first.
        var files = Directory.EnumerateFiles(root, "*.png").Select(p => new FileInfo(p))
            .Where(f => f.Name.Length == 68 && f.Name[..64].All(Uri.IsHexDigit) && (f.Attributes & FileAttributes.ReparsePoint) == 0)
            .OrderBy(f => f.LastWriteTimeUtc).ToList();
        var bytes = files.Sum(f => f.Length); var count = files.Count;
        foreach (var file in files)
        {
            if (bytes <= 64L * 1024 * 1024 && count <= 12) break;
            if (string.Equals(file.FullName, latest, StringComparison.OrdinalIgnoreCase) || string.Equals(file.FullName, saved, StringComparison.OrdinalIgnoreCase)) continue;
            var length = file.Length;
            file.Delete(); bytes -= length; count--;
        }
        if (bytes > 64L * 1024 * 1024 || count > 12) throw new IOException("背景缓存暂时无法清理，请稍后再导入。");
    }

    public BitmapSource? Resolve(string? file)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(file)) return null;
            var path = Path.GetFullPath(file);
            var name = Path.GetFileNameWithoutExtension(path);
            if (!string.Equals(Path.GetDirectoryName(path), root, StringComparison.OrdinalIgnoreCase)
                || name.Length != 64 || !name.All(Uri.IsHexDigit) || Path.GetExtension(path) != ".png") return null;
            RejectLink(root); RejectLink(path);
            using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (input.Length > 16 * 1024 * 1024 || !Convert.ToHexString(SHA256.HashData(input)).Equals(name, StringComparison.OrdinalIgnoreCase)) return null;
            input.Position = 0;
            var frame = BitmapDecoder.Create(input, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];
            if (frame.PixelWidth is < 1 or > 1600 || frame.PixelHeight is < 1 or > 1600) return null;
            input.Position = 0;
            var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = input; image.EndInit(); image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or NotSupportedException or FormatException or System.Security.SecurityException or System.Runtime.InteropServices.COMException)
        { return null; }
    }

    private static void RejectLink(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("图片缓存不能使用链接。");
    }
}
