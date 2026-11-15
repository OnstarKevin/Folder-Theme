using System.Windows;
using System.Windows.Input;
using FolderThemeStudio.App.Services;
using Xunit;

namespace FolderThemeStudio.Core.Tests.Application;

public sealed class FolderEditChordTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void MouseHook_RemainsInstalledForEitherFeature(bool hoverCards, bool quickNotes, bool required)
    {
        Assert.Equal(required, FolderQuickEditMouseHook.IsRequired(hoverCards, quickNotes));
    }
    [Fact]
    public void DeferredShortcut_ConsumesOnlyTheConfiguredChord()
    {
        var decision = new FolderQuickEditDecision(FolderEditChord.Default);
        Assert.False(decision.TryBeginDeferredEdit(FolderMouseButton.Right, ModifierKeys.None));
        Assert.False(decision.OnMouseUp(FolderMouseButton.Right));
        Assert.True(decision.TryBeginDeferredEdit(FolderMouseButton.Right, ModifierKeys.Alt));
        Assert.True(decision.OnMouseUp(FolderMouseButton.Right));
        Assert.False(decision.OnMouseUp(FolderMouseButton.Right));
    }

    [Fact]
    public void DefaultChord_OpensOnlyOnAltRightOverFolder()
    {
        var decision = new FolderQuickEditDecision(FolderEditChord.Default);
        var target = new ExplorerHoverTarget(@"C:\Work\Project", new Rect(0, 0, 100, 100));

        Assert.Equal(@"C:\Work\Project", decision.OnMouseDown(FolderMouseButton.Right, ModifierKeys.Alt, new Point(10, 10), target));
        Assert.True(decision.OnMouseUp(FolderMouseButton.Right));
        Assert.Null(decision.OnMouseDown(FolderMouseButton.Right, ModifierKeys.None, new Point(10, 10), target));
        Assert.False(decision.OnMouseUp(FolderMouseButton.Right));
    }

    [Fact]
    public void CustomChord_DoesNotInterceptMissingOrOffItemTarget()
    {
        var decision = new FolderQuickEditDecision(new FolderEditChord(ModifierKeys.Control | ModifierKeys.Shift, FolderMouseButton.Middle));
        var target = new ExplorerHoverTarget(@"C:\Work\Project", new Rect(0, 0, 100, 100));

        Assert.Null(decision.OnMouseDown(FolderMouseButton.Middle, ModifierKeys.Control | ModifierKeys.Shift, new Point(10, 10), null));
        Assert.Null(decision.OnMouseDown(FolderMouseButton.Middle, ModifierKeys.Control | ModifierKeys.Shift, new Point(200, 200), target));
        Assert.Equal(@"C:\Work\Project", decision.OnMouseDown(FolderMouseButton.Middle, ModifierKeys.Control | ModifierKeys.Shift, new Point(10, 10), target));
        Assert.True(decision.OnMouseUp(FolderMouseButton.Middle));
    }

    [Fact]
    public void ChordValidation_RejectsBareRightClick()
    {
        Assert.False(FolderEditChord.TryNormalize(new FolderEditChord(ModifierKeys.None, FolderMouseButton.Right), out _));
        Assert.True(FolderEditChord.TryNormalize(FolderEditChord.Default, out _));
    }
}
