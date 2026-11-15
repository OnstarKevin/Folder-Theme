using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using FolderThemeStudio.App.Controls;
using FolderThemeStudio.App.ViewModels;

namespace FolderThemeStudio.App;

public partial class FolderQuickNoteWindow : RoundedWindow
{
    private readonly FolderCategoriesViewModel viewModel;

    public FolderQuickNoteWindow(FolderCategoriesViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        DataContext = viewModel;
        Loaded += (_, _) => { PositionNearPointer(); FocusNote(); };
        SizeChanged += (_, _) => { if (IsLoaded) ConstrainToWorkArea(); };
        Closed += (_, _) => viewModel.Dispose();
    }

    public void FocusNote()
    {
        NoteBox.Focus();
        NoteBox.CaretIndex = NoteBox.Text.Length;
    }

    private void PositionNearPointer()
    {
        var pointer = System.Windows.Forms.Cursor.Position;
        var work = System.Windows.Forms.Screen.FromPoint(pointer).WorkingArea;
        var transform = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice
            ?? System.Windows.Media.Matrix.Identity;
        var anchor = transform.Transform(new System.Windows.Point(pointer.X + 16, pointer.Y + 18));
        var topLeft = transform.Transform(new System.Windows.Point(work.Left + 12, work.Top + 12));
        var bottomRight = transform.Transform(new System.Windows.Point(work.Right - 12, work.Bottom - 12));
        Left = Math.Clamp(anchor.X, topLeft.X, Math.Max(topLeft.X, bottomRight.X - ActualWidth));
        Top = Math.Clamp(anchor.Y, topLeft.Y, Math.Max(topLeft.Y, bottomRight.Y - ActualHeight));
    }

    private async void Save_Click(object sender, RoutedEventArgs e) => await SaveAndCloseAsync();

    private void ConstrainToWorkArea()
    {
        var handle = new WindowInteropHelper(this).Handle;
        var work = System.Windows.Forms.Screen.FromHandle(handle).WorkingArea;
        var transform = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice
            ?? System.Windows.Media.Matrix.Identity;
        var topLeft = transform.Transform(new System.Windows.Point(work.Left + 12, work.Top + 12));
        var bottomRight = transform.Transform(new System.Windows.Point(work.Right - 12, work.Bottom - 12));
        Left = Math.Clamp(Left, topLeft.X, Math.Max(topLeft.X, bottomRight.X - ActualWidth));
        Top = Math.Clamp(Top, topLeft.Y, Math.Max(topLeft.Y, bottomRight.Y - ActualHeight));
    }

    private async Task SaveAndCloseAsync()
    {
        if (!viewModel.CanSave) return;
        if (await viewModel.SaveAsync()) Close();
        else ErrorText.Visibility = Visibility.Visible;
    }

    private async void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { e.Handled = true; Close(); }
        else if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            await SaveAndCloseAsync();
        }
    }
}
