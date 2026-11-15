using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FolderThemeStudio.App.DynamicIsland;

/// <summary>
/// 可选的本地消息入口，绑定 IPv4 Loopback，不开放局域网、不需要 URL ACL 或管理员。
/// 只接收带 Bearer 令牌的短 JSON POST；拒绝浏览器 Origin，限长、限时、串行处理。
/// 消息仅保存在内存，不能替代未经授权的 Windows 全局通知监听。
/// </summary>
public sealed class LocalNotificationBridge : IDisposable
{
    private readonly object sync = new();
    private readonly CancellationTokenSource stop = new();
    private readonly TcpListener listener;
    private readonly string token;
    private IslandNotification? latest;
    private readonly Queue<IslandNotification> pending = new();
    public string Status { get; private set; } = "本地消息接口未启动";
    public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;
    public LocalNotificationBridge(int port, string token) { listener = new(IPAddress.Loopback, port); this.token = token; }
    public bool Start()
    {
        try { listener.Start(8); Status = $"本地消息接口已开启（127.0.0.1:{Port}）"; _ = Task.Run(ListenAsync); return true; }
        catch (SocketException) { Status = "本地消息端口不可用，请更换端口"; return false; }
    }
    public IslandNotification? ReadLatest() { lock (sync) return latest is { } message && message.CreatedAt > DateTimeOffset.UtcNow.AddSeconds(-90) ? message : null; }
    public IReadOnlyList<IslandNotification> ReadRecent()
    {
        // 保留有界快照供 UI 设置变更后的重试；成功显示去重由展示队列负责。
        lock (sync) return pending.Where(message => message.CreatedAt >= DateTimeOffset.UtcNow.AddSeconds(-90)).ToArray();
    }
    private async Task ListenAsync()
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                using var client = await listener.AcceptTcpClientAsync(stop.Token).ConfigureAwait(false);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(3));
                var stream = client.GetStream();
                await HandleAsync(stream, timeout.Token).ConfigureAwait(false);
                // 拒绝请求时可能尚有正文。先半关闭发送，再有界排空，避免 Windows 以 RST 覆盖已写出的 HTTP 状态。
                client.Client.Shutdown(SocketShutdown.Send);
                var discard = new byte[1024];
                for (var drained = 0; drained < 16384;)
                {
                    var count = await stream.ReadAsync(discard, timeout.Token).ConfigureAwait(false);
                    if (count == 0) break;
                    drained += count;
                }
            }
            catch (Exception e) when (e is OperationCanceledException or SocketException or IOException or ObjectDisposedException) { }
        }
    }
    private async Task HandleAsync(NetworkStream stream, CancellationToken cancellation)
    {
        var header = new List<byte>();
        var one = new byte[1];
        while (header.Count < 8192)
        {
            if (await stream.ReadAsync(one, cancellation).ConfigureAwait(false) == 0) return;
            header.Add(one[0]);
            var n = header.Count;
            if (n >= 4 && header[n - 4] == 13 && header[n - 3] == 10 && header[n - 2] == 13 && header[n - 1] == 10) break;
        }
        if (header.Count >= 8192) { await ReplyAsync(stream, 413, cancellation); return; }
        var lines = Encoding.ASCII.GetString(header.ToArray()).Split("\r\n");
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines.Skip(1).Where(line => line.Length > 0))
        {
            var colon = line.IndexOf(':');
            if (colon <= 0 || !fields.TryAdd(line[..colon], line[(colon + 1)..].Trim())) { await ReplyAsync(stream, 400, cancellation); return; }
        }
        if (!lines[0].StartsWith("POST /api/notifications HTTP/1.", StringComparison.Ordinal)) { await ReplyAsync(stream, 404, cancellation); return; }
        if (fields.ContainsKey("Origin") || !fields.TryGetValue("Host", out var host) || (host != $"127.0.0.1:{Port}" && host != $"localhost:{Port}")) { await ReplyAsync(stream, 403, cancellation); return; }
        if (!fields.TryGetValue("Authorization", out var auth) || !FixedEquals(auth, "Bearer " + token)) { await ReplyAsync(stream, 401, cancellation); return; }
        if (fields.ContainsKey("Transfer-Encoding") || !fields.TryGetValue("Content-Length", out var length) || !int.TryParse(length, out var size) || size is <= 0 or > 16384) { await ReplyAsync(stream, 413, cancellation); return; }
        var body = new byte[size];
        await stream.ReadExactlyAsync(body, cancellation).ConfigureAwait(false);
        try
        {
            using var json = JsonDocument.Parse(body);
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("title", out var title) || title.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(title.GetString())) { await ReplyAsync(stream, 400, cancellation); return; }
            var text = root.TryGetProperty("body", out var content) && content.ValueKind == JsonValueKind.String ? content.GetString() ?? "" : "";
            var source = root.TryGetProperty("source", out var origin) && origin.ValueKind == JsonValueKind.String
                ? ClipboardPreview.Create(origin.GetString() ?? "") : "本地消息";
            if (string.IsNullOrWhiteSpace(source)) source = "本地消息";
            lock (sync)
            {
                latest = new(ClipboardPreview.Create(title.GetString()!), ClipboardPreview.Create(text))
                    { CreatedAt = DateTimeOffset.UtcNow, Source = source, SourceId = "local:" + source };
                if (pending.Count == 8) pending.Dequeue();
                pending.Enqueue(latest);
            }
            await ReplyAsync(stream, 200, cancellation);
        }
        catch (JsonException) { await ReplyAsync(stream, 400, cancellation); }
    }
    private static bool FixedEquals(string left, string right) => CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(left), Encoding.UTF8.GetBytes(right));
    private static Task ReplyAsync(NetworkStream stream, int code, CancellationToken token)
    {
        var body = code == 200 ? "{\"ok\":true}" : "{\"ok\":false}";
        var bytes = Encoding.ASCII.GetBytes($"HTTP/1.1 {code} Result\r\nContent-Type: application/json\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n{body}");
        return stream.WriteAsync(bytes, token).AsTask();
    }
    public void Dispose() { stop.Cancel(); listener.Stop(); lock (sync) { latest = null; pending.Clear(); } }
}
