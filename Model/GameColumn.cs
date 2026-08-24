using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
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

        /// <summary>Free text (notes, install path, ...): fine in a tooltip, useless as a
        /// colour or shape channel, so it is offered for hover only.</summary>
        public bool HoverOnly { get; set; }

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

        public static List<GameColumn> Discrete => All.Where(f => f.IsDiscrete && !f.HoverOnly).ToList();

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

        private static string JoinNames(IEnumerable<string> names)
        {
            if (names == null)
            {
                return null;
            }

            var list = names.Where(n => !string.IsNullOrEmpty(n)).ToList();
            return list.Count == 0 ? null : string.Join(", ", list);
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

        /// <summary>Free text, hover only. Empty stays empty so the tooltip can skip it.</summary>
        private static GameColumn Text(string id, string name, Func<Game, string> get, int max = 300)
        {
            return new GameColumn
            {
                Id = id,
                Name = name,
                Group = "Text",
                Kind = FieldKind.Categorical,
                HoverOnly = true,
                GetCategory = g => get(g) ?? "(none)",
                GetDisplay = g => Shorten(Strip(get(g)), max)
            };
        }

        /// <summary>Playnite descriptions are HTML; a tooltip wants one plain line.</summary>
        private static string Strip(string html)
        {
            if (string.IsNullOrEmpty(html))
            {
                return null;
            }

            var text = Regex.Replace(html, "<[^>]+>", " ");
            text = WebUtility.HtmlDecode(text);
            text = Regex.Replace(text, @"\s+", " ").Trim();
            return text.Length == 0 ? null : text;
        }

        private static string Shorten(string text, int max)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= max)
            {
                return text;
            }

            return text.Substring(0, max - 1).TrimEnd() + "\u2026";
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
                Cat("source", "Store / source", g => g.Source != null ? g.Source.Name : "(no source)",
                    g => g.Source != null ? g.Source.Name : null),
                Cat("completion", "Completion status", g => g.CompletionStatus != null ? g.CompletionStatus.Name : "(not set)",
                    g => g.CompletionStatus != null ? g.CompletionStatus.Name : null),
                Cat("platform", "Platform", g => First(g.Platforms) ?? "(none)", g => Join(g.Platforms)),
                Cat("genre", "Genre", g => First(g.Genres) ?? "(none)", g => Join(g.Genres)),
                Cat("category", "Category", g => First(g.Categories) ?? "(none)", g => Join(g.Categories)),
                Cat("series", "Series", g => First(g.Series) ?? "(none)", g => Join(g.Series)),
                Cat("developer", "Developer", g => First(g.Developers) ?? "(none)", g => Join(g.Developers)),
                Cat("publisher", "Publisher", g => First(g.Publishers) ?? "(none)", g => Join(g.Publishers)),
                Cat("agerating", "Age rating", g => First(g.AgeRatings) ?? "(none)", g => Join(g.AgeRatings)),
                Cat("region", "Region", g => First(g.Regions) ?? "(none)", g => Join(g.Regions)),
                Cat("feature", "Feature", g => First(g.Features) ?? "(none)", g => Join(g.Features)),
                Cat("tag", "Tag", g => First(g.Tags) ?? "(none)", g => Join(g.Tags)),
                Cat("installed", "Installed", g => g.IsInstalled ? "Installed" : "Not installed", group: "Flags"),
                Cat("favorite", "Favorite", g => g.Favorite ? "Favorite" : "Not favorite",
                    g => g.Favorite ? "Yes" : null, group: "Flags"),
                Cat("hidden", "Hidden", g => g.Hidden ? "Hidden" : "Visible",
                    g => g.Hidden ? "Yes" : null, group: "Flags"),
                Cat("installdrive", "Install drive", g => g.GetInstallDrive() ?? "(not installed)",
                    g => g.GetInstallDrive(), group: "Flags"),

                // ---- free text: hover only ----
                Text("sortingname", "Sorting name", g => g.SortingName),
                Text("version", "Version", g => g.Version),
                Text("notes", "Notes", g => g.Notes, 400),
                Text("description", "Description", g => g.Description, 400),
                Text("installdir", "Install directory", g => g.InstallDirectory),
                Text("links", "Links", g => JoinNames(g.Links?.Select(l => l.Name))),
                Text("roms", "ROMs", g => JoinNames(g.Roms?.Select(r => r.Name)))
            };
        }
    }
}
