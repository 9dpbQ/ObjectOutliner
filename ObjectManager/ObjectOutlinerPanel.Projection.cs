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
        private void RefreshTree(
            OutlinerStructureChange structureChange,
            int requestCount,
            string fallbackReason)
        {
            _treeRefreshQueued = false;
            if (!_isPanelVisible || !IsDocumentReady(_documentSerialNumber))
                return;

            var metrics = new OutlinerRefreshMetrics(
                "full",
                Math.Max(1, requestCount))
            {
                Cause = structureChange?.Cause,
                FallbackReason = fallbackReason,
                DirtyLayerCount = structureChange?.LayerIndices.Count ?? 0,
                ChangedObjectCount = structureChange?.ObjectIds.Count ?? 0
            };
            try
            {
                if (structureChange != null &&
                    (structureChange.Kinds &
                     ~OutlinerStructureChangeKind.ExplicitFull) !=
                        OutlinerStructureChangeKind.None)
                {
                    CancelHistoryInspection(false);
                    foreach (var objectId in structureChange.ObjectIds)
                    {
                        if (_protectedHistoryObjectIds.Contains(objectId))
                            continue;
                        _historyCache.Remove(objectId);
                        _resolvedHistoryObjectIds.Remove(objectId);
                    }
                }
                _nameCell?.CancelActiveEdit();
                var doc = RhinoDoc.FromRuntimeSerialNumber(_documentSerialNumber);
                if (doc == null)
                {
                    metrics.Measure(
                        OutlinerRefreshStage.DataStore,
                        () => RunProgrammaticTreeMutation(() =>
                            _tree.DataStore = new TreeGridItemCollection()));
                    _documentModel = null;
                    _snapshot = null;
                    InvalidateVisibleSelectionRows();
                    _selectedObjectIds.Clear();
                    _status.Text = OutlinerStrings.NoActiveDocument;
                    return;
                }

                _largeDocumentObjectCount = metrics.Measure(
                    OutlinerRefreshStage.ObjectCount,
                    () => OutlinerDocumentReader.CountTopLevelObjects(doc));
                metrics.ObjectCount = _largeDocumentObjectCount;
                var wasLargeDocumentDeferred = _largeDocumentDeferred;
                var useLargeDocumentDeferred =
                    !_forceFullObjectLoad &&
                    _largeDocumentObjectCount > AutomaticObjectLoadLimit;
                if (useLargeDocumentDeferred != wasLargeDocumentDeferred)
                {
                    _loadedLargeLayerIndices.Clear();
                    _missingSelectedObjectIds.Clear();
                }
                _largeDocumentDeferred = useLargeDocumentDeferred;
                metrics.Deferred = _largeDocumentDeferred;
                var objectCountsByLayer = _largeDocumentDeferred
                    ? metrics.Measure(
                        OutlinerRefreshStage.ObjectEnumeration,
                        () => OutlinerDocumentReader
                            .CountTopLevelObjectsByLayer(doc))
                    : null;
                _documentModel = _largeDocumentDeferred
                    ? OutlinerDocumentReader.ReadLayers(
                        doc,
                        _provisionalNames,
                        _loadedLargeLayerIndices,
                        metrics)
                    : OutlinerDocumentReader.Read(
                        doc,
                        _provisionalNames,
                        metrics);
                if (objectCountsByLayer != null)
                {
                    foreach (var layer in _documentModel.Layers)
                    {
                        layer.TopLevelObjectCount = objectCountsByLayer.TryGetValue(
                            layer.Index,
                            out var objectCount)
                                ? objectCount
                                : 0;
                    }
                }
                _fullObjectLoadCompleted = !_largeDocumentDeferred;
                ApplyHistoryCacheToModel();
                _protectedHistoryObjectIds.Clear();
                PopulateRefreshMetricCounts(metrics, _documentModel);
                metrics.Measure(OutlinerRefreshStage.Maintenance, () =>
                {
                    if (_largeDocumentDeferred && !wasLargeDocumentDeferred)
                    {
                        foreach (var layer in _documentModel.Layers)
                            _layerExpansionState[layer.Id] = false;
                    }
                    var activeObjectIds = new HashSet<Guid>(
                        _documentModel.Objects.Select(item => item.Id));
                    foreach (var objectId in _provisionalNames.Keys
                        .Where(id => !activeObjectIds.Contains(id))
                        .ToList())
                    {
                        _provisionalNames.Remove(objectId);
                    }
                    _isolateState.Validate(doc);
                    _isolateState.RebuildPresentation(_documentModel);
                    if (_viewState.HasReveal &&
                        !_documentModel.Objects.Any(item =>
                            item.Id == _viewState.RevealedObjectId))
                    {
                        _viewState.ClearReveal();
                    }
                    var validLayerIds = new HashSet<Guid>(
                        _documentModel.Layers.Select(layer => layer.Id));
                    var validLayerIndices = new HashSet<int>(
                        _documentModel.Layers.Select(layer => layer.Index));
                    _loadedLargeLayerIndices.RemoveWhere(
                        index => !validLayerIndices.Contains(index));
                    foreach (var layerId in _layerExpansionState.Keys
                        .Where(id => !validLayerIds.Contains(id))
                        .ToList())
                    {
                        _layerExpansionState.Remove(layerId);
                    }

                    var validGroupIds = new HashSet<Guid>(
                        _documentModel.Groups.Select(group => group.Id));
                    foreach (var key in _groupExpansionState.Keys
                        .Where(key =>
                            !validGroupIds.Contains(key.GroupId) ||
                            !validLayerIds.Contains(key.LayerId))
                        .ToList())
                    {
                        _groupExpansionState.Remove(key);
                    }

                    var validHistoryIds = new HashSet<Guid>(
                        _documentModel.HistoryByObjectId.Keys);
                    validHistoryIds.UnionWith(_documentModel.Objects
                        .Where(item => item.BlockDefinitionId != Guid.Empty)
                        .Select(item => item.Id));
                    foreach (var objectId in _historyExpansionState.Keys
                        .Where(id => !validHistoryIds.Contains(id))
                        .ToList())
                    {
                        _historyExpansionState.Remove(objectId);
                    }

                    _selectedLayerIds.RemoveWhere(
                        id => !validLayerIds.Contains(id));
                    _viewState.LayerFilter.SelectedLayerIds.RemoveWhere(
                        id => !validLayerIds.Contains(id));
                    if (_viewState.LayerFilter.Scope ==
                            OutlinerLayerScopeMode.SelectedLayerTrees &&
                        _viewState.LayerFilter.SelectedLayerIds.Count == 0)
                    {
                        _viewState.LayerFilter.Scope =
                            OutlinerLayerScopeMode.All;
                    }

                    _selectedObjectIds.Clear();
                    _selectedObjectIds.UnionWith(
                        _selectionController.Read(doc));
                });
                UpdateFilterControlValues();
                RefreshProjection(metrics);
            }
            finally
            {
                metrics.ReportIfSlow();
            }
        }

        private void RefreshProjection(OutlinerRefreshMetrics metrics = null)
        {
            if (_documentModel == null)
                return;

            var ownsMetrics = metrics == null;
            if (ownsMetrics)
            {
                var reason = _viewState.HasSearch
                    ? "search"
                    : _viewState.HasFilters ? "filter"
                    : _viewState.HasReveal ? "reveal" : "projection";
                metrics = new OutlinerRefreshMetrics(reason, 1)
                {
                    ObjectCount = _largeDocumentObjectCount > 0
                        ? _largeDocumentObjectCount
                        : _documentModel.Objects.Count,
                    Deferred = _largeDocumentDeferred
                };
            }
            PopulateRefreshMetricCounts(metrics, _documentModel);

            try
            {
                var snapshot = metrics.Measure(
                    OutlinerRefreshStage.Projection,
                    () => OutlinerTreeProjector.Project(
                        _documentModel,
                        _icons,
                        _layerExpansionState,
                        _groupExpansionState,
                        _historyExpansionState,
                        _viewState,
                        _isolateState,
                        _selectedObjectIds,
                        _groupOrderStore?.GetOrders(
                            _documentSerialNumber)));
                metrics.ProjectedRowCount = snapshot.Items.Count;

                metrics.Measure(OutlinerRefreshStage.DataStore, () =>
                {
                    _projectingTree = true;
                    try
                    {
                        _snapshot = snapshot;
                        InvalidateVisibleSelectionRows();
                        RunProgrammaticTreeMutation(() =>
                            _tree.DataStore = snapshot.Roots);
                    }
                    finally
                    {
                        _projectingTree = false;
                    }
                });

                UpdateStatusText();
                SynchronizeSelectionFromDocument(metrics);
                BeginPendingLayerEdit();
                SchedulePendingFilterNavigation();
                UpdateToolbarButtonState();
            }
            finally
            {
                if (ownsMetrics)
                    metrics.ReportIfSlow();
            }
        }

        private static void PopulateRefreshMetricCounts(
            OutlinerRefreshMetrics metrics,
            OutlinerDocumentModel model)
        {
            if (metrics == null || model == null)
                return;

            metrics.LoadedObjectCount = model.Objects.Count;
            metrics.LayerCount = model.Layers.Count;
            metrics.GroupCount = model.Groups.Count;
            metrics.GroupMembershipCount = model.GroupMembershipCount;
        }

        private void UpdateStatusText()
        {
            if (_snapshot == null || _documentModel == null)
                return;

            _status.Text = _viewState.HasDiscoveryState
                ? OutlinerStrings.FormatFilteredStatus(
                    _snapshot.ObjectCount,
                    _documentModel.Objects.Count,
                    _snapshot.LayerCount,
                    _documentModel.Layers.Count)
                : OutlinerStrings.FormatObjectLayerStatus(
                    _snapshot.ObjectCount,
                    _snapshot.LayerCount);
            if (_largeDocumentDeferred)
            {
                _status.Text = _viewState.HasDiscoveryState
                    ? OutlinerStrings.FormatDeferredMatchStatus(
                        _snapshot.ObjectCount,
                        _documentModel.Objects.Count,
                        _largeDocumentObjectCount)
                    : OutlinerStrings.FormatDeferredStatus(
                        _documentModel.Objects.Count,
                        _largeDocumentObjectCount);
            }
            if (!_largeDocumentDeferred &&
                _viewState.HasDiscoveryState &&
                _snapshot.ObjectCount == 0)
            {
                var hint =
                    _viewState.ContentFilter.SelectedObjectsOnly &&
                    _selectedObjectIds.Count == 0
                        ? OutlinerStrings.NoSelectedObjectsHint
                        : _snapshot.Items.Count == 0
                            ? OutlinerStrings.NoMatchingItemsHint
                            : OutlinerStrings.NoMatchingObjectsHint;
                _status.Text += "  |  " + hint;
            }
            var hiddenSelectionCount =
                _hiddenSelectedObjectCount + _hiddenSelectedLayerCount;
            if (hiddenSelectionCount > 0)
            {
                _status.Text += OutlinerStrings.FormatHiddenSelectionStatus(
                    hiddenSelectionCount);
            }
            if (_revealFilteredSelectionButton != null)
            {
                _revealFilteredSelectionButton.Visible =
                    hiddenSelectionCount > 0 &&
                    _viewState.HasDiscoveryState;
            }
            _loadAllObjectsButton.Visible = _largeDocumentDeferred;
        }

        private void OnSearchTextChanged(object sender, EventArgs e)
        {
            if (_applyingWorkspaceState)
                return;

            CancelRevealNavigation();
            _viewState.ClearReveal();
            var clearingSearch = _viewState.HasSearch &&
                string.IsNullOrWhiteSpace(_searchBox.Text);
            if (clearingSearch)
                PrepareFilterReturnNavigation();
            else
                CancelPendingFilterNavigation();
            _viewState.ClearTemporaryFilterReveal();
            _searchRefreshTimer.Stop();
            _searchRefreshTimer.Start();
        }

        private void OnSearchRefreshTimerElapsed(object sender, EventArgs e)
        {
            _searchRefreshTimer.Stop();
            if (_isClosing)
                return;

            _viewState.SearchText = _searchBox.Text;
            try
            {
                RefreshProjection();
            }
            catch (Exception exception)
            {
                HandleTreeRefreshError(exception);
            }
        }

        private void RefreshTreeSafely(
            OutlinerStructureChange structureChange,
            int requestCount,
            string fallbackReason = null)
        {
            if (!_isPanelVisible || !IsDocumentReady(_documentSerialNumber))
                return;

            try
            {
                RefreshTree(
                    structureChange,
                    requestCount,
                    fallbackReason);
            }
            catch (Exception exception)
            {
                HandleTreeRefreshError(exception);
            }
        }

        private void HandleTreeRefreshError(Exception exception)
        {
            _treeRefreshQueued = false;
            RhinoApp.WriteLine(OutlinerStrings.RefreshErrorLog);
            RhinoApp.WriteLine(exception.ToString());

            RunProgrammaticTreeMutation(() =>
                _tree.DataStore = new TreeGridItemCollection());

            _snapshot = null;
            InvalidateVisibleSelectionRows();
            _status.Text = OutlinerStrings.RefreshError;
            _loadAllObjectsButton.Visible = false;
        }
    }
}
