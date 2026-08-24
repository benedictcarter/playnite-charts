using System;
using System.Collections.Generic;
using System.Linq;
using Playnite.SDK.Models;

namespace PlayniteCharts.Model
{
    /// <summary>
    /// One saved filter on a plot. Numeric and date columns keep a range (an open
    /// end is null); categorical columns keep the values that are switched OFF, so
    /// a category that appears in the library later is included by default rather
    /// than silently missing.
    /// </summary>
    public class FilterConfig
    {
        public const string NoValueKey = "(none)";

        public string FieldId { get; set; }
        public double? Lower { get; set; }
        public double? Upper { get; set; }
        public List<string> Excluded { get; set; } = new List<string>();

        /// <summary>A filter that lets everything through, i.e. not worth applying.</summary>
        public bool IsInert => !Lower.HasValue && !Upper.HasValue && (Excluded == null || Excluded.Count == 0);

        public bool Passes(GameColumn field, Game game)
        {
            if (field == null || IsInert)
            {
                return true;
            }

            if (field.IsContinuous)
            {
                var v = field.GetNumber?.Invoke(game);
                if (!v.HasValue)
                {
                    // no value can be neither inside nor outside a range; once the
                    // range is narrowed at all, these games drop out
                    return false;
                }

                if (Lower.HasValue && v.Value < Lower.Value)
                {
                    return false;
                }

                return !Upper.HasValue || !(v.Value > Upper.Value);
            }

            // a game with tags [Co-op, Indie] is shown while EITHER is still ticked:
            // the boxes say what you want to see, not what to hide
            var any = false;
            foreach (var value in field.Categories(game))
            {
                any = true;
                if (!Excluded.Contains(value, StringComparer.CurrentCultureIgnoreCase))
                {
                    return true;
                }
            }

            return !any && !Excluded.Contains(NoValueKey, StringComparer.CurrentCultureIgnoreCase);
        }

        public FilterConfig Clone()
        {
            return new FilterConfig
            {
                FieldId = FieldId,
                Lower = Lower,
                Upper = Upper,
                Excluded = new List<string>(Excluded ?? new List<string>())
            };
        }
    }

    internal static class FilterListExtensions
    {
        public static bool Contains(this List<string> list, string value, StringComparer comparer)
        {
            if (list == null)
            {
                return false;
            }

            foreach (var item in list)
            {
                if (comparer.Equals(item, value))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
