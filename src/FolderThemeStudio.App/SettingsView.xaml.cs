using System.Windows;
using FolderThemeStudio.App.Controls;
using FolderThemeStudio.App.Settings;
using FolderThemeStudio.App.ViewModels;
using FolderThemeStudio.App.Services;
using System.Windows.Input;

namespace FolderThemeStudio.App;

public partial class SettingsView : System.Windows.Controls.UserControl
{
    private readonly SettingsViewModel viewModel;
    public Func<Task>? ApplySaved { get; set; }
    public event EventHandler? Cancelled;
    public void SelectSection(bool features) => Sections.SelectedIndex = features ? 1 : 0;
    public bool IsBusy { get; private set; }

    private readonly Func<Task>? configureIsland;
    private async void IslandModules_Click(object sender, RoutedEventArgs e) { if (configureIsland is not null) await configureIsland(); }
    public SettingsView(SettingsViewModel viewModel, Func<Task>? configureIsland = null)
    {
        InitializeComponent();
        this.viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = viewModel;
        this.configureIsland = configureIsland;
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (IsBusy) return;
        IsBusy = true; SaveActions.IsEnabled = false;
        try
        {
            if (!await viewModel.SaveAsync()) { StatusText.Text = "请检查输入。"; return; }
            if (ApplySaved is not null) await ApplySaved();
            StatusText.Text = "已保存并应用。";
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or System.Security.SecurityException or InvalidOperationException)
        { StatusText.Text = "未能完全应用设置：" + ex.Message; }
        finally { IsBusy = false; SaveActions.IsEnabled = true; }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        viewModel.Cancel(); Cancelled?.Invoke(this, EventArgs.Empty);
    }

    private void Recommended_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is FolderPalette palette)
            viewModel.ApplyRecommended(palette);
    }

    private void Recent_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is string color)
            viewModel.ApplyRecent(color);
    }

    private void EditChord_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        var button = e.ChangedButton switch
        {
            MouseButton.Right => FolderMouseButton.Right,
            MouseButton.Middle => FolderMouseButton.Middle,
            _ => (FolderMouseButton?)null,
        };
        if (button is null) return;
        var chord = new FolderEditChord(Keyboard.Modifiers, button.Value);
        if (FolderEditChord.TryNormalize(chord, out var normalized)) viewModel.EditChord = normalized;
        e.Handled = true;
    }
}
