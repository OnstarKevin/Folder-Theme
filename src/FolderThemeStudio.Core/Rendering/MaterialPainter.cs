using System.Windows;
using System.Windows.Media;
using FolderThemeStudio.Core.Themes;

namespace FolderThemeStudio.Core.Rendering;

internal static class MaterialPainter
{
    public static void Draw(DrawingContext context, FolderTheme theme, Rect face, double radius, int size, bool simplify)
    {
        if (theme.Material == MaterialStyle.Glass) return;

        switch (theme.Material)
        {
            case MaterialStyle.Clay:
                context.DrawRoundedRectangle(Brush("#FFF4E8", 0.36), null, face, radius, radius);
                var inner = new Rect(face.Left + .018, face.Top + .025, face.Width - .036, face.Height - .05);
                context.DrawRoundedRectangle(null, new Pen(Brush("#FFFFFF", .48), 1d / size), inner, radius, radius);
                context.DrawLine(new Pen(Brush("#3F3451", .24), Math.Max(1d / size, .012)),
                    new Point(face.Left + .08, face.Bottom - .035), new Point(face.Right - .08, face.Bottom - .035));
                break;
            case MaterialStyle.FrostedGlass:
                context.DrawRoundedRectangle(Brush("#FFFFFF", .32), null, face, radius, radius);
                if (!simplify)
                {
                    context.DrawRoundedRectangle(null, new Pen(Brush("#FFFFFF", .55), 1d / size),
                        new Rect(face.Left + .015, face.Top + .015, face.Width - .03, face.Height - .03), radius, radius);
                    context.DrawLine(new Pen(Brush("#FFFFFF", .18), .045),
                        new Point(face.Left + .10, face.Top + .16), new Point(face.Right - .22, face.Bottom - .14));
                }
                break;
            case MaterialStyle.Paper:
                context.DrawRoundedRectangle(Brush("#FFE3AC", .48), null, face, radius, radius);
                if (!simplify)
                {
                    for (var y = face.Top + .17; y < face.Bottom - .04; y += .09)
                    {
                        context.DrawLine(new Pen(Brush("#745D54", .19), 1d / size),
                            new Point(face.Left + .06, y), new Point(face.Right - .06, y));
                    }
                }
                break;
        }
    }

    private static SolidColorBrush Brush(string hex, double opacity)
    {
        var color = (Color)ColorConverter.ConvertFromString(hex)!;
        color.A = (byte)Math.Round(opacity * byte.MaxValue);
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
