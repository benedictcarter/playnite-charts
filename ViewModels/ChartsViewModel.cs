using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using Playnite.SDK;
using Playnite.SDK.Models;
using PlayniteCharts.Controls;
using PlayniteCharts.Model;

namespace PlayniteCharts.ViewModels
{
    public class HoverOption : ObservableObject
    {
        private bool isChecked;

        public GameColumn Field { get; set; }

        public string Name => Field.Name;

        public bool IsChecked
        {
            get => isChecked;
            set => SetValue(ref isChecked, value);
        }
    }

    public class TableRow
    {
        public Game Game { get; set; }
        public List<string> Values { get; set; } = new List<string>();
    }

    public class ChartsViewModel : ObservableObject
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        private readonly ChartsPlugin plugin;
        private readonly IPlayniteAPI api;
        private bool suspendRebuild;
        private PlotConfig selectedPlot;
        private PlotModel model;
        private bool showTable;
        private bool tableDirty = true;
        private IList<Game> domainSource = new List<Game>();

        public ObservableCollection<PlotConfig> Plots { get; }

        public List<GameColumn> XFields { get; }
        public List<GameColumn> YFields { get; }
        public List<GameColumn> SizeFields { get; }
        public List<GameColumn> ColorFields { get; }
        public List<GameColumn> ShapeFields { get; }
        public ObservableCollection<HoverOption> HoverOptions { get; } = new ObservableCollection<HoverOption>();

        public List<string> TableColumns { get; private set; } = new List<string>();
        public List<TableRow> TableRows { get; private set; } = new List<TableRow>();

        public RelayCommand<object> NewPlotCommand { get; }
        public RelayCommand<object> DuplicatePlotCommand { get; }
        public RelayCommand<object> DeletePlotCommand { get; }
        public RelayCommand<object> RefreshCommand { get; }
        public RelayCommand<object> AllHoverCommand { get; }
        public RelayCommand<object> NoHoverCommand { get; }

        public ChartsViewModel(ChartsPlugin plugin, IPlayniteAPI api)
        {
            this.plugin = plugin;
            this.api = api;

            var none = new GameColumn { Id = string.Empty, Name = "(none)" };
            XFields = GameColumns.Continuous;
            YFields = GameColumns.Continuous;
            SizeFields = new[] { none }.Concat(GameColumns.Continuous).ToList();
            ColorFields = new[] { none }.Concat(GameColumns.Discrete).ToList();
            ShapeFields = new[] { none }.Concat(GameColumns.Discrete).ToList();

            foreach (var f in GameColumns.All.OrderBy(f => f.Group).ThenBy(f => f.Name))
            {
                HoverOptions.Add(new HoverOption { Field = f });
            }

            Plots = new ObservableCollection<PlotConfig>(plugin.Settings.Plots);
            Plots.CollectionChanged += OnPlotsChanged;
            foreach (var p in Plots)
            {
                p.PropertyChanged += OnPlotChanged;
            }

            selectedPlot = Plots.FirstOrDefault(p => p.Id == plugin.Settings.LastSelectedPlotId) ?? Plots.FirstOrDefault();
            SyncHoverOptions();

            NewPlotCommand = new RelayCommand<object>(_ => AddPlot(new PlotConfig { Name = UniqueName("New plot") }));
            DuplicatePlotCommand = new RelayCommand<object>(
                o => Duplicate(o as PlotConfig ?? selectedPlot),
                o => (o as PlotConfig ?? selectedPlot) != null);
            DeletePlotCommand = new RelayCommand<object>(o => Delete(o as PlotConfig ?? selectedPlot));
            RefreshCommand = new RelayCommand<object>(_ => Refresh());
            AllHoverCommand = new RelayCommand<object>(_ => SetAllHover(true), _ => SelectedPlot != null);
            NoHoverCommand = new RelayCommand<object>(_ => SetAllHover(false), _ => SelectedPlot != null);

            foreach (var o in HoverOptions)
            {
                o.PropertyChanged += OnHoverOptionChanged;
            }
        }

        public PlotConfig SelectedPlot
        {
            get => selectedPlot;
            set
            {
                if (ReferenceEquals(selectedPlot, value))
                {
                    return;
                }

                SetValue(ref selectedPlot, value);
                plugin.Settings.LastSelectedPlotId = value?.Id ?? Guid.Empty;
                SyncHoverOptions();
                Rebuild();
                OnPropertyChanged(nameof(HasPlot));
            }
        }

        public bool HasPlot => selectedPlot != null;

        public PlotModel Model
        {
            get => model;
            private set => SetValue(ref model, value);
        }

        public bool ShowTable
        {
            get => showTable;
            set
            {
                if (showTable == value)
                {
                    return;
                }

                SetValue(ref showTable, value);
                OnPropertyChanged(nameof(ShowPlot));
                if (showTable && tableDirty)
                {
                    BuildTable();
                }
            }
        }

        public bool ShowPlot => !showTable;

        public bool UseLibraryFilter
        {
            get => plugin.Settings.UseLibraryFilter;
            set
            {
                if (plugin.Settings.UseLibraryFilter != value)
                {
                    plugin.Settings.UseLibraryFilter = value;
                    OnPropertyChanged();
                    Refresh();
                }
            }
        }

        private string sourceSummary;
        public string SourceSummary
        {
            get => sourceSummary;
            private set => SetValue(ref sourceSummary, value);
        }

        /// <summary>Re-reads the games from Playnite and rebuilds the current plot.</summary>
        public void Refresh()
        {
            try
            {
                domainSource = api.Database.Games.Where(g => !g.Hidden).ToList();
                Rebuild();
            }
            catch (Exception e)
            {
                logger.Error(e, "Charts refresh failed.");
            }
        }

        private IList<Game> CurrentGames()
        {
            if (UseLibraryFilter)
            {
                var filtered = api.MainView.FilteredGames;
                if (filtered != null && filtered.Count > 0)
                {
                    return filtered;
                }
            }

            return domainSource;
        }

        private void Rebuild()
        {
            if (suspendRebuild)
            {
                return;
            }

            if (selectedPlot == null)
            {
                Model = null;
                return;
            }

            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var games = CurrentGames();
                if (domainSource.Count == 0)
                {
                    domainSource = games;
                }

                SourceSummary = UseLibraryFilter
                    ? $"Using the library filter - {games.Count:N0} of {domainSource.Count:N0} games"
                    : $"Whole library - {domainSource.Count:N0} games";

                Model = PlotModel.Build(selectedPlot, games, domainSource,
                    PlotTheme.SeriesCapacity, MarkShapes.Count);

                // one row per game x one string per column, and some of those
                // columns strip HTML - far too expensive to do for a hidden table
                tableDirty = true;
                if (showTable)
                {
                    BuildTable();
                }

                logger.Debug($"Charts: rebuilt '{selectedPlot.Name}' in {sw.ElapsedMilliseconds} ms " +
                    $"({Model.Points?.Count ?? 0} points, table {(showTable ? "built" : "deferred")}).");
            }
            catch (Exception e)
            {
                logger.Error(e, "Failed to build plot model.");
                Model = new PlotModel { Config = selectedPlot, Problem = "Could not build this plot: " + e.Message };
            }
        }

        /// <summary>
        /// The table view is the documented relief for the palette slots that sit
        /// below 3:1 contrast - the same rows, as text, with no colour encoding.
        /// </summary>
        private void BuildTable()
        {
            tableDirty = false;
            var m = Model;
            if (m?.Points == null || m.Problem != null)
            {
                TableColumns = new List<string>();
                TableRows = new List<TableRow>();
            }
            else
            {
                var fields = new List<GameColumn> { GameColumns.Get("name") };
                void Add(GameColumn f)
                {
                    if (f != null && !fields.Contains(f))
                    {
                        fields.Add(f);
                    }
                }

                Add(m.XField);
                Add(m.YField);
                Add(m.SizeField);
                Add(m.ColorScale?.Field);
                Add(m.ShapeScale?.Field);
                m.HoverFields.ForEach(Add);

                TableColumns = fields.Select(f => f.Name).ToList();
                TableRows = m.Points
                    .OrderBy(p => p.Game.Name, StringComparer.CurrentCultureIgnoreCase)
                    .Select(p => new TableRow
                    {
                        Game = p.Game,
                        Values = fields.Select(f => f.Display(p.Game) ?? string.Empty).ToList()
                    }).ToList();
            }

            OnPropertyChanged(nameof(TableColumns));
            OnPropertyChanged(nameof(TableRows));
        }

        // -------------------------------------------------------------- plot list

        private void AddPlot(PlotConfig plot)
        {
            Plots.Add(plot);
            SelectedPlot = plot;
            Persist();
        }

        private void Duplicate(PlotConfig plot)
        {
            if (plot == null)
            {
                return;
            }

            AddPlot(plot.Clone(UniqueName(plot.Name + " copy")));
        }

        private void Delete(PlotConfig plot)
        {
            if (plot == null)
            {
                return;
            }

            var idx = Plots.IndexOf(plot);
            Plots.Remove(plot);
            if (ReferenceEquals(plot, selectedPlot))
            {
                SelectedPlot = Plots.ElementAtOrDefault(Math.Min(idx, Plots.Count - 1));
            }

            Persist();
        }

        private string UniqueName(string basis)
        {
            var name = basis;
            var i = 2;
            while (Plots.Any(p => string.Equals(p.Name, name, StringComparison.CurrentCultureIgnoreCase)))
            {
                name = $"{basis} {i++}";
            }

            return name;
        }

        private void OnPlotsChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            foreach (PlotConfig p in e.OldItems ?? (System.Collections.IList)Array.Empty<PlotConfig>())
            {
                p.PropertyChanged -= OnPlotChanged;
            }

            foreach (PlotConfig p in e.NewItems ?? (System.Collections.IList)Array.Empty<PlotConfig>())
            {
                p.PropertyChanged += OnPlotChanged;
            }

            plugin.Settings.Plots = Plots.ToList();
        }

        private void OnPlotChanged(object sender, PropertyChangedEventArgs e)
        {
            if (!ReferenceEquals(sender, selectedPlot))
            {
                return;
            }

            if (e.PropertyName == nameof(PlotConfig.Name))
            {
                // "New" is the create-a-plot row; a real plot may not take that name
                if (string.Equals(selectedPlot.Name?.Trim(), "New", StringComparison.CurrentCultureIgnoreCase))
                {
                    selectedPlot.Name = UniqueName("New plot");
                    return;
                }
            }
            else
            {
                Rebuild();
            }

            Persist();
        }

        /// <summary>Ticking 25 boxes one at a time (and rebuilding each time) is nobody's idea of fun.</summary>
        private void SetAllHover(bool on)
        {
            if (selectedPlot == null)
            {
                return;
            }

            suspendRebuild = true;
            foreach (var o in HoverOptions)
            {
                o.IsChecked = on;
            }

            suspendRebuild = false;
            selectedPlot.HoverFieldIds = HoverOptions.Where(o => o.IsChecked).Select(o => o.Field.Id).ToList();
            Rebuild();
        }

        private void OnHoverOptionChanged(object sender, PropertyChangedEventArgs e)
        {
            if (suspendRebuild || selectedPlot == null || e.PropertyName != nameof(HoverOption.IsChecked))
            {
                return;
            }

            selectedPlot.HoverFieldIds = HoverOptions.Where(o => o.IsChecked).Select(o => o.Field.Id).ToList();
        }

        private void SyncHoverOptions()
        {
            suspendRebuild = true;
            var ids = new HashSet<string>(selectedPlot?.HoverFieldIds ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            foreach (var o in HoverOptions)
            {
                o.IsChecked = ids.Contains(o.Field.Id);
            }

            suspendRebuild = false;
        }

        public void Persist()
        {
            plugin.Settings.Plots = Plots.ToList();
            plugin.PersistSettings();
        }

        /// <summary>
        /// Writes a dragged value back into the library. The point holds whatever
        /// Game object the plot was built from; the write goes to the database's own
        /// instance so Playnite sees it and the rest of the UI updates with it.
        /// </summary>
        public void ApplyEdit(PlotPoint point, GameColumn column, double value)
        {
            if (point?.Game == null || column?.SetNumber == null)
            {
                return;
            }

            try
            {
                var game = api.Database.Games[point.Game.Id] ?? point.Game;
                var before = column.GetNumber?.Invoke(game);
                if (before.HasValue && Math.Abs(before.Value - value) < 1e-9)
                {
                    return;
                }

                column.SetNumber(game, value);
                api.Database.Games.Update(game);
                logger.Info($"Charts: set {column.Name} of '{game.Name}' to {column.Format(value)}.");

                if (!ReferenceEquals(game, point.Game))
                {
                    column.SetNumber(point.Game, value);
                }

                Rebuild();
            }
            catch (Exception e)
            {
                logger.Error(e, "Failed to write a dragged value back to the library.");
                api.Dialogs.ShowErrorMessage(
                    $"Could not set {column.Name}: {e.Message}", "Charts");
            }
        }

        /// <summary>Jump to the clicked game in the library view.</summary>
        public void ActivatePoint(PlotPoint point)
        {
            if (point?.Game == null)
            {
                return;
            }

            try
            {
                api.MainView.SelectGame(point.Game.Id);
                api.MainView.SwitchToLibraryView();
            }
            catch (Exception e)
            {
                logger.Error(e, "Failed to select game from chart.");
            }
        }
    }
}
