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
        private void RequestRefreshForMissingSelection(RhinoDoc doc)
        {
            if (doc == null ||
                _documentModel == null ||
                _snapshot == null)
            {
                _missingSelectedObjectIds.Clear();
                return;
            }

            var missingIds = new HashSet<Guid>(_selectedObjectIds.Where(id =>
            {
                var rhinoObject = doc.Objects.FindId(id);
                if (rhinoObject == null || rhinoObject.IsDeleted)
                    return false;
                return !_documentModel.ObjectsById.ContainsKey(id);
            }));
            if (missingIds.Count == 0)
            {
                _missingSelectedObjectIds.Clear();
                return;
            }

            if (_missingSelectedObjectIds.SetEquals(missingIds))
                return;

            _missingSelectedObjectIds.Clear();
            _missingSelectedObjectIds.UnionWith(missingIds);

            if (_largeDocumentDeferred)
            {
                var loadedAnyLayer = false;
                var refreshLoadedLayer = false;
                foreach (var objectId in missingIds)
                {
                    var rhinoObject = doc.Objects.FindId(objectId);
                    if (rhinoObject == null || rhinoObject.IsDeleted)
                        continue;

                    if (!_documentModel.LayersByIndex.TryGetValue(
                        rhinoObject.Attributes.LayerIndex,
                        out var layer) ||
                        (!_viewState.LayerFilter.Matches(
                            layer,
                            _documentModel) &&
                         !_viewState.TemporarilyRevealedObjectIds.Contains(
                            objectId)))
                    {
                        continue;
                    }

                    if (_loadedLargeLayerIndices.Add(layer.Index))
                        loadedAnyLayer = true;
                    else
                        refreshLoadedLayer = true;
                }

                if (!loadedAnyLayer && !refreshLoadedLayer)
                    return;

                _status.Text = loadedAnyLayer
                    ? OutlinerStrings.LoadingSelectedObjects
                    : OutlinerStrings.RefreshingSelectedObjects;
                QueueTreeRefresh();
                return;
            }

            QueueTreeRefresh();
        }

        private void GetLogicalSelection(
            out HashSet<Guid> logicalGroupIds,
            out HashSet<Guid> logicalObjectIds)
        {
            var selectedGroupIds = new HashSet<Guid>();
            if (_documentModel != null)
            {
                var candidateGroupIds = new HashSet<Guid>();
                foreach (var objectId in _selectedObjectIds)
                {
                    if (_documentModel.ObjectsById.TryGetValue(
                        objectId,
                        out var objectModel))
                    {
                        candidateGroupIds.UnionWith(objectModel.GroupIds);
                    }
                }

                foreach (var groupId in candidateGroupIds)
                {
                    if (IsGroupSelected(groupId))
                        selectedGroupIds.Add(groupId);
                }
            }

            logicalGroupIds = new HashSet<Guid>(selectedGroupIds.Where(groupId =>
            {
                var parentId = _snapshot.GroupHierarchy.GetParentGroupId(groupId);
                while (parentId != Guid.Empty)
                {
                    if (selectedGroupIds.Contains(parentId))
                        return false;
                    parentId = _snapshot.GroupHierarchy.GetParentGroupId(parentId);
                }
                return true;
            }));

            var groupedSelectionIds = new HashSet<Guid>(logicalGroupIds
                .SelectMany(GetGroupMemberIds));
            logicalObjectIds = new HashSet<Guid>(_selectedObjectIds.Where(
                id => !groupedSelectionIds.Contains(id)));
        }

        private ISet<TreeGridItem> UpdateCollapsedSelectionIndicators(
            ISet<Guid> logicalGroupIds,
            ISet<Guid> logicalObjectIds)
        {
            var changedItems = new HashSet<TreeGridItem>();
            if (_snapshot == null)
                return changedItems;

            var nextItems = new HashSet<TreeGridItem>();

            foreach (var groupId in logicalGroupIds)
            {
                if (!_snapshot.GroupSliceItemsByGroupId.TryGetValue(
                    groupId,
                    out var groupItems))
                {
                    if (_viewState.DisplayMode != OutlinerDisplayMode.All)
                    {
                        foreach (var memberId in GetGroupMemberIds(groupId))
                        {
                            if (_selectedObjectIds.Contains(memberId))
                                AddDisplayModeSelectionBoundary(
                                    memberId,
                                    nextItems);
                        }
                    }
                    continue;
                }

                foreach (var groupItem in groupItems)
                    AddCollapsedSelectionBoundary(groupItem, nextItems);
            }

            foreach (var objectId in logicalObjectIds)
            {
                if (!_snapshot.ObjectItems.TryGetValue(objectId, out var objectItem))
                {
                    AddDisplayModeSelectionBoundary(objectId, nextItems);
                    continue;
                }

                AddCollapsedSelectionBoundary(objectItem, nextItems);
            }

            foreach (var item in _collapsedSelectionIndicatorItems)
            {
                if (nextItems.Contains(item))
                    continue;

                item.SetValue(
                    OutlinerValueIndex.ContainsSelectedDescendant,
                    false);
                changedItems.Add(item);
            }
            foreach (var item in nextItems)
            {
                if (_collapsedSelectionIndicatorItems.Contains(item))
                    continue;

                item.SetValue(
                    OutlinerValueIndex.ContainsSelectedDescendant,
                    true);
                changedItems.Add(item);
            }

            _collapsedSelectionIndicatorItems.Clear();
            _collapsedSelectionIndicatorItems.UnionWith(nextItems);
            return changedItems;
        }

        private void AddDisplayModeSelectionBoundary(
            Guid objectId,
            ISet<TreeGridItem> boundaryItems)
        {
            if (_snapshot == null ||
                _documentModel == null ||
                _viewState.DisplayMode == OutlinerDisplayMode.All ||
                !_documentModel.ObjectsById.TryGetValue(
                    objectId,
                    out var objectModel))
            {
                return;
            }

            if (_viewState.DisplayMode == OutlinerDisplayMode.Structure)
            {
                var path = _snapshot.GroupHierarchy.GetCanonicalGroupPath(
                    objectId);
                for (var index = path.Count - 1; index >= 0; index--)
                {
                    if (_snapshot.GroupSliceItems.TryGetValue(
                        new OutlinerGroupSliceKey(
                            path[index],
                            objectModel.LayerId),
                        out var groupItem))
                    {
                        boundaryItems?.Add(groupItem);
                        return;
                    }
                }
            }

            if (_snapshot.LayerItems.TryGetValue(
                objectModel.LayerId,
                out var layerItem))
            {
                boundaryItems?.Add(layerItem);
            }
        }

        private static void AddCollapsedSelectionBoundary(
            TreeGridItem selectedItem,
            ISet<TreeGridItem> boundaryItems)
        {
            var ancestors = new List<TreeGridItem>();
            var parent = selectedItem?.Parent as TreeGridItem;
            while (parent != null)
            {
                ancestors.Add(parent);
                parent = parent.Parent as TreeGridItem;
            }

            ancestors.Reverse();
            foreach (var ancestor in ancestors)
            {
                var node = ancestor.Tag as OutlinerNodeReference;
                if (ancestor.Expanded ||
                    node == null ||
                    (node.Kind != OutlinerNodeKind.Layer &&
                     node.Kind != OutlinerNodeKind.GroupSlice &&
                     node.Kind != OutlinerNodeKind.Object))
                {
                    continue;
                }

                boundaryItems?.Add(ancestor);
                return;
            }
        }

        private IEnumerable<Guid> GetGroupMemberIds(Guid groupId)
        {
            if (_documentModel == null)
                return Enumerable.Empty<Guid>();

            return _documentModel.GroupsById.TryGetValue(groupId, out var group)
                ? group.MemberIds
                : Enumerable.Empty<Guid>();
        }

        private bool IsGroupSelected(Guid groupId)
        {
            if (_documentModel == null ||
                !_documentModel.GroupsById.TryGetValue(groupId, out var group))
            {
                return false;
            }

            var hasSelectableMember = false;
            foreach (var memberId in group.MemberIds)
            {
                if (!_documentModel.ObjectsById.TryGetValue(
                    memberId,
                    out var objectModel) ||
                    objectModel.IsUnavailable)
                {
                    continue;
                }

                hasSelectableMember = true;
                if (!_selectedObjectIds.Contains(memberId))
                    return false;
            }

            return hasSelectableMember;
        }

        private static void AddVisibleItems(
            TreeGridItemCollection items,
            ICollection<TreeGridItem> visibleItems)
        {
            foreach (var rawItem in items)
            {
                var item = rawItem as TreeGridItem;
                if (item == null)
                    continue;

                visibleItems.Add(item);
                if (item.Expanded)
                    AddVisibleItems(item.Children, visibleItems);
            }
        }

        private void InvalidateVisibleSelectionRows()
        {
            _treeGuides.Refresh();
            _visibleSelectionRowsDirty = true;
            _visibleObjectRows.Clear();
            _visibleLayerRows.Clear();
            _visibleGroupRows.Clear();
            _visibleTreeItems.Clear();
            _visibleRows.Clear();
            _renderedSelectedRows.Clear();
            _renderedSelectedRowsValid = false;
            foreach (var item in _collapsedSelectionIndicatorItems)
            {
                item.SetValue(
                    OutlinerValueIndex.ContainsSelectedDescendant,
                    false);
            }
            _collapsedSelectionIndicatorItems.Clear();
        }

        private void EnsureVisibleSelectionRows()
        {
            if (!_visibleSelectionRowsDirty || _snapshot == null)
                return;

            _visibleObjectRows.Clear();
            _visibleLayerRows.Clear();
            _visibleGroupRows.Clear();
            _visibleTreeItems.Clear();
            _visibleRows.Clear();

            AddVisibleItems(_snapshot.Roots, _visibleRows);
            for (var row = 0; row < _visibleRows.Count; row++)
            {
                var item = _visibleRows[row];
                _visibleTreeItems.Add(item);
                var node = item.Tag as OutlinerNodeReference;
                if (node == null)
                    continue;

                switch (node.Kind)
                {
                    case OutlinerNodeKind.Object:
                        _visibleObjectRows[node.Id] = row;
                        break;
                    case OutlinerNodeKind.Layer:
                        _visibleLayerRows[node.Id] = row;
                        break;
                    case OutlinerNodeKind.GroupSlice:
                        if (!_visibleGroupRows.TryGetValue(
                            node.Id,
                            out var groupRows))
                        {
                            groupRows = new List<int>();
                            _visibleGroupRows[node.Id] = groupRows;
                        }
                        groupRows.Add(row);
                        break;
                }
            }

            _visibleSelectionRowsDirty = false;
        }
    }
}
