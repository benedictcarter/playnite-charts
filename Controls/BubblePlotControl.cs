using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using PlayniteCharts.Model;

namespace PlayniteCharts.Controls
{
    /// <summary>
    /// Retained-mode bubble plot. The chart itself is drawn once per model/size
    /// change into one visual; the hover ring and tooltip live in a second visual
    /// so moving the mouse never re-renders thousands of marks.
    /// </summary>
    public class BubblePlotControl : FrameworkElement
    {
        /// <summary>
        /// Fill opacity by how crowded the panel is - a few hundred marks read best
        /// solid, a few thousand only read at all if they are translucent.
        /// </summary>
        private static double MarkOpacity(int count)
        {
            if (count <= 150)
            {
                return 0.9;
            }

            return count >= 1200 ? 0.5 : 0.9 - (count - 150) * 0.4 / 1050;
        }

        private readonly DrawingVisual chartVisual = new DrawingVisual();
        private readonly DrawingVisual overlayVisual = new DrawingVisual();
        private readonly VisualCollection visuals;

        private Rect plotRect;
        private PlotTheme theme;
        private PlotPoint hovered;
        private List<PlotPoint> drawOrder = new List<PlotPoint>();
        private double pixelsPerDip = 1.0;

        public event EventHandler<PlotPoint> PointActivated;

        public static readonly DependencyProperty ModelProperty = DependencyProperty.Register(
            nameof(Model), typeof(PlotModel), typeof(BubblePlotControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnModelChanged));

        public PlotModel Model
        {
            get => (PlotModel)GetValue(ModelProperty);
            set => SetValue(ModelProperty, value);
        }

        public BubblePlotControl()
        {
            visuals = new VisualCollection(this) { chartVisual, overlayVisual };
            ClipToBounds = true;
            Focusable = false;
            Loaded += (s, e) =>
            {
                pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
                theme = null;
                Redraw();
            };
        }

        protected override int VisualChildrenCount => visuals.Count;

        protected override Visual GetVisualChild(int index) => visuals[index];

        private static void OnModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var c = (BubblePlotControl)d;
            c.hovered = null;
            c.Redraw();
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo info)
        {
            base.OnRenderSizeChanged(info);
            Redraw();
        }

        /// <summary>Forces the palette to be re-picked, e.g. after a theme switch.</summary>
        public void InvalidateTheme()
        {
            theme = null;
            Redraw();
        }

        private PlotTheme EnsureTheme()
        {
            // Re-resolve every redraw: the first draw can happen before this control
            // is parented (or the Playnite theme can change under it), and a theme
            // cached from the fallback surface would then be wrong forever.
            var surface = PlotTheme.ResolveSurface(this, Color.FromRgb(0x15, 0x1D, 0x38));
            if (theme == null || theme.Surface != surface)
            {
                theme = PlotTheme.ForSurface(surface);
            }

            return theme;
        }

        // ---------------------------------------------------------------- drawing

        private void Redraw()
        {
            var t = EnsureTheme();
            using (var dc = chartVisual.RenderOpen())
            {
                // transparent fill so the theme shows through but the control still hit-tests
                dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));

                var m = Model;
                if (m == null || ActualWidth < 60 || ActualHeight < 60)
                {
                    return;
                }

                if (m.Problem != null)
                {
                    var msg = Text(m.Problem, t.InkMutedBrush, 13);
                    dc.DrawText(msg, new Point((ActualWidth - msg.Width) / 2, ActualHeight / 2 - msg.Height));
                    return;
                }

                var legendWidth = m.Config.ShowLegend ? MeasureLegend(m, t) : 0;
                var yLabels = m.YTicks.Select(k => Text(k.Label, t.InkMutedBrush, 11)).ToList();
                var leftPad = (yLabels.Count > 0 ? yLabels.Max(l => l.Width) : 0) + 30;
                var bottomPad = 44.0;
                var topPad = 26.0;
                var rightPad = legendWidth > 0 ? legendWidth + 20 : 14;

                plotRect = new Rect(leftPad, topPad,
                    Math.Max(10, ActualWidth - leftPad - rightPad),
                    Math.Max(10, ActualHeight - topPad - bottomPad));

                DrawGrid(dc, m, t, yLabels);
                DrawMarks(dc, m, t);
                DrawAxisTitles(dc, m, t);
                DrawHeader(dc, m, t);
                if (legendWidth > 0)
                {
                    DrawLegend(dc, m, t, new Rect(plotRect.Right + 20, topPad, legendWidth, plotRect.Height));
                }
            }

            RedrawOverlay();
        }

        private void DrawGrid(DrawingContext dc, PlotModel m, PlotTheme t, List<FormattedText> yLabels)
        {
            for (var i = 0; i < m.YTicks.Count; i++)
            {
                var y = Snap(ToScreenY(m, m.YTicks[i].Value));
                if (y < plotRect.Top - 1 || y > plotRect.Bottom + 1)
                {
                    continue;
                }

                dc.DrawLine(t.GridPen, new Point(plotRect.Left, y), new Point(plotRect.Right, y));
                var lbl = yLabels[i];
                dc.DrawText(lbl, new Point(plotRect.Left - 8 - lbl.Width, y - lbl.Height / 2));
            }

            foreach (var tick in m.XTicks)
            {
                var x = Snap(ToScreenX(m, tick.Value));
                if (x < plotRect.Left - 1 || x > plotRect.Right + 1)
                {
                    continue;
                }

                dc.DrawLine(t.GridPen, new Point(x, plotRect.Top), new Point(x, plotRect.Bottom));
                var lbl = Text(tick.Label, t.InkMutedBrush, 11);
                dc.DrawText(lbl, new Point(x - lbl.Width / 2, plotRect.Bottom + 6));
            }

            dc.DrawLine(t.AxisPen, new Point(Snap(plotRect.Left), plotRect.Top), new Point(Snap(plotRect.Left), Snap(plotRect.Bottom)));
            dc.DrawLine(t.AxisPen, new Point(Snap(plotRect.Left), Snap(plotRect.Bottom)), new Point(plotRect.Right, Snap(plotRect.Bottom)));
        }

        private void DrawMarks(DrawingContext dc, PlotModel m, PlotTheme t)
        {
            // biggest first so small marks stay clickable and visible on top
            drawOrder = m.Points.OrderByDescending(p => p.Radius).ToList();
            dc.PushOpacity(MarkOpacity(m.Points.Count));
            foreach (var p in drawOrder)
            {
                var c = new Point(ToScreenX(m, p.X), ToScreenY(m, p.Y));
                var geo = MarkShapes.Create(p.ShapeSlot, c, p.Radius);
                dc.DrawGeometry(t.SeriesBrush(p.ColorSlot), t.RingPen, geo);
            }

            dc.Pop();
        }

        private void DrawAxisTitles(DrawingContext dc, PlotModel m, PlotTheme t)
        {
            var xTitle = Text(m.XField.Name, t.InkBrush, 12);
            dc.DrawText(xTitle, new Point(plotRect.Left + (plotRect.Width - xTitle.Width) / 2, plotRect.Bottom + 24));

            var yTitle = Text(m.YField.Name, t.InkBrush, 12);
            dc.PushTransform(new RotateTransform(-90, 12, plotRect.Top + plotRect.Height / 2));
            dc.DrawText(yTitle, new Point(12 - yTitle.Width / 2, plotRect.Top + plotRect.Height / 2 - yTitle.Height / 2));
            dc.Pop();
        }

        private void DrawHeader(DrawingContext dc, PlotModel m, PlotTheme t)
        {
            var skipped = m.TotalGames - m.PlottedGames;
            var text = skipped > 0
                ? $"{m.PlottedGames:N0} games plotted  ({skipped:N0} without a value on both axes)"
                : $"{m.PlottedGames:N0} games plotted";
            dc.DrawText(Text(text, t.InkMutedBrush, 11), new Point(plotRect.Left, 6));
        }

        // ---------------------------------------------------------------- legend

        private const double SwatchSize = 13;
        private const double RowHeight = 19;

        private IEnumerable<string> LegendLines(PlotModel m)
        {
            if (m.ColorScale != null)
            {
                yield return m.ColorScale.Field.Name;
                foreach (var d in m.ColorScale.Domain)
                {
                    yield return d;
                }
            }

            if (m.ShapeScale != null)
            {
                yield return m.ShapeScale.Field.Name;
                foreach (var d in m.ShapeScale.Domain.Take(MarkShapes.Count))
                {
                    yield return d;
                }
            }

            if (m.SizeField != null)
            {
                yield return m.SizeField.Name;
            }
        }

        private double MeasureLegend(PlotModel m, PlotTheme t)
        {
            var max = 0.0;
            foreach (var line in LegendLines(m))
            {
                max = Math.Max(max, Text(line, t.InkBrush, 11).Width + SwatchSize + 10);
            }

            if (m.SizeField != null)
            {
                // the size swatches are bubbles, so their column is as wide as the biggest one
                foreach (var v in SizeLegendValues(m))
                {
                    max = Math.Max(max, Text(m.SizeField.Format(v), t.InkBrush, 11).Width + m.MaxRadius * 2 + 10);
                }
            }

            return max <= 0 ? 0 : Math.Min(240, max);
        }

        private void DrawLegend(DrawingContext dc, PlotModel m, PlotTheme t, Rect area)
        {
            var y = area.Top;

            if (m.ColorScale != null)
            {
                y = LegendHeading(dc, t, area, y, m.ColorScale.Field.Name);
                for (var i = 0; i < m.ColorScale.Domain.Count; i++)
                {
                    var isOther = m.ColorScale.HasOverflow && i == m.ColorScale.Domain.Count - 1;
                    var slot = isOther ? PlotTheme.SeriesCapacity : i;
                    dc.DrawRoundedRectangle(t.SeriesBrush(slot), null,
                        new Rect(area.Left, y + 2, SwatchSize, SwatchSize), 3, 3);
                    dc.DrawText(Ellipsize(m.ColorScale.Domain[i], t.InkBrush, 11, area.Width - SwatchSize - 8),
                        new Point(area.Left + SwatchSize + 7, y));
                    y += RowHeight;
                }

                y += 8;
            }

            if (m.ShapeScale != null)
            {
                y = LegendHeading(dc, t, area, y, m.ShapeScale.Field.Name);
                for (var i = 0; i < m.ShapeScale.Domain.Count && i < MarkShapes.Count; i++)
                {
                    var geo = MarkShapes.Create(i, new Point(area.Left + SwatchSize / 2, y + 2 + SwatchSize / 2), 6);
                    dc.DrawGeometry(t.InkMutedBrush, null, geo);
                    dc.DrawText(Ellipsize(m.ShapeScale.Domain[i], t.InkBrush, 11, area.Width - SwatchSize - 8),
                        new Point(area.Left + SwatchSize + 7, y));
                    y += RowHeight;
                }

                y += 8;
            }

            if (m.SizeField != null && m.Points.Count > 0)
            {
                y = LegendHeading(dc, t, area, y, m.SizeField.Name);
                var cx = area.Left + m.MaxRadius + 1;
                foreach (var v in SizeLegendValues(m))
                {
                    var r = m.RadiusFor(v);
                    var row = Math.Max(RowHeight, r * 2 + 5);
                    var cy = y + row / 2;
                    dc.DrawGeometry(null, t.SizeRingPen, MarkShapes.Create(0, new Point(cx, cy), r));
                    var lbl = Ellipsize(m.SizeField.Format(v), t.InkBrush, 11,
                        Math.Max(20, area.Width - m.MaxRadius * 2 - 8));
                    dc.DrawText(lbl, new Point(area.Left + m.MaxRadius * 2 + 8, cy - lbl.Height / 2));
                    y += row;
                }
            }
        }

        /// <summary>Smallest / middle / largest, deduped - the usual three-circle key.</summary>
        private static IEnumerable<double> SizeLegendValues(PlotModel m)
        {
            if (!m.HasSizeRange)
            {
                yield return m.SizeMax;
                yield break;
            }

            yield return m.SizeMin;
            var mid = (m.SizeMin + m.SizeMax) / 2;
            if (Math.Abs(mid - m.SizeMin) > 1e-9 && Math.Abs(mid - m.SizeMax) > 1e-9)
            {
                yield return mid;
            }

            yield return m.SizeMax;
        }

        private double LegendHeading(DrawingContext dc, PlotTheme t, Rect area, double y, string title)
        {
            var head = Ellipsize(title.ToUpperInvariant(), t.InkMutedBrush, 10, area.Width);
            dc.DrawText(head, new Point(area.Left, y));
            return y + head.Height + 5;
        }

        // ---------------------------------------------------------------- hover

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var hit = HitTest(e.GetPosition(this));
            if (!ReferenceEquals(hit, hovered))
            {
                hovered = hit;
                Cursor = hit != null ? Cursors.Hand : Cursors.Arrow;
                RedrawOverlay();
            }
        }

        protected override void OnMouseLeave(MouseEventArgs e)
        {
            base.OnMouseLeave(e);
            if (hovered != null)
            {
                hovered = null;
                RedrawOverlay();
            }
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);
            var hit = HitTest(e.GetPosition(this));
            if (hit != null)
            {
                PointActivated?.Invoke(this, hit);
            }
        }

        private PlotPoint HitTest(Point pos)
        {
            var m = Model;
            if (m == null || m.Points.Count == 0 || plotRect.Width <= 0)
            {
                return null;
            }

            PlotPoint best = null;
            var bestDist = double.MaxValue;
            // reverse of draw order: smallest marks are on top, so they win ties
            for (var i = drawOrder.Count - 1; i >= 0; i--)
            {
                var p = drawOrder[i];
                var dx = pos.X - ToScreenX(m, p.X);
                var dy = pos.Y - ToScreenY(m, p.Y);
                var d = Math.Sqrt(dx * dx + dy * dy);
                if (d <= p.Radius + 2 && d < bestDist)
                {
                    best = p;
                    bestDist = d;
                }
            }

            return best;
        }

        private void RedrawOverlay()
        {
            using (var dc = overlayVisual.RenderOpen())
            {
                var m = Model;
                var p = hovered;
                if (m == null || p == null)
                {
                    return;
                }

                var t = EnsureTheme();
                var c = new Point(ToScreenX(m, p.X), ToScreenY(m, p.Y));

                // halo: ink ring outside a surface ring, so it reads on any mark colour
                var ringR = p.Radius + 3.5;
                dc.DrawEllipse(null, new Pen(t.SurfaceBrush, 3), c, ringR, ringR);
                dc.DrawEllipse(null, new Pen(t.InkBrush, 1.5), c, ringR, ringR);
                dc.DrawGeometry(t.SeriesBrush(p.ColorSlot), t.RingPen, MarkShapes.Create(p.ShapeSlot, c, p.Radius));

                DrawTooltip(dc, m, t, p, c);
            }
        }

        private void DrawTooltip(DrawingContext dc, PlotModel m, PlotTheme t, PlotPoint p, Point anchor)
        {
            var lines = new List<Tuple<string, string>>();
            foreach (var f in m.HoverFields)
            {
                // the card's title is already the game name
                if (string.Equals(f.Id, "name", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var v = f.Display(p.Game);
                if (!string.IsNullOrEmpty(v))
                {
                    lines.Add(Tuple.Create(f.Name, v));
                }
            }

            void AddChannel(GameColumn f, string value)
            {
                if (f == null || string.IsNullOrEmpty(value) || lines.Any(l => l.Item1 == f.Name))
                {
                    return;
                }

                lines.Add(Tuple.Create(f.Name, value));
            }

            AddChannel(m.XField, m.XField.Display(p.Game));
            AddChannel(m.YField, m.YField.Display(p.Game));
            AddChannel(m.SizeField, m.SizeField?.Display(p.Game));
            AddChannel(m.ColorScale?.Field, p.ColorKey);
            AddChannel(m.ShapeScale?.Field, p.ShapeKey);

            var title = Text(p.Game.Name ?? "(no name)", t.InkBrush, 12, FontWeights.SemiBold);
            var keys = lines.Select(l => Text(l.Item1, t.InkMutedBrush, 11)).ToList();
            var vals = lines.Select(l => Ellipsize(l.Item2, t.InkBrush, 11, 240)).ToList();

            var keyW = keys.Count > 0 ? keys.Max(k => k.Width) : 0;
            var valW = vals.Count > 0 ? vals.Max(v => v.Width) : 0;
            const double pad = 9, gap = 12;
            var w = Math.Max(title.Width, keyW + gap + valW) + pad * 2;
            var h = title.Height + 4 + keys.Sum(k => k.Height + 2) + pad * 2;

            // keep the card inside the control, flipping around the cursor as needed
            var x = anchor.X + p.Radius + 12;
            var y = anchor.Y - h / 2;
            if (x + w > ActualWidth - 4) x = anchor.X - p.Radius - 12 - w;
            if (x < 4) x = 4;
            if (y + h > ActualHeight - 4) y = ActualHeight - 4 - h;
            if (y < 4) y = 4;

            var card = new Rect(x, y, w, h);
            dc.DrawRoundedRectangle(t.SurfaceBrush, new Pen(t.InkMutedBrush, 1), card, 4, 4);

            var ty = y + pad;
            dc.DrawText(title, new Point(x + pad, ty));
            ty += title.Height + 4;
            for (var i = 0; i < keys.Count; i++)
            {
                dc.DrawText(keys[i], new Point(x + pad, ty));
                dc.DrawText(vals[i], new Point(x + pad + keyW + gap, ty));
                ty += keys[i].Height + 2;
            }
        }

        // ---------------------------------------------------------------- helpers

        private double ToScreenX(PlotModel m, double v) =>
            plotRect.Left + (v - m.XMin) / (m.XMax - m.XMin) * plotRect.Width;

        private double ToScreenY(PlotModel m, double v) =>
            plotRect.Bottom - (v - m.YMin) / (m.YMax - m.YMin) * plotRect.Height;

        private static double Snap(double v) => Math.Round(v) + 0.5;

        private FormattedText Text(string s, Brush brush, double size, FontWeight? weight = null)
        {
            var family = (FontFamily)GetValue(TextElement.FontFamilyProperty) ?? new FontFamily("Segoe UI");
            return new FormattedText(
                s ?? string.Empty,
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                new Typeface(family, FontStyles.Normal, weight ?? FontWeights.Normal, FontStretches.Normal),
                size,
                brush,
                pixelsPerDip);
        }

        private FormattedText Ellipsize(string s, Brush brush, double size, double maxWidth)
        {
            var ft = Text(s, brush, size);
            if (maxWidth > 10)
            {
                ft.MaxTextWidth = maxWidth;
                ft.MaxLineCount = 1;
                ft.Trimming = TextTrimming.CharacterEllipsis;
            }

            return ft;
        }
    }
}
