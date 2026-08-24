using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using PlayniteCharts.Controls;
using PlayniteCharts.Model;
using PlayniteCharts.ViewModels;

namespace PlayniteCharts.Views
{
    public partial class ChartsView : UserControl
    {
        private const string PlotDragFormat = "PlayniteCharts.PlotConfig";

        private ChartsViewModel model;
        private bool inkedMenu;
        private Point dragStart;
        private PlotConfig dragCandidate;

        public ChartsView()
        {
            InitializeComponent();
            Plot.PointActivated += OnPointActivated;
            Plot.PointMenuRequested += OnPointMenuRequested;
            Plot.ValueEdited += OnValueEdited;
            DataContextChanged += OnDataContextChanged;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            model = DataContext as ChartsViewModel;
        }

        private void OnAddFilterClick(object sender, RoutedEventArgs e)
        {
            var menu = AddFilterButton.ContextMenu;
            if (menu == null)
            {
                return;
            }

            // the menu is not in the visual tree, so it cannot walk up to the view model
            menu.DataContext = model;

            // The popup is its own visual tree, so a DynamicResource inside it can
            // miss the theme dictionary and land on the MenuItem default (near-black
            // on Playnite's dark menu). Resolve the brush here - the button really is
            // in the themed tree - and bake it into the item style, which beats both
            // the unresolved setter and plain inheritance.
            if (!inkedMenu && TryFindResource("TextBrush") is Brush ink)
            {
                inkedMenu = true;
                menu.Foreground = ink;
                var style = new Style(typeof(MenuItem), menu.ItemContainerStyle);
                style.Setters.Add(new Setter(ForegroundProperty, ink));
                style.Seal();
                menu.ItemContainerStyle = style;
            }

            menu.PlacementTarget = AddFilterButton;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        }

        // ------------------------------------------------------- reordering plots

        private void OnPlotListMouseDown(object sender, MouseButtonEventArgs e)
        {
            dragStart = e.GetPosition(null);
            dragCandidate = ItemUnder(e.OriginalSource as DependencyObject);
        }

        private void OnPlotListMouseMove(object sender, MouseEventArgs e)
        {
            if (dragCandidate == null || e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }

            // a click that wanders a pixel is still a click, so wait for the
            // system's own drag threshold before taking the mouse hostage
            var moved = e.GetPosition(null) - dragStart;
            if (Math.Abs(moved.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(moved.Y) < SystemParameters.MinimumVerticalDragDistance)
            {
                return;
            }

            var dragged = dragCandidate;
            dragCandidate = null;
            DragDrop.DoDragDrop(PlotList, new DataObject(PlotDragFormat, dragged), DragDropEffects.Move);
        }

        private void OnPlotListDragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(PlotDragFormat) ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;
        }

        private void OnPlotListDrop(object sender, DragEventArgs e)
        {
            e.Handled = true;
            if (model == null || !(e.Data.GetData(PlotDragFormat) is PlotConfig dragged))
            {
                return;
            }

            var from = model.Plots.IndexOf(dragged);
            if (from < 0)
            {
                return;
            }

            // dropped on a row: land before or after it depending on which half was
            // hit, so the last position is reachable. Dropped past the last row: end.
            var target = ItemUnder(e.OriginalSource as DependencyObject);
            int to;
            if (target == null)
            {
                to = model.Plots.Count - 1;
            }
            else
            {
                to = model.Plots.IndexOf(target);
                if (to < 0)
                {
                    return;
                }

                var container = (ListBoxItem)PlotList.ItemContainerGenerator.ContainerFromItem(target);
                if (container != null && e.GetPosition(container).Y > container.ActualHeight / 2)
                {
                    to++;
                }

                // Move() indexes the list with the dragged row already lifted out
                if (to > from)
                {
                    to--;
                }
            }

            if (to != from)
            {
                model.Plots.Move(from, to);
                model.Persist();
            }
        }

        /// <summary>The plot whose row the given visual sits in, if any.</summary>
        private static PlotConfig ItemUnder(DependencyObject source)
        {
            while (source != null && !(source is ListBoxItem))
            {
                source = source is Visual || source is Visual3D
                    ? VisualTreeHelper.GetParent(source)
                    : LogicalTreeHelper.GetParent(source);
            }

            return (source as ListBoxItem)?.DataContext as PlotConfig;
        }

        private void OnPointActivated(object sender, PlotPoint point)
        {
            model?.ActivatePoint(point);
        }

        private void OnPointMenuRequested(object sender, PlotPoint point)
        {
            // Playnite's own games-list menu, borrowed rather than reimplemented -
            // see DesktopGameMenu. Silently does nothing outside the desktop app.
            Interop.DesktopGameMenu.Show(Plot, point?.Game);
        }

        private void OnValueEdited(object sender, ValueEditEventArgs e)
        {
            model?.ApplyEdit(e.Point, e.Column, e.Value);
        }
    }
}
