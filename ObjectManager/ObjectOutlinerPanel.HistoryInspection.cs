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
        private void QueueHistoryInspection(RhinoDoc doc)
        {
            _pendingHistoryObjectId = _selectedObjectIds.Count == 1
                ? _selectedObjectIds.First()
                : Guid.Empty;
            if (_pendingHistoryObjectId == Guid.Empty ||
                _resolvedHistoryObjectIds.Contains(_pendingHistoryObjectId) ||
                doc == null ||
                doc.Objects.HistoryRecordCount <= 0 ||
                _historyInspectionQueued ||
                _isClosing ||
                !_isPanelVisible)
            {
                return;
            }

            _historyInspectionQueued = true;
            var generation = _historyInspectionGeneration;
            Application.Instance.AsyncInvoke(() =>
                InspectPendingHistory(generation));
        }

        private void InspectPendingHistory(int generation)
        {
            if (generation != _historyInspectionGeneration)
                return;

            _historyInspectionQueued = false;
            var objectId = _pendingHistoryObjectId;
            _pendingHistoryObjectId = Guid.Empty;
            if (objectId == Guid.Empty ||
                _isClosing ||
                !_isPanelVisible ||
                _documentModel == null ||
                _snapshot == null ||
                _selectedObjectIds.Count != 1 ||
                !_selectedObjectIds.Contains(objectId) ||
                _resolvedHistoryObjectIds.Contains(objectId) ||
                !_documentModel.ObjectsById.TryGetValue(
                    objectId,
                    out var objectModel))
            {
                return;
            }

            var doc = RhinoDoc.FromRuntimeSerialNumber(_documentSerialNumber);
            if (doc == null)
                return;

            var metrics = new OutlinerRefreshMetrics("history", 1, 50)
            {
                ObjectCount = _largeDocumentObjectCount > 0
                    ? _largeDocumentObjectCount
                    : _documentModel.Objects.Count,
                Deferred = _largeDocumentDeferred,
                ProjectedRowCount = _snapshot.Items.Count
            };
            PopulateRefreshMetricCounts(metrics, _documentModel);
            try
            {
                OutlinerHistoryModel history = null;
                var queryCount = 0;
                var resolved = metrics.Measure(
                    OutlinerRefreshStage.History,
                    () => OutlinerHistoryReader.TryRead(
                        doc,
                        objectId,
                        out history,
                        out queryCount));
                metrics.HistoryQueryCount = queryCount;
                if (!resolved)
                    return;

                _resolvedHistoryObjectIds.Add(objectId);
                if (history == null)
                {
                    _historyCache.Remove(objectId);
                    _documentModel.HistoryByObjectId.Remove(objectId);
                    if (_viewState.CompactHistory)
                        RefreshProjection(metrics);
                    return;
                }

                _historyCache[objectId] = history;
                _documentModel.HistoryByObjectId[objectId] = history;
                if (LoadDeferredHistoryTargets(doc, history))
                {
                    _status.Text = OutlinerStrings.LoadingHistoryObjects;
                    QueueTreeRefresh();
                    return;
                }

                if (_viewState.CompactHistory)
                {
                    RefreshProjection(metrics);
                    return;
                }

                if (!_snapshot.ObjectItems.TryGetValue(
                    objectId,
                    out var objectItem))
                {
                    return;
                }

                _snapshot.UnregisterDescendants(objectItem);
                if (!OutlinerTreeProjector.ApplyObjectRelationships(
                    objectItem,
                    objectModel,
                    _documentModel,
                    _icons,
                    _historyExpansionState))
                {
                    return;
                }

                _snapshot.RegisterDescendants(objectItem);
                InvalidateVisibleSelectionRows();
                metrics.ProjectedRowCount = _snapshot.Items.Count;
                RunProgrammaticTreeMutation(() =>
                    _tree.ReloadItem(objectItem, true));
            }
            finally
            {
                metrics.ReportIfSlow();
            }
        }

        private bool LoadDeferredHistoryTargets(
            RhinoDoc doc,
            OutlinerHistoryModel history)
        {
            if (!_largeDocumentDeferred || doc == null || history == null)
                return false;

            var loadedAnyLayer = false;
            foreach (var targetId in history.ParentIds
                .Concat(history.ChildIds)
                .Distinct())
            {
                if (_documentModel.ObjectsById.ContainsKey(targetId))
                    continue;

                var target = doc.Objects.FindId(targetId);
                if (target == null || target.IsDeleted)
                    continue;

                loadedAnyLayer |= _loadedLargeLayerIndices.Add(
                    target.Attributes.LayerIndex);
            }
            return loadedAnyLayer;
        }

        private void ApplyHistoryCacheToModel()
        {
            if (_documentModel == null)
                return;

            foreach (var entry in _historyCache)
            {
                if (_documentModel.ObjectsById.ContainsKey(entry.Key))
                    _documentModel.HistoryByObjectId[entry.Key] = entry.Value;
            }
        }

        private void CancelHistoryInspection(bool clearCache)
        {
            _historyInspectionGeneration++;
            _historyInspectionQueued = false;
            _pendingHistoryObjectId = Guid.Empty;
            if (!clearCache)
                return;

            _historyCache.Clear();
            _resolvedHistoryObjectIds.Clear();
            _protectedHistoryObjectIds.Clear();
        }
    }
}
