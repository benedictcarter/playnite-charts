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
