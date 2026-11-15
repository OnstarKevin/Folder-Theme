using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using FolderThemeStudio.App;
using FolderThemeStudio.App.Localization;
using FolderThemeStudio.App.Services;
using FolderThemeStudio.App.Settings;
using FolderThemeStudio.App.ViewModels;
using FolderThemeStudio.Core.Tests.TestSupport;
using Xunit;

namespace FolderThemeStudio.Core.Tests.Application;

public sealed class AppleExperienceTests
{
    [Fact]
    public void Home_StartDesignAndReturnKeepsEditedPalette()
    {
        WpfTestHost.Invoke(() =>
        {
            var viewModel = new MainViewModelFixture().CreateViewModel();
            var window = new MainWindow(viewModel);
            try
            {
                var home = Assert.IsAssignableFrom<FrameworkElement>(window.FindName("HomePanel"));
                var workspace = Assert.IsAssignableFrom<FrameworkElement>(window.FindName("EditorWorkspace"));
                Assert.Equal(Visibility.Visible, home.Visibility);
                Assert.Equal(Visibility.Collapsed, workspace.Visibility);
                Assert.IsType<Button>(window.FindName("HomeStartButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(Visibility.Visible, workspace.Visibility);
                viewModel.GradientStart = "#FF9500";
                Assert.IsType<Button>(window.FindName("HomeNavButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(Visibility.Visible, home.Visibility);
                Assert.Equal("#FF9500", viewModel.GradientStart);
            }
            finally { window.RequestExplicitExit(); }
        });
    }

    [Fact]
    public void AppleColors_SelectingSwatchUpdatesActualFolderPalette()
    {
        WpfTestHost.Invoke(() =>
        {
            var viewModel = new MainViewModelFixture().CreateViewModel();
            var window = new MainWindow(viewModel);
            try
            {
                var colors = Assert.IsType<ListBox>(window.FindName("AppleColorSwatches"));
                Assert.True(colors.Items.Count >= 10);
                colors.SelectedIndex = 0;
                Assert.Equal("#007AFF", viewModel.GradientStart);
                colors.SelectedIndex = 1;
                Assert.NotEqual("#007AFF", viewModel.GradientStart);
                Assert.True(viewModel.PaletteEditor.GradientStart.IsValid);
            }
            finally { window.RequestExplicitExit(); }
        });
    }

    [Fact]
    public async Task QuickNote_UsesSmallWindowAndSavesToClickedFolderWithoutPickingPath()
    {
        using var temp = new TemporaryDirectory();
        var clicked = Directory.CreateDirectory(Path.Combine(temp.Path, "Clicked")).FullName;
        var other = Directory.CreateDirectory(Path.Combine(temp.Path, "Other")).FullName;
        var catalog = new JsonFolderCategoryCatalog(Path.Combine(temp.Path, "notes.json"));
        WpfTestHost.Invoke(() =>
        {
            var fixture = new MainViewModelFixture();
            var window = new MainWindow(fixture.CreateViewModel(),
                new JsonAppSettingsStore(Path.Combine(temp.Path, "settings.json")),
                new LocalizationService("zh-CN"), AppSettings.Default,
                ruleDialogs: fixture.Dialogs, folderCategoryCatalog: catalog);
            var frame = new DispatcherFrame();
            Exception? failure = null;
            Window? noteWindow = null;
            async void Verify()
            {
                try
                {
                    await window.OpenFolderCategoryEditorAsync(other);
                    await window.OpenFolderCategoryEditorAsync(clicked);
                    noteWindow = Assert.Single(System.Windows.Application.Current.Windows.Cast<Window>(),
                        item => item.IsVisible && item.DataContext is FolderCategoriesViewModel);
                    Assert.True(noteWindow.Width <= 480, "Desktop quick edit must not open the full manager.");
                    var notes = Assert.IsType<FolderCategoriesViewModel>(noteWindow.DataContext);
                    Assert.Equal(clicked, notes.FolderPath);
                    notes.Note = "Remember this folder";
                    Assert.True(notes.CanSave, "Writing a note must not require choosing a path or category again.");
                    var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    noteWindow.Closed += (_, _) => closed.TrySetResult();
                    Assert.IsType<Button>(noteWindow.FindName("SaveNoteButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await closed.Task.WaitAsync(TimeSpan.FromSeconds(3));
                    Assert.False(noteWindow.IsVisible);
                }
                catch (Exception exception) { failure = exception; }
                finally { noteWindow?.Close(); window.RequestExplicitExit(); frame.Continue = false; }
            }
            window.Dispatcher.BeginInvoke(Verify);
            Dispatcher.PushFrame(frame);
            if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        });
        var saved = Assert.Single(await catalog.LoadAsync());
        Assert.Equal(clicked, saved.FolderPath);
        Assert.Equal("Remember this folder", saved.Note);
    }
}
