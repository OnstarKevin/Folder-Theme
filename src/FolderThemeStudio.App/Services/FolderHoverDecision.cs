using System.Windows;
using Point = System.Windows.Point;

namespace FolderThemeStudio.App.Services;

public interface IExplorerFolderHoverTargetResolver
{
    ExplorerHoverTarget? TryResolve(Point screenPoint);
}

public sealed class FolderHoverDecision
{
    private readonly TimeSpan dwell;
    private string? candidatePath;
    private DateTimeOffset candidateSince;

    public FolderHoverDecision(TimeSpan? dwell = null)
    {
        this.dwell = dwell ?? TimeSpan.FromMilliseconds(550);
        if (this.dwell < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(dwell));
    }

    public FolderCategoryEntry? Update(
        ExplorerHoverTarget? target,
        Point screenPoint,
        IReadOnlyDictionary<string, FolderCategoryEntry> entries,
        DateTimeOffset now)
    {
        if (target is null || !target.ItemBounds.Contains(screenPoint) ||
            !entries.TryGetValue(target.FolderPath, out var entry))
        {
            Reset();
            return null;
        }

        if (!string.Equals(candidatePath, target.FolderPath, StringComparison.OrdinalIgnoreCase))
        {
            candidatePath = target.FolderPath;
            candidateSince = now;
        }

        return now - candidateSince >= dwell ? entry : null;
    }

    public ExplorerHoverTarget? UpdateTarget(ExplorerHoverTarget? target, Point screenPoint, DateTimeOffset now)
    {
        if (target is null || !target.ItemBounds.Contains(screenPoint))
        {
            Reset();
            return null;
        }

        if (!string.Equals(candidatePath, target.FolderPath, StringComparison.OrdinalIgnoreCase))
        {
            candidatePath = target.FolderPath;
            candidateSince = now;
        }

        return now - candidateSince >= dwell ? target : null;
    }

    public void Reset()
    {
        candidatePath = null;
        candidateSince = default;
    }
}

internal sealed class NativeMouseInteraction
{
    private static readonly TimeSpan ClickCooldown = TimeSpan.FromMilliseconds(750);
    private const double DragDistanceSquared = 64;
    private readonly object sync = new();
    private Point downPoint;
    private bool buttonDown;
    private DateTimeOffset suppressUntil;

    public void OnDown(Point point, DateTimeOffset now)
    {
        lock (sync)
        {
            downPoint = point;
            buttonDown = true;
            suppressUntil = now + ClickCooldown;
        }
    }

    public void OnUp(DateTimeOffset now)
    {
        lock (sync)
        {
            buttonDown = false;
            suppressUntil = now + ClickCooldown;
        }
    }

    public bool ShouldSuppress(Point point, DateTimeOffset now)
    {
        lock (sync)
        {
            if (!buttonDown) return now < suppressUntil;
            return !HasMovedForDrag(point);
        }
    }

    public bool IsDragging(Point point)
    {
        lock (sync) return buttonDown && HasMovedForDrag(point);
    }

    private bool HasMovedForDrag(Point point)
    {
        var dx = point.X - downPoint.X;
        var dy = point.Y - downPoint.Y;
        return dx * dx + dy * dy >= DragDistanceSquared;
    }
}
