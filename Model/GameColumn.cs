using System;
using System.Collections.Generic;
using System.Linq;
using Playnite.SDK.Models;

namespace PlayniteCharts.Model
{
    public enum FieldKind
    {
        Numeric,
        Date,
        Categorical
    }

    /// <summary>
    /// One selectable "column" of the Playnite game table, described in a way the
    /// plot can consume generically: a numeric accessor for axes/size and a
    /// string accessor for colour/shape/hover.
    /// </summary>
    public class GameColumn
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Group { get; set; }
        public FieldKind Kind { get; set; }

        /// <summary>Value for a continuous channel (X, Y, size). Null = no data.</summary>
        public Func<Game, double?> GetNumber { get; set; }

        /// <summary>Bucket for a discrete channel (colour, shape). Null = no data.</summary>
        public Func<Game, string> GetCategory { get; set; }

        /// <summary>Human text for tooltips. Falls back to the other two.</summary>
        public Func<Game, string> GetDisplay { get; set; }

        /// <summary>Axis tick / tooltip formatting for numeric values.</summary>
        public Func<double, string> FormatNumber { get; set; }

        public bool IsContinuous => Kind == FieldKind.Numeric || Kind == FieldKind.Date;
        public bool IsDiscrete => Kind == FieldKind.Categorical;

        public string Format(double value)
        {
            if (FormatNumber != null)
            {
                return FormatNumber(value);
            }

            if (Kind == FieldKind.Date)
            {
                return DateTime.FromOADate(value).ToString("yyyy-MM-dd");
            }

            return Math.Abs(value) >= 1000 ? value.ToString("N0") : value.ToString("0.##");
        }

        public string Display(Game game)
        {
            if (GetDisplay != null)
            {
                return GetDisplay(game);
            }

            if (IsContinuous)
            {
                var v = GetNumber?.Invoke(game);
                return v.HasValue ? Format(v.Value) : null;
            }

            return GetCategory?.Invoke(game);
        }

        public override string ToString() => Name;
    }

    public static class GameColumns
    {
        public static readonly IReadOnlyList<GameColumn> All = Build();

        private static readonly Dictionary<string, GameColumn> byId =
            All.ToDictionary(f => f.Id, StringComparer.OrdinalIgnoreCase);

        public static GameColumn Get(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return null;
            }

            return byId.TryGetValue(id, out var f) ? f : null;
        }

        public static List<GameColumn> Continuous => All.Where(f => f.IsContinuous).ToList();

        public static List<GameColumn> Discrete => All.Where(f => f.IsDiscrete).ToList();

        private static string First<T>(IEnumerable<T> items) where T : DatabaseObject
        {
            if (items == null)
            {
                return null;
            }

            var first = items.Where(i => !string.IsNullOrEmpty(i.Name))
                .OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase).FirstOrDefault();
            return first?.Name;
        }

        private static string Join<T>(IEnumerable<T> items) where T : DatabaseObject
        {
            if (items == null)
            {
                return null;
            }

            var names = items.Select(i => i.Name).Where(n => !string.IsNullOrEmpty(n))
                .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToList();
            return names.Count == 0 ? null : string.Join(", ", names);
        }

        private static GameColumn Num(string id, string name, Func<Game, double?> get,
            Func<double, string> fmt = null)
        {
            return new GameColumn
            {
                Id = id,
                Name = name,
                Group = "Numbers",
                Kind = FieldKind.Numeric,
                GetNumber = get,
                FormatNumber = fmt
            };
        }

        private static GameColumn Dt(string id, string name, Func<Game, DateTime?> get)
        {
            return new GameColumn
            {
                Id = id,
                Name = name,
                Group = "Dates",
                Kind = FieldKind.Date,
                GetNumber = g =>
                {
                    var d = get(g);
                    return d.HasValue ? d.Value.ToOADate() : (double?)null;
                },
                GetDisplay = g =>
                {
                    var d = get(g);
                    return d.HasValue ? d.Value.ToString("d MMM yyyy") : null;
                }
            };
        }

        private static GameColumn Cat(string id, string name, Func<Game, string> get,
            Func<Game, string> display = null, string group = "Categories")
        {
            return new GameColumn
            {
                Id = id,
                Name = name,
                Group = group,
                Kind = FieldKind.Categorical,
                GetCategory = get,
                GetDisplay = display ?? get
            };
        }

        private static List<GameColumn> Build()
        {
            return new List<GameColumn>
            {
                // ---- continuous: usable for X, Y and bubble size ----
                Num("playtime", "Playtime (hours)", g => g.Playtime > 0 ? g.Playtime / 3600.0 : (double?)null, v => v.ToString("0.#") + " h"),
                Num("playcount", "Play count", g => g.PlayCount > 0 ? g.PlayCount : (double?)null, v => v.ToString("N0")),
                Num("installsize", "Install size (GB)", g => g.InstallSize.HasValue && g.InstallSize.Value > 0 ? g.InstallSize.Value / 1073741824.0 : (double?)null, v => v.ToString("0.##") + " GB"),
                Num("userscore", "User score", g => g.UserScore),
                Num("criticscore", "Critic score", g => g.CriticScore),
                Num("communityscore", "Community score", g => g.CommunityScore),
                Num("releaseyear", "Release year", g => g.ReleaseYear, v => ((int)v).ToString()),

                Dt("releasedate", "Release date", g => g.ReleaseDate.HasValue ? g.ReleaseDate.Value.Date : (DateTime?)null),
                Dt("added", "Date added", g => g.Added),
                Dt("modified", "Date modified", g => g.Modified),
                Dt("lastactivity", "Last played", g => g.LastActivity),
                Dt("recentactivity", "Recent activity", g => g.RecentActivity),

                // ---- discrete: usable for colour, shape and hover ----
                Cat("name", "Name", g => g.Name, group: "Text"),
                Cat("source", "Store / source", g => g.Source != null ? g.Source.Name : "(no source)"),
                Cat("completion", "Completion status", g => g.CompletionStatus != null ? g.CompletionStatus.Name : "(not set)"),
                Cat("platform", "Platform", g => First(g.Platforms) ?? "(none)", g => Join(g.Platforms) ?? "(none)"),
                Cat("genre", "Genre", g => First(g.Genres) ?? "(none)", g => Join(g.Genres) ?? "(none)"),
                Cat("category", "Category", g => First(g.Categories) ?? "(none)", g => Join(g.Categories) ?? "(none)"),
                Cat("series", "Series", g => First(g.Series) ?? "(none)", g => Join(g.Series) ?? "(none)"),
                Cat("developer", "Developer", g => First(g.Developers) ?? "(none)", g => Join(g.Developers) ?? "(none)"),
                Cat("publisher", "Publisher", g => First(g.Publishers) ?? "(none)", g => Join(g.Publishers) ?? "(none)"),
                Cat("agerating", "Age rating", g => First(g.AgeRatings) ?? "(none)", g => Join(g.AgeRatings) ?? "(none)"),
                Cat("region", "Region", g => First(g.Regions) ?? "(none)", g => Join(g.Regions) ?? "(none)"),
                Cat("feature", "Feature", g => First(g.Features) ?? "(none)", g => Join(g.Features) ?? "(none)"),
                Cat("tag", "Tag", g => First(g.Tags) ?? "(none)", g => Join(g.Tags) ?? "(none)"),
                Cat("installed", "Installed", g => g.IsInstalled ? "Installed" : "Not installed", group: "Flags"),
                Cat("favorite", "Favorite", g => g.Favorite ? "Favorite" : "Not favorite", group: "Flags"),
                Cat("hidden", "Hidden", g => g.Hidden ? "Hidden" : "Visible", group: "Flags"),
                Cat("installdrive", "Install drive", g => g.GetInstallDrive() ?? "(not installed)", group: "Flags")
            };
        }
    }
}
