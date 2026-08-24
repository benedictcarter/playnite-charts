using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Playnite.SDK;
using Playnite.SDK.Plugins;
using PlayniteCharts.Model;
using PlayniteCharts.ViewModels;
using PlayniteCharts.Views;

namespace PlayniteCharts
{
    public class ChartsPlugin : GenericPlugin
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        public override Guid Id { get; } = Guid.Parse("8a4f2c10-5c1e-4b2a-9d3f-6e7b0a1c4d55");

        internal ChartsSettings Settings { get; private set; }

        public ChartsPlugin(IPlayniteAPI api) : base(api)
        {
            Properties = new GenericPluginProperties { HasSettings = false };
            Settings = LoadSettings();
        }

        private ChartsSettings LoadSettings()
        {
            try
            {
                var loaded = LoadPluginSettings<ChartsSettings>();
                if (loaded?.Plots != null && loaded.Plots.Count > 0)
                {
                    loaded.View = loaded.View ?? new ViewSettings();

                    // repairs a settings file already grown by the Json.NET
                    // list-append behaviour described in ViewSettings
                    loaded.View.HoverFieldIds = loaded.View.HoverFieldIds
                        .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                    return loaded;
                }
            }
            catch (Exception e)
            {
                logger.Error(e, "Failed to load Charts settings, falling back to defaults.");
            }

            return ChartsSettings.CreateDefault();
        }

        internal void PersistSettings()
        {
            try
            {
                SavePluginSettings(Settings);
            }
            catch (Exception e)
            {
                logger.Error(e, "Failed to save Charts settings.");
            }
        }

        public override IEnumerable<SidebarItem> GetSidebarItems()
        {
            ChartsView view = null;
            ChartsViewModel model = null;

            yield return new SidebarItem
            {
                Title = "Charts",
                Type = SiderbarItemType.View,
                Icon = BuildIcon(),
                Opened = () =>
                {
                    if (view == null)
                    {
                        model = new ChartsViewModel(this, PlayniteApi);
                        view = new ChartsView { DataContext = model };
                    }

                    model.Refresh();
                    return view;
                },
                Closed = () => model?.Persist()
            };
        }

        /// <summary>
        /// A vector sidebar glyph so the icon follows the active theme's foreground
        /// instead of being a baked-in bitmap.
        /// </summary>
        private static object BuildIcon()
        {
            var canvas = new Canvas { Width = 24, Height = 24 };
            var axis = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse("M 3,2 L 3,21 L 22,21"),
                StrokeThickness = 2,
                SnapsToDevicePixels = true
            };
            axis.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "TextBrush");
            canvas.Children.Add(axis);

            foreach (var dot in new[] { new Point(8, 15), new Point(13, 9), new Point(18, 13) })
            {
                var e = new System.Windows.Shapes.Ellipse { Width = 6, Height = 6 };
                e.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "TextBrush");
                Canvas.SetLeft(e, dot.X - 3);
                Canvas.SetTop(e, dot.Y - 3);
                canvas.Children.Add(e);
            }

            return new Viewbox { Child = canvas, Stretch = Stretch.Uniform };
        }
    }
}
