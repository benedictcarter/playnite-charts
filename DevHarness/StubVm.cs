using System.Collections.Generic;
using System.Collections.ObjectModel;
using Playnite.SDK;
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
        private object selectedRow;

        public StubVm()
        {
            var a = new PlotConfig { Name = "release date vs user score" };
            var b = new PlotConfig { Name = "playtime vs critic score" };
            PlotRows = new ObservableCollection<object> { a, b, new NewPlotRow() };
            selectedRow = a;
            SelectedPlot = a;

            foreach (var f in GameColumns.All)
            {
                HoverOptions.Add(new HoverOption { Field = f, IsChecked = true });
            }
        }

        public ObservableCollection<object> PlotRows { get; }
        public ObservableCollection<HoverOption> HoverOptions { get; } = new ObservableCollection<HoverOption>();

        public object SelectedRow
        {
            get => selectedRow;
            set => SetValue(ref selectedRow, value);
        }

        public PlotConfig SelectedPlot { get; }
        public bool HasPlot => true;
        public bool ShowPlot => true;
        public string SourceSummary => "720 games";

        public List<GameColumn> XFields => GameColumns.Continuous;
        public List<GameColumn> YFields => GameColumns.Continuous;
        public List<GameColumn> SizeFields => GameColumns.Continuous;
        public List<GameColumn> ColorFields => GameColumns.Discrete;
        public List<GameColumn> ShapeFields => GameColumns.Discrete;
    }
}
