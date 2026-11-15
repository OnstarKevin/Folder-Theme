using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace FolderThemeStudio.App.DynamicIsland;

public sealed record LyricLine(TimeSpan At, string Text, string Translation = "");
public interface IIslandLyricsSource { Task<IReadOnlyList<LyricLine>> ReadAsync(string title, string artist, string directory, CancellationToken token); }

/// <summary>歌曲名.lrc 或 歌手 - 歌曲名.lrc。系统媒体 API 不提供歌词，此接口也可替换为合法的本地 HTTP 数据源。</summary>
public sealed class LrcLyricsSource : IIslandLyricsSource
{
    private static readonly Regex Timestamp = new(@"\[(\d+):(\d{2})(?:[.:](\d{1,3}))?\]", RegexOptions.CultureInvariant);
    private static readonly Regex Offset = new(@"\[offset:([+-]?\d+)\]", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    public async Task<IReadOnlyList<LyricLine>> ReadAsync(string title, string artist, string directory, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(directory)) return [];
        var names = new[] { SafeName($"{artist} - {title}") + ".lrc", SafeName(title) + ".lrc" };
        foreach (var name in names)
        {
            var path = Path.Combine(directory, name);
            if (!File.Exists(path)) continue;
            if (new FileInfo(path).Length > 2 * 1024 * 1024) return []; // 不读取任意大小的文件
            return Parse(await File.ReadAllTextAsync(path, token));
        }
        return [];
    }
    private static string SafeName(string name) => string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim();

    public static IReadOnlyList<LyricLine> Parse(string text)
    {
        var result = new List<LyricLine>();
        var offsetMatch = Offset.Match(text);
        var offset = offsetMatch.Success && int.TryParse(offsetMatch.Groups[1].Value, out var value) ? value : 0;
        foreach (var raw in text.Split('\n'))
        {
            var matches = Timestamp.Matches(raw);
            if (matches.Count == 0) continue;
            var words = Timestamp.Replace(raw, string.Empty).Trim();
            if (words.Length == 0) continue;
            foreach (Match match in matches)
            {
                if (!int.TryParse(match.Groups[1].Value, out var minutes) || !int.TryParse(match.Groups[2].Value, out var seconds) || seconds >= 60) continue;
                var fraction = match.Groups[3].Value;
                var ms = fraction.Length == 0 ? 0 : int.Parse(fraction.PadRight(3, '0'), CultureInfo.InvariantCulture);
                var at = TimeSpan.FromMilliseconds(Math.Max(0, minutes * 60000d + seconds * 1000d + ms + offset));
                result.Add(new(at, words));
            }
        }
        return result.OrderBy(line => line.At).ToArray();
    }
    public static string CurrentLine(IReadOnlyList<LyricLine> lines, TimeSpan position) =>
        lines.LastOrDefault(line => line.At <= position)?.Text ?? string.Empty;
}
