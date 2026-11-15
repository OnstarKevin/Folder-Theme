using System.Windows;
using System.Windows.Media;
using FolderThemeStudio.Core.Themes;

namespace FolderThemeStudio.Core.Rendering;

public static class StickerPainter
{
    public static Point ClampPosition(Point relative, double scale, Rect face)
    {
        var diameter = scale * Math.Min(face.Width, face.Height);
        var padX = diameter / (2 * face.Width) + .02;
        var padY = diameter / (2 * face.Height) + .02;
        return new Point(
            Math.Clamp(relative.X, Math.Max(StickerPlacement.MinimumCenter, padX), Math.Min(StickerPlacement.MaximumCenter, 1 - padX)),
            Math.Clamp(relative.Y, Math.Max(StickerPlacement.MinimumCenter, padY), Math.Min(StickerPlacement.MaximumCenter, 1 - padY)));
    }

    public static void Draw(DrawingContext context, FolderTheme theme, Rect face, int size, bool simplify)
    {
        if (theme.Sticker == StickerStyle.None) return;

        var relative = ClampPosition(new Point(theme.StickerX, theme.StickerY), theme.StickerScale, face);
        var diameter = theme.StickerScale * Math.Min(face.Width, face.Height);
        var center = new Point(face.Left + face.Width * relative.X, face.Top + face.Height * relative.Y);
        var color = (Color)ColorConverter.ConvertFromString(theme.StickerColor)!;
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        var border = simplify ? null : new Pen(Brush("#34405C", .36), Math.Max(.01, 1d / size / diameter));

        context.PushClip(new RectangleGeometry(new Rect(face.Left + .01, face.Top + .01, face.Width - .02, face.Height - .02)));
        context.PushTransform(new TranslateTransform(center.X, center.Y));
        context.PushTransform(new RotateTransform(theme.StickerRotation));
        context.PushTransform(new ScaleTransform(diameter, diameter));

        switch (theme.Sticker)
        {
            case StickerStyle.Star:
                context.DrawGeometry(brush, border, Star());
                break;
            case StickerStyle.Flower:
                for (var petal = 0; petal < 5; petal++)
                {
                    var angle = petal * Math.PI * 2 / 5;
                    context.DrawEllipse(brush, border, new Point(Math.Sin(angle) * .28, -Math.Cos(angle) * .28), .2, .2);
                }
                context.DrawEllipse(Brush("#FFD568", 1), null, new Point(0, 0), .16, .16);
                break;
            case StickerStyle.Heart:
                context.DrawGeometry(brush, border, Heart());
                break;
            case StickerStyle.Paw:
                context.DrawEllipse(brush, border, new Point(0, .18), .32, .25);
                if (!simplify)
                {
                    foreach (var toe in new[] { new Point(-.32, -.20), new Point(-.10, -.34), new Point(.13, -.34), new Point(.34, -.20) })
                        context.DrawEllipse(brush, border, toe, .115, .145);
                }
                break;
        }

        context.Pop();
        context.Pop();
        context.Pop();
        context.Pop();
    }

    private static StreamGeometry Star()
    {
        var geometry = new StreamGeometry();
        using var writer = geometry.Open();
        var points = Enumerable.Range(0, 10).Select(index =>
        {
            var angle = -Math.PI / 2 + index * Math.PI / 5;
            var radius = index % 2 == 0 ? .48 : .22;
            return new Point(Math.Cos(angle) * radius, Math.Sin(angle) * radius);
        }).ToArray();
        writer.BeginFigure(points[0], true, true);
        writer.PolyLineTo(points.Skip(1).ToArray(), true, false);
        geometry.Freeze();
        return geometry;
    }

    private static StreamGeometry Heart()
    {
        var geometry = new StreamGeometry();
        using var writer = geometry.Open();
        writer.BeginFigure(new Point(0, .46), true, true);
        writer.BezierTo(new Point(-.55, .04), new Point(-.60, -.28), new Point(-.26, -.39), true, false);
        writer.BezierTo(new Point(-.08, -.46), new Point(0, -.31), new Point(0, -.22), true, false);
        writer.BezierTo(new Point(0, -.31), new Point(.08, -.46), new Point(.26, -.39), true, false);
        writer.BezierTo(new Point(.60, -.28), new Point(.55, .04), new Point(0, .46), true, false);
        geometry.Freeze();
        return geometry;
    }

    private static SolidColorBrush Brush(string value, double opacity)
    {
        var color = (Color)ColorConverter.ConvertFromString(value)!;
        color.A = (byte)Math.Round(opacity * byte.MaxValue);
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
