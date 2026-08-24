using System;
using System.Collections.Generic;
using System.Linq;
using Playnite.SDK.Models;

namespace PlayniteCharts.Model
{
    public class PlotPoint
    {
        public Game Game { get; set; }
        public double X { get; set; }
        public double Y { get; set; }

        /// <summary>Bubble radius in device-independent pixels.</summary>
        public double Radius { get; set; }

        public int ColorSlot { get; set; }
        public int ShapeSlot { get; set; }
        public string ColorKey { get; set; }
        public string ShapeKey { get; set; }
    }

    /// <summary>
    /// Maps category values to fixed slots. The domain is taken from the whole
    /// library, not the filtered subset, so changing the library filter never
    /// repaints the categories that survive it.
    /// </summary>
    public class CategoryScale
    {
        public const string OtherKey = "Other";

        private readonly Dictionary<string, int> slots = new Dictionary<string, int>(StringComparer.CurrentCultureIgnoreCase);

        public GameColumn Field { get; }
        public int Capacity { get; }
        public List<string> Domain { get; } = new List<string>();
        public bool HasOverflow { get; private set; }

        public CategoryScale(GameColumn field, IEnumerable<Game> domainSource, int capacity)
        {
            Field = field;
            Capacity = capacity;

            var values = new SortedSet<string>(StringComparer.CurrentCultureIgnoreCase);
            foreach (var g in domainSource)
            {
                var v = field.GetCategory(g);
                if (!string.IsNullOrEmpty(v))
                {
                    values.Add(v);
                }
            }

            foreach (var v in values)
            {
                if (Domain.Count < capacity)
                {
                    slots[v] = Domain.Count;
                    Domain.Add(v);
                }
                else
                {
                    HasOverflow = true;
                }
            }

            if (HasOverflow)
            {
                Domain.Add(OtherKey);
            }
        }

        /// <summary>Slot index; the overflow bucket is the last entry of the domain.</summary>
        public int Slot(Game game)
        {
            var v = Field.GetCategory(game);
            if (!string.IsNullOrEmpty(v) && slots.TryGetValue(v, out var i))
            {
                return i;
            }

            return HasOverflow ? Domain.Count - 1 : Math.Max(0, Capacity - 1);
        }

        public string Key(Game game)
        {
            var v = Field.GetCategory(game);
            return !string.IsNullOrEmpty(v) && slots.ContainsKey(v) ? v : (HasOverflow ? OtherKey : v);
        }
    }

    public class AxisTick
    {
        public double Value { get; set; }
        public string Label { get; set; }
    }

    public class PlotModel
    {
        public PlotConfig Config { get; set; }
        public GameColumn XField { get; set; }
        public GameColumn YField { get; set; }
        public GameColumn SizeField { get; set; }
        public CategoryScale ColorScale { get; set; }
        public CategoryScale ShapeScale { get; set; }
        public List<GameColumn> HoverFields { get; set; } = new List<GameColumn>();

        public List<PlotPoint> Points { get; set; } = new List<PlotPoint>();
        public double XMin { get; set; }
        public double XMax { get; set; }
        public double YMin { get; set; }
        public double YMax { get; set; }
        public List<AxisTick> XTicks { get; set; } = new List<AxisTick>();
        public List<AxisTick> YTicks { get; set; } = new List<AxisTick>();

        /// <summary>Value range of the size column and the radii it maps onto.</summary>
        public double SizeMin { get; set; }
        public double SizeMax { get; set; }
        public double MinRadius { get; set; }
        public double MaxRadius { get; set; }
        public bool HasSizeRange { get; set; }

        /// <summary>The radius a size value gets - area-proportional, so radius by sqrt.</summary>
        public double RadiusFor(double value)
        {
            if (!HasSizeRange)
            {
                return (MinRadius + MaxRadius) / 2;
            }

            // the AREA carries the number, and it is anchored at zero: twice the
            // value is twice the ink. Data that goes below zero has no honest zero
            // anchor, so there the area spans the observed range instead.
            var t = SizeMin >= 0 && SizeMax > 0
                ? value / SizeMax
                : (value - SizeMin) / (SizeMax - SizeMin);

            t = Math.Max(0, Math.Min(1, t));

            // the floor only exists so the smallest marks stay visible
            return Math.Max(MinRadius, MaxRadius * Math.Sqrt(t));
        }

        public int TotalGames { get; set; }
        public int PlottedGames => Points.Count;

        public string Problem { get; set; }

        public static PlotModel Build(PlotConfig config, IList<Game> games, IList<Game> domainSource,
            int colorCapacity, int shapeCapacity)
        {
            var m = new PlotModel { Config = config, TotalGames = games.Count };
            m.XField = GameColumns.Get(config.XFieldId);
            m.YField = GameColumns.Get(config.YFieldId);
            m.SizeField = GameColumns.Get(config.SizeFieldId);

            if (m.XField == null || m.YField == null)
            {
                m.Problem = "Pick a column for both the X and Y axis.";
                return m;
            }

            var colorField = GameColumns.Get(config.ColorFieldId);
            var shapeField = GameColumns.Get(config.ShapeFieldId);
            m.ColorScale = colorField != null ? new CategoryScale(colorField, domainSource, colorCapacity) : null;
            m.ShapeScale = shapeField != null ? new CategoryScale(shapeField, domainSource, shapeCapacity) : null;
            m.HoverFields = (config.HoverFieldIds ?? new List<string>())
                .Select(GameColumns.Get).Where(f => f != null).ToList();

            // "count a missing number as 0": only for numeric columns. A missing date
            // read as 0 would land the game on 30 December 1899 and stretch the axis
            // across a century of empty space.
            Func<GameColumn, Game, double?> read = (f, g) =>
            {
                var v = f.GetNumber(g);
                if (v.HasValue || !config.MissingAsZero || f.Kind != FieldKind.Numeric)
                {
                    return v;
                }

                return 0;
            };

            // size scale: area-proportional (radius by sqrt) between the configured bounds
            var minR = Math.Max(3.0, config.MinBubbleSize);
            var maxR = Math.Max(minR + 1, config.MaxBubbleSize);
            double sizeMin = 0, sizeMax = 0;
            var haveSize = false;
            if (m.SizeField != null)
            {
                var vals = games.Select(g => read(m.SizeField, g)).Where(v => v.HasValue).Select(v => v.Value).ToList();
                if (vals.Count > 0)
                {
                    sizeMin = vals.Min();
                    sizeMax = vals.Max();
                    haveSize = sizeMax > sizeMin;
                }
            }

            m.MinRadius = minR;
            m.MaxRadius = maxR;
            m.SizeMin = sizeMin;
            m.SizeMax = sizeMax;
            m.HasSizeRange = haveSize;

            var defaultR = m.SizeField == null ? (minR + maxR) / 2 : minR;

            foreach (var g in games)
            {
                var x = read(m.XField, g);
                var y = read(m.YField, g);
                if (!x.HasValue || !y.HasValue)
                {
                    continue;
                }

                var r = defaultR;
                if (m.SizeField != null)
                {
                    var s = read(m.SizeField, g);
                    if (s.HasValue && haveSize)
                    {
                        r = m.RadiusFor(s.Value);
                    }
                }

                m.Points.Add(new PlotPoint
                {
                    Game = g,
                    X = x.Value,
                    Y = y.Value,
                    Radius = r,
                    ColorSlot = m.ColorScale?.Slot(g) ?? 0,
                    ShapeSlot = m.ShapeScale?.Slot(g) ?? 0,
                    ColorKey = m.ColorScale?.Key(g),
                    ShapeKey = m.ShapeScale?.Key(g)
                });
            }

            if (m.Points.Count == 0)
            {
                m.Problem = $"No games have a value for both {m.XField.Name} and {m.YField.Name}.";
                return m;
            }

            SetRange(m.Points.Select(p => p.X), out var x0, out var x1);
            SetRange(m.Points.Select(p => p.Y), out var y0, out var y1);
            m.XMin = x0;
            m.XMax = x1;
            m.YMin = y0;
            m.YMax = y1;
            m.XTicks = Ticks(m.XField, x0, x1, 7);
            m.YTicks = Ticks(m.YField, y0, y1, 6);
            return m;
        }

        private static void SetRange(IEnumerable<double> values, out double min, out double max)
        {
            min = double.MaxValue;
            max = double.MinValue;
            foreach (var v in values)
            {
                if (v < min) min = v;
                if (v > max) max = v;
            }

            if (min > max)
            {
                min = 0;
                max = 1;
                return;
            }

            if (Math.Abs(max - min) < 1e-9)
            {
                var pad = Math.Abs(max) > 1e-9 ? Math.Abs(max) * 0.1 : 1;
                min -= pad;
                max += pad;
                return;
            }

            var margin = (max - min) * 0.06;
            min -= margin;
            max += margin;
        }

        public static List<AxisTick> Ticks(GameColumn field, double min, double max, int target)
        {
            if (field.Kind == FieldKind.Date)
            {
                return DateTicks(min, max, target);
            }

            var ticks = new List<AxisTick>();
            var span = max - min;
            if (span <= 0)
            {
                return ticks;
            }

            var raw = span / Math.Max(2, target);
            var mag = Math.Pow(10, Math.Floor(Math.Log10(raw)));
            var norm = raw / mag;
            var step = (norm <= 1 ? 1 : norm <= 2 ? 2 : norm <= 5 ? 5 : 10) * mag;
            var start = Math.Ceiling(min / step) * step;
            for (var v = start; v <= max + step * 1e-6; v += step)
            {
                ticks.Add(new AxisTick { Value = v, Label = field.Format(v) });
            }

            return ticks;
        }

        private static List<AxisTick> DateTicks(double min, double max, int target)
        {
            var ticks = new List<AxisTick>();
            var from = DateTime.FromOADate(Math.Max(min, 1));
            var to = DateTime.FromOADate(Math.Max(max, 2));
            var days = (to - from).TotalDays;

            if (days > 366 * 2)
            {
                var yearStep = Math.Max(1, (int)Math.Ceiling((to.Year - from.Year + 1.0) / target));
                var y = (int)(Math.Ceiling(from.Year / (double)yearStep) * yearStep);
                for (; y <= to.Year; y += yearStep)
                {
                    ticks.Add(new AxisTick { Value = new DateTime(y, 1, 1).ToOADate(), Label = y.ToString() });
                }
            }
            else if (days > 62)
            {
                var monthStep = Math.Max(1, (int)Math.Ceiling(days / 30.0 / target));
                var cur = new DateTime(from.Year, from.Month, 1).AddMonths(1);
                while (cur <= to)
                {
                    ticks.Add(new AxisTick { Value = cur.ToOADate(), Label = cur.ToString("MMM yy") });
                    cur = cur.AddMonths(monthStep);
                }
            }
            else
            {
                var dayStep = Math.Max(1, (int)Math.Ceiling(days / target));
                var cur = from.Date.AddDays(1);
                while (cur <= to)
                {
                    ticks.Add(new AxisTick { Value = cur.ToOADate(), Label = cur.ToString("d MMM") });
                    cur = cur.AddDays(dayStep);
                }
            }

            return ticks;
        }
    }
}
