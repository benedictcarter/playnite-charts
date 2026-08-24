using System;
using System.Linq;
using System.Windows.Controls;
using System.Windows;
using System.Windows.Media;

namespace PlayniteCharts.Controls
{
    /// <summary>
    /// Chart colours. The categorical slots are a validated palette: every one of
    /// the 28 pairs clears the CVD and normal-vision separation gates against the
    /// surface it is drawn on, because a bubble plot puts arbitrary pairs of
    /// categories side by side (unlike a bar chart, where only neighbours meet).
    /// Slots are assigned in fixed order and never cycled - the 9th category folds
    /// into a neutral "Other".
    ///
    /// Light and dark are the same hues re-stepped for their own surface, not an
    /// automatic flip. Generated and checked with the data-viz validator; see
    /// LESSONS_LEARNT.md for why the stock eight-slot palette could not be used.
    /// </summary>
    public class PlotTheme
    {
        public const int SeriesCapacity = 8;

        public bool IsDark { get; private set; }
        public Color Surface { get; private set; }
        public Color Ink { get; private set; }
        public Color InkMuted { get; private set; }
        public Color Grid { get; private set; }
        public Color Other { get; private set; }
        public Color[] Series { get; private set; }

        public Brush SurfaceBrush { get; private set; }
        public Brush InkBrush { get; private set; }
        public Brush InkMutedBrush { get; private set; }
        public Pen GridPen { get; private set; }
        public Pen AxisPen { get; private set; }

        private Brush[] seriesBrushes;
        private Pen ringPen;

        public Brush SeriesBrush(int slot) => seriesBrushes[Clamp(slot)];

        /// <summary>2px surface ring so overlapping marks stay separable.</summary>
        public Pen RingPen => ringPen;

        /// <summary>Outline for the unfilled size-key bubbles in the legend.</summary>
        public Pen SizeRingPen { get; private set; }

        public Color SeriesColor(int slot) => Series[Clamp(slot)];

        private int Clamp(int slot) => slot < 0 || slot >= Series.Length ? Series.Length - 1 : slot;

        private static readonly string[] LightSeries = PaletteData.LightSeries;
        private static readonly string[] DarkSeries = PaletteData.DarkSeries;

        /// <summary>
        /// Whether the last theme built was a dark one. A ramp preview swatch in the
        /// settings panel is not inside the chart and has no surface of its own to
        /// measure, so it borrows the chart's answer. Worst case - nothing has been
        /// drawn yet - it previews the light steps and corrects on the first render.
        /// </summary>
        public static bool LastWasDark { get; private set; }

        public static PlotTheme ForSurface(Color surface)
        {
            var lum = 0.2126 * Lin(surface.R) + 0.7152 * Lin(surface.G) + 0.0722 * Lin(surface.B);
            var dark = lum < 0.25;
            LastWasDark = dark;
            var t = new PlotTheme
            {
                IsDark = dark,
                Surface = surface,
                Ink = dark ? Color.FromRgb(0xF2, 0xF2, 0xF2) : Color.FromRgb(0x14, 0x14, 0x14),
                InkMuted = dark ? Color.FromRgb(0xA8, 0xAE, 0xC2) : Color.FromRgb(0x5C, 0x5C, 0x5C),
                Grid = dark ? Blend(surface, Color.FromRgb(255, 255, 255), 0.16)
                            : Blend(surface, Color.FromRgb(0, 0, 0), 0.12),
                Other = dark ? Color.FromRgb(0x8A, 0x90, 0x9C) : Color.FromRgb(0x77, 0x77, 0x77)
            };

            var hexes = dark ? DarkSeries : LightSeries;
            t.Series = hexes.Select(Parse).Concat(new[] { t.Other }).ToArray();
            t.seriesBrushes = t.Series.Select(c => Freeze(new SolidColorBrush(c))).ToArray();
            t.SurfaceBrush = Freeze(new SolidColorBrush(surface));
            t.InkBrush = Freeze(new SolidColorBrush(t.Ink));
            t.InkMutedBrush = Freeze(new SolidColorBrush(t.InkMuted));
            t.GridPen = Freeze(new Pen(new SolidColorBrush(t.Grid), 1));
            t.AxisPen = Freeze(new Pen(new SolidColorBrush(t.InkMuted), 1));
            t.ringPen = Freeze(new Pen(t.SurfaceBrush, 2));
            t.SizeRingPen = Freeze(new Pen(t.InkMutedBrush, 1));
            return t;
        }

        private static double Lin(byte v)
        {
            var c = v / 255.0;
            return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        private static Color Blend(Color a, Color b, double t)
        {
            return Color.FromRgb(
                (byte)(a.R + (b.R - a.R) * t),
                (byte)(a.G + (b.G - a.G) * t),
                (byte)(a.B + (b.B - a.B) * t));
        }

        private static Color Parse(string hex) => (Color)ColorConverter.ConvertFromString(hex);

        private static T Freeze<T>(T f) where T : Freezable
        {
            f.Freeze();
            return f;
        }

        /// <summary>
        /// Resolves the surface actually behind the plot by walking up for the first
        /// opaque background, so the palette matches whatever Playnite theme is active.
        /// </summary>
        public static Color ResolveSurface(FrameworkElement element, Color fallback)
        {
            var current = element as DependencyObject;
            while (current != null)
            {
                Brush b = null;
                if (current is Control c)
                {
                    b = c.Background;
                }
                else if (current is Panel p)
                {
                    b = p.Background;
                }
                else if (current is Border bo)
                {
                    b = bo.Background;
                }

                if (b is SolidColorBrush scb && scb.Color.A > 200)
                {
                    return scb.Color;
                }

                if (b is GradientBrush gb && gb.GradientStops.Count > 0)
                {
                    var stop = gb.GradientStops.OrderBy(s => Math.Abs(s.Offset - 0.5)).First();
                    if (stop.Color.A > 200)
                    {
                        return stop.Color;
                    }
                }

                current = VisualTreeHelper.GetParent(current) ?? LogicalTreeHelper.GetParent(current);
            }

            return fallback;
        }
    }
}
