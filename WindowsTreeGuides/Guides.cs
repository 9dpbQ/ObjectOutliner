using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;

namespace ObjectOutliner.WindowsGuides
{
    public sealed class Guides : Adorner, IDisposable
    {
        private readonly DataGrid grid;
        private readonly Func<object, object> parent;
        private readonly Func<object, bool> isLast;
        private AdornerLayer layer;
        private DataGridRowsPresenter rows;

        public static IDisposable Attach(object control, Func<object, object> parent, Func<object, bool> isLast)
        {
            return control is DataGrid grid ? new Guides(grid, parent, isLast) : null;
        }

        private Guides(DataGrid grid, Func<object, object> parent, Func<object, bool> isLast) : base(grid)
        {
            this.grid = grid;
            this.parent = parent;
            this.isLast = isLast;
            IsHitTestVisible = false;
            Focusable = false;
            grid.Loaded += LoadedGrid;
            grid.Unloaded += UnloadedGrid;
            grid.LoadingRow += RowChanged;
            grid.UnloadingRow += RowChanged;
            grid.SizeChanged += SizeChangedGrid;
            grid.ColumnReordered += ColumnMoved;
            grid.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(Scrolled));
            if (grid.IsLoaded) LoadedGrid(null, null);
        }
        private void LoadedGrid(object sender, RoutedEventArgs e)
        {
            if (layer != null) return;
            layer = AdornerLayer.GetAdornerLayer(grid);
            layer?.Add(this);
        }
        private void UnloadedGrid(object sender, RoutedEventArgs e)
        {
            layer?.Remove(this);
            layer = null;
            rows = null;
        }
        private void RowChanged(object sender, DataGridRowEventArgs e) => InvalidateVisual();
        private void SizeChangedGrid(object sender, SizeChangedEventArgs e) => InvalidateVisual();
        private void ColumnMoved(object sender, DataGridColumnEventArgs e) => InvalidateVisual();
        private void Scrolled(object sender, ScrollChangedEventArgs e) => InvalidateVisual();
        public void Dispose()
        {
            grid.Loaded -= LoadedGrid;
            grid.Unloaded -= UnloadedGrid;
            grid.LoadingRow -= RowChanged;
            grid.UnloadingRow -= RowChanged;
            grid.SizeChanged -= SizeChangedGrid;
            grid.ColumnReordered -= ColumnMoved;
            grid.RemoveHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(Scrolled));
            UnloadedGrid(null, null);
        }
        private static T Find<T>(DependencyObject root) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T match) return match;
                var nested = Find<T>(child);
                if (nested != null) return nested;
            }
            return null;
        }
        protected override void OnRender(DrawingContext dc)
        {
            if (rows == null || !rows.IsDescendantOf(grid)) rows = Find<DataGridRowsPresenter>(grid);
            if (rows == null || grid.Columns.Count == 0) return;
            var spans = new Dictionary<object, Rect>();
            Rect clip = Rect.Empty;
            var pen = new Pen(grid.Foreground ?? SystemColors.ControlTextBrush, 1);
            // Only realized rows: accumulate one continuous trunk per ancestor.
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(rows); i++)
            {
                var row = VisualTreeHelper.GetChild(rows, i) as DataGridRow;
                if (row == null || row.Item == null || !row.IsVisible) continue;
                var cells = Find<DataGridCellsPresenter>(row);
                var cell = cells?.ItemContainerGenerator.ContainerFromIndex(0) as DataGridCell;
                if (cell == null) continue;
                var bounds = cell.TransformToAncestor(grid).TransformBounds(new Rect(cell.RenderSize));
                if (clip.IsEmpty)
                {
                    var port = rows.TransformToAncestor(grid).TransformBounds(new Rect(rows.RenderSize));
                    clip = Rect.Intersect(port, new Rect(bounds.Left, 0, bounds.Width, grid.ActualHeight));
                    if (clip.IsEmpty) return;
                    dc.PushClip(new RectangleGeometry(clip));
                }
                var ancestors = new List<object>();
                for (var p = parent(row.Item); p != null; p = parent(p)) ancestors.Add(p);
                var mid = bounds.Top + bounds.Height / 2;
                object child = row.Item;
                for (int j = 0; j < ancestors.Count; j++)
                {
                    var x = bounds.Left + 8 + 16 * (ancestors.Count - 1 - j);
                    if (j == 0) dc.DrawLine(pen, new Point(x, mid), new Point(x + 9, mid));
                    if (j == 0 || !isLast(child))
                    {
                        var end = j == 0 && isLast(child) ? mid : bounds.Bottom;
                        var segment = new Rect(new Point(x, bounds.Top), new Point(x, end));
                        if (spans.TryGetValue(ancestors[j], out var previous)) segment.Union(previous);
                        spans[ancestors[j]] = segment;
                    }
                    child = ancestors[j];
                }
            }
            foreach (var span in spans.Values) dc.DrawLine(pen, span.TopLeft, span.BottomLeft);
            if (!clip.IsEmpty) dc.Pop();
        }
    }
}
