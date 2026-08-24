using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace PlayniteCharts.Controls
{
    /// <summary>
    /// A continuous colour ramp: a value's position on the channel, 0..1, becomes a
    /// colour. Used when the colour column holds numbers - binning a critic score
    /// into eight arbitrary categories tells you nothing, a ramp tells you the
    /// gradient at a glance.
    ///
    /// Light and dark carry their own stops rather than one set flipped, for the
    /// same reason the categorical palette does: a sequential ramp has to end away
    /// from its surface at both ends, and "away from white" and "away from a near
    /// black" are different colours.
    ///
    /// Interpolation is in LINEAR light, not sRGB. Lerping sRGB bytes darkens the
    /// middle of every transition - a red-to-blue midpoint comes out a muddy plum
    /// instead of a clean neutral - because sRGB is a gamma curve, not a measure of
    /// light.
    /// </summary>
    public class ColorRamp
    {
        public string Id { get; private set; }
        public string Name { get; private set; }

        /// <summary>True for the two-poles-and-a-neutral ramps, which is worth
        /// knowing: they read as "which side of the middle", not "how much".</summary>
        public bool IsDiverging { get; private set; }

        private readonly Color[] light;
        private readonly Color[] dark;

        private ColorRamp(string id, string name, bool diverging, string[] lightHex, string[] darkHex)
        {
            Id = id;
            Name = name;
            IsDiverging = diverging;
            light = lightHex.Select(Parse).ToArray();
            dark = darkHex.Select(Parse).ToArray();
        }

        public Color[] Stops(bool isDark) => isDark ? dark : light;

        /// <summary>Left-to-right swatch of the whole ramp, for the picker.</summary>
        public Brush Preview
        {
            get
            {
                var stops = Stops(PlotTheme.LastWasDark);
                var b = new LinearGradientBrush(
                    new GradientStopCollection(stops.Select((c, i) =>
                        new GradientStop(c, (double)i / (stops.Length - 1)))),
                    new Point(0, 0), new Point(1, 0));
                b.Freeze();
                return b;
            }
        }

        /// <summary>The colour at <paramref name="t"/> (0..1) on this ramp.</summary>
        public Color Sample(double t, bool isDark)
        {
            var stops = Stops(isDark);
            if (t <= 0 || double.IsNaN(t))
            {
                return stops[0];
            }

            if (t >= 1)
            {
                return stops[stops.Length - 1];
            }

            var pos = t * (stops.Length - 1);
            var i = (int)Math.Floor(pos);
            return Mix(stops[i], stops[i + 1], pos - i);
        }

        private static Color Mix(Color a, Color b, double f)
        {
            return Color.FromRgb(
                Chan(a.R, b.R, f),
                Chan(a.G, b.G, f),
                Chan(a.B, b.B, f));
        }

        private static byte Chan(byte a, byte b, double f)
        {
            var v = Lin(a) + (Lin(b) - Lin(a)) * f;
            var srgb = v <= 0.0031308 ? v * 12.92 : 1.055 * Math.Pow(v, 1 / 2.4) - 0.055;
            return (byte)Math.Max(0, Math.Min(255, Math.Round(srgb * 255)));
        }

        private static double Lin(byte c)
        {
            var v = c / 255.0;
            return v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }

        private static Color Parse(string hex) => (Color)ColorConverter.ConvertFromString(hex);

        // ------------------------------------------------------------------ ramps

        /// <summary>
        /// The offered set. Sequential ramps are one hue light-to-dark, which is the
        /// honest default for magnitude. The diverging pair is two hues about a
        /// NEUTRAL middle - a hue in the middle would invent a third category. The
        /// rainbow is last on purpose: it has no perceptual order, so equal steps of
        /// value are not equal steps of colour and it invents banding that is not in
        /// the data. It is here because it is asked for, not because it is good.
        /// </summary>
        public static readonly IReadOnlyList<ColorRamp> All = new List<ColorRamp>
        {
            new ColorRamp("blue", "Blue", false,
                new[] { "#a9cdf0", "#6ea9e0", "#3d7fc4", "#1e5794", "#0b3565" },
                new[] { "#2b5f8f", "#3f82bd", "#5da5e0", "#8cc6f5", "#cfe9ff" }),

            new ColorRamp("green", "Green", false,
                new[] { "#aed9a8", "#77bd72", "#45914a", "#256e2c", "#0e4a1d" },
                new[] { "#2c6b3c", "#3f9152", "#5cb56d", "#8ed492", "#cdeecd" }),

            new ColorRamp("orange", "Orange", false,
                new[] { "#f7c79b", "#f0a05c", "#d97a25", "#ab5710", "#6f3306" },
                new[] { "#8a4d12", "#b56d1c", "#d98f33", "#f0b571", "#ffdcb4" }),

            new ColorRamp("purple", "Purple", false,
                new[] { "#cbb4e8", "#a884d4", "#8055b8", "#5b2f8c", "#3a1560" },
                new[] { "#55307f", "#7549a8", "#9a6dcc", "#bd97e3", "#e2d2f5" }),

            new ColorRamp("redblue", "Red - grey - blue", true,
                new[] { "#a5232a", "#d4574c", "#e59182", "#b8b8b8", "#84a8cc", "#4380b8", "#12518f" },
                new[] { "#d84a4f", "#e8807a", "#d9a9a2", "#9aa0aa", "#8fb6d8", "#4f95d4", "#2f79c4" }),

            new ColorRamp("orangeteal", "Orange - grey - teal", true,
                new[] { "#a85508", "#d67f24", "#e8ab6b", "#b8b8b8", "#78b4ac", "#2f8a7f", "#0d5c55" },
                new[] { "#d67f1c", "#e8a54f", "#d4bb99", "#9aa0aa", "#84c6bd", "#35a394", "#1f8578" }),

            new ColorRamp("viridis", "Viridis", false,
                new[] { "#440154", "#414487", "#2a788e", "#22a884", "#7ad151", "#c8e020" },
                new[] { "#4b3f8f", "#3b6b96", "#2a978c", "#4ec06e", "#9fdb4a", "#fde725" }),

            new ColorRamp("rainbow", "Rainbow (ROYGBIV)", false,
                new[] { "#d31b1b", "#e07000", "#c9a400", "#1f9d2f", "#1f5fd6", "#4b30b0", "#8b32d6" },
                new[] { "#ff4d4d", "#ff9a2b", "#ffe03d", "#4ede5a", "#4d94ff", "#8f7aff", "#c46bff" })
        };

        public static ColorRamp Default => All[0];

        public static ColorRamp Get(string id)
        {
            return All.FirstOrDefault(r => r.Id == id) ?? Default;
        }
    }
}
