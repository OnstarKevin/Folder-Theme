using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Diagnostics;
using System.ComponentModel;
using System.Globalization;
using Point = System.Windows.Point;
using Size = System.Windows.Size;
using Color = System.Windows.Media.Color;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace FolderThemeStudio.App.DynamicIsland;

public static class IslandWindowPolicy
{
    public static double WheelOpacity(double current, int delta) => Math.Clamp(current + delta / 120d * .05, .2, 1);
    public static Point ClampPosition(Point requested, Rect workArea, Size windowSize) => new(
        Math.Clamp(requested.X, workArea.Left, Math.Max(workArea.Left, workArea.Right - windowSize.Width)),
        Math.Clamp(requested.Y, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - windowSize.Height)));
    public static Point SnapPosition(Point position, Rect workArea, Size size)
    {
        var clamped = ClampPosition(position, workArea, size);
        var right = Math.Max(workArea.Left, workArea.Right - size.Width);
        var bottom = Math.Max(workArea.Top, workArea.Bottom - size.Height);
        return new(Math.Abs(clamped.X - workArea.Left) <= 18 ? workArea.Left : Math.Abs(clamped.X - right) <= 18 ? right : clamped.X,
            Math.Abs(clamped.Y - workArea.Top) <= 18 ? workArea.Top : Math.Abs(clamped.Y - bottom) <= 18 ? bottom : clamped.Y);
    }
}

/// <summary>窗口拥有计时器与剪贴板句柄；关闭时全部解除，主软件仍可继续运行。</summary>
public partial class DynamicIslandWindow : Window
{
    public IslandViewModel Model { get; }
    private readonly DispatcherTimer clock = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer refresh = new();
    private IIslandClipboardListener? clipboard;
    private IslandNativeInteraction? nativeInteraction;
    private IslandHoverWindow? hoverCard;
    private readonly DispatcherTimer hoverDwell = new() { Interval = TimeSpan.FromMilliseconds(650) };
    private readonly DispatcherTimer hoverDismiss = new() { Interval = TimeSpan.FromMilliseconds(180) };
    private readonly DispatcherTimer editTimeout = new() { Interval = TimeSpan.FromSeconds(30) };
    private bool hoverInside, closed;
    private Point lastHoverPoint;
    public bool IsEditMode { get; private set; }
    private bool controlledClose;
    private readonly IslandDragPhysics dragPhysics = new();
    private readonly Stopwatch dragClock = Stopwatch.StartNew();
    private bool dragging, rendering;
    private double lastLocationTime, lastFrameTime;
    private Point lastDragPosition;
    private readonly IslandWidthSpring widthSpring = new(220);
    private readonly IslandWidthSpring heightSpring = new(36.4, 36.4, 42);
    private readonly RotateTransform borderRotation = new(0, .5, .5);
    private readonly LinearGradientBrush flowBrush = new(new GradientStopCollection
    {
        new(Color.FromRgb(175, 167, 246), 0), new(Color.FromRgb(135, 211, 235), .25),
        new(Color.FromRgb(173, 222, 201), .5), new(Color.FromRgb(241, 173, 199), .75),
        new(Color.FromRgb(175, 167, 246), 1),
    }, new Point(0, 0), new Point(1, 1));
    private bool stretching, resizing;
    private double lastStretchTime;
    public event EventHandler? SnapshotChanged;
    public event EventHandler? SettingsRequested;
    public event EventHandler? CloseRequested;
    public event EventHandler? ExitRequested;

    public DynamicIslandWindow(IslandSettings settings, IslandViewModel? model = null,
        Func<HwndSource, Action<string>, IIslandClipboardListener>? clipboardFactory = null)
    {
        InitializeComponent();
        IsVisibleChanged += (_, _) => UpdateBorderAnimation();
        SystemParameters.StaticPropertyChanged += SystemAppearanceChanged;
        Model = model ?? new IslandViewModel(settings);
        Width = Math.Min(Model.Settings.WindowWidth, MaxWidth);
        widthSpring.Reset(Width);
        DataContext = Model;
        ApplyTextAppearance();
        Model.PropertyChanged += Model_Changed;
        Opacity = settings.Normalize().Opacity;
        refresh.Interval = TimeSpan.FromSeconds(Model.Settings.RefreshSeconds);
        clock.Tick += Clock_Tick;
        refresh.Tick += Refresh_Tick;
        hoverDwell.Tick += HoverDwell_Tick;
        hoverDismiss.Tick += HoverDismiss_Tick;
        editTimeout.Tick += EditTimeout_Tick;
        PreviewKeyDown += Window_PreviewKeyDown;
        SourceInitialized += (_, _) =>
        {
            var source = (HwndSource)PresentationSource.FromVisual(this);
            nativeInteraction = new IslandNativeInteraction(source);
            nativeInteraction.MouseMoved += ObserveMouse;
            UpdateNativeInteraction();
            clipboard = clipboardFactory?.Invoke(source, Model.SetClipboard) ?? new ClipboardTextListener(source, Model.SetClipboard);
            clipboard.SetEnabled(Model.ShowClipboard);
        };
        SizeChanged += (_, _) => { if (IsLoaded) ConstrainPosition(); };
        Closing += (_, e) =>
        {
            if (!controlledClose && CloseRequested is not null) { e.Cancel = true; CloseRequested.Invoke(this, EventArgs.Empty); }
        };
        Closed += (_, _) =>
        {
            closed = true;
            SystemParameters.StaticPropertyChanged -= SystemAppearanceChanged;
            borderRotation.BeginAnimation(RotateTransform.AngleProperty, null);
            hoverDwell.Stop(); hoverDismiss.Stop(); editTimeout.Stop();
            hoverDwell.Tick -= HoverDwell_Tick; hoverDismiss.Tick -= HoverDismiss_Tick; editTimeout.Tick -= EditTimeout_Tick;
            PreviewKeyDown -= Window_PreviewKeyDown;
            nativeInteraction?.Dispose();
            hoverCard?.Close(); hoverCard = null;
            StopDragRendering();
            StopStretchRendering(); Model.PropertyChanged -= Model_Changed;
            clock.Stop(); refresh.Stop();
            clock.Tick -= Clock_Tick; refresh.Tick -= Refresh_Tick;
            clipboard?.Dispose();
            Model.Dispose();
        };
    }
    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var work = GetWorkArea();
        var saved = Model.Settings;
        Left = saved.Left ?? work.Left + (work.Width - ActualWidth) / 2;
        Top = saved.Top ?? work.Top + 16;
        ConstrainPosition();
        UpdateWidth();
        clock.Start(); refresh.Start();
        _ = Model.RefreshQuoteAsync();
        await Model.RefreshAsync();
    }
    private void Clock_Tick(object? sender, EventArgs e)
    {
        Model.TickClock();
        _ = Model.RefreshNotificationsAsync();
        _ = Model.RefreshQuoteAsync();
    }
    private async void Refresh_Tick(object? sender, EventArgs e) => await Model.RefreshAsync(includeNotifications: false);
    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!IsEditMode || e.ButtonState != MouseButtonState.Pressed) return;
        editTimeout.Stop();
        dragging = true; lastDragPosition = new(Left, Top); lastLocationTime = dragClock.Elapsed.TotalSeconds;
        if (MotionEnabled) StartDragRendering();
        try { DragMove(); } catch (InvalidOperationException) { }
        finally
        {
            dragging = false; dragPhysics.Release();
            var position = Model.Settings.SnapToEdges
                ? IslandWindowPolicy.SnapPosition(new(Left, Top), GetWorkArea(), new(ActualWidth, ActualHeight))
                : IslandWindowPolicy.ClampPosition(new(Left, Top), GetWorkArea(), new(ActualWidth, ActualHeight));
            Left = position.X; Top = position.Y;
            SnapshotChanged?.Invoke(this, EventArgs.Empty);
            if (!closed && IsEditMode) editTimeout.Start();
        }
    }
    private void Window_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!IsEditMode) return;
        Opacity = IslandWindowPolicy.WheelOpacity(Opacity, e.Delta);
        e.Handled = true;
        SnapshotChanged?.Invoke(this, EventArgs.Empty);
    }
    private void Window_LocationChanged(object? sender, EventArgs e)
    {
        if (dragging && MotionEnabled)
        {
            var now = dragClock.Elapsed.TotalSeconds;
            var dt = Math.Max(now - lastLocationTime, 1d / 240);
            dragPhysics.Drag((Left - lastDragPosition.X) / dt, (Top - lastDragPosition.Y) / dt);
            lastDragPosition = new(Left, Top); lastLocationTime = now;
        }
        if (IsLoaded && !dragging && !resizing) SnapshotChanged?.Invoke(this, EventArgs.Empty);
    }
    private bool MotionEnabled => Model.Settings.DragMotion && SystemParameters.ClientAreaAnimation;
    private void StartDragRendering()
    {
        if (rendering) return;
        rendering = true; lastFrameTime = dragClock.Elapsed.TotalSeconds;
        CompositionTarget.Rendering += RenderDragFrame;
    }
    private void StopDragRendering()
    {
        CompositionTarget.Rendering -= RenderDragFrame; rendering = false; dragPhysics.Reset();
        DragScale.ScaleX = DragScale.ScaleY = 1; DragTilt.Angle = 0;
        DragShadow.BlurRadius = 10; DragShadow.Opacity = .2;
    }
    private void RenderDragFrame(object? sender, EventArgs e)
    {
        if (!MotionEnabled) { StopDragRendering(); return; }
        var now = dragClock.Elapsed.TotalSeconds;
        // 停止移动后即使鼠标仍按着也自然恢复，帧回调只在拖动/回弹时存在。
        if (dragging && now - lastLocationTime > .08) dragPhysics.Release();
        dragPhysics.Step(now - lastFrameTime); lastFrameTime = now;
        DragScale.ScaleX = dragPhysics.ScaleX; DragScale.ScaleY = dragPhysics.ScaleY; DragTilt.Angle = dragPhysics.Angle;
        var strength = Math.Clamp(Math.Abs(dragPhysics.ScaleX - 1) / .012, 0, 1);
        DragShadow.BlurRadius = 10 + strength * 3; DragShadow.Opacity = .2 + strength * .1;
        if (!dragging && dragPhysics.IsResting) StopDragRendering();
    }
    public IslandSettings CaptureSettings() => Model.Settings with { Left = Left, Top = Top, WindowWidth = Width, Opacity = Opacity };
    public void CloseFromController() { controlledClose = true; Close(); }
    public void ApplySettings(IslandSettings settings)
    {
        Model.ApplySettings(settings);
        ApplyTextAppearance();
        if (!MotionEnabled) StopDragRendering();
        Opacity = settings.Normalize().Opacity;
        refresh.Interval = TimeSpan.FromSeconds(Model.Settings.RefreshSeconds);
        clipboard?.SetEnabled(Model.ShowClipboard);
        UpdateNativeInteraction(); UpdateWidth();
        _ = Model.RefreshQuoteAsync();
        _ = Model.RefreshAsync();
    }
    private void Model_Changed(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IslandViewModel.CompactText) or nameof(IslandViewModel.MetricsText) or nameof(IslandViewModel.IsMusicPlaying) && IsLoaded) UpdateWidth();
    }
    private void ApplyTextAppearance()
    {
        CompactMessage.FontFamily = new IslandFontStore().Resolve(Model.Settings.CustomFontPath, Model.Settings.CustomFontFamily);
        CompactMessage.Foreground = Model.Settings.LightText ? System.Windows.Media.Brushes.WhiteSmoke : new SolidColorBrush(System.Windows.Media.Color.FromRgb(48, 48, 60));
        CompactMetrics.FontFamily = CompactMessage.FontFamily; CompactMetrics.Foreground = CompactMessage.Foreground;
        DragShadow.Color = Model.Settings.LightText ? Colors.Black : Colors.White;
        if (hoverCard is not null) hoverCard.FontFamily = CompactMessage.FontFamily;
        ApplySurfaceAppearance();
    }
    private void ApplySurfaceAppearance()
    {
        var s = Model.Settings;
        IslandSurface.Background = IsEditMode ? new SolidColorBrush(Color.FromArgb(8, 255, 255, 255)) : System.Windows.Media.Brushes.Transparent;
        IslandSurface.BorderThickness = new Thickness(s.FlowBorder ? .8 : IsEditMode ? 1 : 0);
        IslandSurface.BorderBrush = s.FlowBorder ? flowBrush : System.Windows.Media.Brushes.LightGray;
        IslandBackgroundLayers.Visibility = s.ImageBackground ? Visibility.Visible : Visibility.Collapsed;
        IslandBackgroundLayers.Opacity = s.BackgroundOpacity;
        IslandBackgroundLayers.OpacityMask = IslandSurfaceAppearance.CreateFadeMask(s);
        var custom = s.ImageBackground ? new IslandBackgroundStore().Resolve(s.BackgroundImagePath) : null;
        IslandImageBrush.ImageSource = s.ImageBackground ? custom ?? IslandSurfaceAppearance.Background : null;
        IslandImageBrush.Viewbox = custom is null ? new Rect(.12, .1, .76, .35) : new Rect(0, 0, 1, 1);
        IslandImageBrush.AlignmentY = s.BackgroundAlignment switch { "Top" => AlignmentY.Top, "Bottom" => AlignmentY.Bottom, _ => AlignmentY.Center };
        IslandTintLayer.Background = new SolidColorBrush(Color.FromArgb((byte)(255 * s.BackgroundLighten), 255, 255, 255));
        UpdateBorderAnimation();
    }
    private void SystemAppearanceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SystemParameters.ClientAreaAnimation)) return;
        Dispatcher.InvokeAsync(() => { if (!closed) { UpdateBorderAnimation(); UpdateWidth(); } });
    }
    private void UpdateBorderAnimation()
    {
        if (Model is null) return;
        borderRotation.BeginAnimation(RotateTransform.AngleProperty, null);
        flowBrush.RelativeTransform = borderRotation;
        if (!closed && IsVisible && Model.Settings.FlowBorder && Model.Settings.AnimateBorder && SystemParameters.ClientAreaAnimation)
            borderRotation.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(24)) { RepeatBehavior = RepeatBehavior.Forever });
    }
    private void UpdateWidth()
    {
        var text = new FormattedText(Model.CompactText, CultureInfo.CurrentUICulture, System.Windows.FlowDirection.LeftToRight,
            new Typeface(CompactMessage.FontFamily, CompactMessage.FontStyle, CompactMessage.FontWeight, CompactMessage.FontStretch),
            CompactMessage.FontSize, System.Windows.Media.Brushes.Black, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        var metrics = new FormattedText(Model.MetricsText, CultureInfo.CurrentUICulture, System.Windows.FlowDirection.LeftToRight,
            new Typeface(CompactMetrics.FontFamily, CompactMetrics.FontStyle, CompactMetrics.FontWeight, CompactMetrics.FontStretch),
            CompactMetrics.FontSize, System.Windows.Media.Brushes.Black, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        widthSpring.Target = Math.Max(Model.IsMusicPlaying ? 286 : 144, text.WidthIncludingTrailingWhitespace + (Model.ShowMetrics ? metrics.WidthIncludingTrailingWhitespace + 10 : 0) + 32);
        heightSpring.Target = Model.IsMusicPlaying ? 42 : 36.4;
        if (!Model.Settings.StretchMotion || !SystemParameters.ClientAreaAnimation)
        {
            StopStretchRendering(); widthSpring.Reset(widthSpring.Target); heightSpring.Reset(heightSpring.Target); SetIslandWidth(widthSpring.Value); return;
        }
        if (stretching || (widthSpring.IsResting && heightSpring.IsResting)) return;
        stretching = true; lastStretchTime = dragClock.Elapsed.TotalSeconds;
        CompositionTarget.Rendering += RenderStretchFrame;
    }
    private void RenderStretchFrame(object? sender, EventArgs e)
    {
        if (!Model.Settings.StretchMotion || !SystemParameters.ClientAreaAnimation)
        { widthSpring.Reset(widthSpring.Target); heightSpring.Reset(heightSpring.Target); SetIslandWidth(widthSpring.Value); StopStretchRendering(); return; }
        var now = dragClock.Elapsed.TotalSeconds;
        widthSpring.Step(now - lastStretchTime); heightSpring.Step(now - lastStretchTime); lastStretchTime = now;
        SetIslandWidth(widthSpring.Value);
        if (widthSpring.IsResting && heightSpring.IsResting) { StopStretchRendering(); SnapshotChanged?.Invoke(this, EventArgs.Empty); }
    }
    private void SetIslandWidth(double value)
    {
        resizing = true;
        try
        {
            // 以中心为锚点伸缩，拖动期间保留鼠标位置；始终限制在当前显示器工作区内。
            var center = Left + Width / 2;
            var dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
            Width = Math.Floor(Math.Min(value, MaxWidth) * dpi) / dpi;
            Height = Math.Floor(heightSpring.Value * VisualTreeHelper.GetDpi(this).DpiScaleY) / VisualTreeHelper.GetDpi(this).DpiScaleY;
            var radius = new CornerRadius(Height / 2);
            IslandSurface.CornerRadius = IslandImageLayer.CornerRadius = IslandTintLayer.CornerRadius = radius;
            if (!dragging && double.IsFinite(center)) Left = center - Width / 2;
            ConstrainPosition();
            if (hoverCard?.IsVisible == true) PositionHoverCard();
        }
        finally { resizing = false; }
    }
    private void StopStretchRendering() { CompositionTarget.Rendering -= RenderStretchFrame; stretching = false; }
    public void SetEditMode(bool enabled)
    {
        if (closed) return;
        IsEditMode = enabled;
        ApplySurfaceAppearance();
        HideHover(); hoverInside = false;
        editTimeout.Stop();
        UpdateNativeInteraction();
        if (enabled) { editTimeout.Start(); Activate(); Focus(); }
    }
    private void UpdateNativeInteraction()
    {
        nativeInteraction?.Configure(Model.Settings.ClickThrough, Model.Settings.HoverDetails, IsEditMode);
        if (!Model.Settings.HoverDetails || IsEditMode) { HideHover(); hoverInside = false; }
    }
    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (IsEditMode && e.Key == Key.Escape) { SetEditMode(false); e.Handled = true; }
    }
    private void FinishEditing_Click(object sender, RoutedEventArgs e) => SetEditMode(false);
    private void EditTimeout_Tick(object? sender, EventArgs e) => SetEditMode(false);
    private void ObserveMouse(Point screenPoint)
    {
        if (closed || IsEditMode || !IsVisible || !Model.Settings.HoverDetails) return;
        lastHoverPoint = screenPoint;
        var local = PointFromScreen(screenPoint);
        var inside = new Rect(0, 0, ActualWidth, ActualHeight).Contains(local) && nativeInteraction?.IsUnobscuredAt(screenPoint) == true;
        if (!inside && hoverCard?.IsVisible == true)
        {
            var cardPoint = hoverCard.PointFromScreen(screenPoint);
            inside = new Rect(0, 0, hoverCard.ActualWidth, hoverCard.ActualHeight).Contains(cardPoint) && hoverCard.IsUnobscuredAt(screenPoint);
        }
        if (inside)
        {
            hoverDismiss.Stop();
            if (!hoverInside && hoverCard?.IsVisible != true) hoverDwell.Start();
        }
        else
        {
            hoverDwell.Stop();
            if (hoverCard?.IsVisible == true && !hoverDismiss.IsEnabled) hoverDismiss.Start();
        }
        hoverInside = inside;
    }
    private void HoverDwell_Tick(object? sender, EventArgs e)
    {
        hoverDwell.Stop();
        if (!hoverInside || closed || IsEditMode || !IsVisible || !Model.Settings.HoverDetails) return;
        if (nativeInteraction?.IsUnobscuredAt(lastHoverPoint) != true) { hoverInside = false; return; }
        if (hoverCard is null)
        {
            hoverCard = new IslandHoverWindow { DataContext = Model, Owner = this, FontFamily = CompactMessage.FontFamily };
            hoverCard.SizeChanged += (_, _) => { if (hoverCard?.IsVisible == true) PositionHoverCard(); };
        }
        PositionHoverCard();
        hoverCard.Show();
    }
    private void PositionHoverCard()
    {
        if (hoverCard is null) return;
        var work = GetWorkArea();
        var top = Top + ActualHeight + 4;
        if (top + hoverCard.Height > work.Bottom) top = Top - hoverCard.Height - 4;
        var position = IslandWindowPolicy.ClampPosition(new(Left + (ActualWidth - hoverCard.Width) / 2, top), work, new(hoverCard.Width, hoverCard.Height));
        hoverCard.Left = position.X; hoverCard.Top = position.Y;
    }
    private void HoverDismiss_Tick(object? sender, EventArgs e) => HideHover();
    private void HideHover() { hoverDwell.Stop(); hoverDismiss.Stop(); hoverCard?.Hide(); }
    private Rect GetWorkArea()
    {
        // Screen 的坐标是物理像素；通过当前 HWND 的 DPI 变换换成 WPF 坐标。
        var work = System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle).WorkingArea;
        var transform = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        return new Rect(transform.Transform(new Point(work.Left, work.Top)), transform.Transform(new Point(work.Right, work.Bottom)));
    }
    private void ConstrainPosition()
    {
        var point = IslandWindowPolicy.ClampPosition(new(Left, Top), GetWorkArea(), new(ActualWidth, ActualHeight));
        Left = point.X; Top = point.Y;
    }
    private void Settings_Click(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke(this, EventArgs.Empty);
    private void CloseIsland_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);
    private void ExitApplication_Click(object sender, RoutedEventArgs e) => ExitRequested?.Invoke(this, EventArgs.Empty);
    private async void TogglePlayback_Click(object sender, RoutedEventArgs e) => await Model.ControlMediaAsync(IslandMediaCommand.TogglePlayPause);
    private async void PreviousTrack_Click(object sender, RoutedEventArgs e) => await Model.ControlMediaAsync(IslandMediaCommand.Previous);
    private async void NextTrack_Click(object sender, RoutedEventArgs e) => await Model.ControlMediaAsync(IslandMediaCommand.Next);
}
