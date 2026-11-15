using System.IO;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FolderThemeStudio.App;
using FolderThemeStudio.Core.Tests.TestSupport;
using Xunit;

namespace FolderThemeStudio.Core.Tests.Application;

public sealed class DefaultWallpaperTests
{
    [Fact]
    public void DefaultWallpaper_IsBundledAsExactSixteenByNineImage()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var path = Path.Combine(root, "src", "FolderThemeStudio.App", "Assets", "DefaultWallpaper.png");
        Assert.True(File.Exists(path), "The default wallpaper must ship with the application.");
        using var stream = File.OpenRead(path);
        var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames.Single();
        Assert.Equal(1920, frame.PixelWidth);
        Assert.Equal(1080, frame.PixelHeight);
    }

    [Fact]
    public void WorkspaceAndDesktop_DefaultToWallpaper_AndSolidPreviewRemainsSelectable()
    {
        WpfTestHost.Invoke(() =>
        {
            var vm = new MainViewModelFixture().CreateViewModel();
            Assert.Equal("DefaultWallpaper", vm.DesktopBackground);
            var window = new MainWindow(vm);
            try
            {
                window.ShowActivated = false;
                window.ShowInTaskbar = false;
                window.Opacity = 0;
                window.Left = -10000;
                window.Top = -10000;
                window.Show();
                window.UpdateLayout();
                var workspace = Assert.IsType<Border>(window.FindName("WorkspaceWallpaper"));
                Assert.IsType<ImageBrush>(workspace.Background);
                var veil = Assert.IsType<Border>(window.FindName("WorkspaceReadabilityVeil"));
                Assert.InRange(Assert.IsType<SolidColorBrush>(veil.Background).Color.A, (byte)180, (byte)240);
                var desktop = Assert.IsType<Border>(window.FindName("DesktopPreviewSurface"));
                Assert.IsType<ImageBrush>(desktop.Background);
                var picker = Assert.IsType<ComboBox>(window.FindName("DesktopBackgroundPicker"));
                picker.SelectedIndex = 0;
                window.UpdateLayout();
                Assert.Equal(Color.FromRgb(27, 42, 74), Assert.IsType<SolidColorBrush>(desktop.Background).Color);
                Assert.IsType<ImageBrush>(workspace.Background);
                picker.SelectedIndex = 3;
                window.UpdateLayout();
                Assert.IsType<ImageBrush>(desktop.Background);
            }
            finally { window.Close(); }
        });
    }
}
