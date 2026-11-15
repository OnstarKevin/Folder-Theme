using System.IO;
using System.Security.Cryptography;
using System.Windows.Markup;
using System.Windows.Media;
using FontFamily = System.Windows.Media.FontFamily;

namespace FolderThemeStudio.App.DynamicIsland;

public sealed record ImportedIslandFont(string FilePath, string FamilyName, IReadOnlyList<string> Families);

/// <summary>Private, local font copies. No font installation or system font redistribution.</summary>
public sealed class IslandFontStore
{
    public const string DefaultFamily = "Microsoft YaHei UI, Microsoft YaHei";
    private const long MaximumBytes = 32L * 1024 * 1024;
    private readonly string root;

    public IslandFontStore(string? root = null)
    {
        this.root = Path.GetFullPath(root ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FolderThemeStudio", "fonts"));
    }

    public ImportedIslandFont Import(string source)
    {
        var extension = Path.GetExtension(source).ToLowerInvariant();
        if (!IsExtension(extension)) throw new InvalidDataException("请选择 TTF、OTF 或 TTC 字体文件。");
        // Keep the source immutable until both hashing and copying finish.
        using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
        ValidateHeader(input);
        Directory.CreateDirectory(root);
        RejectReparse(root);
        var stage = Path.Combine(root, ".import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        string? published = null;
        var complete = false;
        try
        {
            var stagedFile = Path.Combine(stage, "font" + extension);
            string digest;
            using (var output = new FileStream(stagedFile, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                var buffer = new byte[81920];
                int count;
                long copied = 0;
                while ((count = input.Read(buffer, 0, buffer.Length)) != 0)
                {
                    copied += count;
                    if (copied > MaximumBytes) throw new InvalidDataException("字体文件不能超过 32 MB。");
                    hash.AppendData(buffer, 0, count);
                    output.Write(buffer, 0, count);
                }
                output.Flush(true);
                digest = Convert.ToHexString(hash.GetHashAndReset());
            }
            var directory = Path.Combine(root, digest);
            var destination = Path.Combine(directory, "font" + extension);
            if (Directory.Exists(directory))
            {
                // Never trust an existing file solely because its directory has the right name.
                RejectReparse(directory);
                var healthy = false;
                if (File.Exists(destination))
                {
                    RejectReparse(destination);
                    try { healthy = MatchesHash(destination, digest); }
                    catch (InvalidDataException) { /* A damaged header can be repaired below. */ }
                }
                // Only replace this exact private file with the freshly hashed copy.
                if (!healthy) File.Move(stagedFile, destination, true);
            }
            else
            {
                // WPF retains directory handles after decoding. Publish the completed
                // copy first so no subsequent directory rename depends on those handles.
                Directory.Move(stage, directory);
                published = directory;
            }
            var families = ReadFamilies(destination);
            complete = true;
            return new(destination, families[0], families);
        }
        finally
        {
            if (Directory.Exists(stage)) Directory.Delete(stage, true);
            if (!complete && published is not null && Directory.Exists(published)) Directory.Delete(published, true);
        }
    }

    public FontFamily Resolve(string? filePath, string? familyName)
    {
        if (string.IsNullOrWhiteSpace(filePath) || string.IsNullOrWhiteSpace(familyName)) return Default();
        if (!GetFamilies(filePath).Contains(familyName, StringComparer.Ordinal)) return Default();
        return new FontFamily(DirectoryUri(Path.GetDirectoryName(Path.GetFullPath(filePath))!), "./#" + familyName);
    }

    /// <summary>Restore every family in a validated private font collection.</summary>
    public IReadOnlyList<string> GetFamilies(string? filePath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(filePath)) return Array.Empty<string>();
            var path = Path.GetFullPath(filePath);
            var directory = Path.GetDirectoryName(path)!;
            var digest = Path.GetFileName(directory);
            if (!string.Equals(Path.GetDirectoryName(directory), root, StringComparison.OrdinalIgnoreCase)
                || digest.Length != 64 || !digest.All(Uri.IsHexDigit)
                || !IsExtension(Path.GetExtension(path).ToLowerInvariant())
                || !string.Equals(Path.GetFileNameWithoutExtension(path), "font", StringComparison.OrdinalIgnoreCase)) return Array.Empty<string>();
            RejectReparse(root);
            RejectReparse(directory);
            if (!File.Exists(path) || !MatchesHash(path, digest)) return Array.Empty<string>();
            return ReadFamilies(path);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or FormatException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            return Array.Empty<string>();
        }
    }

    private static FontFamily Default() => new(DefaultFamily);
    private static bool IsExtension(string extension) => extension is ".ttf" or ".otf" or ".ttc";
    private static Uri DirectoryUri(string directory) => new(Path.TrimEndingDirectorySeparator(directory) + Path.DirectorySeparatorChar);

    private static void ValidateHeader(Stream input)
    {
        if (input.Length < 12 || input.Length > MaximumBytes) throw new InvalidDataException("字体文件无效或超过 32 MB。");
        Span<byte> header = stackalloc byte[4];
        input.ReadExactly(header);
        input.Position = 0;
        if (!(header.SequenceEqual(new byte[] { 0, 1, 0, 0 }) || header.SequenceEqual("OTTO"u8)
            || header.SequenceEqual("ttcf"u8) || header.SequenceEqual("true"u8)))
            throw new InvalidDataException("文件不是有效的字体。");
    }

    private static void RejectReparse(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("字体存储路径不能使用链接。");
    }

    private static bool MatchesHash(string file, string digest)
    {
        RejectReparse(file);
        using var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
        ValidateHeader(input);
        return string.Equals(Convert.ToHexString(SHA256.HashData(input)), digest, StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> ReadFamilies(string file)
    {
        var names = new List<string>();
        foreach (var family in Fonts.GetFontFamilies(DirectoryUri(Path.GetDirectoryName(file)!)))
        {
            // Force actual typeface decoding, rather than accepting a fallback family.
            if (!family.GetTypefaces().Any(face => face.TryGetGlyphTypeface(out var glyph)
                && string.Equals(glyph.FontUri.LocalPath, file, StringComparison.OrdinalIgnoreCase))) continue;
            var name = family.FamilyNames.TryGetValue(XmlLanguage.GetLanguage("en-us"), out var english)
                ? english : family.FamilyNames.Values.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(name)) names.Add(name);
        }
        if (names.Count == 0) throw new InvalidDataException("无法读取字体中的字族。");
        return names.Distinct(StringComparer.Ordinal).ToArray();
    }
}
