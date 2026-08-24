using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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

            BenchTable(db);
            SmokeTestView(Path.Combine(outDir, "chartsview.png"), games);
            return 0;
        }

        /// <summary>
        /// What "hover everything" costs the table: one string per column per game,
        /// with the free-text columns stripping HTML. This is what froze Playnite.
        /// </summary>
        private static void BenchTable(FakeDatabase db)
        {
            var rnd = new Random(7);
            var html = string.Concat(Enumerable.Repeat(
                "<p>Some <b>marketing</b> copy about the game.</p>", 40));
            var games = new List<Game>();
            for (var i = 0; i < 5000; i++)
            {
                games.Add(new Game($"Bench {i}")
                {
                    Description = html,
                    Notes = html,
                    UserScore = rnd.Next(1, 100),
                    Added = DateTime.Today
                });
            }

            var fields = GameColumns.All;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var rows = games.Select(g => fields.Select(f => f.Display(g) ?? string.Empty).ToList()).ToList();
            Console.WriteLine($"table: {rows.Count} games x {fields.Count} columns in {sw.ElapsedMilliseconds} ms");
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

        private static List<Game> SeedLibrary(FakeDatabase db)
        {
            var rnd = new Random(20260824);

            var sources = Stores.Select(n => new GameSource(n)).ToList();
            sources.ForEach(db.Sources.Add);
            var statuses = Statuses.Select(n => new CompletionStatus(n)).ToList();
            statuses.ForEach(db.CompletionStatuses.Add);
            var genres = GenreNames.Select(n => new Genre(n)).ToList();
            genres.ForEach(db.Genres.Add);

            var games = new List<Game>();
            for (var i = 0; i < 720; i++)
            {
                var year = 1998 + rnd.Next(0, 28);
                var g = new Game($"Test Game {i + 1:000}")
                {
                    ReleaseDate = new ReleaseDate(year, rnd.Next(1, 13), rnd.Next(1, 28)),
                    Playtime = (ulong)(Math.Pow(rnd.NextDouble(), 3) * 400 * 3600),
                    Added = DateTime.Today.AddDays(-rnd.Next(0, 1400)),
                    SourceId = sources[Weighted(rnd, sources.Count)].Id,
                    CompletionStatusId = statuses[Weighted(rnd, statuses.Count)].Id,
                    GenreIds = new List<Guid> { genres[rnd.Next(genres.Count)].Id }
                };

                // scores correlate a little with the year so the cloud is not a blob
                if (rnd.NextDouble() > 0.18)
                {
                    g.UserScore = Clamp(40 + (year - 1998) * 1.1 + rnd.Next(-28, 29));
                }

                if (rnd.NextDouble() > 0.25)
                {
                    g.CriticScore = Clamp(50 + (year - 1998) * 0.9 + rnd.Next(-25, 26));
                }

                games.Add(g);
                db.Games.Add(g);
            }

            return games;
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
