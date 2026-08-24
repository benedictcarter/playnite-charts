using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Playnite.SDK;
using Playnite.SDK.Models;
using PlayniteCharts.Controls;
using PlayniteCharts.Model;

namespace PlayniteCharts.DevHarness
{
    /// <summary>
    /// Renders the bubble plot to PNG without Playnite, so the chart can be looked
    /// at (dataviz step 7) against both the dark and light surface.
    ///
    ///   PlayniteCharts.DevHarness.exe [output-dir]
    /// </summary>
    internal class Surface
    {
        public Surface(string name, Color color)
        {
            Name = name;
            Color = color;
        }

        public string Name { get; }
        public Color Color { get; }
    }

    internal static class Program
    {
        private const int Width = 1180;
        private const int Height = 720;

        // the settings panel is a tall scroller; the smoke shot shows all of it at once
        private const int TallHeight = 1500;

        [STAThread]
        private static int Main(string[] args)
        {
            var outDir = args.Length > 0 ? args[0] : Path.Combine(Path.GetTempPath(), "playnite-charts");
            Directory.CreateDirectory(outDir);

            var db = new FakeDatabase();
            var games = SeedLibrary(db);

            // Game's navigation properties (Source, CompletionStatus, Genres, ...)
            // resolve through an internal static hook; the harness feeds it a fake.
            typeof(Game)
                .GetProperty("DatabaseReference", BindingFlags.NonPublic | BindingFlags.Static)
                .SetValue(null, db);

            var cases = new[]
            {
                new PlotConfig
                {
                    Name = "release date vs user score",
                    XFieldId = "releasedate", YFieldId = "userscore",
                    SizeFieldId = "criticscore", ColorFieldId = "completion", ShapeFieldId = "source"
                },
                new PlotConfig
                {
                    Name = "playtime vs critic score",
                    XFieldId = "playtime", YFieldId = "criticscore",
                    SizeFieldId = string.Empty, ColorFieldId = "genre", ShapeFieldId = string.Empty
                }
            };

            var surfaces = new[]
            {
                new Surface("dark", Color.FromRgb(0x15, 0x1D, 0x38)),
                new Surface("light", Color.FromRgb(0xF5, 0xF5, 0xF5))
            };

            var view = new ViewSettings
            {
                HoverFieldIds = GameColumns.All.Select(f => f.Id).ToList()
            };

            // every ramp, on both surfaces, over a numeric colour column - the only
            // way to see whether a ramp actually reads against the ground it sits on
            var rampCase = new PlotConfig
            {
                Name = "Ramps", XFieldId = "playtime", YFieldId = "criticscore",
                SizeFieldId = string.Empty, ColorFieldId = "userscore", ShapeFieldId = string.Empty
            };

            foreach (var ramp in ColorRamp.All)
            {
                var rm = PlotModel.Build(rampCase, new ViewSettings { ColorRampId = ramp.Id },
                    games, games, PlotTheme.SeriesCapacity, MarkShapes.Count);
                foreach (var s in surfaces)
                {
                    var file = Path.Combine(outDir, $"ramp-{ramp.Id}-{s.Name}.png");
                    Render(rm, s.Color, file, false);
                    Console.WriteLine(file);
                }
            }

            foreach (var cfg in cases)
            {
                var model = PlotModel.Build(cfg, view, games, games, PlotTheme.SeriesCapacity, MarkShapes.Count);
                if (model.Problem != null)
                {
                    Console.WriteLine($"{cfg.Name}: PROBLEM - {model.Problem}");
                    continue;
                }

                foreach (var s in surfaces)
                {
                    var file = Path.Combine(outDir, $"{Slug(cfg.Name)}-{s.Name}.png");
                    Render(model, s.Color, file, false);
                    Console.WriteLine($"{file}  ({model.PlottedGames} of {model.TotalGames} plotted)");
                }

                if (!string.IsNullOrEmpty(cfg.SizeFieldId))
                {
                    // the size column narrowed to a window: the bubbles must spread
                    // across it rather than all coming out near-max
                    var field = GameColumns.Get(cfg.SizeFieldId);
                    var span = games.Select(g => field.GetNumber(g)).Where(v => v.HasValue)
                        .Select(v => v.Value).ToList();
                    var lo = span.Min() + (span.Max() - span.Min()) * 0.7;
                    var windowed = PlotModel.Build(cfg, new ViewSettings
                    {
                        HoverFieldIds = view.HoverFieldIds,
                        Filters = new List<FilterConfig>
                        {
                            new FilterConfig { FieldId = cfg.SizeFieldId, Lower = lo }
                        }
                    }, games.Where(g => field.GetNumber(g) >= lo).ToList(), games,
                        PlotTheme.SeriesCapacity, MarkShapes.Count);
                    var windowFile = Path.Combine(outDir, $"{Slug(cfg.Name)}-sizewindow.png");
                    Render(windowed, surfaces[0].Color, windowFile, false);
                    Console.WriteLine(windowFile);
                }

                var titled = PlotModel.Build(cfg, new ViewSettings
                {
                    HoverFieldIds = view.HoverFieldIds,
                    ShowTitles = true
                }, games, games, PlotTheme.SeriesCapacity, MarkShapes.Count);
                var titleFile = Path.Combine(outDir, $"{Slug(cfg.Name)}-titles.png");
                Render(titled, surfaces[0].Color, titleFile, false);
                Console.WriteLine(titleFile);

                var hoverFile = Path.Combine(outDir, $"{Slug(cfg.Name)}-hover.png");
                Render(model, surfaces[0].Color, hoverFile, true);
                Console.WriteLine(hoverFile);

                if (model.YField.IsEditable || model.XField.IsEditable)
                {
                    var dragFile = Path.Combine(outDir, $"{Slug(cfg.Name)}-drag.png");
                    Render(model, surfaces[0].Color, dragFile, false, true);
                    Console.WriteLine(dragFile);
                }
            }

            SmokeTestView(Path.Combine(outDir, "chartsview.png"), games);
            return 0;
        }

        /// <summary>
        /// Parses ChartsView.xaml for real. A XAML error only shows up at load time,
        /// and in Playnite that means a silent extension-load failure in the log.
        /// </summary>
        private static void SmokeTestView(string file, IList<Game> library)
        {
            if (Application.Current == null)
            {
                new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            }

            // stand-ins for the Playnite theme keys the view asks for by DynamicResource;
            // if a key is misspelled the text falls back to black and the PNG shows it
            var res = Application.Current.Resources;
            res["TextBrush"] = new SolidColorBrush(Color.FromRgb(0xF2, 0xF2, 0xF2));
            res["TextBrushDarker"] = new SolidColorBrush(Color.FromRgb(0xA3, 0xA3, 0xA3));
            res["NormalBorderBrush"] = new SolidColorBrush(Color.FromRgb(0x40, 0x48, 0x60));
            res["NormalBrush"] = new SolidColorBrush(Color.FromRgb(0x2A, 0x32, 0x4C));
            res["GlyphBrush"] = new SolidColorBrush(Color.FromRgb(0x7E, 0x9C, 0xD8));

            try
            {
                var host = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(0x15, 0x1D, 0x38)),
                    Child = new Views.ChartsView { DataContext = new StubVm(library) },
                    Width = Width,
                    Height = TallHeight
                };
                host.Measure(new Size(Width, TallHeight));
                host.Arrange(new Rect(0, 0, Width, TallHeight));
                host.UpdateLayout();

                var bmp = new RenderTargetBitmap(Width, TallHeight, 96, 96, PixelFormats.Pbgra32);
                bmp.Render(host);
                var png = new PngBitmapEncoder();
                png.Frames.Add(BitmapFrame.Create(bmp));
                using (var fs = File.Create(file))
                {
                    png.Save(fs);
                }

                Console.WriteLine("ChartsView.xaml loaded OK -> " + file);
            }
            catch (Exception e)
            {
                Console.WriteLine("ChartsView.xaml FAILED: " + e);
            }
        }

        private static void Render(PlotModel model, Color surface, string file, bool withHover, bool withDrag = false)
        {
            var plot = new BubblePlotControl { Model = model };
            var host = new Border
            {
                Background = new SolidColorBrush(surface),
                Padding = new Thickness(12),
                Child = plot,
                Width = Width,
                Height = Height
            };

            host.Measure(new Size(Width, Height));
            host.Arrange(new Rect(0, 0, Width, Height));
            host.UpdateLayout();

            if (withDrag)
            {
                // no mouse offscreen: poke the private drag state mid-gesture
                var flags = BindingFlags.NonPublic | BindingFlags.Instance;
                var pick = model.Points.OrderByDescending(pt => pt.Radius).Skip(4).First();
                var field = model.YField.IsEditable ? model.YField : model.XField;
                typeof(BubblePlotControl).GetField("hovered", flags).SetValue(plot, pick);
                typeof(BubblePlotControl).GetField("dragPoint", flags).SetValue(plot, pick);
                typeof(BubblePlotControl).GetField("dragField", flags).SetValue(plot, field);
                typeof(BubblePlotControl).GetField("dragOnY", flags).SetValue(plot, ReferenceEquals(field, model.YField));
                typeof(BubblePlotControl).GetField("dragging", flags).SetValue(plot, true);
                typeof(BubblePlotControl).GetField("dragValue", flags).SetValue(plot, field.Snap(88));
                typeof(BubblePlotControl).GetMethod("Redraw", flags).Invoke(plot, null);
            }

            if (withHover)
            {
                // no mouse offscreen: poke the private hover state so the tooltip draws
                var flags = BindingFlags.NonPublic | BindingFlags.Instance;
                var pick = model.Points
                    .OrderByDescending(pt => pt.Radius)
                    .Skip(model.Points.Count / 3)
                    .First();
                typeof(BubblePlotControl).GetField("hovered", flags).SetValue(plot, pick);
                typeof(BubblePlotControl).GetMethod("RedrawOverlay", flags).Invoke(plot, null);
            }

            var bmp = new RenderTargetBitmap(Width, Height, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(host);

            var png = new PngBitmapEncoder();
            png.Frames.Add(BitmapFrame.Create(bmp));
            using (var fs = File.Create(file))
            {
                png.Save(fs);
            }
        }

        private static string Slug(string s) => s.Replace(' ', '-');

        // ------------------------------------------------------------ fake library

        private static readonly string[] Stores = { "Steam", "GOG", "Epic", "Xbox", "itch.io", "Battle.net", "Ubisoft" };
        private static readonly string[] Statuses = { "Not played", "Playing", "Beaten", "Completed", "Abandoned", "On hold", "Plan to play" };
        private static readonly string[] GenreNames = { "Action", "RPG", "Strategy", "Puzzle", "Racing", "Shooter", "Simulation", "Platformer", "Horror", "Sports" };

        // A fixed day, not DateTime.Today. The seeded Random is what makes two runs
        // comparable, and "Date added" is a plottable column, so a moving today
        // would quietly shift that axis from one day to the next.
        private static readonly DateTime SeedDay = new DateTime(2026, 8, 24);

        private const int GameCount = 720;
        private const int FirstYear = 1998;
        private const int YearSpan = 28;

        private static List<Game> SeedLibrary(FakeDatabase db)
        {
            var rnd = new Random(20260824);

            var sources = Fill(db.Sources, Stores, n => new GameSource(n));
            var statuses = Fill(db.CompletionStatuses, Statuses, n => new CompletionStatus(n));
            var genres = Fill(db.Genres, GenreNames, n => new Genre(n));

            var games = new List<Game>();
            for (var i = 0; i < GameCount; i++)
            {
                var year = FirstYear + rnd.Next(0, YearSpan);
                var game = new Game($"Test Game {i + 1:000}")
                {
                    // 28 days in every month: the day is noise here, and every
                    // month has a 28th, so no month needs a special case
                    ReleaseDate = new ReleaseDate(year, rnd.Next(1, 13), rnd.Next(1, 29)),
                    Playtime = (ulong)(Math.Pow(rnd.NextDouble(), 3) * 400 * 3600),
                    Added = SeedDay.AddDays(-rnd.Next(0, 1400)),
                    SourceId = sources[Weighted(rnd, sources.Count)].Id,
                    CompletionStatusId = statuses[Weighted(rnd, statuses.Count)].Id,
                    GenreIds = new List<Guid> { genres[rnd.Next(genres.Count)].Id }
                };

                // scores correlate a little with the year so the cloud is not a
                // blob, and the two drift apart so they are not the same column
                if (rnd.NextDouble() > 0.18)
                {
                    game.UserScore = Clamp(40 + (year - FirstYear) * 1.1 + rnd.Next(-28, 29));
                }

                if (rnd.NextDouble() > 0.25)
                {
                    game.CriticScore = Clamp(50 + (year - FirstYear) * 0.9 + rnd.Next(-25, 26));
                }

                games.Add(game);
                db.Games.Add(game);
            }

            return games;
        }

        /// <summary>Builds one item per name, adds them all, and hands them back.</summary>
        private static List<T> Fill<T>(IItemCollection<T> into, string[] names, Func<string, T> make)
            where T : DatabaseObject
        {
            var items = names.Select(make).ToList();
            foreach (var item in items)
            {
                into.Add(item);
            }

            return items;
        }

        /// <summary>Skews towards the first entries, like a real store/status spread.</summary>
        private static int Weighted(Random rnd, int count)
        {
            var v = Math.Pow(rnd.NextDouble(), 1.9);
            return Math.Min(count - 1, (int)(v * count));
        }

        private static int Clamp(double v) => (int)Math.Max(1, Math.Min(100, v));
    }
}
