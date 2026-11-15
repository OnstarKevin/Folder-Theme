using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using FolderThemeStudio.App.Services;
using Brush = System.Windows.Media.Brush;
using Point = System.Windows.Point;
using ColorConverter = System.Windows.Media.ColorConverter;
using DragEventArgs = System.Windows.DragEventArgs;
using DragDropEffects = System.Windows.DragDropEffects;
using DataFormats = System.Windows.DataFormats;

namespace FolderThemeStudio.App;

public partial class FolderCategoryPopupWindow : Window, IFolderHoverCardPresenter
{
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;
    private const uint SwpShowWindow = 0x0040;
    private const uint MonitorDefaultToNearest = 2;
    private static readonly nint HwndTopmostHandle = new(-1);
    private readonly FolderLiveContentService liveContent = new();
    private readonly FolderDropService dropService = new();
    private ExplorerInfoTipSuppressor? infoTips;
    private nint handle;
    private int showRevision;
    private volatile bool shown;
    private bool dropInteractionEnabled;
    private string? currentFolderPath;
    private Rect lastItemBounds;

    public FolderCategoryPopupWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => ApplyOverlayStyle();
        Closed += (_, _) => infoTips?.Dispose();
    }

    public void Show(FolderCategoryEntry entry, Rect itemBounds)
    {
        currentFolderPath = entry.FolderPath;
        lastItemBounds = itemBounds;
        var revision = ++showRevision;
        CategoryTitle.Text = entry.Category;
        CategoryNote.Text = entry.Note;
        CategoryNote.Visibility = string.IsNullOrWhiteSpace(entry.Note) ? Visibility.Collapsed : Visibility.Visible;
        CategoryAccent.Background = new LinearGradientBrush(
            (System.Windows.Media.Color)ColorConverter.ConvertFromString(entry.GradientStartHex),
            (System.Windows.Media.Color)ColorConverter.ConvertFromString(entry.GradientEndHex),
            new Point(0, 0), new Point(0, 1));

        RecentFilesList.ItemsSource = null;
        FolderInfoText.Text = string.Empty;
        FolderInfoText.Visibility = Visibility.Collapsed;
        EmptyHint.Visibility = Visibility.Collapsed;
        DropTargetsPanel.Visibility = Visibility.Collapsed;
        DropStatus.Text = string.Empty;
        if (!IsVisible) base.Show();
        shown = true;
        UpdateLayout();
        PositionNear(itemBounds);
        _ = LoadContentAsync(entry.FolderPath, revision);
    }

    public new void Hide()
    {
        infoTips?.SetTarget(null);
        shown = false;
        showRevision++;
        if (IsVisible) base.Hide();
        SetDropInteractionEnabled(false);
    }

    public bool IsPointerOverCard(Point screenPoint)
    {
        if (!shown || handle == nint.Zero || !NativeMethods.GetWindowRect(handle, out var bounds)) return false;
        return screenPoint.X >= bounds.Left && screenPoint.X < bounds.Right &&
               screenPoint.Y >= bounds.Top && screenPoint.Y < bounds.Bottom;
    }

    public void SetCardOpacity(double opacity) => CardPanel.Opacity = Math.Clamp(opacity, 0, 1);

    public void SetInfoTipTarget(ExplorerHoverTarget? target)
    {
        if (target is not null && target.RootWindowHandle != nint.Zero) infoTips ??= new ExplorerInfoTipSuppressor();
        infoTips?.SetTarget(target);
    }

    public void SetDropInteractionEnabled(bool enabled)
    {
        if (dropInteractionEnabled == enabled) return;
        dropInteractionEnabled = enabled;
        if (handle != nint.Zero) ApplyOverlayStyle();
    }

    private void ApplyOverlayStyle()
    {
        handle = new WindowInteropHelper(this).Handle;
        var style = NativeMethods.GetWindowLongPtr(handle, NativeMethods.GwlExStyle).ToInt64();
        style |= NativeMethods.WsExNoActivate | NativeMethods.WsExToolWindow;
        if (dropInteractionEnabled) style &= ~NativeMethods.WsExTransparent;
        else style |= NativeMethods.WsExTransparent;
        NativeMethods.SetWindowLongPtr(handle, NativeMethods.GwlExStyle, new nint(style));
        NativeMethods.SetWindowPos(handle, nint.Zero, 0, 0, 0, 0,
            SwpNoSize | SwpNoMove | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);
    }

    private async Task LoadContentAsync(string path, int revision)
    {
        FolderLiveContent content;
        try { content = await Task.Run(() => liveContent.Read(path)).ConfigureAwait(false); }
        catch (Exception exception) when (exception is ArgumentException or System.IO.IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            content = new FolderLiveContent([], [new FolderDropTarget("", path)]);
        }
        if (Dispatcher.HasShutdownStarted) return;
        try { await Dispatcher.InvokeAsync(() =>
        {
            if (!shown || revision != showRevision || !string.Equals(currentFolderPath, path, StringComparison.OrdinalIgnoreCase)) return;
            RecentFilesList.ItemsSource = content.RecentFiles;
            if (content.Info is { } info)
            {
                var culture = System.Globalization.CultureInfo.CurrentUICulture;
                var format = Convert.ToString(TryFindResource("LiveFolder.InfoFormat")) ?? "Created: {0}\nModified: {1}\nContents: {2} files · {3} folders\nSize: {4}";
                var sizeText = FolderInfoSummary.FormatSize(info.SizeBytes);
                if (info.SizePartial) sizeText += Convert.ToString(TryFindResource("LiveFolder.InfoPartial")) ?? " (partial)";
                var prefix = info.ContentsPartial ? "≥ " : string.Empty;
                FolderInfoText.Text = string.Format(culture, format,
                    info.CreatedUtc.ToLocalTime().ToString("g", culture), info.ModifiedUtc.ToLocalTime().ToString("g", culture),
                    prefix + info.FileCount, prefix + info.FolderCount, sizeText);
                FolderInfoText.Visibility = Visibility.Visible;
            }
            EmptyHint.Visibility = content.RecentFiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            DropTargetsPanel.Children.Clear();
            foreach (var target in content.DropTargets)
            {
                var label = string.Equals(target.Path, path, StringComparison.OrdinalIgnoreCase)
                    ? Convert.ToString(TryFindResource("LiveFolder.ThisFolder")) ?? target.Name
                    : target.Name;
                var row = new Border
                {
                    Tag = target.Path,
                    AllowDrop = true,
                    Margin = new Thickness(0, 3, 0, 3),
                    Padding = new Thickness(8, 6, 8, 6),
                    CornerRadius = new CornerRadius(6),
                    Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(238, 244, 253)),
                    Child = new TextBlock { Text = label, Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(23, 32, 51)) }
                };
                row.DragOver += DropTarget_DragOver;
                row.Drop += DropTarget_Drop;
                DropTargetsPanel.Children.Add(row);
            }
            UpdateLayout();
            PositionNear(lastItemBounds);
        }); }
        catch (TaskCanceledException) { }
    }

    private void Card_DragEnter(object sender, DragEventArgs e)
    {
        if (GetDraggedFiles(e).Length == 0) return;
        DropTargetsPanel.Visibility = Visibility.Visible;
        UpdateLayout();
        PositionNear(lastItemBounds);
        e.Effects = DragDropEffects.None;
        e.Handled = true;
    }

    private void Card_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.None;
        e.Handled = true;
    }

    private void DropTarget_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = GetDraggedFiles(e).Length == 0 ? DragDropEffects.None : DragDropEffects.Move;
        e.Handled = true;
    }

    private async void DropTarget_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        var paths = GetDraggedFiles(e);
        if (paths.Length == 0 || sender is not Border { Tag: string destination } || currentFolderPath is null) return;
        var root = currentFolderPath;
        var revision = showRevision;
        DropTargetsPanel.Visibility = Visibility.Collapsed;
        var result = await Task.Run(() => dropService.Move(paths, root, destination));
        if (!shown || revision != showRevision) return;
        var format = Convert.ToString(TryFindResource("LiveFolder.MoveResult")) ?? "Moved {0} file(s); {1} failed";
        DropStatus.Text = string.Format(System.Globalization.CultureInfo.CurrentCulture, format, result.MovedCount, result.Errors.Count);
        DropStatus.ToolTip = result.Errors.Count == 0 ? null : string.Join(Environment.NewLine, result.Errors);
        await LoadContentAsync(root, revision);
    }

    private static string[] GetDraggedFiles(DragEventArgs e) =>
        e.Data.GetDataPresent(DataFormats.FileDrop) && e.Data.GetData(DataFormats.FileDrop) is string[] paths
            ? paths
            : [];

    private void PositionNear(Rect bounds)
    {
        if (handle == nint.Zero) handle = new WindowInteropHelper(this).Handle;
        if (!NativeMethods.GetWindowRect(handle, out var windowRect)) return;
        var monitor = NativeMethods.MonitorFromPoint(new NativeMethods.NativePoint((int)bounds.Left, (int)bounds.Top), MonitorDefaultToNearest);
        var info = new NativeMethods.MonitorInfo { Size = Marshal.SizeOf<NativeMethods.MonitorInfo>() };
        if (monitor == nint.Zero || !NativeMethods.GetMonitorInfo(monitor, ref info)) return;

        var width = windowRect.Right - windowRect.Left;
        var height = windowRect.Bottom - windowRect.Top;
        var x = (int)bounds.Right + 18;
        var y = (int)bounds.Top;
        if (x + width > info.Work.Right) x = (int)bounds.Left - width - 18;
        if (y + height > info.Work.Bottom) y = info.Work.Bottom - height;
        x = Math.Clamp(x, info.Work.Left, Math.Max(info.Work.Left, info.Work.Right - width));
        y = Math.Clamp(y, info.Work.Top, Math.Max(info.Work.Top, info.Work.Bottom - height));
        NativeMethods.SetWindowPos(handle, HwndTopmostHandle, x, y, 0, 0, SwpNoSize | SwpNoActivate | SwpShowWindow);
    }

    private static class NativeMethods
    {
        internal const int GwlExStyle = -20;
        internal const long WsExTransparent = 0x00000020L;
        internal const long WsExToolWindow = 0x00000080L;
        internal const long WsExNoActivate = 0x08000000L;

        [StructLayout(LayoutKind.Sequential)]
        internal struct NativePoint
        {
            internal int X;
            internal int Y;
            internal NativePoint(int x, int y) { X = x; Y = y; }
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct NativeRect { internal int Left; internal int Top; internal int Right; internal int Bottom; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        internal struct MonitorInfo
        {
            internal int Size;
            internal NativeRect Monitor;
            internal NativeRect Work;
            internal uint Flags;
        }

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
        internal static extern nint GetWindowLongPtr(nint hwnd, int index);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        internal static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetWindowRect(nint hwnd, out NativeRect rect);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern nint MonitorFromPoint(NativePoint point, uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetWindowPos(nint hwnd, nint insertAfter, int x, int y, int width, int height, uint flags);
    }
}
