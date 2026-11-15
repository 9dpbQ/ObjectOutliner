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
        private void OnTreeExpansionStarting(
            object sender,
            TreeGridViewItemCancelEventArgs e)
        {
            if (_projectingTree || _revealInProgress || _isClosing)
                return;

            BeginExpansionSelectionSuppression();
        }

        private void BeginKeyboardExpansionSuppression(Keys key)
        {
            if (key != Keys.Left && key != Keys.Right)
                return;

            var item = _tree.SelectedItem as TreeGridItem;
            if (item == null || item.Children.Count == 0)
                return;

            var willChangeExpansion =
                (key == Keys.Left && item.Expanded) ||
                (key == Keys.Right && !item.Expanded);
            if (willChangeExpansion)
                BeginExpansionSelectionSuppression();
        }

        private void BeginExpansionSelectionSuppression()
        {
            var generation = _selectionEventGate.BeginExpansion();
            Application.Instance.AsyncInvoke(() =>
                Application.Instance.AsyncInvoke(() =>
                    ReleaseExpansionSelectionSuppression(generation)));
        }

        private void OnTreeExpansionChanged(object sender, TreeGridViewItemEventArgs e)
        {
            var item = e.Item as TreeGridItem;
            var node = item?.Tag as OutlinerNodeReference;
            if (IsSameTreeNode(_potentialExpansionItem, item))
            {
                _potentialExpansionOccurred = true;
                _deferredPointerSelectionChange = false;
            }
            InvalidateVisibleSelectionRows();
            if (!_projectingTree && node != null)
            {
                if (!_viewState.HasSearch &&
                    !_viewState.HasReveal &&
                    node.Kind == OutlinerNodeKind.Layer)
                {
                    _layerExpansionState[node.Id] = item.Expanded;
                    var layerModel = _documentModel?.Layers.FirstOrDefault(
                        layer => layer.Id == node.Id);
                    if (_largeDocumentDeferred &&
                        item.Expanded &&
                        layerModel != null &&
                        layerModel.HasDeferredObjects &&
                        _loadedLargeLayerIndices.Add(node.LayerIndex))
                    {
                        _status.Text = OutlinerStrings.LoadingLayerObjects;
                        QueueTreeRefresh();
                    }
                }
                else if (!_viewState.HasSearch &&
                         !_viewState.HasReveal &&
                         node.Kind == OutlinerNodeKind.GroupSlice)
                {
                    SetGroupExpansionState(
                        new OutlinerGroupSliceKey(node.Id, node.OwnerId),
                        item.Expanded);
                    CaptureWorkspaceState();
                }
                else if (node.Kind == OutlinerNodeKind.Object)
                {
                    _historyExpansionState[node.Id] = item.Expanded;
                    CaptureWorkspaceState();
                    if (_documentModel?.ObjectsById.TryGetValue(
                            node.Id,
                            out var objectModel) == true &&
                        objectModel.BlockDefinitionId != Guid.Empty)
                    {
                        QueueBlockExpansionRefresh(node.Id, item);
                    }
                }
            }
            if (!_projectingTree && !_revealInProgress)
                QueueSelectionRefresh();
        }

        private void ReleaseExpansionSelectionSuppression(int generation)
        {
            if (_isClosing ||
                !_selectionEventGate.CompleteExpansion(generation))
                return;

            SynchronizeSelectionFromDocument();
        }

        private void QueueBlockExpansionRefresh(Guid objectId, TreeGridItem item)
        {
            if (!item.Expanded)
            {
                _pendingBlockExpansionItems.Remove(objectId);
                return;
            }

            _pendingBlockExpansionItems[objectId] = item;
            if (_blockExpansionRefreshQueued)
                return;

            _blockExpansionRefreshQueued = true;
            // AppKit must finish traversing the old rows before we replace them.
            Application.Instance.AsyncInvoke(() =>
                Application.Instance.AsyncInvoke(RefreshPendingBlockExpansions));
        }

        private void RefreshPendingBlockExpansions()
        {
            _blockExpansionRefreshQueued = false;
            var pending = _pendingBlockExpansionItems.ToArray();
            _pendingBlockExpansionItems.Clear();
            if (_isClosing || !_isPanelVisible || _snapshot == null ||
                _documentModel == null || _treeRefreshQueued)
                return;

            var objectIds = pending.Where(entry =>
                entry.Value.Expanded &&
                _snapshot.ObjectItems.TryGetValue(entry.Key, out var current) &&
                ReferenceEquals(current, entry.Value) &&
                _documentModel.ObjectsById.ContainsKey(entry.Key))
                .Select(entry => entry.Key).ToList();
            if (objectIds.Count == 0)
                return;

            if (_viewState.CompactHistory)
                RefreshProjection();
            else
                foreach (var objectId in objectIds)
                    RefreshObjectRelationships(objectId);
        }

        private void CancelExpansionSelectionProtection()
        {
            _selectionEventGate.CancelExpansion();
        }

        private void RefreshObjectRelationships(Guid objectId)
        {
            if (_viewState.CompactHistory)
            {
                RefreshProjection();
                return;
            }

            if (_documentModel == null ||
                _snapshot == null ||
                !_documentModel.ObjectsById.TryGetValue(
                    objectId,
                    out var objectModel) ||
                !_snapshot.ObjectItems.TryGetValue(
                    objectId,
                    out var objectItem))
            {
                return;
            }

            _projectingTree = true;
            try
            {
                RunProgrammaticTreeMutation(() =>
                {
                    _snapshot.UnregisterDescendants(objectItem);
                    OutlinerTreeProjector.ApplyObjectRelationships(
                        objectItem,
                        objectModel,
                        _documentModel,
                        _icons,
                        _historyExpansionState);
                    _snapshot.RegisterDescendants(objectItem);
                    _tree.ReloadItem(objectItem, true);
                }, false);
            }
            finally
            {
                _projectingTree = false;
            }
        }
    }
}
