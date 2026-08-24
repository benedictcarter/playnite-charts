using System;
using System.Collections.Generic;
using System.Linq;
using PlayniteCharts.Model;

namespace PlayniteCharts
{
    /// <summary>Everything persisted by the plugin, written through SavePluginSettings.</summary>
    public class ChartsSettings
    {
        public List<PlotConfig> Plots { get; set; } = new List<PlotConfig>();

        /// <summary>Filters, hover columns and appearance - shared by every plot.</summary>
        public ViewSettings View { get; set; } = new ViewSettings();

        public Guid LastSelectedPlotId { get; set; }

        /// <summary>Plot the library view's current filter result rather than the whole library.</summary>
        public bool UseLibraryFilter { get; set; } = true;

        /// <summary>
        /// Lifts a pre-shared-settings file: the view settings used to sit on each
        /// plot, so take them from whichever plot was last open and forget the rest.
        /// </summary>
        public void Migrate()
        {
            if (Plots == null)
            {
                Plots = new List<PlotConfig>();
            }

            if (!Plots.Any(p => p.HasLegacyView))
            {
                if (View == null)
                {
                    View = new ViewSettings();
                }

                return;
            }

            var source = Plots.FirstOrDefault(p => p.Id == LastSelectedPlotId && p.HasLegacyView)
                ?? Plots.First(p => p.HasLegacyView);
            View = ViewSettings.FromLegacyPlot(source);
            foreach (var p in Plots)
            {
                p.DropLegacyView();
            }
        }

        public static ChartsSettings CreateDefault()
        {
            return new ChartsSettings
            {
                View = new ViewSettings { HoverFieldIds = new List<string> { "name", "playtime" } },
                Plots = new List<PlotConfig>
                {
                    new PlotConfig
                    {
                        Name = "Release date vs user score",
                        XFieldId = "releasedate",
                        YFieldId = "userscore",
                        SizeFieldId = "criticscore",
                        ColorFieldId = "completion",
                        ShapeFieldId = "source"
                    },
                    new PlotConfig
                    {
                        Name = "Playtime vs critic score",
                        XFieldId = "playtime",
                        YFieldId = "criticscore",
                        SizeFieldId = "installsize",
                        ColorFieldId = "genre",
                        ShapeFieldId = "installed"
                    }
                }
            };
        }
    }
}
