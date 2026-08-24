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

        /// <summary>
        /// Every value the game holds on this column. Tags, genres and the like are
        /// lists, and a filter has to treat each entry as its own thing - colour and
        /// shape still collapse to <see cref="GetCategory"/> because a mark can only
        /// have one of each. Null means the column is single-valued.
        /// </summary>
        public Func<Game, IEnumerable<string>> GetCategories { get; set; }

        public bool IsMultiValued => GetCategories != null;

        /// <summary>Human text for tooltips. Falls back to the other two.</summary>
        public Func<Game, string> GetDisplay { get; set; }

        /// <summary>Axis tick / tooltip formatting for numeric values.</summary>
        public Func<double, string> FormatNumber { get; set; }

        /// <summary>Free text (notes, install path, ...): fine in a tooltip, useless as a
        /// colour or shape channel, so it is offered for hover only.</summary>
        public bool HoverOnly { get; set; }

        /// <summary>Writes a value back onto the game. Null (the default) means the
        /// column is read-only and its axis cannot be dragged.</summary>
        public Action<Game, double> SetNumber { get; set; }

        /// <summary>Clamps and snaps a dragged value to something the column can
        /// actually hold (user score is a whole number from 0 to 100).</summary>
        public Func<double, double> Quantize { get; set; }

        /// <summary>Grid step this column would rather have than a computed "nice"
        /// one. 0 lets the axis choose. A 0-100 score reads in tens, and the
        /// automatic pick lands on 20 as soon as the axis is short.</summary>
        public double PreferredTickStep { get; set; }

        public bool IsEditable => SetNumber != null;

        /// <summary>The value a drag to this position would actually store.</summary>
        public double Snap(double value) => Quantize != null ? Quantize(value) : value;

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

        /// <summary>The values a filter offers, de-duplicated, never null.</summary>
        public IEnumerable<string> Categories(Game game)
        {
            if (GetCategories != null)
            {
                var seen = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
                foreach (var v in GetCategories(game) ?? Enumerable.Empty<string>())
                {
                    if (!string.IsNullOrEmpty(v) && seen.Add(v))
                    {
                        yield return v;
                    }
                }

                yield break;
            }

            var single = GetCategory?.Invoke(game);
            if (!string.IsNullOrEmpty(single))
            {
                yield return single;
            }
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

        /// <summary>A 0-100 rating, gridded in tens. Given a setter it also becomes
        /// a score the user owns, so its axis can be dragged.</summary>
        private static GameColumn Score(string id, string name, Func<Game, double?> get,
            Action<Game, int> set = null)
        {
            var f = Num(id, name, get);
            f.PreferredTickStep = 10;
            if (set == null)
            {
                return f;
            }

            f.Quantize = v => Math.Max(0, Math.Min(100, Math.Round(v)));
            f.SetNumber = (g, v) => set(g, (int)Math.Max(0, Math.Min(100, Math.Round(v))));
            return f;
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

        /// <summary>
        /// A column whose games each hold a list (tags, genres, ...). Colour and
        /// shape keep using the first value; filters see them one by one.
        /// </summary>
        private static GameColumn Multi<T>(string id, string name, Func<Game, IEnumerable<T>> get)
            where T : DatabaseObject
        {
            var f = Cat(id, name, g => First(get(g)) ?? "(none)", g => Join(get(g)));
            f.GetCategories = g => (get(g) ?? Enumerable.Empty<T>())
                .Select(i => i.Name).Where(n => !string.IsNullOrEmpty(n));
            return f;
        }

        private static List<GameColumn> Build()
        {
            return new List<GameColumn>
            {
                // ---- continuous: usable for X, Y and bubble size ----
                Num("playtime", "Playtime (hours)", g => g.Playtime > 0 ? g.Playtime / 3600.0 : (double?)null, v => v.ToString("0.#") + " h"),
                Num("playcount", "Play count", g => g.PlayCount > 0 ? g.PlayCount : (double?)null, v => v.ToString("N0")),
                Num("installsize", "Install size (GB)", g => g.InstallSize.HasValue && g.InstallSize.Value > 0 ? g.InstallSize.Value / 1073741824.0 : (double?)null, v => v.ToString("0.##") + " GB"),
                Score("userscore", "User score", g => g.UserScore, (g, v) => g.UserScore = v),
                Score("criticscore", "Critic score", g => g.CriticScore),
                Score("communityscore", "Community score", g => g.CommunityScore),
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
                Multi("platform", "Platform", g => g.Platforms),
                Multi("genre", "Genre", g => g.Genres),
                Multi("category", "Category", g => g.Categories),
                Multi("series", "Series", g => g.Series),
                Multi("developer", "Developer", g => g.Developers),
                Multi("publisher", "Publisher", g => g.Publishers),
                Multi("agerating", "Age rating", g => g.AgeRatings),
                Multi("region", "Region", g => g.Regions),
                Multi("feature", "Feature", g => g.Features),
                Multi("tag", "Tag", g => g.Tags),
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
