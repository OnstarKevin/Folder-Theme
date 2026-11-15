using System.Security.Cryptography;
using System.Text;

namespace FolderThemeStudio.App.DynamicIsland;

/// <summary>短暂提醒而非通知历史：只接收启用后的消息，最多排队八条，指纹和正文只留在内存。</summary>
public sealed class IslandNotificationQueue
{
    private readonly Queue<IslandNotification> pending = new();
    private readonly HashSet<string> seen = new(StringComparer.Ordinal);
    private readonly Queue<string> seenOrder = new();
    private DateTimeOffset startedAt, until;
    public IslandNotification? Current { get; private set; }
    public int PendingCount => pending.Count;
    public IslandNotificationQueue(DateTimeOffset startedAt) => this.startedAt = startedAt;

    public void Clear(DateTimeOffset now)
    {
        pending.Clear(); seen.Clear(); seenOrder.Clear(); Current = null; startedAt = now;
    }
    public void Enqueue(IEnumerable<IslandNotification> messages, DateTimeOffset now, string blockedSources)
    {
        var blocked = blockedSources.Split([';', '\r', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        foreach (var message in messages.OrderBy(item => item.CreatedAt))
        {
            // 未提供时间的旧适配器仍可接入；原生/本地消息均提供明确时间与身份。
            if (message.CreatedAt != default && (message.CreatedAt < startedAt || message.CreatedAt < now.AddSeconds(-90) || message.CreatedAt > now.AddSeconds(5))) continue;
            if (blocked.Any(item => item.Equals(message.Source, StringComparison.OrdinalIgnoreCase) || item.Equals(message.SourceId, StringComparison.OrdinalIgnoreCase))) continue;
            var identity = $"{message.SourceId}\0{message.Source}\0{message.Id}\0{message.CreatedAt.UtcTicks}\0{message.Title}\0{message.Body}";
            var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
            if (!seen.Add(fingerprint)) continue;
            seenOrder.Enqueue(fingerprint);
            if (seenOrder.Count > 512) seen.Remove(seenOrder.Dequeue());
            if (pending.Count == 8) pending.Dequeue();
            pending.Enqueue(message);
        }
        Advance(now);
    }
    public void Advance(DateTimeOffset now)
    {
        if (Current is not null && now < until) return;
        Current = null;
        while (pending.TryDequeue(out var next))
        {
            if (next.CreatedAt != default && next.CreatedAt < now.AddSeconds(-90)) continue;
            Current = next; until = now.AddSeconds(6); break;
        }
    }
}

/// <summary>批量读取避免两次 UI 刷新之间的突发消息被覆盖；兼容原有单条适配器。</summary>
public interface IIslandNotificationBatchSource
{
    Task<IReadOnlyList<IslandNotification>> ReadRecentAsync(CancellationToken token);
}
