using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace PlayniteCharts.Controls
{
    /// <summary>
    /// A two-handled slider: WPF ships a one-value Slider, and a filter needs both
    /// ends of a range. Drawn directly rather than templated, like the plot itself,
    /// so it costs one visual and inherits nothing from the Playnite theme except
    /// the two brushes it is handed.
    /// </summary>
    public class RangeSlider : FrameworkElement
    {
        private const double HandleRadius = 6;
        private const double TrackHeight = 3;

        private bool draggingUpper;
        private bool dragging;

        public RangeSlider()
        {
            Height = 22;
            MinWidth = 60;
            Focusable = false;
            Cursor = Cursors.Hand;
        }

        public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
            nameof(Minimum), typeof(double), typeof(RangeSlider),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
            nameof(Maximum), typeof(double), typeof(RangeSlider),
            new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty LowerProperty = DependencyProperty.Register(
            nameof(Lower), typeof(double), typeof(RangeSlider),
            new FrameworkPropertyMetadata(0.0,
                FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public static readonly DependencyProperty UpperProperty = DependencyProperty.Register(
            nameof(Upper), typeof(double), typeof(RangeSlider),
            new FrameworkPropertyMetadata(1.0,
                FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        /// <summary>Snap step; 0 slides continuously.</summary>
        public static readonly DependencyProperty StepProperty = DependencyProperty.Register(
            nameof(Step), typeof(double), typeof(RangeSlider), new PropertyMetadata(0.0));

        public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(
            nameof(TrackBrush), typeof(Brush), typeof(RangeSlider),
            new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty AccentBrushProperty = DependencyProperty.Register(
            nameof(AccentBrush), typeof(Brush), typeof(RangeSlider),
            new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

        public double Minimum { get => (double)GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
        public double Maximum { get => (double)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
        public double Lower { get => (double)GetValue(LowerProperty); set => SetValue(LowerProperty, value); }
        public double Upper { get => (double)GetValue(UpperProperty); set => SetValue(UpperProperty, value); }
        public double Step { get => (double)GetValue(StepProperty); set => SetValue(StepProperty, value); }

        public Brush TrackBrush { get => (Brush)GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }
        public Brush AccentBrush { get => (Brush)GetValue(AccentBrushProperty); set => SetValue(AccentBrushProperty, value); }

        /// <summary>Raised when a drag ends, so the caller can save and rebuild once.</summary>
        public event EventHandler DragCompleted;

        private double Span => Math.Abs(Maximum - Minimum) < 1e-12 ? 1 : Maximum - Minimum;

        private double ToX(double value) =>
            HandleRadius + (value - Minimum) / Span * Math.Max(1, ActualWidth - HandleRadius * 2);

        private double FromX(double x)
        {
            var v = Minimum + (x - HandleRadius) / Math.Max(1, ActualWidth - HandleRadius * 2) * Span;
            v = Math.Max(Minimum, Math.Min(Maximum, v));
            if (Step > 0)
            {
                v = Minimum + Math.Round((v - Minimum) / Step) * Step;
            }

            return Math.Max(Minimum, Math.Min(Maximum, v));
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            var midY = ActualHeight / 2;

            // full-width hit area: a 3px track would be a miserable click target
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));

            var track = new Rect(HandleRadius, midY - TrackHeight / 2,
                Math.Max(1, ActualWidth - HandleRadius * 2), TrackHeight);
            dc.DrawRoundedRectangle(TrackBrush, null, track, 1.5, 1.5);

            var lx = ToX(Lower);
            var ux = ToX(Upper);
            dc.DrawRoundedRectangle(AccentBrush, null,
                new Rect(Math.Min(lx, ux), track.Y, Math.Abs(ux - lx), TrackHeight), 1.5, 1.5);

            dc.DrawEllipse(AccentBrush, null, new Point(lx, midY), HandleRadius, HandleRadius);
            dc.DrawEllipse(AccentBrush, null, new Point(ux, midY), HandleRadius, HandleRadius);
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            var x = e.GetPosition(this).X;

            // grab whichever handle is nearer; a click on bare track drags that one to here
            draggingUpper = Math.Abs(x - ToX(Upper)) <= Math.Abs(x - ToX(Lower));
            dragging = true;
            CaptureMouse();
            Move(x);
            e.Handled = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (dragging && e.LeftButton == MouseButtonState.Pressed)
            {
                Move(e.GetPosition(this).X);
            }
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);
            if (dragging)
            {
                dragging = false;
                ReleaseMouseCapture();
                DragCompleted?.Invoke(this, EventArgs.Empty);
            }
        }

        protected override void OnLostMouseCapture(MouseEventArgs e)
        {
            base.OnLostMouseCapture(e);
            dragging = false;
        }

        private void Move(double x)
        {
            var v = FromX(x);
            if (draggingUpper)
            {
                Upper = Math.Max(v, Lower);
            }
            else
            {
                Lower = Math.Min(v, Upper);
            }
        }
    }
}
