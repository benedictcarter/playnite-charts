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
    /// <summary>A mark was dragged along an editable axis: write it back.</summary>
    public class ValueEditEventArgs : EventArgs
    {
        public ValueEditEventArgs(PlotPoint point, GameColumn column, double value)
        {
            Point = point;
            Column = column;
            Value = value;
        }

        public PlotPoint Point { get; }
        public GameColumn Column { get; }
        public double Value { get; }
    }

    /// <summary>
    /// Retained-mode bubble plot. The chart is drawn once per model/size change
    /// into one visual; the hover ring, tooltip and titles live in a second visual,
    /// so moving the mouse never re-renders thousands of marks.
    /// </summary>
    public class BubblePlotControl : FrameworkElement
    {
        private const double SwatchSize = 13;
        private const double RowHeight = 19;
        private const double GradientBarWidth = 13;
        private const double GradientBarHeight = 96;
        private const double TitleSize = 11;
        private const double TitleGap = 4;

        /// <summary>A click must not nudge a score, so a drag only starts past this.</summary>
        private const double DragSlop = 4;

        /// <summary>Steps the colour ramp is frozen into - finer than the eye
        /// resolves on a 12px mark, and a fixed cost whatever the game count.</summary>
        private const int RampSteps = 64;

        private readonly DrawingVisual chartVisual = new DrawingVisual();
        private readonly DrawingVisual overlayVisual = new DrawingVisual();
        private readonly VisualCollection visuals;

        private Rect plotRect;
        private PlotTheme theme;
        private PlotPoint hovered;
        private Brush[] rampBrushes;

        // measuring one FormattedText per game is the expensive half of drawing
        // titles, and hovering re-runs the layout - so measure each name once
        private readonly Dictionary<PlotPoint, FormattedText> labelCache =
            new Dictionary<PlotPoint, FormattedText>();
        private readonly Dictionary<PlotPoint, FormattedText> shadowCache =
            new Dictionary<PlotPoint, FormattedText>();
        private List<PlotPoint> drawOrder = new List<PlotPoint>();
        private double pixelsPerDip = 1.0;

        private PlotPoint dragPoint;
        private GameColumn dragField;
        private bool dragOnY;
        private bool dragging;
        private Point dragOrigin;
        private double dragValue;

        public event EventHandler<PlotPoint> PointActivated;

        /// <summary>Right-click on a bubble - the host decides what menu that means.</summary>
        public event EventHandler<PlotPoint> PointMenuRequested;

        /// <summary>A mark was dragged along an editable axis and dropped.</summary>
        public event EventHandler<ValueEditEventArgs> ValueEdited;

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
            c.ResetCaches();
            c.hovered = null;
            c.Redraw();
        }

        /// <summary>
        /// Drops everything measured or frozen for one model on one surface. Both
        /// halves depend on the theme, not just the data: the ink colour is baked
        /// into each measured label, and the ramp is stepped for the surface it is
        /// drawn on.
        /// </summary>
        private void ResetCaches()
        {
            labelCache.Clear();
            shadowCache.Clear();
            rampBrushes = null;
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo info)
        {
            base.OnRenderSizeChanged(info);
            Redraw();
        }

        /// <summary>Forces the palette to be re-picked, e.g. after a theme switch.</summary>
        public void InvalidateTheme()
        {
            ResetCaches();
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

                var legendWidth = m.ShowLegend ? MeasureLegend(m, t) : 0;
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
                if (dragging && ReferenceEquals(p, dragPoint))
                {
                    // it is being drawn by the overlay at the mouse instead
                    continue;
                }

                var c = new Point(ToScreenX(m, p.X), ToScreenY(m, p.Y));
                var geo = MarkShapes.Create(p.ShapeSlot, c, p.Radius);
                dc.DrawGeometry(Fill(m, t, p), t.RingPen, geo);
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

            var editable = EditableAxis(m, out _);
            if (editable != null)
            {
                text += $"   -   drag a bubble to set its {editable.Name.ToLowerInvariant()}";
            }

            dc.DrawText(Text(text, t.InkMutedBrush, 11), new Point(plotRect.Left, 6));
        }

        // ---------------------------------------------------------------- legend

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

            if (m.ColorGradient != null)
            {
                yield return m.ColorGradient.Field.Name;
            }

            if (m.SizeField != null)
            {
                yield return m.SizeField.Name;
            }
        }

        /// <summary>A mark's fill: a fixed palette slot for a categorical colour
        /// column, a point on the ramp for a numeric one.</summary>
        private Brush Fill(PlotModel m, PlotTheme t, PlotPoint p)
        {
            if (m.ColorGradient == null)
            {
                return t.SeriesBrush(p.ColorSlot);
            }

            var steps = RampBrushes(m, t);
            var i = (int)Math.Round(p.ColorT * (RampSteps - 1));
            return steps[i < 0 ? 0 : i > RampSteps - 1 ? RampSteps - 1 : i];
        }

        /// <summary>The ramp as a handful of frozen brushes: a 5000-game plot would
        /// otherwise freeze one Brush per bubble.</summary>
        private Brush[] RampBrushes(PlotModel m, PlotTheme t)
        {
            if (rampBrushes == null)
            {
                var ramp = ColorRamp.Get(m.ColorRampId);
                rampBrushes = new Brush[RampSteps];
                for (var i = 0; i < RampSteps; i++)
                {
                    var b = new SolidColorBrush(ramp.Sample((double)i / (RampSteps - 1), t.IsDark));
                    b.Freeze();
                    rampBrushes[i] = b;
                }
            }

            return rampBrushes;
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

            if (m.ColorGradient != null)
            {
                foreach (var v in m.ColorGradient.KeyValues())
                {
                    max = Math.Max(max, Text(m.ColorGradient.Field.Format(v), t.InkBrush, 11).Width
                        + GradientBarWidth + 10);
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

            if (m.ColorGradient != null)
            {
                y = LegendHeading(dc, t, area, y, m.ColorGradient.Field.Name);
                y = DrawGradientKey(dc, m, t, area, y);
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

        /// <summary>
        /// The key for a ramped colour column: the ramp itself as a bar, with the
        /// ends and the middle labelled. A swatch list would be a lie here - the
        /// values in between are real, not rounded into bins.
        /// </summary>
        private double DrawGradientKey(DrawingContext dc, PlotModel m, PlotTheme t, Rect area, double y)
        {
            var stops = ColorRamp.Get(m.ColorRampId).Stops(t.IsDark);
            var bar = new Rect(area.Left, y + 2, GradientBarWidth, GradientBarHeight);

            // top of the bar is the top of the range, so it reads with the y axis
            var brush = new LinearGradientBrush(
                new GradientStopCollection(stops.Select((c, i) =>
                    new GradientStop(c, 1 - (double)i / (stops.Length - 1)))),
                new Point(0, 0), new Point(0, 1));
            brush.Freeze();
            dc.DrawRoundedRectangle(brush, t.SizeRingPen, bar, 3, 3);

            var values = m.ColorGradient.KeyValues().Reverse().ToList();
            for (var i = 0; i < values.Count; i++)
            {
                var f = values.Count == 1 ? 0 : (double)i / (values.Count - 1);
                var label = Ellipsize(m.ColorGradient.Field.Format(values[i]), t.InkBrush, 11,
                    Math.Max(20, area.Width - GradientBarWidth - 10));
                var cy = bar.Top + f * bar.Height;
                dc.DrawText(label, new Point(bar.Right + 7,
                    Math.Min(bar.Bottom - label.Height, Math.Max(bar.Top, cy - label.Height / 2))));
            }

            return bar.Bottom + 4;
        }

        /// <summary>Smallest / middle / largest, deduped - the usual three-circle key.</summary>
        private static IEnumerable<double> SizeLegendValues(PlotModel m)
        {
            return m.SizeScale == null ? Enumerable.Empty<double>() : m.SizeScale.KeyValues();
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
            var pos = e.GetPosition(this);
            if (dragPoint != null && TrackDrag(pos))
            {
                return;
            }

            var hit = HitTest(pos);
            if (!ReferenceEquals(hit, hovered))
            {
                hovered = hit;
                Cursor = DragCursor(hit);
                RedrawOverlay();
            }
        }

        private Cursor DragCursor(PlotPoint hit)
        {
            if (hit == null)
            {
                return Cursors.Arrow;
            }

            return EditableAxis(Model, out var onY) == null
                ? Cursors.Hand
                : (onY ? Cursors.SizeNS : Cursors.SizeWE);
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
            var point = dragPoint;
            var field = dragField;
            var value = dragValue;
            var dropped = dragging;
            EndDrag();

            if (dropped)
            {
                ValueEdited?.Invoke(this, new ValueEditEventArgs(point, field, value));
                return;
            }

            var hit = HitTest(e.GetPosition(this));
            if (hit != null)
            {
                PointActivated?.Invoke(this, hit);
            }
        }

        protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseRightButtonUp(e);
            var hit = HitTest(e.GetPosition(this));
            if (hit == null)
            {
                return;
            }

            // the menu is about to cover the plot, so make it obvious which game it
            // belongs to: leave that bubble hovered while it is open
            if (!ReferenceEquals(hovered, hit))
            {
                hovered = hit;
                RedrawOverlay();
            }

            e.Handled = true;
            PointMenuRequested?.Invoke(this, hit);
        }

        // --------------------------------------------------------------- dragging

        /// <summary>
        /// The one axis a drag is allowed to move along: the first of Y then X whose
        /// column can be written back. Movement on the other axis is ignored, so a
        /// game never slides sideways into a release date it does not have.
        /// </summary>
        private static GameColumn EditableAxis(PlotModel m, out bool onY)
        {
            onY = true;
            if (m?.Problem != null)
            {
                return null;
            }

            if (m?.YField?.IsEditable == true)
            {
                return m.YField;
            }

            if (m?.XField?.IsEditable == true)
            {
                onY = false;
                return m.XField;
            }

            return null;
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            var field = EditableAxis(Model, out var onY);
            if (field == null)
            {
                return;
            }

            var hit = HitTest(e.GetPosition(this));
            if (hit == null)
            {
                return;
            }

            dragPoint = hit;
            dragField = field;
            dragOnY = onY;
            dragOrigin = e.GetPosition(this);
            dragValue = field.Snap(onY ? hit.Y : hit.X);
            CaptureMouse();

            // only so Escape reaches OnKeyDown; put back the way it was on drop
            Focusable = true;
            Focus();
        }

        /// <summary>Returns true once the pointer is actually dragging a mark.</summary>
        private bool TrackDrag(Point pos)
        {
            var m = Model;
            if (m == null || Mouse.LeftButton != MouseButtonState.Pressed)
            {
                CancelDrag();
                return false;
            }

            if (!dragging)
            {
                var moved = Math.Abs(pos.X - dragOrigin.X) + Math.Abs(pos.Y - dragOrigin.Y);
                if (moved < DragSlop)
                {
                    return true;
                }

                dragging = true;
                hovered = dragPoint;
                Redraw();
            }

            dragValue = dragField.Snap(dragOnY ? FromScreenY(m, pos.Y) : FromScreenX(m, pos.X));
            RedrawOverlay();
            return true;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Key == Key.Escape && dragPoint != null)
            {
                CancelDrag();
                e.Handled = true;
            }
        }

        protected override void OnLostMouseCapture(MouseEventArgs e)
        {
            base.OnLostMouseCapture(e);
            CancelDrag();
        }

        private void CancelDrag()
        {
            var redraw = dragging;
            EndDrag();
            if (redraw)
            {
                Redraw();
            }
        }

        private void EndDrag()
        {
            if (dragPoint == null)
            {
                return;
            }

            dragPoint = null;
            dragField = null;
            dragging = false;
            Focusable = false;
            if (IsMouseCaptured)
            {
                ReleaseMouseCapture();
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
                if (m == null || m.Problem != null || plotRect.Width < 10)
                {
                    return;
                }

                var t = EnsureTheme();

                // titles live on the overlay, not the chart: the hovered game always
                // gets its name, which re-runs the layout on every hover change, and
                // that must not mean redrawing every bubble
                if (m.ShowTitles)
                {
                    DrawTitles(dc, m, t);
                }

                var p = hovered;
                if (p == null)
                {
                    return;
                }

                var c = new Point(ToScreenX(m, p.X), ToScreenY(m, p.Y));
                if (dragging)
                {
                    // the free coordinate follows the value; the other one cannot move
                    if (dragOnY)
                    {
                        c.Y = ToScreenY(m, dragValue);
                    }
                    else
                    {
                        c.X = ToScreenX(m, dragValue);
                    }

                    DrawDragGuide(dc, t, c);
                }

                // halo: ink ring outside a surface ring, so it reads on any mark colour
                var ringR = p.Radius + 3.5;
                dc.DrawEllipse(null, new Pen(t.SurfaceBrush, 3), c, ringR, ringR);
                dc.DrawEllipse(null, new Pen(t.InkBrush, 1.5), c, ringR, ringR);
                dc.DrawGeometry(Fill(m, t, p), t.RingPen, MarkShapes.Create(p.ShapeSlot, c, p.Radius));

                if (dragging)
                {
                    DrawDragReadout(dc, t, p, c);
                }
                else
                {
                    DrawTooltip(dc, m, t, p, c);
                }
            }
        }

        /// <summary>
        /// Names beside the bubbles, best-effort. Each label is tried level with its
        /// bubble first, then one line up, then one line down; if all three collide
        /// with a label already placed, this game goes without. Highest LabelRank is
        /// laid out first, so it is the low-scoring games that lose their name - and
        /// the hovered game is laid out before everything, evicting whatever would
        /// otherwise have sat where its name goes.
        /// </summary>
        private void DrawTitles(DrawingContext dc, PlotModel m, PlotTheme t)
        {
            var order = m.Points
                .OrderByDescending(p => ReferenceEquals(p, hovered))
                .ThenByDescending(p => p.LabelRank)
                .ToList();

            var placed = new List<Rect>();
            var line = TitleSize + 3;
            foreach (var p in order)
            {
                var label = Cached(labelCache, p, t.InkBrush);
                if (label.Width > plotRect.Width / 2)
                {
                    continue;
                }

                var c = new Point(ToScreenX(m, p.X), ToScreenY(m, p.Y));

                // to the right of the bubble, or to its left if that would leave the plot
                var x = c.X + p.Radius + TitleGap;
                if (x + label.Width > plotRect.Right)
                {
                    x = c.X - p.Radius - TitleGap - label.Width;
                }

                if (x < plotRect.Left)
                {
                    continue;
                }

                var box = Rect.Empty;
                foreach (var dy in new[] { 0.0, -line, line })
                {
                    var candidate = new Rect(x, c.Y - label.Height / 2 + dy, label.Width, label.Height);
                    if (candidate.Top < plotRect.Top || candidate.Bottom > plotRect.Bottom)
                    {
                        continue;
                    }

                    // 2px of surface between neighbours, so adjacent names stay two names
                    var probe = candidate;
                    probe.Inflate(2, 1);
                    if (placed.Any(r => r.IntersectsWith(probe)))
                    {
                        continue;
                    }

                    box = candidate;
                    break;
                }

                if (box.IsEmpty)
                {
                    continue;
                }

                placed.Add(box);

                // a one-pixel surface shadow keeps the name readable over a bubble
                var origin = box.Location;
                dc.DrawText(Cached(shadowCache, p, t.SurfaceBrush), new Point(origin.X + 1, origin.Y + 1));
                dc.DrawText(label, origin);
            }
        }

        private FormattedText Cached(Dictionary<PlotPoint, FormattedText> cache, PlotPoint p, Brush ink)
        {
            if (!cache.TryGetValue(p, out var text))
            {
                text = Text(p.Game?.Name ?? string.Empty, ink, TitleSize);
                cache[p] = text;
            }

            return text;
        }

        /// <summary>The rail the mark is sliding on, so the constraint is visible.</summary>
        private void DrawDragGuide(DrawingContext dc, PlotTheme t, Point c)
        {
            var pen = new Pen(t.InkMutedBrush, 1)
            {
                DashStyle = new DashStyle(new double[] { 3, 3 }, 0)
            };

            if (dragOnY)
            {
                dc.DrawLine(pen, new Point(Snap(c.X), plotRect.Top), new Point(Snap(c.X), plotRect.Bottom));
            }
            else
            {
                dc.DrawLine(pen, new Point(plotRect.Left, Snap(c.Y)), new Point(plotRect.Right, Snap(c.Y)));
            }
        }

        /// <summary>Name and the value that would be written, right by the mark.</summary>
        private void DrawDragReadout(DrawingContext dc, PlotTheme t, PlotPoint p, Point c)
        {
            var name = Ellipsize(p.Game?.Name ?? string.Empty, t.InkBrush, 12, 260);
            var value = Text($"{dragField.Name}: {dragField.Format(dragValue)}", t.InkMutedBrush, 11);
            const double pad = 8;
            var size = new Size(Math.Max(name.Width, value.Width) + pad * 2,
                name.Height + value.Height + pad * 2 + 2);

            // inside the plot, so the legend to the right stays readable
            var box = PlaceCard(c, p.Radius, size, plotRect);
            dc.DrawRoundedRectangle(t.SurfaceBrush, new Pen(t.InkMutedBrush, 1), box, 4, 4);
            dc.DrawText(name, new Point(box.X + pad, box.Y + pad));
            dc.DrawText(value, new Point(box.X + pad, box.Y + pad + name.Height + 2));
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
            AddChannel(m.ColorScale?.Field ?? m.ColorGradient?.Field, p.ColorKey);
            AddChannel(m.ShapeScale?.Field, p.ShapeKey);

            var title = Text(p.Game.Name ?? "(no name)", t.InkBrush, 12, FontWeights.SemiBold);

            // "hover everything" can be 25 rows; never let the card run off the control
            var maxLines = Math.Max(1, (int)((ActualHeight - 40 - title.Height) / 15));
            var truncated = lines.Count > maxLines;
            if (truncated)
            {
                lines = lines.Take(maxLines - 1).ToList();
                lines.Add(Tuple.Create(string.Empty, "\u2026"));
            }

            var keys = lines.Select(l => Text(l.Item1, t.InkMutedBrush, 11)).ToList();
            var vals = lines.Select(l => Ellipsize(l.Item2, t.InkBrush, 11, 320)).ToList();

            var keyW = keys.Count > 0 ? keys.Max(k => k.Width) : 0;
            var valW = vals.Count > 0 ? vals.Max(v => v.Width) : 0;
            const double pad = 9, gap = 12;
            var size = new Size(Math.Max(title.Width, keyW + gap + valW) + pad * 2,
                title.Height + 4 + keys.Sum(k => k.Height + 2) + pad * 2);

            // the whole control, not just the plot: a card by a mark near the right
            // edge may sit over the legend rather than be pushed off the axis
            var card = PlaceCard(anchor, p.Radius, size,
                new Rect(4, 4, Math.Max(1, ActualWidth - 8), Math.Max(1, ActualHeight - 8)));
            dc.DrawRoundedRectangle(t.SurfaceBrush, new Pen(t.InkMutedBrush, 1), card, 4, 4);

            var ty = card.Y + pad;
            dc.DrawText(title, new Point(card.X + pad, ty));
            ty += title.Height + 4;
            for (var i = 0; i < keys.Count; i++)
            {
                dc.DrawText(keys[i], new Point(card.X + pad, ty));
                dc.DrawText(vals[i], new Point(card.X + pad + keyW + gap, ty));
                ty += keys[i].Height + 2;
            }
        }

        /// <summary>
        /// Puts a card beside a mark: to its right, flipped to the left if that
        /// would overflow, then nudged so the whole card stays inside the bounds.
        /// </summary>
        private static Rect PlaceCard(Point anchor, double radius, Size size, Rect bounds)
        {
            const double gap = 12;
            var x = anchor.X + radius + gap;
            if (x + size.Width > bounds.Right)
            {
                x = anchor.X - radius - gap - size.Width;
            }

            return new Rect(
                Math.Max(bounds.Left, Math.Min(bounds.Right - size.Width, x)),
                Math.Max(bounds.Top, Math.Min(bounds.Bottom - size.Height, anchor.Y - size.Height / 2)),
                size.Width,
                size.Height);
        }

        // ---------------------------------------------------------------- helpers

        private double ToScreenX(PlotModel m, double v) =>
            plotRect.Left + (v - m.XMin) / (m.XMax - m.XMin) * plotRect.Width;

        private double ToScreenY(PlotModel m, double v) =>
            plotRect.Bottom - (v - m.YMin) / (m.YMax - m.YMin) * plotRect.Height;

        private double FromScreenX(PlotModel m, double x) =>
            plotRect.Width <= 0 ? m.XMin : m.XMin + (x - plotRect.Left) / plotRect.Width * (m.XMax - m.XMin);

        private double FromScreenY(PlotModel m, double y) =>
            plotRect.Height <= 0 ? m.YMin : m.YMin + (plotRect.Bottom - y) / plotRect.Height * (m.YMax - m.YMin);

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
