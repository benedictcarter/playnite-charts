using System;
using System.Collections.Generic;
using PlayniteCharts.Model;

namespace PlayniteCharts
{
    /// <summary>Everything persisted by the plugin, written through SavePluginSettings.</summary>
    public class ChartsSettings
    {
        public List<PlotConfig> Plots { get; set; } = new List<PlotConfig>();

        public Guid LastSelectedPlotId { get; set; }

        /// <summary>Plot the library view's current filter result rather than the whole library.</summary>
        public bool UseLibraryFilter { get; set; } = true;

        public static ChartsSettings CreateDefault()
        {
            return new ChartsSettings
            {
                Plots = new List<PlotConfig>
                {
                    new PlotConfig
                    {
                        Name = "Release date vs user score",
                        XFieldId = "releasedate",
                        YFieldId = "userscore",
                        SizeFieldId = "criticscore",
                        ColorFieldId = "completion",
                        ShapeFieldId = "source",
                        HoverFieldIds = new List<string> { "name", "playtime" }
                    },
                    new PlotConfig
                    {
                        Name = "Playtime vs critic score",
                        XFieldId = "playtime",
                        YFieldId = "criticscore",
                        SizeFieldId = "installsize",
                        ColorFieldId = "genre",
                        ShapeFieldId = "installed",
                        HoverFieldIds = new List<string> { "name", "platform", "lastactivity" }
                    }
                }
            };
        }
    }
}
