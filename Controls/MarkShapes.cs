using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace PlayniteCharts.Controls
{
    /// <summary>
    /// Secondary (non-colour) encoding channel. Eight silhouettes that stay
    /// distinguishable at the 8px minimum mark size; slots are assigned in fixed
    /// order and never cycled - past the last one categories fold into "Other".
    /// </summary>
    public static class MarkShapes
    {
        public const int Count = 8;

        /// <summary>Geometry centred on <paramref name="c"/> with the given outer radius.</summary>
        public static Geometry Create(int slot, Point c, double r)
        {
            switch (((slot % Count) + Count) % Count)
            {
                case 0:
                    return new EllipseGeometry(c, r, r);
                case 1:
                    {
                        // matched area with the circle so size stays comparable across shapes
                        var h = r * 0.8862;
                        return new RectangleGeometry(new Rect(c.X - h, c.Y - h, h * 2, h * 2));
                    }
                case 2:
                    return Polygon(c, r * 1.35, 3, -90);
                case 3:
                    return Polygon(c, r * 1.25, 4, -90);
                case 4:
                    return Polygon(c, r * 1.35, 3, 90);
                case 5:
                    return Bar(c, r, false);
                case 6:
                    return Bar(c, r, true);
                default:
                    return Polygon(c, r * 1.07, 6, -90);
            }
        }

        private static Geometry Polygon(Point c, double r, int sides, double startDeg)
        {
            var pts = new List<Point>(sides);
            for (var i = 0; i < sides; i++)
            {
                var a = (startDeg + i * 360.0 / sides) * Math.PI / 180.0;
                pts.Add(new Point(c.X + r * Math.Cos(a), c.Y + r * Math.Sin(a)));
            }

            var fig = new PathFigure { StartPoint = pts[0], IsClosed = true, IsFilled = true };
            for (var i = 1; i < pts.Count; i++)
            {
                fig.Segments.Add(new LineSegment(pts[i], true));
            }

            var g = new PathGeometry();
            g.Figures.Add(fig);
            return g;
        }

        /// <summary>Plus / cross built from two overlapping bars so it still reads when filled.</summary>
        private static Geometry Bar(Point c, double r, bool rotated)
        {
            var len = r * 1.3;
            var thick = r * 0.52;
            var g = new GeometryGroup { FillRule = FillRule.Nonzero };
            g.Children.Add(new RectangleGeometry(new Rect(c.X - len, c.Y - thick, len * 2, thick * 2)));
            g.Children.Add(new RectangleGeometry(new Rect(c.X - thick, c.Y - len, thick * 2, len * 2)));
            if (rotated)
            {
                g.Transform = new RotateTransform(45, c.X, c.Y);
            }

            return g;
        }
    }
}
