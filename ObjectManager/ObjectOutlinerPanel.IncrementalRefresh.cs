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
        private void QueueTreeRefresh()
        {
            QueueStructureRefresh(OutlinerStructureChange.Full());
        }

        private void QueueStructureRefresh(OutlinerStructureChange change)
        {
            if (change == null ||
                change.IsEmpty ||
                _isClosing ||
                !_isPanelVisible ||
                !IsDocumentReady(_documentSerialNumber))
                return;

            if (!OutlinerDiagnosticMode.IsEnabled)
                _diagnosticStructureQueuedAt = 0;
            else if (_diagnosticStructureQueuedAt == 0)
                _diagnosticStructureQueuedAt = Stopwatch.GetTimestamp();
            _stateRefreshTimer.Stop();
            if (_activeStateChange != null)
                _pendingStateChange.Merge(_activeStateChange);
            _pendingStructureChange.Merge(change);
            _treeRefreshQueued = true;
            _treeRefreshRequestCount++;
            _treeRefreshTimer.Stop();
            _treeRefreshTimer.Interval =
                _pendingStructureChange.RequiresFullRefresh ? 0.2 : 0.075;
            _treeRefreshTimer.Start();
        }

        private void QueueStateRefresh(OutlinerStateChange change)
        {
            if (change == null ||
                change.IsEmpty ||
                _isClosing ||
                !_isPanelVisible ||
                !IsDocumentReady(_documentSerialNumber))
            {
                return;
            }

            if (_treeRefreshQueued)
            {
                _pendingStateChange.Merge(change);
                _stateRefreshRequestCount++;
                return;
            }

            _pendingStateChange.Merge(change);
            _stateRefreshRequestCount++;
            _stateRefreshTimer.Stop();
            _stateRefreshTimer.Start();
        }

        private void OnStateRefreshTimerElapsed(object sender, EventArgs e)
        {
            _stateRefreshTimer.Stop();
            if (_treeRefreshQueued ||
                _pendingStateChange.IsEmpty ||
                _isClosing ||
                !_isPanelVisible ||
                !IsDocumentReady(_documentSerialNumber))
            {
                return;
            }

            var change = new OutlinerStateChange();
            change.Merge(_pendingStateChange);
            var requestCount = Math.Max(1, _stateRefreshRequestCount);
            ClearPendingStateRefresh();

            var doc = RhinoDoc.FromRuntimeSerialNumber(_documentSerialNumber);
            if (doc == null || _documentModel == null || _snapshot == null)
            {
                QueueTreeRefresh();
                return;
            }

            var metrics = new OutlinerRefreshMetrics("state", requestCount, 50)
            {
                ObjectCount = _largeDocumentObjectCount,
                Deferred = _largeDocumentDeferred
            };
            PopulateRefreshMetricCounts(metrics, _documentModel);
            metrics.ProjectedRowCount = _snapshot.Items.Count;
            try
            {
                var refreshedItems = new HashSet<TreeGridItem>();
                var synchronized = metrics.Measure(
                    OutlinerRefreshStage.Maintenance,
                    () => OutlinerStateSynchronizer.Refresh(
                        doc,
                        _documentModel,
                        _snapshot,
                        _icons,
                        _isolateState,
                        change,
                        _largeDocumentDeferred,
                        refreshedItems));
                if (!synchronized)
                {
                    QueueTreeRefresh();
                    return;
                }

                if (RequiresFilterProjection(change) ||
                    (change.LayerNamesChanged &&
                     _documentModel.BlockDefinitionsById.Count > 0) ||
                    (_viewState.CompactHistory &&
                     change.FavoritesChanged &&
                     change.ObjectIds.Count > 0))
                {
                    RefreshProjection(metrics);
                    return;
                }

                metrics.Measure(
                    OutlinerRefreshStage.DataStore,
                    () => ReloadStateItems(refreshedItems));
                var selectionMayHaveChanged =
                    change.LayerStateChanged ||
                    change.IsolateChanged ||
                    change.ObjectIds.Count > 0;
                if (selectionMayHaveChanged)
                    SynchronizeSelectionFromDocument(metrics);
                UpdateToolbarButtonState();
            }
            finally
            {
                metrics.ReportIfSlow();
            }
        }

        private void ReloadStateItems(ICollection<TreeGridItem> items)
        {
            if (items == null || items.Count == 0)
                return;

            EnsureVisibleSelectionRows();
            var visibleItems = items
                .Where(_visibleTreeItems.Contains)
                .ToList();
            if (visibleItems.Count == 0)
                return;

            RunProgrammaticTreeMutation(() =>
            {
                if (visibleItems.Count > 128)
                {
                    _tree.ReloadData();
                    return;
                }

                foreach (var item in visibleItems)
                    _tree.ReloadItem(item, false);
            }, false);
        }

        private void ClearPendingStateRefresh()
        {
            _stateRefreshTimer?.Stop();
            _pendingStateChange?.Clear();
            _stateRefreshRequestCount = 0;
        }

        private bool TryApplyIncrementalStructureRefresh(
            OutlinerStructureChange change,
            int requestCount,
            out string fallbackReason)
        {
            fallbackReason = string.Empty;
            if (change == null || change.IsEmpty)
            {
                fallbackReason = "empty-change";
                return false;
            }
            if (TryApplyIncrementalLayerAdditions(change, requestCount))
                return true;
            if (change.RequiresFullRefresh)
            {
                fallbackReason = "full-required";
                return false;
            }
            if (_documentModel == null || _snapshot == null)
            {
                fallbackReason = "missing-snapshot";
                return false;
            }
            if (_viewState.HasDiscoveryState || _viewState.HasReveal)
            {
                fallbackReason = "filtered-view";
                return false;
            }
            if (_isolateState.IsActive || _isolateState.IsCapturing)
            {
                fallbackReason = "isolate-active";
                return false;
            }
            var allowedKinds =
                OutlinerStructureChangeKind.ObjectAdded |
                OutlinerStructureChangeKind.ObjectDeleted |
                OutlinerStructureChangeKind.ObjectUndeleted |
                OutlinerStructureChangeKind.ObjectPurged |
                OutlinerStructureChangeKind.ObjectReplaced |
                OutlinerStructureChangeKind.ObjectAttributes |
                OutlinerStructureChangeKind.GroupTable;
            if ((change.Kinds & ~allowedKinds) != 0)
            {
                fallbackReason = "unsupported-change-kind";
                return false;
            }

            var doc = RhinoDoc.FromRuntimeSerialNumber(_documentSerialNumber);
            if (doc == null)
            {
                fallbackReason = "missing-document";
                return false;
            }

            var metrics = new OutlinerRefreshMetrics(
                "structure",
                Math.Max(1, requestCount),
                50)
            {
                ObjectCount = _largeDocumentObjectCount,
                Deferred = _largeDocumentDeferred,
                Cause = change.Cause,
                DirtyLayerCount = change.LayerIndices.Count,
                ChangedObjectCount = change.ObjectIds.Count
            };
            PopulateRefreshMetricCounts(metrics, _documentModel);
            try
            {
                var dirtyLayerIndices = new HashSet<int>(change.LayerIndices);
                IDictionary<int, OutlinerGroupModel> groupReplacements = null;
                IDictionary<int, OutlinerGroupModel> proposedGroupsByIndex = null;
                if (change.HasGroupImpact)
                {
                    if (_largeDocumentDeferred)
                    {
                        fallbackReason = "group-impact-deferred";
                        metrics.FallbackReason = fallbackReason;
                        return false;
                    }
                    if (change.GroupIndices.Count == 0 ||
                        change.GroupIndices.Count > 512)
                    {
                        fallbackReason = change.GroupIndices.Count == 0
                            ? "missing-dirty-groups"
                            : "too-many-dirty-groups";
                        metrics.FallbackReason = fallbackReason;
                        return false;
                    }

                    string groupFailure = null;
                    var groupsCaptured = metrics.Measure(
                        OutlinerRefreshStage.Groups,
                        () => OutlinerDocumentReader.TryReadGroups(
                            doc,
                            _documentModel,
                            change.GroupIndices,
                            out groupReplacements,
                            out groupFailure));
                    if (!groupsCaptured)
                    {
                        fallbackReason = groupFailure ??
                            "group-capture-failed";
                        metrics.FallbackReason = fallbackReason;
                        return false;
                    }

                    proposedGroupsByIndex = _documentModel.GroupsByIndex
                        .ToDictionary(entry => entry.Key, entry => entry.Value);
                    foreach (var entry in groupReplacements)
                    {
                        if (_documentModel.GroupsByIndex.TryGetValue(
                            entry.Key,
                            out var oldGroup))
                        {
                            AddMemberLayerIndices(
                                doc,
                                oldGroup.MemberIds,
                                dirtyLayerIndices);
                        }
                        if (entry.Value == null)
                        {
                            proposedGroupsByIndex.Remove(entry.Key);
                            continue;
                        }

                        proposedGroupsByIndex[entry.Key] = entry.Value;
                        AddMemberLayerIndices(
                            doc,
                            entry.Value.MemberIds,
                            dirtyLayerIndices);
                    }
                }

                var changedIds = new HashSet<Guid>(change.ObjectIds);
                var historyImpact = metrics.Measure(
                    OutlinerRefreshStage.History,
                    () => OutlinerHistoryImpact.Resolve(
                        changedIds,
                        _documentModel.HistoryByObjectId.Values.Concat(
                            _historyCache.Values)));
                AddMemberLayerIndices(doc, historyImpact, dirtyLayerIndices);
                metrics.Detail("historyImpactObjects", historyImpact.Count);

                if (dirtyLayerIndices.Count == 0)
                {
                    if (change.HasGroupImpact)
                        return true;
                    fallbackReason = "no-dirty-layers";
                    metrics.FallbackReason = fallbackReason;
                    return false;
                }
                var affectedObjectCount = dirtyLayerIndices.Sum(index =>
                    _documentModel.LayersByIndex.TryGetValue(index, out var candidateLayer) &&
                    _documentModel.ObjectsByLayerId.TryGetValue(candidateLayer.Id, out var candidateObjects)
                        ? candidateObjects.Count : 0);
                metrics.Detail("affectedLayerObjects", affectedObjectCount);
                if (!OutlinerRefreshPolicy.UseIncrementalLayers(
                    dirtyLayerIndices.Count, affectedObjectCount, _documentModel.Objects.Count))
                {
                    fallbackReason = "dirty-layer-work-budget";
                    metrics.FallbackReason = fallbackReason;
                    return false;
                }

                var dirtyLayers = new List<OutlinerLayerModel>();
                foreach (var layerIndex in dirtyLayerIndices)
                {
                    if (!_documentModel.LayersByIndex.TryGetValue(
                        layerIndex,
                        out var layer))
                    {
                        fallbackReason = "missing-layer-model";
                        metrics.FallbackReason = fallbackReason;
                        return false;
                    }
                    if (!layer.ObjectsLoaded)
                    {
                        fallbackReason = "unloaded-layer";
                        metrics.FallbackReason = fallbackReason;
                        return false;
                    }
                    if (!_snapshot.LayerItems.ContainsKey(layer.Id))
                    {
                        fallbackReason = "missing-layer-row";
                        metrics.FallbackReason = fallbackReason;
                        return false;
                    }
                    dirtyLayers.Add(layer);
                }
                metrics.DirtyLayerCount = dirtyLayers.Count;

                IDictionary<Guid, IList<OutlinerObjectModel>> replacements = null;
                string captureFailure = null;
                var captured = metrics.Measure(
                    OutlinerRefreshStage.ObjectEnumeration,
                    () => OutlinerDocumentReader.TryReadLayerObjects(
                        doc,
                        _documentModel,
                        dirtyLayerIndices,
                        _provisionalNames,
                        out replacements,
                        out captureFailure,
                        proposedGroupsByIndex));
                if (!captured)
                {
                    fallbackReason = captureFailure ?? "layer-capture-failed";
                    metrics.FallbackReason = fallbackReason;
                    return false;
                }

                var oldDirtyObjectCount = dirtyLayers.Sum(layer =>
                    _documentModel.ObjectsByLayerId.TryGetValue(
                        layer.Id,
                        out var objects)
                            ? objects.Count
                            : 0);
                var newDirtyObjectCount = replacements.Values.Sum(
                    objects => objects?.Count ?? 0);
                var proposedLoadedCount = _documentModel.Objects.Count -
                    oldDirtyObjectCount + newDirtyObjectCount;
                if (!_largeDocumentDeferred &&
                    !_forceFullObjectLoad &&
                    proposedLoadedCount > AutomaticObjectLoadLimit)
                {
                    fallbackReason = "deferred-threshold-crossed";
                    metrics.FallbackReason = fallbackReason;
                    return false;
                }

                ISet<Guid> removedObjectIds = null;
                string modelFailure = null;
                var modelUpdated = metrics.Measure(
                    OutlinerRefreshStage.Maintenance,
                    () => _documentModel.TryReplaceLayerObjects(
                        replacements,
                        out removedObjectIds,
                        out modelFailure,
                        groupReplacements));
                if (!modelUpdated)
                {
                    fallbackReason = modelFailure ?? "model-update-failed";
                    metrics.FallbackReason = fallbackReason;
                    return false;
                }

                _documentModel.MergeBlockDefinitions(
                    OutlinerDocumentReader.ReadBlockDefinitions(
                        doc,
                        replacements.Values.SelectMany(items =>
                            items ?? Array.Empty<OutlinerObjectModel>())));

                foreach (var objectId in removedObjectIds)
                    _provisionalNames.Remove(objectId);
                foreach (var objectId in changedIds)
                {
                    if (_protectedHistoryObjectIds.Remove(objectId))
                    {
                        if (_historyCache.TryGetValue(
                                objectId,
                                out var observedHistory))
                        {
                            _documentModel.HistoryByObjectId[objectId] =
                                observedHistory;
                        }
                        continue;
                    }
                    _historyCache.Remove(objectId);
                    _resolvedHistoryObjectIds.Remove(objectId);
                    _documentModel.HistoryByObjectId.Remove(objectId);
                }

                var historyStackPlan = _viewState.CompactHistory
                    ? OutlinerHistoryStackPlanner.Create(_documentModel)
                    : null;
                var groupOrders = _groupOrderStore?.GetOrders(
                    _documentSerialNumber);
                var layerProjections = metrics.Measure(
                    OutlinerRefreshStage.Projection,
                    () => dirtyLayers.ToDictionary(
                        layer => layer.Id,
                        layer => _viewState.DisplayMode ==
                                OutlinerDisplayMode.LayersOnly
                            ? new OutlinerLayerProjection()
                            : OutlinerTreeProjector.ProjectLayerContent(
                                _documentModel,
                                layer,
                                _icons,
                                _groupExpansionState,
                                _historyExpansionState,
                                _isolateState,
                                null,
                                null,
                                null,
                                historyStackPlan,
                                _viewState.DisplayMode ==
                                    OutlinerDisplayMode.All,
                                groupOrders)));
                _snapshot.SetGroupHierarchy(_documentModel.GroupHierarchy);

                var replacedAllLayers = true;
                metrics.Measure(OutlinerRefreshStage.DataStore, () =>
                {
                    _projectingTree = true;
                    try
                    {
                        RunProgrammaticTreeMutation(() =>
                        {
                            foreach (var layer in dirtyLayers)
                            {
                                if (!_snapshot.ReplaceLayerContent(
                                    layer.Id,
                                    layerProjections[layer.Id]))
                                {
                                    replacedAllLayers = false;
                                    break;
                                }
                                _tree.ReloadItem(
                                    _snapshot.LayerItems[layer.Id],
                                    true);
                            }
                        }, false);
                    }
                    finally
                    {
                        _projectingTree = false;
                    }
                });
                if (!replacedAllLayers)
                {
                    fallbackReason = "snapshot-update-failed";
                    metrics.FallbackReason = fallbackReason;
                    return false;
                }

                if (_largeDocumentDeferred)
                {
                    _largeDocumentObjectCount = Math.Max(
                        0,
                        _largeDocumentObjectCount - oldDirtyObjectCount +
                            newDirtyObjectCount);
                }
                else
                {
                    _largeDocumentObjectCount = _documentModel.Objects.Count;
                }
                metrics.ObjectCount = _largeDocumentObjectCount;
                PopulateRefreshMetricCounts(metrics, _documentModel);
                metrics.ProjectedRowCount = _snapshot.Items.Count;
                InvalidateVisibleSelectionRows();
                UpdateStatusText();
                SynchronizeSelectionFromDocument(metrics);
                BeginPendingLayerEdit();
                return true;
            }
            finally
            {
                metrics.ReportIfSlow();
            }
        }

        private bool TryApplyIncrementalLayerAdditions(
            OutlinerStructureChange change,
            int requestCount)
        {
            if (change == null ||
                change.RequiresFullRefresh ||
                change.Kinds != OutlinerStructureChangeKind.LayerTable ||
                change.AddedLayerIndices.Count == 0 ||
                change.AddedLayerIndices.Count != change.LayerIndices.Count ||
                change.ObjectIds.Count > 0 ||
                change.GroupIndices.Count > 0 ||
                _documentModel == null ||
                _snapshot == null ||
                _viewState.HasDiscoveryState ||
                _viewState.HasReveal ||
                _isolateState.IsActive ||
                _isolateState.IsCapturing)
            {
                return false;
            }

            var doc = RhinoDoc.FromRuntimeSerialNumber(_documentSerialNumber);
            if (doc == null)
                return false;

            var metrics = new OutlinerRefreshMetrics(
                "structure",
                Math.Max(1, requestCount),
                50)
            {
                ObjectCount = _largeDocumentObjectCount,
                Deferred = _largeDocumentDeferred,
                Cause = change.Cause,
                DirtyLayerCount = change.AddedLayerIndices.Count
            };
            try
            {
                var additions = metrics.Measure(
                    OutlinerRefreshStage.Layers,
                    () => change.AddedLayerIndices
                        .Select(index => OutlinerDocumentReader.ReadLayer(doc, index))
                        .Where(layer => layer != null)
                        .ToList());
                if (additions.Count != change.AddedLayerIndices.Count)
                    return false;

                var orderedAdditions = new List<OutlinerLayerModel>();
                var remainingAdditions = additions.ToList();
                while (remainingAdditions.Count > 0)
                {
                    var ready = remainingAdditions
                        .Where(layer =>
                            layer.ParentId == Guid.Empty ||
                            _documentModel.LayersById.ContainsKey(
                                layer.ParentId) ||
                            orderedAdditions.Any(parent =>
                                parent.Id == layer.ParentId))
                        .ToList();
                    if (ready.Count == 0)
                    {
                        metrics.FallbackReason = "missing-parent-layer";
                        return false;
                    }

                    orderedAdditions.AddRange(ready);
                    foreach (var layer in ready)
                        remainingAdditions.Remove(layer);
                }

                var addedRows = new List<Tuple<OutlinerLayerModel, TreeGridItem>>();
                foreach (var layer in orderedAdditions)
                {
                    if (!_documentModel.TryAddLayer(
                        layer,
                        out var modelFailure))
                    {
                        metrics.FallbackReason = modelFailure;
                        return false;
                    }

                    var item = OutlinerTreeProjector.CreateLayerItem(
                        layer,
                        _icons,
                        _layerExpansionState,
                        _isolateState);
                    if (!_snapshot.TryAddLayer(layer, item))
                    {
                        metrics.FallbackReason = "snapshot-layer-add-failed";
                        return false;
                    }
                    addedRows.Add(Tuple.Create(layer, item));
                }

                metrics.Measure(OutlinerRefreshStage.DataStore, () =>
                {
                    RunProgrammaticTreeMutation(() =>
                    {
                        var rootChanged = addedRows.Any(pair =>
                            pair.Item1.ParentId == Guid.Empty);
                        if (rootChanged)
                            _tree.ReloadData();
                        foreach (var parentId in addedRows
                            .Select(pair => pair.Item1.ParentId)
                            .Where(id => id != Guid.Empty)
                            .Distinct())
                        {
                            if (_snapshot.LayerItems.TryGetValue(
                                parentId,
                                out var parentItem))
                            {
                                _tree.ReloadItem(parentItem, true);
                            }
                        }
                    }, false);
                });

                PopulateRefreshMetricCounts(metrics, _documentModel);
                metrics.ProjectedRowCount = _snapshot.Items.Count;
                InvalidateVisibleSelectionRows();
                UpdateStatusText();
                BeginPendingLayerEdit();
                UpdateToolbarButtonState();
                return true;
            }
            finally
            {
                metrics.ReportIfSlow();
            }
        }

        private void AddMemberLayerIndices(
            RhinoDoc doc,
            IEnumerable<Guid> memberIds,
            ISet<int> layerIndices)
        {
            if (layerIndices == null)
                return;

            foreach (var memberId in memberIds ?? Enumerable.Empty<Guid>())
            {
                if (_documentModel != null &&
                    _documentModel.ObjectsById.TryGetValue(
                        memberId,
                        out var oldMember))
                {
                    layerIndices.Add(oldMember.LayerIndex);
                }

                var currentMember = doc?.Objects.FindId(memberId);
                if (currentMember != null && !currentMember.IsDeleted)
                    layerIndices.Add(currentMember.Attributes.LayerIndex);
            }
        }

        private void OnTreeRefreshTimerElapsed(object sender, EventArgs e)
        {
            _treeRefreshTimer.Stop();
            if (!_treeRefreshQueued ||
                _isClosing ||
                !_isPanelVisible ||
                !IsDocumentReady(_documentSerialNumber))
            {
                return;
            }

            using (var diagnostic = OutlinerDiagnosticScope.Start(
                "dispatch-structure", _documentSerialNumber,
                _diagnosticStructureQueuedAt, _treeRefreshRequestCount))
            {
                _diagnosticStructureQueuedAt = 0;
                var change = _pendingStructureChange.Clone();
                var requestCount = Math.Max(1, _treeRefreshRequestCount);
                _pendingStructureChange.Clear();
                _treeRefreshRequestCount = 0;
                _treeRefreshQueued = false;
                // UI timers run after this synchronous refresh returns, including fallback paths.
                if (!_pendingStateChange.IsEmpty)
                    _stateRefreshTimer.Start();
                string fallbackReason;
                try
                {
                    if (TryApplyIncrementalStructureRefresh(
                        change,
                        requestCount,
                        out fallbackReason))
                    {
                        return;
                    }
                }
                catch (Exception exception)
                {
                    fallbackReason = "incremental-exception";
                    RhinoApp.WriteLine(
                        "Object Outliner incremental refresh fell back to full refresh:");
                    RhinoApp.WriteLine(exception.ToString());
                }

                RefreshTreeSafely(change, requestCount, fallbackReason);
            }
        }

        private long _diagnosticSelectionQueuedAt;
        private long _diagnosticStructureQueuedAt;
        private int _diagnosticSelectionRequests;
        private int _diagnosticSuppressedDocumentEvents;
    }
}
