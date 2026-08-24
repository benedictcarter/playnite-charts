using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Playnite.SDK;
using Playnite.SDK.Models;
using PlayniteCharts.Model;

namespace PlayniteCharts.ViewModels
{
    public class FilterValueOption : ObservableObject
    {
        private bool isChecked = true;

        public string Value { get; set; }

        /// <summary>How many games in the library carry this value.</summary>
        public int Count { get; set; }

        public string Label => Count > 0 ? $"{Value}  ({Count:N0})" : Value;

        public bool IsChecked
        {
            get => isChecked;
            set => SetValue(ref isChecked, value);
        }
    }

    /// <summary>
    /// One filter row: a range for a number or a date, a tick list for anything
    /// categorical. The domain comes from the whole library, never the filtered
    /// subset, so ticking a box off does not make other boxes disappear.
    /// </summary>
    public class FilterViewModel : ObservableObject
    {
        private readonly Action changed;
        private readonly bool missingAsZero;
        private bool loading = true;
        private double lower;
        private double upper;

        public GameColumn Field { get; }
        public FilterConfig Config { get; }

        public ObservableCollection<FilterValueOption> Values { get; } = new ObservableCollection<FilterValueOption>();

        public RelayCommand<object> AllCommand { get; }
        public RelayCommand<object> NoneCommand { get; }

        public FilterViewModel(GameColumn field, FilterConfig config, IEnumerable<Game> library,
            bool missingAsZero, Action changed)
        {
            Field = field;
            Config = config;
            this.missingAsZero = missingAsZero && field.Kind == FieldKind.Numeric;
            this.changed = changed;

            AllCommand = new RelayCommand<object>(_ => SetAll(true));
            NoneCommand = new RelayCommand<object>(_ => SetAll(false));

            if (field.IsContinuous)
            {
                LoadRange(library);
            }
            else
            {
                LoadValues(library);
            }

            loading = false;
        }

        public string Name => Field.Name;

        public bool IsRange => Field.IsContinuous;

        public bool IsList => !Field.IsContinuous;

        public double Minimum { get; private set; }

        public double Maximum { get; private set; }

        /// <summary>Whole numbers snap; dates snap to the day; the rest slide free.</summary>
        public double Step { get; private set; }

        public double Lower
        {
            get => lower;
            set
            {
                if (Math.Abs(lower - value) < 1e-9)
                {
                    return;
                }

                SetValue(ref lower, value);

                // an end left at the domain edge is stored as "open", so the filter
                // widens by itself when the library grows past it
                Config.Lower = value <= Minimum + 1e-9 ? (double?)null : value;
                OnPropertyChanged(nameof(RangeText));
                Touch();
            }
        }

        public double Upper
        {
            get => upper;
            set
            {
                if (Math.Abs(upper - value) < 1e-9)
                {
                    return;
                }

                SetValue(ref upper, value);
                Config.Upper = value >= Maximum - 1e-9 ? (double?)null : value;
                OnPropertyChanged(nameof(RangeText));
                Touch();
            }
        }

        public string RangeText => $"{Field.Format(lower)}  to  {Field.Format(upper)}";

        public string Summary
        {
            get
            {
                if (IsRange)
                {
                    return Config.IsInert ? "any" : RangeText;
                }

                var on = Values.Count(v => v.IsChecked);
                return on == Values.Count ? "any" : $"{on} of {Values.Count}";
            }
        }

        private void LoadRange(IEnumerable<Game> library)
        {
            var min = double.MaxValue;
            var max = double.MinValue;
            foreach (var g in library)
            {
                var v = Field.GetNumber?.Invoke(g);
                if (!v.HasValue && missingAsZero)
                {
                    // the plot draws these at 0, so the slider has to be able to reach them
                    v = 0;
                }

                if (v.HasValue)
                {
                    min = Math.Min(min, v.Value);
                    max = Math.Max(max, v.Value);
                }
            }

            if (min > max)
            {
                min = 0;
                max = 1;
            }

            if (Math.Abs(max - min) < 1e-9)
            {
                max = min + 1;
            }

            Minimum = min;
            Maximum = max;
            Step = Field.Kind == FieldKind.Date ? 1 : (max - min > 40 ? 1 : 0);
            lower = Math.Max(min, Math.Min(max, Config.Lower ?? min));
            upper = Math.Max(lower, Math.Min(max, Config.Upper ?? max));
        }

        private void LoadValues(IEnumerable<Game> library)
        {
            var counts = new Dictionary<string, int>(StringComparer.CurrentCultureIgnoreCase);
            var missing = 0;
            foreach (var g in library)
            {
                var any = false;
                foreach (var v in Field.Categories(g))
                {
                    any = true;
                    counts.TryGetValue(v, out var n);
                    counts[v] = n + 1;
                }

                if (!any)
                {
                    missing++;
                }
            }

            var excluded = new HashSet<string>(Config.Excluded ?? new List<string>(),
                StringComparer.CurrentCultureIgnoreCase);

            foreach (var pair in counts.OrderBy(p => p.Key, StringComparer.CurrentCultureIgnoreCase))
            {
                Add(pair.Key, pair.Value, excluded);
            }

            if (missing > 0)
            {
                Add(FilterConfig.NoValueKey, missing, excluded);
            }
        }

        private void Add(string value, int count, HashSet<string> excluded)
        {
            var option = new FilterValueOption
            {
                Value = value,
                Count = count,
                IsChecked = !excluded.Contains(value)
            };

            option.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(FilterValueOption.IsChecked))
                {
                    WriteExcluded();
                }
            };

            Values.Add(option);
        }

        private void SetAll(bool on)
        {
            loading = true;
            foreach (var v in Values)
            {
                v.IsChecked = on;
            }

            loading = false;
            WriteExcluded();
        }

        private void WriteExcluded()
        {
            Config.Excluded = Values.Where(v => !v.IsChecked).Select(v => v.Value).ToList();
            Touch();
        }

        private void Touch()
        {
            if (loading)
            {
                return;
            }

            OnPropertyChanged(nameof(Summary));
            changed?.Invoke();
        }
    }
}
