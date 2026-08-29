using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace PcDashApp.Logos;

/// <summary>
/// Draws the T52 generic-icon fallback set as plain outline shapes -- built from
/// primitive WPF shapes (Line/Rectangle/Ellipse/Polyline) on a fixed 24x24 canvas,
/// not sourced brand marks, matching the mockup's own framing ("plain hand-drawn
/// outline shapes"). Kept as code (not a .xaml ResourceDictionary/DataTemplate per
/// icon) so a new icon is just one more case here, no XAML resource wiring needed.
/// </summary>
public static class GenericIconFactory
{
    private const double ArtboardSize = 24;

    /// <summary>Builds a fresh visual for <paramref name="iconId"/>, scaled to
    /// <paramref name="size"/> device-independent pixels square. Returns a plain
    /// question-mark glyph for an unrecognized id rather than throwing -- a future
    /// client with a newer icon set added should still render *something* against
    /// an older picker/library, not crash.</summary>
    public static UIElement CreateIcon(string iconId, double size, Brush? stroke = null)
    {
        stroke ??= Brushes.DimGray;
        var canvas = new Canvas { Width = ArtboardSize, Height = ArtboardSize };
        BuildInto(canvas, iconId, stroke);

        return new Viewbox
        {
            Width = size,
            Height = size,
            Child = canvas
        };
    }

    private static void BuildInto(Canvas c, string iconId, Brush stroke)
    {
        switch (iconId)
        {
            case GenericIconIds.Folder:
                AddPolyline(c, stroke, (3, 6), (9, 6), (11, 8), (21, 8), (21, 19), (3, 19), (3, 6));
                break;

            case GenericIconIds.Terminal:
                AddPolyline(c, stroke, (3, 4), (21, 4), (21, 20), (3, 20), (3, 4));
                AddPolyline(c, stroke, (6, 9), (10, 12), (6, 15));
                AddLine(c, stroke, 12, 16, 17, 16);
                break;

            case GenericIconIds.Grid:
                AddRect(c, stroke, 3, 3, 7, 7);
                AddRect(c, stroke, 14, 3, 7, 7);
                AddRect(c, stroke, 3, 14, 7, 7);
                AddRect(c, stroke, 14, 14, 7, 7);
                break;

            case GenericIconIds.Cube:
                AddPolyline(c, stroke, (12, 3), (20, 7.5), (20, 16.5), (12, 21), (4, 16.5), (4, 7.5), (12, 3));
                AddLine(c, stroke, 12, 3, 12, 12);
                AddLine(c, stroke, 12, 12, 20, 7.5);
                AddLine(c, stroke, 12, 12, 4, 7.5);
                break;

            case GenericIconIds.Chip:
                AddRect(c, stroke, 8, 8, 8, 8);
                AddLine(c, stroke, 10, 8, 10, 4);
                AddLine(c, stroke, 14, 8, 14, 4);
                AddLine(c, stroke, 10, 16, 10, 20);
                AddLine(c, stroke, 14, 16, 14, 20);
                AddLine(c, stroke, 8, 10, 4, 10);
                AddLine(c, stroke, 8, 14, 4, 14);
                AddLine(c, stroke, 16, 10, 20, 10);
                AddLine(c, stroke, 16, 14, 20, 14);
                break;

            case GenericIconIds.Gear:
                AddEllipse(c, stroke, 12, 12, 5);
                AddEllipse(c, stroke, 12, 12, 1.6, fill: stroke);
                foreach (var angleDeg in new double[] { 0, 45, 90, 135, 180, 225, 270, 315 })
                {
                    var rad = angleDeg * Math.PI / 180.0;
                    var (dx, dy) = (Math.Cos(rad), Math.Sin(rad));
                    AddLine(c, stroke, 12 + dx * 7, 12 + dy * 7, 12 + dx * 10, 12 + dy * 10);
                }
                break;

            case GenericIconIds.Layers:
                AddPolyline(c, stroke, (12, 3), (21, 8), (12, 13), (3, 8), (12, 3));
                AddPolyline(c, stroke, (3, 12), (12, 17), (21, 12));
                AddPolyline(c, stroke, (3, 16), (12, 21), (21, 16));
                break;

            case GenericIconIds.Flask:
                AddPolyline(c, stroke, (10, 3), (10, 9), (4, 19), (4, 21), (20, 21), (20, 19), (14, 9), (14, 3));
                AddLine(c, stroke, 9, 3, 15, 3);
                AddLine(c, stroke, 7, 16, 17, 16);
                break;

            default:
                var tb = new TextBlock
                {
                    Text = "?",
                    FontSize = 16,
                    FontWeight = FontWeights.Bold,
                    Foreground = stroke,
                    Width = ArtboardSize,
                    Height = ArtboardSize,
                    TextAlignment = TextAlignment.Center
                };
                Canvas.SetTop(tb, 2);
                c.Children.Add(tb);
                break;
        }
    }

    private static void AddLine(Canvas c, Brush stroke, double x1, double y1, double x2, double y2)
    {
        c.Children.Add(new Line
        {
            X1 = x1, Y1 = y1, X2 = x2, Y2 = y2,
            Stroke = stroke, StrokeThickness = 1.6,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round
        });
    }

    private static void AddPolyline(Canvas c, Brush stroke, params (double X, double Y)[] points)
    {
        var pc = new PointCollection(points.Select(p => new Point(p.X, p.Y)));
        c.Children.Add(new Polyline
        {
            Points = pc, Stroke = stroke, StrokeThickness = 1.6,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            Fill = Brushes.Transparent
        });
    }

    private static void AddRect(Canvas c, Brush stroke, double x, double y, double w, double h)
    {
        var r = new Rectangle
        {
            Width = w, Height = h, Stroke = stroke, StrokeThickness = 1.6, Fill = Brushes.Transparent
        };
        Canvas.SetLeft(r, x);
        Canvas.SetTop(r, y);
        c.Children.Add(r);
    }

    private static void AddEllipse(Canvas c, Brush stroke, double cx, double cy, double r, Brush? fill = null)
    {
        var e = new Ellipse
        {
            Width = r * 2, Height = r * 2, Stroke = stroke, StrokeThickness = 1.6,
            Fill = fill ?? Brushes.Transparent
        };
        Canvas.SetLeft(e, cx - r);
        Canvas.SetTop(e, cy - r);
        c.Children.Add(e);
    }
}
