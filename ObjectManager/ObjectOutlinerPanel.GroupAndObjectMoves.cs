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
        private bool MoveDraggedGroup(RhinoDoc doc, OutlinerDropTarget target)
        {
            var sources = _draggedGroupNodes ??
                new List<OutlinerNodeReference>();
            if (sources.Count == 0 || target?.TargetNode == null)
                return false;

            if (target.TargetNode.Kind == OutlinerNodeKind.GroupSlice &&
                (target.Placement == OutlinerDropPlacement.Before ||
                 target.Placement == OutlinerDropPlacement.After))
            {
                return ReorderGroupSlices(doc, sources, target);
            }

            if (sources.Count > 1)
            {
                return target.TargetNode.Kind == OutlinerNodeKind.Layer &&
                    MoveGroupSlicesToLayer(
                        doc,
                        sources,
                        target.TargetNode.Id);
            }

            var source = sources[0];

            var sourceGroup = _documentModel?.Groups.FirstOrDefault(
                item => item.Id == source.Id);
            if (sourceGroup == null || sourceGroup.MemberIds.Count == 0)
                return false;

            if (target.TargetNode.Kind == OutlinerNodeKind.GroupSlice)
            {
                PreserveGroupSliceExpansion(target.TargetNode);
                var targetGroup = doc.Groups.FindIndex(target.TargetNode.NativeIndex);
                if (targetGroup == null ||
                    targetGroup.IsDeleted ||
                    targetGroup.Id != target.TargetNode.Id)
                    return false;

                var memberIds = sourceGroup.MemberIds.Distinct().ToList();
                var removals = new Dictionary<Guid, IList<int>>();
                var oldAncestorIds = GetGroupAncestorIds(source.Id);
                var newAncestorIds = new[] { target.TargetNode.Id }
                    .Concat(GetGroupAncestorIds(target.TargetNode.Id))
                    .Distinct()
                    .ToList();
                var newAncestorSet = new HashSet<Guid>(newAncestorIds);
                var parentIndicesToRemove = oldAncestorIds
                    .Where(ancestorId => !newAncestorSet.Contains(ancestorId))
                    .Select(ancestorId => _documentModel?.Groups.FirstOrDefault(
                        item => item.Id == ancestorId))
                    .Where(item => item != null)
                    .Select(item => item.Index)
                    .Distinct()
                    .ToList();
                if (parentIndicesToRemove.Count > 0)
                {
                    foreach (var memberId in memberIds)
                        removals[memberId] = parentIndicesToRemove;
                }

                var parentIndicesToAdd = newAncestorIds
                    .Select(ancestorId => _documentModel?.Groups.FirstOrDefault(
                        item => item.Id == ancestorId))
                    .Where(item => item != null)
                    .Select(item => item.Index)
                    .Distinct()
                    .ToList();

                return ChangeObjectGroupMemberships(
                    doc,
                    removals,
                    parentIndicesToAdd,
                    memberIds,
                    OutlinerStrings.UndoMoveGroup);
            }

            if (target.TargetNode.Kind != OutlinerNodeKind.Layer)
                return false;

            if (target.TargetNode.Id != source.OwnerId)
            {
                return MoveGroupSliceToLayer(
                    doc,
                    source,
                    target.TargetNode.Id);
            }

            var parentId = _snapshot?.GroupHierarchy.GetParentGroupId(source.Id) ??
                Guid.Empty;
            var parent = _documentModel?.Groups.FirstOrDefault(
                item => item.Id == parentId);
            return parent != null && RemoveObjectsFromGroups(
                doc,
                sourceGroup.MemberIds.Distinct().ToDictionary(
                    id => id,
                    id => (IList<int>)new List<int> { parent.Index }),
                OutlinerStrings.UndoRemoveGroupFromParent);
        }

        private bool ReorderGroupSlices(
            RhinoDoc doc,
            IEnumerable<OutlinerNodeReference> sources,
            OutlinerDropTarget target)
        {
            if (doc == null ||
                _documentModel == null ||
                _snapshot == null ||
                _groupOrderStore == null ||
                target?.TargetNode == null ||
                !CanReorderGroupSlices(sources, target.TargetNode))
            {
                return false;
            }

            var layerId = target.TargetNode.OwnerId;
            var parentId = _snapshot.GroupHierarchy.GetParentGroupId(
                target.TargetNode.Id);
            var orders = _groupOrderStore.GetOrders(_documentSerialNumber);
            var siblings = _documentModel.Groups
                .Where(group =>
                    _snapshot.GroupSliceItems.ContainsKey(
                        new OutlinerGroupSliceKey(group.Id, layerId)) &&
                    _snapshot.GroupHierarchy.GetParentGroupId(group.Id) ==
                        parentId)
                .OrderBy(group => orders.ContainsKey(
                    new OutlinerGroupSliceKey(group.Id, layerId)) ? 0 : 1)
                .ThenBy(group => orders.TryGetValue(
                    new OutlinerGroupSliceKey(group.Id, layerId),
                    out var order) ? order : int.MaxValue)
                .ThenBy(group => group.Index)
                .Select(group => group.Id)
                .ToList();
            var movedIds = (sources ??
                    Enumerable.Empty<OutlinerNodeReference>())
                .Select(source => source.Id)
                .Distinct()
                .ToList();
            var reordered = GroupOrderPlanner.MoveBeforeOrAfter(
                siblings,
                movedIds,
                target.TargetNode.Id,
                target.Placement == OutlinerDropPlacement.After);
            if (siblings.SequenceEqual(reordered))
                return false;

            var undoRecord = doc.BeginUndoRecord(
                OutlinerStrings.UndoReorderGroups);
            try
            {
                return _groupOrderStore.SetSequence(
                    doc,
                    layerId,
                    reordered);
            }
            finally
            {
                if (undoRecord != 0)
                    doc.EndUndoRecord(undoRecord);
            }
        }

        private bool MoveGroupSliceToLayer(
            RhinoDoc doc,
            OutlinerNodeReference source,
            Guid targetLayerId)
        {
            return MoveGroupSlicesToLayer(
                doc,
                new[] { source },
                targetLayerId);
        }

        private bool MoveGroupSlicesToLayer(
            RhinoDoc doc,
            IEnumerable<OutlinerNodeReference> sources,
            Guid targetLayerId)
        {
            var targetLayer = doc?.Layers.FindId(targetLayerId);
            var sourceSlices = (sources ??
                    Enumerable.Empty<OutlinerNodeReference>())
                .Where(source =>
                    source != null &&
                    source.Kind == OutlinerNodeKind.GroupSlice &&
                    source.OwnerId != targetLayerId)
                .GroupBy(source =>
                    new OutlinerGroupSliceKey(source.Id, source.OwnerId))
                .Select(group => group.First())
                .ToList();
            if (doc == null ||
                sourceSlices.Count == 0 ||
                targetLayer == null ||
                targetLayer.IsDeleted ||
                targetLayer.IsReference)
            {
                return false;
            }

            var sliceMemberIds = sourceSlices
                .SelectMany(source => GetGroupSliceMemberIds(doc, source))
                .Distinct()
                .ToList();
            if (sliceMemberIds.Count == 0)
                return false;

            var sliceMemberSet = new HashSet<Guid>(sliceMemberIds);
            var sourceLayerIndices = sliceMemberIds
                .Select(doc.Objects.FindId)
                .Where(item => item != null)
                .Select(item => item.Attributes.LayerIndex)
                .Distinct()
                .ToList();
            var affectedLayerIndices = sourceLayerIndices
                .Concat(new[] { targetLayer.Index })
                .Distinct()
                .ToList();
            var desiredByLayer = affectedLayerIndices.ToDictionary(
                layerIndex => layerIndex,
                layerIndex => GetOrderedObjectIds(doc, layerIndex)
                    .Where(id => !sliceMemberSet.Contains(id))
                    .ToList());

            var orderedFromSourceLayers = sourceLayerIndices
                .SelectMany(layerIndex => GetOrderedObjectIds(doc, layerIndex))
                .Where(sliceMemberSet.Contains)
                .Distinct()
                .ToList();
            var orderedMemberSet = new HashSet<Guid>(orderedFromSourceLayers);
            var orderedMembers = orderedFromSourceLayers
                .Concat(sliceMemberIds.Where(id => !orderedMemberSet.Contains(id)))
                .ToList();
            desiredByLayer[targetLayer.Index].AddRange(orderedMembers);

            var selectedBeforeMove = doc.Objects
                .GetSelectedObjects(false, false)
                .Select(item => item.Id)
                .ToList();

            var changed = ModifyDocument(
                doc,
                OutlinerStrings.UndoMoveGroupToLayer,
                () =>
            {
                var attributesChanged = false;
                foreach (var pair in desiredByLayer)
                {
                    for (var order = 0; order < pair.Value.Count; order++)
                    {
                        var objectId = pair.Value[order];
                        var rhinoObject = doc.Objects.FindId(objectId);
                        if (rhinoObject == null || rhinoObject.IsReference)
                            continue;

                        int currentOrder;
                        var hasCurrentOrder = ObjectOrderStore.TryGetOrder(
                            rhinoObject.Attributes,
                            out currentOrder);
                        if (rhinoObject.Attributes.LayerIndex == pair.Key &&
                            hasCurrentOrder &&
                            currentOrder == order)
                        {
                            continue;
                        }

                        var attributes = rhinoObject.Attributes.Duplicate();
                        attributes.LayerIndex = pair.Key;
                        ObjectOrderStore.SetOrder(attributes, order);
                        attributesChanged |= doc.Objects.ModifyAttributes(
                            objectId,
                            attributes,
                            true);
                    }
                }
                return attributesChanged;
            }, false);
            if (changed)
                RestoreSelectableDocumentSelection(doc, selectedBeforeMove);
            return changed;
        }

        private IList<Guid> GetGroupAncestorIds(Guid groupId)
        {
            var result = new List<Guid>();
            var currentId = _snapshot?.GroupHierarchy.GetParentGroupId(groupId) ??
                Guid.Empty;
            var visited = new HashSet<Guid>();
            while (currentId != Guid.Empty && visited.Add(currentId))
            {
                result.Add(currentId);
                currentId = _snapshot?.GroupHierarchy.GetParentGroupId(currentId) ??
                    Guid.Empty;
            }
            return result;
        }

        private Guid GetPresentationGroupId(Guid objectId)
        {
            return _snapshot?.GroupHierarchy.GetCanonicalContainerId(objectId) ??
                Guid.Empty;
        }

        private bool MoveDraggedObjects(RhinoDoc doc, OutlinerDropTarget target)
        {
            if (target?.TargetNode?.Kind == OutlinerNodeKind.GroupSlice)
                return MoveDraggedObjectsToGroup(doc, target.TargetNode);

            if (ShouldRemoveDraggedObjectsFromImmediateGroups(doc, target))
                return RemoveDraggedObjectsFromImmediateGroups(doc);

            if (target?.TargetNode == null ||
                (target.TargetNode.Kind != OutlinerNodeKind.Layer &&
                 target.TargetNode.Kind != OutlinerNodeKind.Object))
                return false;

            var targetObject = target.TargetNode.Kind == OutlinerNodeKind.Object
                ? doc.Objects.FindId(target.TargetNode.Id)
                : null;
            var targetLayer = target.TargetNode.Kind == OutlinerNodeKind.Layer
                ? doc.Layers.FindId(target.TargetNode.Id)
                : targetObject == null
                    ? null
                    : doc.Layers[targetObject.Attributes.LayerIndex];
            if (targetLayer == null)
                return false;

            var draggedIds = _draggedNodeIds.ToList();
            var draggedSet = new HashSet<Guid>(draggedIds);
            var sourceLayerIndices = draggedIds
                .Select(doc.Objects.FindId)
                .Where(item => item != null)
                .Select(item => item.Attributes.LayerIndex)
                .Distinct()
                .ToList();
            var affectedLayerIndices = sourceLayerIndices
                .Concat(new[] { targetLayer.Index })
                .Distinct()
                .ToList();
            var desiredByLayer = affectedLayerIndices.ToDictionary(
                layerIndex => layerIndex,
                layerIndex => GetOrderedObjectIds(doc, layerIndex)
                    .Where(id => !draggedSet.Contains(id))
                    .ToList());

            var targetOrder = desiredByLayer[targetLayer.Index];
            var insertionIndex = targetOrder.Count;
            if (targetObject != null)
            {
                insertionIndex = targetOrder.IndexOf(targetObject.Id);
                if (insertionIndex < 0)
                    return false;
                if (target.Placement == OutlinerDropPlacement.After)
                    insertionIndex++;
            }
            targetOrder.InsertRange(insertionIndex, draggedIds);

            var selectedBeforeMove = doc.Objects
                .GetSelectedObjects(false, false)
                .Select(item => item.Id)
                .ToList();

            var changed = ModifyDocument(
                doc,
                OutlinerStrings.UndoMoveOrReorderObjects,
                () =>
            {
                var attributesChanged = false;
                foreach (var pair in desiredByLayer)
                {
                    for (var order = 0; order < pair.Value.Count; order++)
                    {
                        var objectId = pair.Value[order];
                        var rhinoObject = doc.Objects.FindId(objectId);
                        if (rhinoObject == null || rhinoObject.IsReference)
                            continue;

                        int currentOrder;
                        var hasCurrentOrder = ObjectOrderStore.TryGetOrder(
                            rhinoObject.Attributes,
                            out currentOrder);
                        if (rhinoObject.Attributes.LayerIndex == pair.Key &&
                            hasCurrentOrder &&
                            currentOrder == order)
                            continue;

                        var attributes = rhinoObject.Attributes.Duplicate();
                        attributes.LayerIndex = pair.Key;
                        ObjectOrderStore.SetOrder(attributes, order);
                        attributesChanged |= doc.Objects.ModifyAttributes(
                            objectId,
                            attributes,
                            true);
                    }
                }
                return attributesChanged;
            }, false);
            if (changed)
                RestoreSelectableDocumentSelection(doc, selectedBeforeMove);
            return changed;
        }

        private bool MoveDraggedObjectsToGroup(
            RhinoDoc doc,
            OutlinerNodeReference groupNode)
        {
            if (_draggedNodeIds == null)
                return false;

            PreserveGroupSliceExpansion(groupNode);

            var group = doc.Groups.FindIndex(groupNode.NativeIndex);
            if (group == null || group.IsDeleted || group.Id != groupNode.Id)
                return false;

            var removals = new Dictionary<Guid, IList<int>>();
            var newMemberIds = new List<Guid>();
            foreach (var objectId in _draggedNodeIds.Distinct())
            {
                var rhinoObject = doc.Objects.FindId(objectId);
                if (rhinoObject == null ||
                    rhinoObject.IsDeleted ||
                    rhinoObject.IsReference ||
                    IsObjectUnavailable(doc, rhinoObject))
                    continue;

                var groupIndices = rhinoObject.Attributes.GetGroupList() ??
                    Array.Empty<int>();
                var path = _snapshot?.GroupHierarchy.GetCanonicalGroupPath(objectId) ??
                    Array.Empty<Guid>();
                var targetIndex = path.IndexOf(groupNode.Id);
                if (targetIndex >= 0 && targetIndex < path.Count - 1)
                {
                    var indicesToRemove = path
                        .Skip(targetIndex + 1)
                        .Select(groupId => _documentModel?.Groups.FirstOrDefault(
                            candidate => candidate.Id == groupId))
                        .Where(candidate => candidate != null)
                        .Select(candidate => candidate.Index)
                        .Distinct()
                        .ToList();
                    if (indicesToRemove.Count > 0)
                        removals[objectId] = indicesToRemove;
                }
                else if (!groupIndices.Contains(groupNode.NativeIndex))
                {
                    newMemberIds.Add(objectId);
                }
            }

            if (newMemberIds.Count == 0 && removals.Count == 0)
                return false;

            return ChangeObjectGroupMemberships(
                doc,
                removals,
                new List<int> { groupNode.NativeIndex },
                newMemberIds,
                OutlinerStrings.UndoMoveObjectsToGroup);
        }

        private void PreserveGroupSliceExpansion(
            OutlinerNodeReference groupNode)
        {
            if (groupNode == null ||
                groupNode.Kind != OutlinerNodeKind.GroupSlice ||
                groupNode.Id == Guid.Empty ||
                groupNode.OwnerId == Guid.Empty ||
                _snapshot == null)
            {
                return;
            }

            var key = new OutlinerGroupSliceKey(
                groupNode.Id,
                groupNode.OwnerId);
            if (_snapshot.GroupSliceItems.TryGetValue(key, out var item))
                _groupExpansionState[key] = item.Expanded;
        }

        private bool ShouldRemoveDraggedObjectsFromImmediateGroups(
            RhinoDoc doc,
            OutlinerDropTarget target)
        {
            if (_draggedNodeIds == null ||
                target?.TargetNode?.Kind != OutlinerNodeKind.Layer)
                return false;

            var targetLayer = doc.Layers.FindId(target.TargetNode.Id);
            if (targetLayer == null)
                return false;

            var objects = _draggedNodeIds
                .Select(doc.Objects.FindId)
                .Where(item => item != null)
                .ToList();
            return objects.Count == _draggedNodeIds.Count &&
                objects.All(item => item.Attributes.LayerIndex == targetLayer.Index) &&
                objects.Any(item => GetPresentationGroupId(item.Id) != Guid.Empty);
        }

        private bool RemoveDraggedObjectsFromImmediateGroups(RhinoDoc doc)
        {
            var removals = new Dictionary<Guid, IList<int>>();
            foreach (var objectId in _draggedNodeIds.Distinct())
            {
                var containerId = GetPresentationGroupId(objectId);
                var group = _documentModel?.Groups.FirstOrDefault(
                    candidate => candidate.Id == containerId);
                if (group != null)
                    removals[objectId] = new List<int> { group.Index };
            }

            return RemoveObjectsFromGroups(
                doc,
                removals,
                OutlinerStrings.UndoRemoveObjectsFromGroup);
        }

        private bool RemoveObjectsFromGroups(
            RhinoDoc doc,
            IDictionary<Guid, IList<int>> removals,
            string undoDescription)
        {
            return ChangeObjectGroupMemberships(
                doc,
                removals,
                new List<int>(),
                new List<Guid>(),
                undoDescription);
        }

        private bool ChangeObjectGroupMemberships(
            RhinoDoc doc,
            IDictionary<Guid, IList<int>> removals,
            IList<int> addToGroupIndices,
            IList<Guid> additions,
            string undoDescription)
        {
            if ((removals == null || removals.Count == 0) &&
                (addToGroupIndices == null || addToGroupIndices.Count == 0 ||
                 additions == null || additions.Count == 0))
                return false;

            var selectedBeforeChange = doc.Objects
                .GetSelectedObjects(false, false)
                .Select(item => item.Id)
                .ToList();
            var changed = ModifyDocument(doc, undoDescription, () =>
            {
                var membershipChanged = false;
                if (addToGroupIndices != null && additions != null && additions.Count > 0)
                {
                    foreach (var groupIndex in addToGroupIndices.Distinct())
                        membershipChanged |= doc.Groups.AddToGroup(
                            groupIndex,
                            additions.Distinct());
                }

                if (removals != null)
                {
                    foreach (var pair in removals)
                    {
                        var rhinoObject = doc.Objects.FindId(pair.Key);
                        if (rhinoObject == null || rhinoObject.IsReference)
                            continue;

                        var attributes = rhinoObject.Attributes.Duplicate();
                        foreach (var groupIndex in pair.Value.Distinct())
                            attributes.RemoveFromGroup(groupIndex);
                        membershipChanged |= doc.Objects.ModifyAttributes(
                            pair.Key,
                            attributes,
                            true);
                    }
                }
                return membershipChanged;
            }, false);
            if (changed)
                RestoreSelectableDocumentSelection(doc, selectedBeforeChange);
            return changed;
        }

        private static List<Guid> GetOrderedObjectIds(RhinoDoc doc, int layerIndex)
        {
            var layer = layerIndex >= 0 && layerIndex < doc.Layers.Count
                ? doc.Layers[layerIndex]
                : null;
            if (layer == null)
                return new List<Guid>();

            var layerObjects = doc.Objects.FindByLayer(layer) ?? new RhinoObject[0];
            return layerObjects
                .Where(item => item != null && !item.IsDeleted)
                .Select(item =>
                {
                    int order;
                    return new
                    {
                        Item = item,
                        Order = ObjectOrderStore.TryGetOrder(item, out order)
                            ? (int?)order
                            : null
                    };
                })
                .OrderBy(item => item.Order.HasValue ? 0 : 1)
                .ThenBy(item => item.Order ?? int.MaxValue)
                .ThenBy(item => item.Item.RuntimeSerialNumber)
                .Select(item => item.Item.Id)
                .ToList();
        }
    }
}
