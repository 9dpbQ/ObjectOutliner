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
        private void OnTreeDragOver(object sender, DragEventArgs e)
        {
            var doc = GetEditableDocument();
            var target = GetDropTarget(e, doc);
            var canDrop = CanDrop(doc, target);
            e.Effects = canDrop ? DragEffects.Move : DragEffects.None;
            UpdateDragHover(canDrop ? target : null);
        }

        private void OnTreeDragDrop(object sender, DragEventArgs e)
        {
            var doc = GetEditableDocument();
            var target = GetDropTarget(e, doc);
            if (!CanDrop(doc, target))
            {
                e.Effects = DragEffects.None;
                return;
            }

            bool changed;
            if (_draggedNodeKind == OutlinerNodeKind.Object)
                changed = MoveDraggedObjects(doc, target);
            else if (_draggedNodeKind == OutlinerNodeKind.Layer)
                changed = MoveDraggedLayers(doc, target);
            else
                changed = MoveDraggedGroup(doc, target);
            e.Effects = changed
                ? DragEffects.Move
                : DragEffects.None;
        }

        private void OnTreeDragEnd(object sender, DragEventArgs e)
        {
            ClearDragState();
        }

        private void ClearDragState()
        {
            EndStateGesture();
            CancelReferencePointer();
            ClearPotentialExpansionGesture();
            ClearCommandInputPointer();
            _dragSourceNode = null;
            _blockedPointerNode = null;
            _blockedPointerColumn = null;
            _draggedNodeKind = null;
            _draggedNodeIds = null;
            _draggedGroupNodes = null;
            _dragHoverLayerId = Guid.Empty;
            _dragHoverStartedAt = DateTime.MinValue;
        }

        private bool IsDraggableNode(RhinoDoc doc, OutlinerNodeReference node)
        {
            if (node.Kind == OutlinerNodeKind.Object)
            {
                var rhinoObject = doc.Objects.FindId(node.Id);
                return rhinoObject != null &&
                       !rhinoObject.IsDeleted &&
                       !rhinoObject.IsReference &&
                       !IsObjectUnavailable(doc, rhinoObject);
            }

            if (node.Kind == OutlinerNodeKind.GroupSlice)
                return IsEditableGroupSlice(doc, node);

            if (node.Kind != OutlinerNodeKind.Layer)
                return false;

            var layer = doc.Layers.FindId(node.Id);
            return layer != null && !layer.IsDeleted && !layer.IsReference;
        }

        private bool IsSingleLayerGroupSlice(
            RhinoDoc doc,
            OutlinerNodeReference node)
        {
            if (!IsEditableGroupSlice(doc, node))
                return false;

            var group = _documentModel.Groups.FirstOrDefault(item => item.Id == node.Id);
            var objectsById = _documentModel.Objects.ToDictionary(item => item.Id);
            var memberLayerIds = group.MemberIds
                .Where(objectsById.ContainsKey)
                .Select(id => objectsById[id].LayerId)
                .Distinct()
                .ToList();
            var membersAreEditable = group.MemberIds.All(id =>
            {
                var rhinoObject = doc.Objects.FindId(id);
                return rhinoObject != null &&
                    !rhinoObject.IsDeleted &&
                    !rhinoObject.IsReference;
            });
            return membersAreEditable &&
                memberLayerIds.Count == 1 &&
                memberLayerIds[0] == node.OwnerId;
        }

        private bool IsEditableGroupSlice(
            RhinoDoc doc,
            OutlinerNodeReference node)
        {
            if (node == null ||
                node.Kind != OutlinerNodeKind.GroupSlice ||
                _documentModel == null)
                return false;

            var nativeGroup = doc.Groups.FindIndex(node.NativeIndex);
            var group = _documentModel.Groups.FirstOrDefault(item => item.Id == node.Id);
            if (nativeGroup == null ||
                nativeGroup.IsDeleted ||
                nativeGroup.Id != node.Id ||
                group == null)
                return false;

            var sliceMemberIds = GetGroupSliceMemberIds(doc, node);
            return sliceMemberIds.Count > 0 && sliceMemberIds.All(id =>
            {
                var rhinoObject = doc.Objects.FindId(id);
                return rhinoObject != null &&
                    !rhinoObject.IsDeleted &&
                    !rhinoObject.IsReference;
            });
        }

        private IList<Guid> GetGroupSliceMemberIds(
            RhinoDoc doc,
            OutlinerNodeReference node)
        {
            if (doc == null ||
                node == null ||
                node.Kind != OutlinerNodeKind.GroupSlice ||
                _documentModel == null)
            {
                return new List<Guid>();
            }

            var group = _documentModel.Groups.FirstOrDefault(item => item.Id == node.Id);
            if (group == null)
                return new List<Guid>();

            return group.MemberIds
                .Where(id =>
                {
                    var rhinoObject = doc.Objects.FindId(id);
                    if (rhinoObject == null || rhinoObject.IsDeleted)
                        return false;
                    var layer = doc.Layers[rhinoObject.Attributes.LayerIndex];
                    return layer != null && layer.Id == node.OwnerId;
                })
                .Distinct()
                .ToList();
        }

        private static bool IsObjectUnavailable(RhinoDoc doc, RhinoObject rhinoObject)
        {
            if (rhinoObject.IsHidden || rhinoObject.IsLocked)
                return true;

            var layerIndex = rhinoObject.Attributes.LayerIndex;
            var layer = layerIndex >= 0 && layerIndex < doc.Layers.Count
                ? doc.Layers[layerIndex]
                : null;
            var visited = new HashSet<Guid>();
            while (layer != null && visited.Add(layer.Id))
            {
                if (!layer.IsVisible || layer.IsLocked)
                    return true;
                layer = layer.ParentLayerId == Guid.Empty
                    ? null
                    : doc.Layers.FindId(layer.ParentLayerId);
            }

            return false;
        }

        private OutlinerDropTarget GetDropTarget(DragEventArgs e, RhinoDoc doc)
        {
            if (doc == null ||
                !_draggedNodeKind.HasValue ||
                _draggedNodeIds == null ||
                _draggedNodeIds.Count == 0)
                return null;

            var dataType = _draggedNodeKind == OutlinerNodeKind.Object
                ? ObjectDragDataType
                : _draggedNodeKind == OutlinerNodeKind.Layer
                    ? LayerDragDataType
                    : GroupDragDataType;
            if (!ReferenceEquals(e.Source, _tree) && !e.Data.Contains(dataType))
                return null;

            var dragInfo = _tree.GetDragInfo(e);
            if (dragInfo == null)
                return null;

            var item = dragInfo.Item as TreeGridItem;
            var node = item?.Tag as OutlinerNodeReference;
            if (node == null)
            {
                var parentItem = dragInfo.Parent as TreeGridItem;
                var parentNode = parentItem?.Tag as OutlinerNodeReference;
                if (parentNode != null && parentNode.Kind == OutlinerNodeKind.Layer)
                {
                    dragInfo.Item = parentItem;
                    dragInfo.RestrictToOver();
                    return new OutlinerDropTarget(parentNode, OutlinerDropPlacement.Over);
                }

                if (_draggedNodeKind != OutlinerNodeKind.Layer)
                    return null;

                var rootLayers = GetSortedActiveLayers(doc)
                    .Where(layer => layer.ParentLayerId == Guid.Empty)
                    .ToList();
                var insertIndex = dragInfo.InsertIndex;
                if (insertIndex >= 0 && insertIndex < rootLayers.Count)
                {
                    var targetLayer = rootLayers[insertIndex];
                    return new OutlinerDropTarget(
                        OutlinerNodeReference.ForLayer(
                            targetLayer.Id,
                            targetLayer.Index),
                        OutlinerDropPlacement.Before);
                }

                return new OutlinerDropTarget(null, OutlinerDropPlacement.Append);
            }

            if (_draggedNodeKind == OutlinerNodeKind.Object)
            {
                if (node.Kind == OutlinerNodeKind.Layer)
                {
                    dragInfo.RestrictToOver();
                    return new OutlinerDropTarget(node, OutlinerDropPlacement.Over);
                }

                if (node.Kind == OutlinerNodeKind.GroupSlice)
                {
                    dragInfo.RestrictToOver();
                    return new OutlinerDropTarget(node, OutlinerDropPlacement.Over);
                }

                if (node.Kind != OutlinerNodeKind.Object)
                    return null;

                dragInfo.RestrictToInsert();
                return new OutlinerDropTarget(node, GetDropPlacement(dragInfo.Position));
            }

            if (_draggedNodeKind == OutlinerNodeKind.GroupSlice)
            {
                if (node.Kind != OutlinerNodeKind.GroupSlice &&
                    node.Kind != OutlinerNodeKind.Layer)
                    return null;

                if (node.Kind == OutlinerNodeKind.Layer ||
                    dragInfo.Position == GridDragPosition.Over)
                {
                    dragInfo.RestrictToOver();
                    return new OutlinerDropTarget(
                        node,
                        OutlinerDropPlacement.Over);
                }

                dragInfo.RestrictToInsert();
                return new OutlinerDropTarget(
                    node,
                    GetDropPlacement(dragInfo.Position));
            }

            if (node.Kind != OutlinerNodeKind.Layer)
                return null;

            return new OutlinerDropTarget(node, GetDropPlacement(dragInfo.Position));
        }

        private void UpdateDragHover(OutlinerDropTarget target)
        {
            var layerId = target?.Placement == OutlinerDropPlacement.Over &&
                          target.TargetNode?.Kind == OutlinerNodeKind.Layer
                ? target.TargetNode.Id
                : Guid.Empty;
            if (layerId == Guid.Empty)
            {
                _dragHoverLayerId = Guid.Empty;
                _dragHoverStartedAt = DateTime.MinValue;
                return;
            }

            if (_dragHoverLayerId != layerId)
            {
                _dragHoverLayerId = layerId;
                _dragHoverStartedAt = DateTime.UtcNow;
                return;
            }

            if (DateTime.UtcNow - _dragHoverStartedAt < TimeSpan.FromMilliseconds(650) ||
                _snapshot == null ||
                !_snapshot.LayerItems.TryGetValue(layerId, out var item) ||
                item.Expanded)
                return;

            item.Expanded = true;
            InvalidateVisibleSelectionRows();
            if (!_viewState.HasSearch)
                _layerExpansionState[layerId] = true;
            _tree.Invalidate();
        }

        private static OutlinerDropPlacement GetDropPlacement(GridDragPosition position)
        {
            switch (position)
            {
                case GridDragPosition.Before:
                    return OutlinerDropPlacement.Before;
                case GridDragPosition.After:
                    return OutlinerDropPlacement.After;
                default:
                    return OutlinerDropPlacement.Over;
            }
        }

        private bool CanDrop(RhinoDoc doc, OutlinerDropTarget target)
        {
            if (!_draggedNodeKind.HasValue)
                return false;

            if (_draggedNodeKind == OutlinerNodeKind.Object)
                return CanDropObjects(doc, target);
            if (_draggedNodeKind == OutlinerNodeKind.Layer)
                return CanDropLayers(doc, target);
            return CanDropGroup(doc, target);
        }

        private bool CanDropObjects(RhinoDoc doc, OutlinerDropTarget target)
        {
            if (doc == null || target?.TargetNode == null || _draggedNodeIds == null)
                return false;

            if (target.TargetNode.Kind != OutlinerNodeKind.Layer &&
                target.TargetNode.Kind != OutlinerNodeKind.Object &&
                target.TargetNode.Kind != OutlinerNodeKind.GroupSlice)
                return false;

            var draggedObjectsAreValid = _draggedNodeIds.All(objectId =>
            {
                var rhinoObject = doc.Objects.FindId(objectId);
                return rhinoObject != null &&
                       !rhinoObject.IsDeleted &&
                       !rhinoObject.IsReference &&
                       !IsObjectUnavailable(doc, rhinoObject);
            });
            if (!draggedObjectsAreValid)
                return false;

            if (target.TargetNode.Kind == OutlinerNodeKind.GroupSlice)
            {
                if (target.Placement != OutlinerDropPlacement.Over)
                    return false;

                var group = doc.Groups.FindIndex(target.TargetNode.NativeIndex);
                if (group == null ||
                    group.IsDeleted ||
                    group.Id != target.TargetNode.Id)
                    return false;

                return _draggedNodeIds.Any(objectId =>
                {
                    var rhinoObject = doc.Objects.FindId(objectId);
                    var groupIndices = rhinoObject.Attributes.GetGroupList() ??
                        Array.Empty<int>();
                    if (!groupIndices.Contains(target.TargetNode.NativeIndex))
                        return true;

                    var path = _snapshot?.GroupHierarchy
                        .GetCanonicalGroupPath(objectId) ?? Array.Empty<Guid>();
                    var targetIndex = path.IndexOf(target.TargetNode.Id);
                    return targetIndex >= 0 && targetIndex < path.Count - 1;
                });
            }

            Layer targetLayer;
            if (target.TargetNode.Kind == OutlinerNodeKind.Layer)
            {
                targetLayer = doc.Layers.FindId(target.TargetNode.Id);
            }
            else
            {
                if (_draggedNodeIds.Contains(target.TargetNode.Id))
                    return false;
                var rhinoObject = doc.Objects.FindId(target.TargetNode.Id);
                targetLayer = rhinoObject == null
                    ? null
                    : doc.Layers[rhinoObject.Attributes.LayerIndex];
            }

            if (targetLayer == null || targetLayer.IsDeleted || targetLayer.IsReference)
                return false;

            if (target.TargetNode.Kind == OutlinerNodeKind.Object)
            {
                var targetGroupId = GetPresentationGroupId(target.TargetNode.Id);
                if (_draggedNodeIds.Any(id =>
                    GetPresentationGroupId(id) != targetGroupId))
                    return false;
            }

            return true;
        }

        private bool CanDropGroup(RhinoDoc doc, OutlinerDropTarget target)
        {
            var sources = _draggedGroupNodes ??
                new List<OutlinerNodeReference>();
            if (doc == null ||
                sources.Count == 0 ||
                target?.TargetNode == null ||
                sources.Any(candidate =>
                    !IsEditableGroupSlice(doc, candidate)))
                return false;

            if (target.TargetNode.Kind == OutlinerNodeKind.Layer)
            {
                if (target.Placement != OutlinerDropPlacement.Over)
                    return false;
                var targetLayer = doc.Layers.FindId(target.TargetNode.Id);
                if (targetLayer == null ||
                    targetLayer.IsDeleted ||
                    targetLayer.IsReference)
                {
                    return false;
                }

                if (sources.Count > 1)
                {
                    var movingSources = sources
                        .Where(candidate =>
                            candidate.OwnerId != target.TargetNode.Id)
                        .ToList();
                    return movingSources.Count > 0 &&
                        movingSources.All(candidate =>
                            GetGroupSliceMemberIds(doc, candidate).Count > 0);
                }

                var layerSource = sources[0];
                if (target.TargetNode.Id != layerSource.OwnerId)
                    return GetGroupSliceMemberIds(doc, layerSource).Count > 0;

                return IsSingleLayerGroupSlice(doc, layerSource) &&
                    _snapshot?.GroupHierarchy.GetParentGroupId(layerSource.Id) !=
                        Guid.Empty;
            }

            if (target.Placement == OutlinerDropPlacement.Before ||
                target.Placement == OutlinerDropPlacement.After)
            {
                return CanReorderGroupSlices(sources, target.TargetNode);
            }

            if (sources.Count != 1)
                return false;

            var source = sources[0];
            if (target.TargetNode.Kind != OutlinerNodeKind.GroupSlice ||
                target.TargetNode.Id == source.Id ||
                target.TargetNode.OwnerId != source.OwnerId ||
                !IsSingleLayerGroupSlice(doc, source) ||
                !IsSingleLayerGroupSlice(doc, target.TargetNode))
                return false;

            if (_snapshot?.GroupHierarchy.IsAncestorOrSelf(
                    source.Id,
                    target.TargetNode.Id) == true)
                return false;

            var currentParentId = _snapshot?.GroupHierarchy
                .GetParentGroupId(source.Id) ?? Guid.Empty;
            if (_snapshot?.GroupHierarchy.IsAncestorOrSelf(
                    target.TargetNode.Id,
                    source.Id) == true)
                return currentParentId != target.TargetNode.Id;

            var sourceMembers = new HashSet<Guid>(GetGroupMemberIds(source.Id));
            var targetMembers = new HashSet<Guid>(GetGroupMemberIds(
                target.TargetNode.Id));
            return sourceMembers.Count > 0 && !sourceMembers.IsSubsetOf(targetMembers);
        }

        private bool CanReorderGroupSlices(
            IEnumerable<OutlinerNodeReference> sources,
            OutlinerNodeReference target)
        {
            if (_snapshot == null ||
                target == null ||
                target.Kind != OutlinerNodeKind.GroupSlice)
            {
                return false;
            }

            var sourceList = (sources ??
                    Enumerable.Empty<OutlinerNodeReference>())
                .Where(source => source != null)
                .GroupBy(source =>
                    new OutlinerGroupSliceKey(source.Id, source.OwnerId))
                .Select(group => group.First())
                .ToList();
            if (sourceList.Count == 0 ||
                sourceList.Any(source =>
                    source.OwnerId != target.OwnerId ||
                    source.Id == target.Id))
            {
                return false;
            }

            var targetParentId = _snapshot.GroupHierarchy
                .GetParentGroupId(target.Id);
            return sourceList.All(source =>
                _snapshot.GroupHierarchy.GetParentGroupId(source.Id) ==
                    targetParentId);
        }
    }
}
