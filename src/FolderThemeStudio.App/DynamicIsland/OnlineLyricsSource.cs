using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FolderThemeStudio.App.DynamicIsland;

public sealed record LyricsResult(IReadOnlyList<LyricLine> Lines, string Provider);

/// <summary>
/// 根据 NotchPeninsula 的公开接口路线独立实现，使用系统 TLS 校验。
/// 每一来源独立超时，响应限制 2 MiB；只有匹配歌名/歌手的曲目才取词。
/// 不执行响应内容、不下载封面或音频，也不伪造请求者 IP。
/// </summary>
public sealed class OnlineLyricsSource
{
    private static readonly HttpClient Shared = new() { Timeout = Timeout.InfiniteTimeSpan };
    private readonly HttpClient http;
    public OnlineLyricsSource(HttpClient? http = null) => this.http = http ?? Shared;

    public async Task<LyricsResult> FetchAsync(IslandMedia song, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(song.Title) || string.IsNullOrWhiteSpace(song.Artist)) return new([], "缺少歌名或歌手");
        var neteaseFirst = song.SourceApp.Contains("netease", StringComparison.OrdinalIgnoreCase) || song.SourceApp.Contains("cloudmusic", StringComparison.OrdinalIgnoreCase);
        var providers = neteaseFirst ? new[] { "netease", "tencent", "official", "lrclib" } : new[] { "tencent", "netease", "official", "lrclib" };
        foreach (var provider in providers)
        {
            token.ThrowIfCancellationRequested();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            try
            {
                var result = provider switch
                {
                    "lrclib" => await LrcLibAsync(song, timeout.Token),
                    "official" => await NeteaseAsync(song, timeout.Token),
                    _ => await LuoYueAsync(song, provider, timeout.Token),
                };
                if (result.Lines.Count > 0) return result;
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { }
            catch (Exception e) when (e is HttpRequestException or System.IO.IOException or JsonException or InvalidOperationException or FormatException) { }
        }
        return new([], "未找到同步歌词或服务暂不可用");
    }

    private async Task<LyricsResult> LuoYueAsync(IslandMedia song, string platform, CancellationToken token)
    {
        var query = Uri.EscapeDataString($"{song.Title} {song.Artist}");
        var path = platform == "tencent" ? "tencent/search/song" : "netease";
        using var search = await GetAsync($"https://api.vkeys.cn/v2/music/{path}?word={query}", token);
        var list = Property(search.RootElement, "data");
        if (list.ValueKind != JsonValueKind.Array) return new([], "");
        foreach (var candidate in list.EnumerateArray().Take(60))
        {
            if (!Matches(song, Text(candidate, "song"), Text(candidate, "singer"))) continue;
            // 回退来源也要检验已知时长，不能让同名演唱会/剪辑版本先命中。
            var interval = Property(candidate, "interval");
            var duration = Property(candidate, "duration");
            var candidateSeconds = interval.TryNumber(out var seconds) ? seconds : duration.TryNumber(out var durationValue) ? durationValue : 0;
            if (candidateSeconds > 0 && song.Duration > TimeSpan.Zero && Math.Abs(candidateSeconds - song.Duration.TotalSeconds) > 4) continue;
            var id = Property(candidate, "id");
            var identifier = id.ValueKind == JsonValueKind.Number ? id.GetRawText() : id.ValueKind == JsonValueKind.String ? id.GetString() : null;
            if (string.IsNullOrEmpty(identifier)) continue;
            using var lyric = await GetAsync($"https://api.vkeys.cn/v2/music/{platform}/lyric?id={Uri.EscapeDataString(identifier)}", token);
            var data = Property(lyric.RootElement, "data");
            var lrc = Text(data, "lrc");
            if (lrc.Length == 0) lrc = Text(data, "rc");
            var lines = Parse(lrc, Text(data, "trans"));
            if (lines.Count > 0) return new(lines, platform == "tencent" ? "落月 / QQ" : "落月 / 网易云");
        }
        return new([], "");
    }

    private async Task<LyricsResult> NeteaseAsync(IslandMedia song, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://music.163.com/api/search/get/web")
        { Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["s"] = $"{song.Title} {song.Artist}", ["type"] = "1", ["limit"] = "20", ["offset"] = "0" }) };
        request.Headers.Referrer = new("https://music.163.com/");
        using var search = await JsonAsync(request, token);
        var candidates = Property(Property(search.RootElement, "result"), "songs");
        if (candidates.ValueKind != JsonValueKind.Array) return new([], "");
        foreach (var candidate in candidates.EnumerateArray().Take(20))
        {
            var artists = Property(candidate, "artists");
            var artist = artists.ValueKind == JsonValueKind.Array ? string.Join("/", artists.EnumerateArray().Select(item => Text(item, "name"))) : "";
            if (!Matches(song, Text(candidate, "name"), artist)) continue;
            var duration = Property(candidate, "duration");
            if (duration.TryNumber(out var ms) && song.Duration > TimeSpan.Zero && Math.Abs(ms / 1000 - song.Duration.TotalSeconds) > 4) continue;
            var id = Property(candidate, "id");
            if (id.ValueKind != JsonValueKind.Number || !id.TryGetInt64(out var songId)) continue;
            using var lyric = await GetAsync($"https://music.163.com/api/song/lyric?id={songId}&lv=-1&tv=-1", token);
            var lines = Parse(Text(Property(lyric.RootElement, "lrc"), "lyric"), Text(Property(lyric.RootElement, "tlyric"), "lyric"));
            if (lines.Count > 0) return new(lines, "网易云");
        }
        return new([], "");
    }

    private async Task<LyricsResult> LrcLibAsync(IslandMedia song, CancellationToken token)
    {
        var query = $"https://lrclib.net/api/get?track_name={Uri.EscapeDataString(song.Title)}&artist_name={Uri.EscapeDataString(song.Artist)}";
        if (song.Duration > TimeSpan.Zero) query += "&duration=" + song.Duration.TotalSeconds.ToString("0", CultureInfo.InvariantCulture);
        using var data = await GetAsync(query, token);
        if (!Matches(song, Text(data.RootElement, "trackName"), Text(data.RootElement, "artistName"))) return new([], "");
        var duration = Property(data.RootElement, "duration");
        if (duration.TryNumber(out var seconds) && song.Duration > TimeSpan.Zero && Math.Abs(seconds - song.Duration.TotalSeconds) > 4) return new([], "");
        return new(Parse(Text(data.RootElement, "syncedLyrics"), ""), "LRCLIB");
    }

    private Task<JsonDocument> GetAsync(string uri, CancellationToken token) => JsonAsync(new(HttpMethod.Get, uri), token, ownsRequest: true);
    private async Task<JsonDocument> JsonAsync(HttpRequestMessage request, CancellationToken token, bool ownsRequest = false)
    {
        try
        {
            request.Headers.UserAgent.ParseAdd("FolderThemeStudio/0.1 (Windows; lyrics)");
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > 2 * 1024 * 1024) throw new HttpRequestException("Lyrics response too large");
            await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            using var memory = new System.IO.MemoryStream();
            var buffer = new byte[8192];
            int count;
            while ((count = await stream.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
            {
                if (memory.Length + count > 2 * 1024 * 1024) throw new HttpRequestException("Lyrics response too large");
                memory.Write(buffer, 0, count);
            }
            return JsonDocument.Parse(memory.ToArray());
        }
        finally { if (ownsRequest) request.Dispose(); }
    }
    private static IReadOnlyList<LyricLine> Parse(string lrc, string translation)
    {
        var lines = LrcLyricsSource.Parse(WebUtility.HtmlDecode(lrc));
        var translated = LrcLyricsSource.Parse(WebUtility.HtmlDecode(translation)).GroupBy(line => line.At).ToDictionary(group => group.Key, group => group.First().Text);
        return lines.Select(line => translated.TryGetValue(line.At, out var text) ? line with { Translation = text } : line).ToArray();
    }
    private static bool Matches(IslandMedia song, string title, string artist)
    {
        static string Normalize(string value) => Regex.Replace(value.Normalize(NormalizationForm.FormKC), @"[\s\p{P}\p{S}]", "").ToUpperInvariant();
        if (Normalize(title).Length == 0 || Normalize(title) != Normalize(song.Title)) return false;
        var candidates = Regex.Split(artist, @"[/,&、;]").Select(Normalize).Where(value => value.Length > 0).ToArray();
        var requested = Regex.Split(song.Artist, @"[/,&、;]").Select(Normalize).Where(value => value.Length > 0).ToArray();
        return requested.Length > 0 && requested.All(candidates.Contains);
    }
    private static JsonElement Property(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var property) ? property : default;
    private static string Text(JsonElement value, string name) => Property(value, name) is { ValueKind: JsonValueKind.String } property ? property.GetString() ?? "" : "";
}

internal static class JsonNumber
{
    internal static bool TryNumber(this JsonElement value, out double number) { number = 0; return value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out number) && double.IsFinite(number); }
}
