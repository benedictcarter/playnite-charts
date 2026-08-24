using System;
using System.Collections.Generic;
using System.Linq;
using Playnite.SDK;

namespace PlayniteCharts.Model
{
    /// <summary>
    /// A saved bubble plot definition. Field references are stored as ids so the
    /// serialized settings survive changes to the field registry.
    /// </summary>
    public class PlotConfig : ObservableObject
    {
        private string name = "New plot";
        private string xFieldId = "releasedate";
        private string yFieldId = "userscore";
        private string sizeFieldId = "criticscore";
        private string colorFieldId = "completion";
        private string shapeFieldId = "source";
        private List<string> hoverFieldIds = GameColumns.All.Select(f => f.Id).ToList();
        private bool showLegend = true;
        private bool missingAsZero;
        private double minBubbleSize = 3;
        private double maxBubbleSize = 12;

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

        public List<string> HoverFieldIds
        {
            get => hoverFieldIds;
            set => SetValue(ref hoverFieldIds, value);
        }

        /// <summary>Plot a game with no value on a numeric channel at 0 instead of
        /// dropping it. Dates are exempt - 0 there is 1899.</summary>
        public bool MissingAsZero
        {
            get => missingAsZero;
            set => SetValue(ref missingAsZero, value);
        }

        public bool ShowLegend
        {
            get => showLegend;
            set => SetValue(ref showLegend, value);
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
                ShapeFieldId = ShapeFieldId,
                HoverFieldIds = new List<string>(HoverFieldIds ?? new List<string>()),
                MissingAsZero = MissingAsZero,
                ShowLegend = ShowLegend,
                MinBubbleSize = MinBubbleSize,
                MaxBubbleSize = MaxBubbleSize
            };
        }
    }
}
