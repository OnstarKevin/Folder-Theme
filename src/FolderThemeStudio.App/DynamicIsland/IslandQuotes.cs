using System.Net.Http;
using System.Text.Json;

namespace FolderThemeStudio.App.DynamicIsland;

public sealed record IslandQuote(string Text, string Source, string Author, string Id)
{
    public string Credit => string.Join(" · ", new[] { Author, Source }.Where(s => !string.IsNullOrWhiteSpace(s)));
}

public interface IIslandQuoteSource
{
    Task<IslandQuote?> ReadAsync(CancellationToken token);
}

/// <summary>仅发送固定的金句请求，不发送剪贴板、图片或其他用户数据。调用方负责缓存及限频。</summary>
public sealed class HitokotoQuoteSource : IIslandQuoteSource
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(6), MaxResponseContentBufferSize = 32 * 1024 };
    public async Task<IslandQuote?> ReadAsync(CancellationToken token)
    {
        var json = await Client.GetStringAsync("https://v1.hitokoto.cn/?encode=json&max_length=40", token);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return null;
        string Read(string key) => root.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
        var text = Read("hitokoto");
        if (string.IsNullOrWhiteSpace(text) || text.Length > 1024) return null;
        return new(ClipboardPreview.Create(text), Read("from"), Read("from_who"), Read("uuid"));
    }
}
