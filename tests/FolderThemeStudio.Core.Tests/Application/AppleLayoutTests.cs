using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FolderThemeStudio.App;
using FolderThemeStudio.App.Localization;
using FolderThemeStudio.App.Services;
using FolderThemeStudio.App.Settings;
using FolderThemeStudio.App.ViewModels;
using FolderThemeStudio.Core.Rendering;
using FolderThemeStudio.Core.Themes;
using FolderThemeStudio.Core.Tests.TestSupport;
using Xunit;

namespace FolderThemeStudio.Core.Tests.Application;

public sealed class AppleLayoutTests
{
    [Theory]
    [InlineData(1280)]
    [InlineData(760)]
    public void HomeAndStudio_PrimaryActionsFitAtDesktopAndCompactWidths(int width)
    {
        WpfTestHost.Invoke(() =>
        {
            var model = new MainViewModelFixture().CreateViewModel();
            model.ApplyPalette(AppleColors.All[0].Palette);
            model.Material = MaterialStyle.Satin;
            model.SelectedMode = FolderThemeStudio.Core.Application.ApplicationMode.Compatible;
            var window = new MainWindow(model) { Width = width, Height = 820, ShowActivated = false, ShowInTaskbar = false, Opacity = 0, Left = -10000, Top = -10000 };
            try
            {
                new LocalizationService("zh-CN");
                window.Show();
                window.UpdateLayout();
                var theme = FolderTheme.IceBlue with { Material = MaterialStyle.Satin, GradientStart = "#007AFF", GradientEnd = "#0055D4", GlowStrength = .06, HighlightStrength = .28, StrokeOpacity = .45, Opacity = 1 };
                ((Image)window.FindName("HomeHeroIcon")).Source = FolderIconRenderer.Render(theme, 256);
                window.UpdateLayout();
                FitsInside(window, (FrameworkElement)window.FindName("HomeStartButton"));
                Capture(window, $"home-{width}.png");
                ((Button)window.FindName("HomeStartButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                ((Image)window.FindName("StickerDragPreview")).Source = FolderIconRenderer.Render(theme, 256);
                window.UpdateLayout();
                FitsInside(window, (FrameworkElement)window.FindName("AppleColorSwatches"));
                Capture(window, $"studio-{width}.png");
            }
            finally { window.RequestExplicitExit(); }
        });
    }

    [Fact]
    public void QuickNote_DefaultViewKeepsNoteAndSaveVisibleWithoutPathPicker()
    {
        using var temp = new TemporaryDirectory();
        var folder = Directory.CreateDirectory(Path.Combine(temp.Path, "项目资料")).FullName;
        WpfTestHost.Invoke(() =>
        {
            var fixture = new MainViewModelFixture();
            var notes = new FolderCategoriesViewModel(new JsonFolderCategoryCatalog(Path.Combine(temp.Path, "notes.json")),
                new NullFolderHoverMonitor(), fixture.Dialogs, new LocalizationService("zh-CN"), new ImmediateViewModelDispatcher());
            notes.SelectFolderForQuickNote(folder);
            notes.Note = "这周整理一下设计素材。";
            var window = new FolderQuickNoteWindow(notes) { ShowActivated = false, Opacity = 0 };
            try
            {
                window.Show();
                window.UpdateLayout();
                FitsInside(window, (FrameworkElement)window.FindName("NoteBox"));
                FitsInside(window, (FrameworkElement)window.FindName("SaveNoteButton"));
                Assert.DoesNotContain(Descendants(window).OfType<Button>(), button => ReferenceEquals(button.Command, notes.PickFolderCommand));
                Capture(window, "quick-note.png");
            }
            finally { window.Close(); }
        });
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            yield return child;
            foreach (var item in Descendants(child)) yield return item;
        }
    }

    private static void FitsInside(Window window, FrameworkElement element)
    {
        var root = (FrameworkElement)window.Content;
        var bounds = element.TransformToAncestor(root).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
        Assert.True(element.ActualWidth > 0 && element.ActualHeight > 0);
        Assert.InRange(bounds.Left, -1, root.ActualWidth);
        Assert.InRange(bounds.Right, 0, root.ActualWidth + 1);
        Assert.InRange(bounds.Top, -1, root.ActualHeight);
        Assert.InRange(bounds.Bottom, 0, root.ActualHeight + 1);
    }

    private static void Capture(Window window, string name)
    {
        if (Environment.GetEnvironmentVariable("FTS_CAPTURE_UI") != "1") return;
        var root = VisualTreeHelper.GetParent((FrameworkElement)window.Content) as FrameworkElement
            ?? (FrameworkElement)window.Content;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth), (int)Math.Ceiling(root.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        var background = new DrawingVisual();
        using (var context = background.RenderOpen()) context.DrawRectangle(window.Background, null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
        bitmap.Render(background);
        bitmap.Render(root);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var output = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "artifacts", "ui-beta16"));
        Directory.CreateDirectory(output);
        using var stream = File.Create(Path.Combine(output, name));
        encoder.Save(stream);
    }
}
