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
                    SizeFieldId = "criticscore", ColorFieldId = "completion", ShapeFieldId = "source",
                    HoverFieldIds = new List<string> { "name", "genre" }
                },
                new PlotConfig
                {
                    Name = "playtime vs critic score",
                    XFieldId = "playtime", YFieldId = "criticscore",
                    SizeFieldId = string.Empty, ColorFieldId = "genre", ShapeFieldId = string.Empty,
                    HoverFieldIds = new List<string> { "name" }
                }
            };

            var surfaces = new[]
            {
                new Surface("dark", Color.FromRgb(0x15, 0x1D, 0x38)),
                new Surface("light", Color.FromRgb(0xF5, 0xF5, 0xF5))
            };

            foreach (var cfg in cases)
            {
                var model = PlotModel.Build(cfg, games, games, PlotTheme.SeriesCapacity, MarkShapes.Count);
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

                var hoverFile = Path.Combine(outDir, $"{Slug(cfg.Name)}-hover.png");
                Render(model, surfaces[0].Color, hoverFile, true);
                Console.WriteLine(hoverFile);
            }

            SmokeTestView();
            return 0;
        }

        /// <summary>
        /// Parses ChartsView.xaml for real. A XAML error only shows up at load time,
        /// and in Playnite that means a silent extension-load failure in the log.
        /// </summary>
        private static void SmokeTestView()
        {
            if (Application.Current == null)
            {
                new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            }

            try
            {
                var view = new Views.ChartsView();
                view.Measure(new Size(Width, Height));
                view.Arrange(new Rect(0, 0, Width, Height));
                Console.WriteLine("ChartsView.xaml loaded OK");
            }
            catch (Exception e)
            {
                Console.WriteLine("ChartsView.xaml FAILED: " + e);
            }
        }

        private static void Render(PlotModel model, Color surface, string file, bool withHover)
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
