using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FolderThemeStudio.App;
using FolderThemeStudio.App.Services;
using FolderThemeStudio.Core.Tests.TestSupport;
using Xunit;

namespace FolderThemeStudio.Core.Tests.Application;

public sealed class FolderLivePopupTests
{
    [Fact]
    public void Popup_IsClickThroughUntilFileDragNeedsDropTargets()
    {
        WpfTestHost.Invoke(() =>
        {
            new FolderThemeStudio.App.Localization.LocalizationService("zh-CN");
            var window = new FolderCategoryPopupWindow { Opacity = 0 };
            try
            {
                window.Show(new FolderCategoryEntry(@"C:\Work\Project", "Project", "Note", "#2868D7"), new Rect(10, 10, 60, 40));
                var handle = new WindowInteropHelper(window).Handle;
                Assert.True((GetWindowLongPtr(handle, -20).ToInt64() & 0x20) != 0);

                window.SetDropInteractionEnabled(true);
                Assert.True((GetWindowLongPtr(handle, -20).ToInt64() & 0x20) == 0);
                window.SetDropInteractionEnabled(false);
                Assert.True((GetWindowLongPtr(handle, -20).ToInt64() & 0x20) != 0);
            }
            finally { window.Hide(); window.Close(); }
        });
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint window, int index);

    [Fact]
    public void Popup_ExposesRecentFileAndDropTargetsForAnOrdinaryFolder()
    {
        using var temp = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(temp.Path, "recent.txt"), "hello");
        Directory.CreateDirectory(Path.Combine(temp.Path, "Child"));
        WpfTestHost.Invoke(() =>
        {
            // WPF Application 由测试共用，明确设置本测试断言的语言，避免依赖测试顺序。
            new FolderThemeStudio.App.Localization.LocalizationService("zh-CN");
            var window = new FolderCategoryPopupWindow { Opacity = 0 };
            try
            {
                window.Show(new FolderCategoryEntry(temp.Path, "Folder", "", "#2868D7"), new Rect(10, 10, 60, 40));
                Assert.IsType<ItemsControl>(window!.FindName("RecentFilesList"));
                Assert.IsType<StackPanel>(window.FindName("DropTargetsPanel"));
                Assert.IsType<TextBlock>(window.FindName("FolderInfoText"));
                Assert.True(window.AllowDrop);

                var frame = new DispatcherFrame();
                var deadline = DateTime.UtcNow.AddSeconds(3);
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
                timer.Tick += (_, _) =>
                {
                    if (RecentFilesLoaded() || DateTime.UtcNow >= deadline) frame.Continue = false;
                };
                timer.Start();
                Dispatcher.PushFrame(frame);
                timer.Stop();
                Assert.True(RecentFilesLoaded(), "Live folder content was not loaded.");
                var info = ((TextBlock)window.FindName("FolderInfoText")).Text;
                Assert.Contains("创建", info);
                Assert.Contains("修改", info);
                Assert.Contains("5 B", info);
                Assert.Contains("1 个文件", info);
                Assert.Contains("1 个文件夹", info);
                if (Environment.GetEnvironmentVariable("FTS_CAPTURE_UI") == "1")
                {
                    window.UpdateLayout();
                    var root = (FrameworkElement)window.Content;
                    var bitmap = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth), (int)Math.Ceiling(root.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(root);
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    var output = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "artifacts", "ui-beta13"));
                    Directory.CreateDirectory(output);
                    using var stream = File.Create(Path.Combine(output, "folder-info-note.png"));
                    encoder.Save(stream);
                }

                bool RecentFilesLoaded() =>
                    ((ItemsControl)window.FindName("RecentFilesList")).Items.Count == 1 &&
                    ((StackPanel)window.FindName("DropTargetsPanel")).Children.Count == 2;
            }
            finally { window.Hide(); window.Close(); }
        });
    }
}
