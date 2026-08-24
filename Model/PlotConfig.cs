using System;
using System.Collections.Generic;
using Playnite.SDK;

namespace PlayniteCharts.Model
{
    /// <summary>
    /// A saved bubble plot definition: what is mapped to what. Field references are
    /// stored as ids so the serialized settings survive changes to the field
    /// registry. Anything that is about the view rather than the mapping - filters,
    /// hover columns, appearance - lives on <see cref="ViewSettings"/> and is shared
    /// by every plot.
    /// </summary>
    public class PlotConfig : ObservableObject
    {
        private string name = "New plot";
        private string xFieldId = "releasedate";
        private string yFieldId = "userscore";
        private string sizeFieldId = "criticscore";
        private string colorFieldId = "completion";
        private string shapeFieldId = "source";

        public Guid Id { get; set; } = Guid.NewGuid();

        public string Name
        {
            get => name;
            set => SetValue(ref name, value);
        }

        public string XFieldId
        {
            get => xFieldId;
            set => SetValue(ref xFieldId, value);
        }

        public string YFieldId
        {
            get => yFieldId;
            set => SetValue(ref yFieldId, value);
        }

        public string SizeFieldId
        {
            get => sizeFieldId;
            set => SetValue(ref sizeFieldId, value);
        }

        public string ColorFieldId
        {
            get => colorFieldId;
            set => SetValue(ref colorFieldId, value);
        }

        public string ShapeFieldId
        {
            get => shapeFieldId;
            set => SetValue(ref shapeFieldId, value);
        }

        // ---- legacy: these were per-plot before the settings were shared. They are
        // still deserialized so an existing settings file can be lifted into
        // ViewSettings once, then they stay null and are never written to again.

        public List<string> HoverFieldIds { get; set; }
        public List<FilterConfig> Filters { get; set; }
        public bool ShowLegend { get; set; }
        public bool MissingAsZero { get; set; }
        public double MinBubbleSize { get; set; }
        public double MaxBubbleSize { get; set; }

        /// <summary>True while this plot still carries pre-shared-settings values.</summary>
        public bool HasLegacyView =>
            HoverFieldIds != null || Filters != null || MinBubbleSize > 0 || MaxBubbleSize > 0;

        public void DropLegacyView()
        {
            HoverFieldIds = null;
            Filters = null;
            ShowLegend = false;
            MissingAsZero = false;
            MinBubbleSize = 0;
            MaxBubbleSize = 0;
        }

        public PlotConfig Clone(string newName)
        {
            return new PlotConfig
            {
                Id = Guid.NewGuid(),
                Name = newName,
                XFieldId = XFieldId,
                YFieldId = YFieldId,
                SizeFieldId = SizeFieldId,
                ColorFieldId = ColorFieldId,
                ShapeFieldId = ShapeFieldId
            };
        }
    }
}
