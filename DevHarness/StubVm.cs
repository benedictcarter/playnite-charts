using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Playnite.SDK;
using Playnite.SDK.Models;
using PlayniteCharts.Controls;
using PlayniteCharts.Model;
using PlayniteCharts.ViewModels;

namespace PlayniteCharts.DevHarness
{
    /// <summary>
    /// Just enough view model to render ChartsView's left panel offscreen. The real
    /// one needs a live plugin and IPlayniteAPI, which the harness has no business
    /// standing up - but the row templates and theme brushes are worth looking at.
    /// </summary>
    internal class StubVm : ObservableObject
    {
        public StubVm(IList<Game> library = null)
        {
            var a = new PlotConfig { Name = "release date vs user score" };
            var b = new PlotConfig { Name = "playtime vs critic score" };
            Plots = new ObservableCollection<PlotConfig> { a, b };
            SelectedPlot = a;

            foreach (var f in GameColumns.All)
            {
                HoverOptions.Add(new HoverOption { Field = f, IsChecked = true });
            }

            FilterFields = GameColumns.All.Where(f => !f.HoverOnly).ToList();
            AddFilterCommand = new RelayCommand<object>(_ => { });
            RemoveFilterCommand = new RelayCommand<object>(_ => { });
            ClearFiltersCommand = new RelayCommand<object>(_ => { });

            if (library == null)
            {
                return;
            }

            // one of each shape of filter, so the templates get looked at
            Show("userscore", library);
            Show("genre", library);
        }

        public ObservableCollection<PlotConfig> Plots { get; }
        public ObservableCollection<HoverOption> HoverOptions { get; } = new ObservableCollection<HoverOption>();
        public ObservableCollection<FilterViewModel> Filters { get; } = new ObservableCollection<FilterViewModel>();

        public PlotConfig SelectedPlot { get; set; }
        public bool HasPlot => true;
        public bool ShowPlot => true;
        public bool HasFilters => Filters.Count > 0;
        public ViewSettings View { get; } = new ViewSettings();
        public IReadOnlyList<ColorRamp> ColorRamps => ColorRamp.All;
        public string SourceSummary => "720 games";

        public IReadOnlyList<GameColumn> XFields => GameColumns.Continuous;
        public IReadOnlyList<GameColumn> YFields => GameColumns.Continuous;
        public IReadOnlyList<GameColumn> SizeFields { get; } = Optional(GameColumns.Continuous);
        public IReadOnlyList<GameColumn> ColorFields { get; } = Optional(GameColumns.Colorable);
        public IReadOnlyList<GameColumn> ShapeFields { get; } = Optional(GameColumns.Discrete);
        public IReadOnlyList<GameColumn> FilterFields { get; }

        public RelayCommand<object> AddFilterCommand { get; }
        public RelayCommand<object> RemoveFilterCommand { get; }
        public RelayCommand<object> ClearFiltersCommand { get; }

        /// <summary>Mirrors ChartsViewModel: an optional channel starts with "(none)".</summary>
        private static IReadOnlyList<GameColumn> Optional(IEnumerable<GameColumn> fields)
        {
            var none = new GameColumn { Id = string.Empty, Name = "(none)" };
            return new[] { none }.Concat(fields).ToList();
        }

        private void Show(string fieldId, IList<Game> library)
        {
            var field = GameColumns.All.FirstOrDefault(f => f.Id == fieldId);
            if (field == null)
            {
                return;
            }

            Filters.Add(new FilterViewModel(field, new FilterConfig { FieldId = fieldId }, library, false, () => { }));
        }
    }
}
