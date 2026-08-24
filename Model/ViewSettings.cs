using System;
using System.Collections.Generic;
using System.Linq;
using Playnite.SDK;

namespace PlayniteCharts.Model
{
    /// <summary>
    /// The parts of the Charts tab that are about *looking at* the library rather
    /// than about one plot: which games are in scope, what the hover card says, how
    /// the bubbles are drawn. They live here, once, instead of on each PlotConfig,
    /// so that filtering down to a set of games and then flipping between plots
    /// explores that same set - switching the visualisation must not silently
    /// change the data under it.
    /// </summary>
    public class ViewSettings : ObservableObject
    {
        private List<string> hoverFieldIds = new List<string> { "name", "playtime" };
        private List<FilterConfig> filters = new List<FilterConfig>();
        private bool showLegend = true;
        private bool missingAsZero;
        private double minBubbleSize = 3;
        private double maxBubbleSize = 12;

        public List<string> HoverFieldIds
        {
            get => hoverFieldIds;
            set => SetValue(ref hoverFieldIds, value ?? new List<string>());
        }

        /// <summary>Which games any plot is allowed to draw.</summary>
        public List<FilterConfig> Filters
        {
            get => filters;
            set => SetValue(ref filters, value ?? new List<FilterConfig>());
        }

        public bool ShowLegend
        {
            get => showLegend;
            set => SetValue(ref showLegend, value);
        }

        /// <summary>Plot a game with no value on a numeric channel at 0 instead of
        /// dropping it. Dates are exempt - 0 there is 1899.</summary>
        public bool MissingAsZero
        {
            get => missingAsZero;
            set => SetValue(ref missingAsZero, value);
        }

        public double MinBubbleSize
        {
            get => minBubbleSize;
            set => SetValue(ref minBubbleSize, value);
        }

        public double MaxBubbleSize
        {
            get => maxBubbleSize;
            set => SetValue(ref maxBubbleSize, value);
        }

        /// <summary>Lifts the values a pre-shared-settings plot carried on its own.</summary>
        public static ViewSettings FromLegacyPlot(PlotConfig plot)
        {
            var v = new ViewSettings();
            if (plot == null)
            {
                return v;
            }

            if (plot.HoverFieldIds != null && plot.HoverFieldIds.Count > 0)
            {
                v.HoverFieldIds = new List<string>(plot.HoverFieldIds);
            }

            v.Filters = (plot.Filters ?? new List<FilterConfig>()).Select(f => f.Clone()).ToList();
            v.ShowLegend = plot.ShowLegend;
            v.MissingAsZero = plot.MissingAsZero;
            v.MinBubbleSize = plot.MinBubbleSize > 0 ? plot.MinBubbleSize : v.MinBubbleSize;
            v.MaxBubbleSize = plot.MaxBubbleSize > 0 ? plot.MaxBubbleSize : v.MaxBubbleSize;
            return v;
        }
    }
}
