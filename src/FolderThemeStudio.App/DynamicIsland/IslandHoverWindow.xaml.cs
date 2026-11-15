using System.Windows;
using System.Windows.Interop;

namespace FolderThemeStudio.App.DynamicIsland;

public partial class IslandHoverWindow : Window
{
    private IslandNativeInteraction? native;
    private IslandViewModel? model;
    internal bool IsUnobscuredAt(System.Windows.Point point) => native?.IsUnobscuredAt(point) == true;
    public IslandHoverWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (model is not null) model.PropertyChanged -= ModelChanged;
            model = DataContext as IslandViewModel;
            if (model is not null) model.PropertyChanged += ModelChanged;
            ResizeForContent();
        };
        SourceInitialized += (_, _) =>
        {
            native = new IslandNativeInteraction((HwndSource)PresentationSource.FromVisual(this));
            native.Configure(clickThrough: false, observeMouse: false);
        };
        Closed += (_, _) => { native?.Dispose(); if (model is not null) model.PropertyChanged -= ModelChanged; DataContext = null; };
    }
    private void ModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    { if (e.PropertyName == nameof(IslandViewModel.HasMediaSession)) ResizeForContent(); }
    private void ResizeForContent() => Height = model?.HasMediaSession == true ? 176 : 112;
    private async void Previous_Click(object sender, RoutedEventArgs e) { if (model is not null) await model.ControlMediaAsync(IslandMediaCommand.Previous); }
    private async void Playback_Click(object sender, RoutedEventArgs e) { if (model is not null) await model.ControlMediaAsync(IslandMediaCommand.TogglePlayPause); }
    private async void Next_Click(object sender, RoutedEventArgs e) { if (model is not null) await model.ControlMediaAsync(IslandMediaCommand.Next); }
}
