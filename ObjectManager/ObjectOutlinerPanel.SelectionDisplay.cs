using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using Eto.Drawing;
using Eto.Forms;
using Rhino;
using Rhino.DocObjects;
using Rhino.UI;
using ObjectManager.Outliner;

namespace ObjectManager
{
    public sealed partial class ObjectOutlinerPanel
    {
        private void SynchronizeSelectionFromDocument(
            OutlinerRefreshMetrics metrics = null)
        {
            using (var diagnostic = OutlinerDiagnosticScope.Start("selection-sync", _documentSerialNumber))
            {
                var ownsMetrics = metrics == null;
                if (ownsMetrics)
                {
                    metrics = new OutlinerRefreshMetrics("selection", 1, 50)
                    {
                        ObjectCount = _largeDocumentObjectCount,
                        Deferred = _largeDocumentDeferred
                    };
                    PopulateRefreshMetricCounts(metrics, _documentModel);
                    metrics.ProjectedRowCount = _snapshot?.Items.Count ?? 0;
                }

                try
                {
                    _selectionRefreshQueued = false;
                    if (_selectionEventGate.ExpansionPending)
                        return;

                    var doc = RhinoDoc.FromRuntimeSerialNumber(_documentSerialNumber);
                    if (doc == null || _snapshot == null)
                        return;

                    ISet<TreeGridItem> markerItems = null;
                    HashSet<int> selectedRows = null;
                    var canRenderRows = true;
                    metrics.Measure(OutlinerRefreshStage.Selection, () =>
                    {
                        var previousSelection = new HashSet<Guid>(
                            _selectedObjectIds);
                        _selectedObjectIds.Clear();
                        metrics.MeasureDetail("readRhinoSelection", () =>
                            _selectedObjectIds.UnionWith(_selectionController.Read(doc)));
                        metrics.Detail("selectedObjects", _selectedObjectIds.Count);
                        var selectionChanged = !previousSelection.SetEquals(
                            _selectedObjectIds);
                        if (selectionChanged &&
                            _viewState.ContentFilter.SelectedObjectsOnly)
                        {
                            QueueFilteredSelectionProjection();
                        }
                        if (selectionChanged &&
                            _viewState.HasTemporaryFilterReveal)
                        {
                            _viewState.ClearTemporaryFilterReveal();
                            QueueFilteredSelectionProjection();
                        }
                        metrics.MeasureDetail("missingSelection", () => RequestRefreshForMissingSelection(doc));
                        metrics.MeasureDetail("queueHistory", () => QueueHistoryInspection(doc));

                        var logicalStarted = Stopwatch.GetTimestamp();
                        GetLogicalSelection(
                            out var logicalGroupIds,
                            out var logicalObjectIds);
                        markerItems = UpdateCollapsedSelectionIndicators(
                            logicalGroupIds,
                            logicalObjectIds);
                        metrics.Detail("logicalSelectionAndMarkersUs", (Stopwatch.GetTimestamp() - logicalStarted) * 1000000L / Stopwatch.Frequency);

                        if (_nameCell != null && _nameCell.HasActiveEdit)
                        {
                            canRenderRows = false;
                            return;
                        }

                        metrics.MeasureDetail("visibleRowIndex", EnsureVisibleSelectionRows);
                        var rowsStarted = Stopwatch.GetTimestamp();
                        selectedRows = new HashSet<int>();
                        foreach (var objectId in _selectedObjectIds)
                        {
                            if (_visibleObjectRows.TryGetValue(objectId, out var row))
                                selectedRows.Add(row);
                        }
                        foreach (var layerId in _selectedLayerIds)
                        {
                            if (_visibleLayerRows.TryGetValue(layerId, out var row))
                                selectedRows.Add(row);
                        }
                        foreach (var groupId in logicalGroupIds)
                        {
                            if (!_visibleGroupRows.TryGetValue(groupId, out var rows))
                                continue;

                            foreach (var row in rows)
                                selectedRows.Add(row);
                        }
                        metrics.Detail("mapSelectedRowsUs", (Stopwatch.GetTimestamp() - rowsStarted) * 1000000L / Stopwatch.Frequency);
                        metrics.MeasureDetail("hiddenSelection", () => UpdateHiddenSelectionState(doc));
                    });

                    metrics.MeasureDetail("statusText", UpdateStatusText);

                    if (_nameCell != null && markerItems != null)
                    {
                        metrics.Measure(
                            OutlinerRefreshStage.CustomCells,
                            () => _nameCell.RefreshItems(markerItems));
                    }

                    if (!canRenderRows || selectedRows == null)
                        return;

                    metrics.RenderedSelectionRowCount = selectedRows.Count;
                    if (_renderedSelectedRowsValid &&
                        _renderedSelectedRows.SetEquals(selectedRows))
                    {
                        metrics.SelectionWriteMode = "unchanged";
                        return;
                    }

                    metrics.Measure(OutlinerRefreshStage.SelectedRows, () =>
                        ApplyRenderedSelectionRows(selectedRows, metrics));
                    _renderedSelectedRows.Clear();
                    _renderedSelectedRows.UnionWith(selectedRows);
                    _renderedSelectedRowsValid = true;
                }
                finally
                {
                    metrics.Detail("suppressedDocumentEvents", _diagnosticSuppressedDocumentEvents);
                    _diagnosticSuppressedDocumentEvents = 0;
                    if (ownsMetrics)
                        metrics.ReportIfSlow();
                }
            }
        }

        private void ApplyRenderedSelectionRows(
            ISet<int> selectedRows,
            OutlinerRefreshMetrics metrics)
        {
            var desiredRows = selectedRows ?? new HashSet<int>();
            metrics.RenderedSelectionRowCount = desiredRows.Count;
            _programmaticSelectionEventCount = 0;
            if (!_renderedSelectedRowsValid)
            {
                metrics.SelectionRowsAdded = desiredRows.Count;
                metrics.SelectionWriteMode = "replace-initial";
                SetProgrammaticTreeSelection(() =>
                    _tree.SelectedRows = desiredRows
                        .OrderBy(row => row)
                        .ToList());
                metrics.ProgrammaticSelectionEventCount =
                    _programmaticSelectionEventCount;
                return;
            }

            var rowsToUnselect = _renderedSelectedRows
                .Where(row => !desiredRows.Contains(row))
                .ToList();
            var rowsToSelect = desiredRows
                .Where(row => !_renderedSelectedRows.Contains(row))
                .OrderBy(row => row)
                .ToList();
            metrics.SelectionRowsAdded = rowsToSelect.Count;
            metrics.SelectionRowsRemoved = rowsToUnselect.Count;
            if (rowsToUnselect.Count == 0 && rowsToSelect.Count == 0)
            {
                metrics.SelectionWriteMode = "none";
                return;
            }

            if (OutlinerRefreshPolicy.ReplaceSelection(rowsToSelect.Count, rowsToUnselect.Count))
            {
                metrics.SelectionWriteMode = "replace";
                SetProgrammaticTreeSelection(() =>
                    _tree.SelectedRows = desiredRows
                        .OrderBy(row => row)
                        .ToList());
                metrics.ProgrammaticSelectionEventCount =
                    _programmaticSelectionEventCount;
                return;
            }

            metrics.SelectionWriteMode = "delta";
            SetProgrammaticTreeSelection(() =>
            {
                foreach (var row in rowsToUnselect)
                    _tree.UnselectRow(row);
                foreach (var row in rowsToSelect)
                    _tree.SelectRow(row);
            });
            metrics.ProgrammaticSelectionEventCount =
                _programmaticSelectionEventCount;
        }
    }
}
