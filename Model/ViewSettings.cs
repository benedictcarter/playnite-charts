using System.Collections.Generic;
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
        // Empty, not a starter selection: Playnite persists settings with Json.NET,
        // which by default *adds* to a list a property already holds rather than
        // replacing it, so a non-empty initialiser re-appended itself on every load.
        // The out-of-the-box selection lives in ChartsSettings.CreateDefault.
        private List<string> hoverFieldIds = new List<string>();
        private List<FilterConfig> filters = new List<FilterConfig>();
        private bool showLegend = true;
        private bool showTitles;
        private string colorRampId = "blue";
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

        /// <summary>Write each game's name beside its bubble, as far as they fit.</summary>
        public bool ShowTitles
        {
            get => showTitles;
            set => SetValue(ref showTitles, value);
        }

        /// <summary>Which ramp a numeric colour column is drawn with. Appearance, so
        /// it is shared: the ramp you like is the ramp you like on every plot.</summary>
        public string ColorRampId
        {
            get => colorRampId;
            set => SetValue(ref colorRampId, string.IsNullOrEmpty(value) ? "blue" : value);
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
    }
}
