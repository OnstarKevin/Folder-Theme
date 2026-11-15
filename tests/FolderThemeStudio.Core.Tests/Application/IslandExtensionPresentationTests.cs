using FolderThemeStudio.App.DynamicIsland;
using Xunit;

namespace FolderThemeStudio.Core.Tests.Application;

public sealed class IslandExtensionPresentationTests
{
    [Fact]
    public void ExtensionFillsIdleTimeButNeverInterruptsClipboardAndExpires()
    {
        var now = DateTimeOffset.UtcNow;
        using var model = new IslandViewModel(new() { ShowCpu = false, ShowMemory = false, ShowMusic = false, ShowClipboard = true }, utcNow: () => now);
        model.SetExtensionContent("focus", "专注 24:59", "专注计时", 30);
        Assert.Equal("专注 24:59", model.CompactText);
        model.SetClipboard("我的文档");
        Assert.Contains("我的文档", model.CompactText);
        now = now.AddSeconds(11); model.TickClock();
        Assert.Equal("专注 24:59", model.CompactText);
        now = now.AddSeconds(20); model.TickClock();
        Assert.DoesNotContain("专注", model.CompactText);
    }

    [Fact]
    public void RemovingOnePluginPreservesOtherPluginAndBoundsUntrustedContent()
    {
        using var model = new IslandViewModel(new() { ShowCpu = false, ShowMemory = false, ShowMusic = false });
        model.SetExtensionContent("one", "第一项", "", 60);
        model.SetExtensionContent("two", new string('长', 600) + "\n第二行", "详情", 60);
        Assert.DoesNotContain("\n", model.CompactText); Assert.InRange(model.CompactText.Length, 1, 80);
        model.ClearExtensionContent("two"); Assert.Equal("第一项", model.CompactText);
        model.ClearExtensionContent("one"); Assert.DoesNotContain("第一项", model.CompactText);
    }

    [Fact]
    public void ClipboardHookRespectsModuleSwitchAndUnchangedTextDoesNotRedrawEachClockTick()
    {
        using var model = new IslandViewModel(new() { ShowCpu = false, ShowMemory = false, ShowMusic = false });
        var events = new List<string>(); var redraws = 0;
        model.HookEvent += (_, e) => events.Add(e.Name);
        model.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(model.CompactText)) redraws++; };
        model.SetClipboard("secret"); Assert.Empty(events);
        model.SetExtensionContent("focus", "专注", "", 30);
        redraws = 0; model.TickClock(); model.TickClock(); Assert.Equal(0, redraws);
        model.ApplySettings(model.Settings with { ShowClipboard = true });
        model.SetClipboard("hello"); Assert.Equal(["clipboard.changed"], events);
    }
}
