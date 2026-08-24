using System;
using System.Collections.Generic;
using System.Linq;
using Playnite.SDK.Models;

namespace PlayniteCharts.Model
{
    /// <summary>
    /// The one place a number becomes a fraction of a visual channel. Size and
    /// colour ask the same question - "where does this value sit between the ends?"
    /// - so the rules only have to be right once:
    ///
    ///  * the range is the *plotted* range, so it follows the filters;
    ///  * a narrowed filter on the column wins over the observed range, because a
    ///    window the user chose is the range they mean;
    ///  * zero anchoring is available (twice the value, twice the ink) but only for
    ///    channels that can honestly use it, only while the range is not a chosen
    ///    window, and never for dates, whose zero is December 1899.
    ///
    /// What the fraction then drives is the caller's business: size takes its square
    /// root so the AREA carries the number, colour reads it straight off a ramp.
    /// </summary>
    public class ChannelScale
    {
        public GameColumn Field { get; private set; }

        /// <summary>Ends of the range, after any filter window is applied.</summary>
        public double Min { get; private set; }
        public double Max { get; private set; }

        /// <summary>False when every value is identical (or there are none) - the
        /// channel then carries nothing and the caller should draw a neutral.</summary>
        public bool HasRange { get; private set; }

        /// <summary>See <see cref="Fraction"/>.</summary>
        public bool AnchoredAtZero { get; private set; }

        /// <summary>
        /// Builds the scale for one column over the games actually being plotted.
        /// <paramref name="read"/> is the caller's value reader, so "count a missing
        /// number as 0" stays in one place too. <paramref name="allowZeroAnchor"/>
        /// asks for the zero anchor where it is meaningful - size wants it, a colour
        /// ramp does not, since a ramp's ends are the ends of the data by definition.
        /// Returns null when there is no column on the channel.
        /// </summary>
        public static ChannelScale For(GameColumn field, IEnumerable<Game> games,
            IEnumerable<FilterConfig> filters, Func<GameColumn, Game, double?> read, bool allowZeroAnchor)
        {
            if (field == null)
            {
                return null;
            }

            var s = new ChannelScale { Field = field };
            var vals = games.Select(g => read(field, g)).Where(v => v.HasValue).Select(v => v.Value).ToList();
            if (vals.Count == 0)
            {
                return s;
            }

            s.Min = vals.Min();
            s.Max = vals.Max();

            // Filter bounds sit at null on the domain edge, so an untouched slider
            // leaves the range alone and only a real narrowing counts as a window.
            var window = (filters ?? Enumerable.Empty<FilterConfig>())
                .FirstOrDefault(f => f != null && !f.IsInert && f.FieldId == field.Id);
            var windowed = false;
            if (window != null)
            {
                if (window.Lower.HasValue && window.Lower.Value > s.Min)
                {
                    s.Min = window.Lower.Value;
                    windowed = true;
                }

                if (window.Upper.HasValue && window.Upper.Value < s.Max)
                {
                    s.Max = window.Upper.Value;
                    windowed = true;
                }
            }

            s.HasRange = s.Max > s.Min;
            s.AnchoredAtZero = allowZeroAnchor && !windowed
                && s.Min >= 0 && s.Max > 0 && field.Kind != FieldKind.Date;
            return s;
        }

        /// <summary>
        /// Where the value sits on the channel, 0..1. Span-normalised -
        /// (v - Min) / (Max - Min) - unless the scale is anchored at zero, where the
        /// bottom of the channel is 0 rather than the smallest value present.
        /// </summary>
        public double Fraction(double value)
        {
            if (!HasRange)
            {
                return 0.5;
            }

            var t = AnchoredAtZero ? value / Max : (value - Min) / (Max - Min);
            return t < 0 ? 0 : t > 1 ? 1 : t;
        }

        /// <summary>Smallest / middle / largest, deduped - what a legend key shows.</summary>
        public IEnumerable<double> KeyValues()
        {
            if (!HasRange)
            {
                yield return Max;
                yield break;
            }

            yield return Min;
            var mid = (Min + Max) / 2;
            if (Math.Abs(mid - Min) > 1e-9 && Math.Abs(mid - Max) > 1e-9)
            {
                yield return mid;
            }

            yield return Max;
        }
    }
}
