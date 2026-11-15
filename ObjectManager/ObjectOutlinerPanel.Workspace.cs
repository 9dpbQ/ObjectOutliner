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
        private void ApplyWorkspaceState()
        {
            if (_workspaceStateApplied)
                return;

            _workspaceStateApplied = true;
            var state = _workspaceStore?.Get(_documentSerialNumber);
            if (state == null || !state.Initialized)
            {
                PublishGroupExpansionState();
                ApplyPendingCopiedGroupExpansions();
                ApplyPendingObservedHistory();
                return;
            }

            _layerExpansionState.Clear();
            foreach (var entry in state.LayerExpansion)
                _layerExpansionState[entry.Key] = entry.Value;
            _groupExpansionState.Clear();
            foreach (var entry in state.GroupExpansion)
                _groupExpansionState[entry.Key] = entry.Value;
            _historyExpansionState.Clear();
            foreach (var entry in state.HistoryExpansion)
                _historyExpansionState[entry.Key] = entry.Value;

            _forceFullObjectLoad = state.WasFullyLoaded;
            _viewState.SearchText = state.SearchText;
            _viewState.LayerFilter.Scope = state.LayerScope;
            _viewState.LayerFilter.Visibility = state.LayerVisibility;
            _viewState.LayerFilter.Lock = state.LayerLock;
            _viewState.LayerFilter.Occupancy = state.LayerOccupancy;
            _viewState.LayerFilter.SelectedLayerIds.Clear();
            _viewState.LayerFilter.SelectedLayerIds.UnionWith(
                state.FilterLayerIds);
            _viewState.ContentFilter.ObjectTypes.Clear();
            _viewState.ContentFilter.ObjectTypes.UnionWith(state.ObjectTypes);
            _viewState.ContentFilter.FavoritesOnly = state.FavoritesOnly;
            _viewState.ContentFilter.SelectedObjectsOnly =
                state.SelectedObjectsOnly;
            _viewState.ContentFilter.UserNamedOnly = state.UserNamedOnly;
            _viewState.ContentFilter.NotesOnly = state.NotesOnly;
            _viewState.DisplayMode = state.DisplayMode;
            _viewState.CompactHistory = state.CompactHistory;
            _selectedObjectIds.Clear();
            _selectedObjectIds.UnionWith(state.SelectedObjectIds);
            _selectedLayerIds.Clear();
            _selectedLayerIds.UnionWith(state.SelectedLayerIds);
            _isolateState.Restore(state.Isolation);

            _applyingWorkspaceState = true;
            try
            {
                _searchBox.Text = state.SearchText ?? string.Empty;
                if (_historyStackButton != null)
                    _historyStackButton.Checked = state.CompactHistory;
                UpdateFilterControlValues();
                UpdateDisplayModePresentation();
            }
            finally
            {
                _applyingWorkspaceState = false;
            }
            PublishGroupExpansionState();
            ApplyPendingCopiedGroupExpansions();
            ApplyPendingObservedHistory();
        }

        private void SetGroupExpansionState(
            OutlinerGroupSliceKey key,
            bool expanded)
        {
            _groupExpansionState[key] = expanded;
            _relationshipHub?.SetGroupExpansionState(
                _documentSerialNumber,
                key,
                expanded);
        }

        private void PublishGroupExpansionState()
        {
            _relationshipHub?.ReplaceGroupExpansionState(
                _documentSerialNumber,
                _groupExpansionState);
        }

        private void OnGroupsCopied(
            object sender,
            OutlinerGroupsCopiedEventArgs e)
        {
            if (e == null ||
                e.TargetDocumentSerialNumber != _documentSerialNumber ||
                _isClosing)
            {
                return;
            }

            ApplyCopiedGroupExpansions(e.Relations);
            _relationshipHub?.TakePendingCopiedGroups(_documentSerialNumber);
        }

        private void ApplyPendingCopiedGroupExpansions()
        {
            ApplyCopiedGroupExpansions(
                _relationshipHub?.TakePendingCopiedGroups(
                    _documentSerialNumber));
        }

        private void OnHistoryObserved(
            object sender,
            OutlinerHistoryObservedEventArgs e)
        {
            if (e == null ||
                e.DocumentSerialNumber != _documentSerialNumber ||
                _isClosing)
            {
                return;
            }

            MergeObservedHistory(e.Relations);
            _relationshipHub?.TakePendingHistory(_documentSerialNumber);
            var observedIds = e.Relations
                .Where(item => item != null)
                .Select(item => item.ObjectId)
                .Where(id => id != Guid.Empty)
                .Distinct()
                .ToList();
            Application.Instance.AsyncInvoke(() =>
                RefreshObservedHistoryRows(observedIds));
        }

        private void RefreshObservedHistoryRows(IList<Guid> objectIds)
        {
            if (_isClosing ||
                !_isPanelVisible ||
                _treeRefreshQueued ||
                _documentModel == null ||
                _snapshot == null)
            {
                return;
            }

            var loadedIds = (objectIds ?? Array.Empty<Guid>())
                .Where(_documentModel.ObjectsById.ContainsKey)
                .ToList();
            if (loadedIds.Count == 0)
                return;

            if (_viewState.CompactHistory)
            {
                RefreshProjection();
                return;
            }

            foreach (var objectId in loadedIds)
                RefreshObjectRelationships(objectId);
        }

        private void ApplyPendingObservedHistory()
        {
            MergeObservedHistory(
                _relationshipHub?.TakePendingHistory(_documentSerialNumber));
        }

        private void MergeObservedHistory(
            IEnumerable<OutlinerHistoryModel> relations)
        {
            foreach (var relation in relations ??
                Enumerable.Empty<OutlinerHistoryModel>())
            {
                if (relation == null ||
                    relation.ObjectId == Guid.Empty ||
                    !relation.HasRelations)
                {
                    continue;
                }

                _historyCache.TryGetValue(
                    relation.ObjectId,
                    out var previous);
                var merged = new OutlinerHistoryModel
                {
                    ObjectId = relation.ObjectId,
                    ParentIds = relation.ParentIds
                        .Concat(previous?.ParentIds ?? Array.Empty<Guid>())
                        .Where(id => id != Guid.Empty)
                        .Distinct()
                        .ToList(),
                    ChildIds = relation.ChildIds
                        .Concat(previous?.ChildIds ?? Array.Empty<Guid>())
                        .Where(id => id != Guid.Empty)
                        .Distinct()
                        .ToList()
                };
                _historyCache[merged.ObjectId] = merged;
                _protectedHistoryObjectIds.Add(merged.ObjectId);
                if (_documentModel != null &&
                    _documentModel.ObjectsById.ContainsKey(merged.ObjectId))
                {
                    _documentModel.HistoryByObjectId[merged.ObjectId] = merged;
                }
            }
        }

        private void ApplyCopiedGroupExpansions(
            IEnumerable<OutlinerCopiedGroupRelation> relations)
        {
            var changed = false;
            foreach (var relation in relations ??
                Enumerable.Empty<OutlinerCopiedGroupRelation>())
            {
                if (relation == null || relation.TargetGroupId == Guid.Empty)
                    continue;

                foreach (var entry in relation.ExpansionByTargetLayerId)
                {
                    var key = new OutlinerGroupSliceKey(
                        relation.TargetGroupId,
                        entry.Key);
                    SetGroupExpansionState(key, entry.Value);
                    if (_snapshot != null &&
                        _snapshot.GroupSliceItems.TryGetValue(
                            key,
                            out var item))
                    {
                        item.Expanded = entry.Value;
                    }
                    changed = true;
                }
            }

            if (changed)
                CaptureWorkspaceState();
        }

        private void CaptureWorkspaceState()
        {
            if (_workspaceStore == null || _documentSerialNumber == 0)
                return;

            var previous = _workspaceStore.Get(_documentSerialNumber);
            var modelFullyLoaded = _documentModel != null &&
                _fullObjectLoadCompleted;
            var state = new OutlinerWorkspaceState
            {
                Initialized = true,
                WasFullyLoaded = modelFullyLoaded ||
                    (_documentModel == null &&
                     previous != null &&
                     previous.WasFullyLoaded),
                LastObjectCount = Math.Max(
                    0,
                    _largeDocumentObjectCount > 0
                        ? _largeDocumentObjectCount
                        : _documentModel?.Objects.Count ??
                          previous?.LastObjectCount ?? 0),
                LastProjectedRowCount = Math.Max(
                    0,
                    _snapshot?.Items.Count ??
                    previous?.LastProjectedRowCount ?? 0),
                SearchText = _searchBox?.Text ?? _viewState.SearchText,
                LayerScope = _viewState.LayerFilter.Scope,
                LayerVisibility = _viewState.LayerFilter.Visibility,
                LayerLock = _viewState.LayerFilter.Lock,
                LayerOccupancy = _viewState.LayerFilter.Occupancy,
                FavoritesOnly = _viewState.ContentFilter.FavoritesOnly,
                SelectedObjectsOnly =
                    _viewState.ContentFilter.SelectedObjectsOnly,
                UserNamedOnly = _viewState.ContentFilter.UserNamedOnly,
                NotesOnly = _viewState.ContentFilter.NotesOnly,
                DisplayMode = _viewState.DisplayMode,
                CompactHistory = _viewState.CompactHistory,
                Isolation = _isolateState.CreateSnapshot() ??
                    previous?.Isolation
            };
            foreach (var entry in _layerExpansionState)
                state.LayerExpansion[entry.Key] = entry.Value;
            foreach (var entry in _groupExpansionState)
                state.GroupExpansion[entry.Key] = entry.Value;
            foreach (var entry in _historyExpansionState)
                state.HistoryExpansion[entry.Key] = entry.Value;
            state.FilterLayerIds.UnionWith(
                _viewState.LayerFilter.SelectedLayerIds);
            state.ObjectTypes.UnionWith(
                _viewState.ContentFilter.ObjectTypes);
            state.SelectedLayerIds.UnionWith(_selectedLayerIds);

            var doc = RhinoDoc.FromRuntimeSerialNumber(_documentSerialNumber);
            if (doc != null)
                state.SelectedObjectIds.UnionWith(_selectionController.Read(doc));
            else
                state.SelectedObjectIds.UnionWith(_selectedObjectIds);

            _workspaceStore.Set(_documentSerialNumber, state);
            PublishGroupExpansionState();
        }
    }
}
