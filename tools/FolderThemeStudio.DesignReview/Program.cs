using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal static class Program
{
    private static Window? floating;
    private static readonly SolidColorBrush Ink = new(Color.FromRgb(43, 46, 58));
    private static readonly SolidColorBrush Muted = new(Color.FromRgb(111, 115, 128));
    private static readonly Choice[] Choices = [new("A", "Noto Sans SC", "半粗 · 推荐", FontWeights.SemiBold), new("B", "Microsoft YaHei UI", "微软雅黑 UI · 粗体", FontWeights.Bold), new("C", "Noto Serif SC", "宋体风格 · 半粗", FontWeights.SemiBold)];
    private static BitmapImage? background;
    private sealed record Choice(string Id, string Family, string Label, FontWeight Weight);
    [STAThread]
    public static void Main(string[] args)
    {
        var application = new Application();
        var capture = args.Length == 2 && args[0] == "--capture";
        var sheet = CreateSheet(!capture);
        var work = SystemParameters.WorkArea;
        var window = new Window { Title = "灵动岛字体审批 · 第1项", Width = Math.Min(730, work.Width - 32), Height = Math.Min(760, work.Height - 40), MinWidth = 420, Background = Brushes.WhiteSmoke, FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI"), Content = new ScrollViewer { Content = sheet, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, WindowStartupLocation = WindowStartupLocation.CenterScreen };
        window.Closed += (_, _) => { floating?.Close(); floating = null; };
        if (capture)
        {
            window.ContentRendered += (_, _) =>
            {
                sheet.Width = 680;
                sheet.Measure(new Size(680, double.PositiveInfinity)); sheet.Arrange(new Rect(sheet.DesiredSize)); sheet.UpdateLayout();
                var visual = new DrawingVisual();
                using (var drawing = visual.RenderOpen())
                {
                    drawing.DrawRectangle(Brushes.WhiteSmoke, null, new Rect(sheet.RenderSize));
                    var brush = new VisualBrush(sheet) { ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(sheet.RenderSize), ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(sheet.RenderSize), Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top };
                    drawing.DrawRectangle(brush, null, new Rect(sheet.RenderSize));
                }
                var last = (FrameworkElement)sheet.Children[sheet.Children.Count - 1];
                var contentBottom = last.TransformToAncestor(sheet).TransformBounds(new Rect(last.RenderSize)).Bottom + 24;
                var image = new RenderTargetBitmap((int)Math.Ceiling(sheet.ActualWidth * 2), (int)Math.Ceiling(Math.Min(sheet.ActualHeight, contentBottom) * 2), 192, 192, PixelFormats.Pbgra32);
                image.Render(visual);
                var output = Path.GetFullPath(args[1]); Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
                using (var stream = File.Create(output)) encoder.Save(stream);
                var fonts = Choices.Select(c => new { c.Id, c.Family, c.Label, Installed = Fonts.SystemFontFamilies.Any(f => f.Source.Equals(c.Family, StringComparison.OrdinalIgnoreCase) || f.FamilyNames.Values.Contains(c.Family)) });
                File.WriteAllText(Path.ChangeExtension(output, ".json"), JsonSerializer.Serialize(fonts));
                application.Shutdown();
            };
        }
        application.Run(window);
    }
    private static StackPanel CreateSheet(bool interactive)
    {
        var stack = new StackPanel { Margin = new Thickness(30, 24, 30, 24), Background = Brushes.WhiteSmoke };
        stack.Children.Add(Text("先确认：字、尺寸与原来的背景", 24, FontWeights.SemiBold));
        stack.Children.Add(Text("26 → 36.4 DIP  ·  高度 +40%  ·  单行 / 最长 302 DIP", 13, FontWeights.Normal, true, new Thickness(0, 9, 0, 16)));
        stack.Children.Add(Text("当前版本 · 11.5 常规 / 26 DIP", 12, FontWeights.Normal, true));
        var previous = new Border { Height = 45, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 14) };
        previous.Child = new TextBlock { Text = "♫ 晚风经过，故事继续。 02:16", FontSize = 11.5, FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI"), Foreground = Ink, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 14, 0) };
        stack.Children.Add(previous);
        foreach (var choice in Choices)
        {
            var row = new StackPanel { Margin = new Thickness(0, 0, 0, 19) };
            row.Children.Add(Text($"{choice.Id}   {choice.Family}   /   {choice.Label}", 13, FontWeights.SemiBold));
            var line = new Grid { Margin = new Thickness(0, 11, 0, 0) };
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            line.Children.Add(Pill(choice));
            if (interactive)
            {
                var button = new Button { Content = "桌面试用 " + choice.Id, Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(20, 0, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
                button.Click += (_, _) => ShowFloating(choice); Grid.SetColumn(button, 1); line.Children.Add(button);
            }
            row.Children.Add(line); stack.Children.Add(row);
        }
        stack.Children.Add(Text("同一字号 14 DIP · 原《天色》图片淡化并向下渐隐", 12, FontWeights.Normal, true, new Thickness(0, 0, 0, 7)));
        stack.Children.Add(Text("桌面预览保留点击穿透；背景与字体审批不会修改正式软件。", 12, FontWeights.Normal, true));
        if (interactive)
        {
            var close = new Button { Content = "关闭桌面预览", Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(0, 18, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
            close.Click += (_, _) => { floating?.Close(); floating = null; }; stack.Children.Add(close);
        }
        return stack;
    }
    private static TextBlock Text(string text, double size, FontWeight weight, bool muted = false, Thickness? margin = null) => new() { Text = text, FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI"), FontSize = size, FontWeight = weight, Foreground = muted ? Muted : Ink, Margin = margin ?? new Thickness(0), TextWrapping = TextWrapping.Wrap };
    private static Border Pill(Choice choice)
    {
        background ??= new BitmapImage(new Uri("pack://application:,,,/FolderThemeStudio.FontReview;component/OriginalIslandSky.png"));
        var image = new Border { CornerRadius = new CornerRadius(18.2), Background = new ImageBrush(background) { Stretch = Stretch.UniformToFill, Viewbox = new Rect(.12, .1, .76, .35) }, OpacityMask = new LinearGradientBrush(new GradientStopCollection { new(Colors.White, 0), new(Color.FromArgb(80, 255, 255, 255), .55), new(Colors.Transparent, 1) }, new Point(0, 0), new Point(0, 1)) };
        var grid = new Grid(); grid.Children.Add(image);
        grid.Children.Add(new Border { Background = new SolidColorBrush(Color.FromArgb(72, 255, 255, 255)), CornerRadius = new CornerRadius(18.2) });
        grid.Children.Add(new TextBlock { Text = "♫ 晚风经过，故事继续。 02:16", FontFamily = new FontFamily(choice.Family + ", Microsoft YaHei UI"), FontWeight = choice.Weight, FontSize = 14, Foreground = Ink, TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 14, 0) });
        return new Border { Width = 302.3622, Height = 36.4, HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(18.2), Background = new SolidColorBrush(Color.FromArgb(220, 247, 244, 251)), BorderBrush = new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)), BorderThickness = new Thickness(1), Child = grid };
    }
    private static void ShowFloating(Choice choice)
    {
        floating?.Close();
        var work = SystemParameters.WorkArea;
        floating = new Window { Title = "字体审批预览 " + choice.Id, Width = 302.3622, Height = 36.4, Left = work.Left + (work.Width - 302.3622) / 2, Top = work.Top + 22, AllowsTransparency = true, Background = Brushes.Transparent, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, Topmost = true, ShowInTaskbar = false, ShowActivated = false, Content = Pill(choice) };
        floating.SourceInitialized += (_, _) => { var handle = new WindowInteropHelper(floating).Handle; SetWindowLongPtr(handle, -20, new nint(GetWindowLongPtr(handle, -20).ToInt64() | 0x08000020)); };
        floating.Show();
    }
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern nint SetWindowLongPtr(nint window, int index, nint value);
}
