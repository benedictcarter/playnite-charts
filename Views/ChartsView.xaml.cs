using System;
using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Data;
using PlayniteCharts.Controls;
using PlayniteCharts.Model;
using PlayniteCharts.ViewModels;

namespace PlayniteCharts.Views
{
    public partial class ChartsView : UserControl
    {
        private ChartsViewModel model;

        public ChartsView()
        {
            InitializeComponent();
            Plot.PointActivated += OnPointActivated;
            Plot.ValueEdited += OnValueEdited;
            DataContextChanged += OnDataContextChanged;
        }

        private void OnDataContextChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
        {
            if (model != null)
            {
                model.PropertyChanged -= OnModelPropertyChanged;
            }

            model = DataContext as ChartsViewModel;
            if (model != null)
            {
                model.PropertyChanged += OnModelPropertyChanged;
                RebuildTableColumns();
            }
        }

        private void OnModelPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ChartsViewModel.TableColumns))
            {
                RebuildTableColumns();
            }
        }

        private void OnPointActivated(object sender, PlotPoint point)
        {
            model?.ActivatePoint(point);
        }

        private void OnValueEdited(object sender, ValueEditEventArgs e)
        {
            model?.ApplyEdit(e.Point, e.Column, e.Value);
        }

        /// <summary>The table's columns follow whichever fields the current plot uses.</summary>
        private void RebuildTableColumns()
        {
            if (!(Table.View is GridView grid) || model == null)
            {
                return;
            }

            grid.Columns.Clear();
            for (var i = 0; i < model.TableColumns.Count; i++)
            {
                grid.Columns.Add(new GridViewColumn
                {
                    Header = model.TableColumns[i],
                    DisplayMemberBinding = new Binding($"Values[{i}]"),
                    Width = i == 0 ? 240 : double.NaN
                });
            }
        }
    }
}
