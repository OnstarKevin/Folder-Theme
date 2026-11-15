using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Point = System.Windows.Point;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;

namespace FolderThemeStudio.App.DynamicIsland;

public static class IslandSurfaceAppearance
{
    private static readonly Lazy<BitmapImage> OriginalSky = new(() =>
    {
        var image = new BitmapImage(); image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.DecodePixelWidth = 900;
        image.UriSource = new Uri("pack://application:,,,/FolderThemeStudio.App;component/Assets/IslandSky.png");
        image.EndInit(); image.Freeze();
        return image;
    });
    public static BitmapImage Background => OriginalSky.Value;

    public static Brush CreateFadeMask(IslandSettings settings)
    {
        var s = settings.Normalize();
        if (s.BackgroundFadeDirection == "None") return Brushes.White;
        var stops = new GradientStopCollection();
        for (var i = 0; i <= 16; i++)
        {
            var offset = i / 16d;
            var progress = Math.Clamp((offset - s.BackgroundFadeStart) / (1 - s.BackgroundFadeStart), 0, 1);
            if (s.BackgroundFadeProfile == "Smooth") progress = progress * progress * (3 - 2 * progress);
            var alpha = (byte)Math.Round(255 * (1 - s.BackgroundFadeStrength * progress));
            stops.Add(new GradientStop(Color.FromArgb(alpha, 255, 255, 255), offset));
        }
        Brush mask = s.BackgroundFadeDirection == "Edges"
            ? new RadialGradientBrush(stops) { Center = new(.5, .5), GradientOrigin = new(.5, .5), RadiusX = .5, RadiusY = .5 }
            : new LinearGradientBrush(stops,
                s.BackgroundFadeDirection switch { "Up" => new Point(0, 1), "Left" => new Point(1, 0), _ => new Point(0, 0) },
                s.BackgroundFadeDirection switch { "Up" => new Point(0, 0), "Left" => new Point(0, 0), "Right" => new Point(1, 0), _ => new Point(0, 1) });
        mask.Freeze(); return mask;
    }

    public static ImageBrush CreateImageBrush(BitmapSource source, IslandSettings settings, bool custom)
    {
        var brush = new ImageBrush(source) { Stretch = Stretch.UniformToFill,
            AlignmentY = settings.BackgroundAlignment switch { "Top" => AlignmentY.Top, "Bottom" => AlignmentY.Bottom, _ => AlignmentY.Center },
            Viewbox = custom ? new Rect(0, 0, 1, 1) : new Rect(.12, .1, .76, .35) };
        brush.Freeze(); return brush;
    }
}
