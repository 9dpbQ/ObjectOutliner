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
        private static List<Guid> NormalizeDraggedLayerIds(
            RhinoDoc doc,
            IEnumerable<Guid> layerIds)
        {
            var orderedIds = layerIds.Distinct().ToList();
            var selected = new HashSet<Guid>(orderedIds);
            return orderedIds.Where(layerId =>
            {
                var layer = doc.Layers.FindId(layerId);
                if (layer == null)
                    return false;

                var parentId = layer.ParentLayerId;
                var visited = new HashSet<Guid>();
                while (parentId != Guid.Empty && visited.Add(parentId))
                {
                    if (selected.Contains(parentId))
                        return false;
                    var parent = doc.Layers.FindId(parentId);
                    if (parent == null)
                        break;
                    parentId = parent.ParentLayerId;
                }
                return true;
            }).ToList();
        }

        private bool CanDropLayers(RhinoDoc doc, OutlinerDropTarget target)
        {
            if (doc == null || _draggedNodeIds == null || _draggedNodeIds.Count == 0)
                return false;

            Guid parentId;
            Guid relativeLayerId;
            if (!TryGetLayerDropDestination(
                    doc,
                    target,
                    out parentId,
                    out relativeLayerId))
                return false;

            var movingIds = new HashSet<Guid>(_draggedNodeIds);
            if (relativeLayerId != Guid.Empty && movingIds.Contains(relativeLayerId))
                return false;

            if (parentId != Guid.Empty)
            {
                var parent = doc.Layers.FindId(parentId);
                if (parent == null || parent.IsDeleted || parent.IsReference)
                    return false;
            }

            var movingLayers = new List<Layer>();
            foreach (var layerId in _draggedNodeIds)
            {
                var layer = doc.Layers.FindId(layerId);
                if (layer == null || layer.IsDeleted || layer.IsReference)
                    return false;
                if (IsLayerDescendantOf(doc, parentId, layerId))
                    return false;
                movingLayers.Add(layer);
            }

            var movingNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (movingLayers.Any(layer => !movingNames.Add(layer.Name)))
                return false;

            var occupiedNames = new HashSet<string>(
                GetSortedActiveLayers(doc)
                    .Where(layer => layer.ParentLayerId == parentId &&
                                    !movingIds.Contains(layer.Id))
                    .Select(layer => layer.Name),
                StringComparer.OrdinalIgnoreCase);
            return movingLayers.All(layer => !occupiedNames.Contains(layer.Name));
        }

        private static bool TryGetLayerDropDestination(
            RhinoDoc doc,
            OutlinerDropTarget target,
            out Guid parentId,
            out Guid relativeLayerId)
        {
            parentId = Guid.Empty;
            relativeLayerId = Guid.Empty;
            if (target == null)
                return false;

            if (target.TargetNode == null)
                return target.Placement == OutlinerDropPlacement.Append;

            if (target.TargetNode.Kind != OutlinerNodeKind.Layer)
                return false;

            var targetLayer = doc.Layers.FindId(target.TargetNode.Id);
            if (targetLayer == null || targetLayer.IsDeleted || targetLayer.IsReference)
                return false;

            if (target.Placement == OutlinerDropPlacement.Over)
            {
                parentId = targetLayer.Id;
                return true;
            }

            if (target.Placement != OutlinerDropPlacement.Before &&
                target.Placement != OutlinerDropPlacement.After)
                return false;

            parentId = targetLayer.ParentLayerId;
            relativeLayerId = targetLayer.Id;
            return true;
        }

        private static bool IsLayerDescendantOf(
            RhinoDoc doc,
            Guid candidateId,
            Guid ancestorId)
        {
            var currentId = candidateId;
            var visited = new HashSet<Guid>();
            while (currentId != Guid.Empty && visited.Add(currentId))
            {
                if (currentId == ancestorId)
                    return true;
                var current = doc.Layers.FindId(currentId);
                if (current == null)
                    break;
                currentId = current.ParentLayerId;
            }
            return false;
        }

        private bool MoveDraggedLayers(RhinoDoc doc, OutlinerDropTarget target)
        {
            Guid parentId;
            Guid relativeLayerId;
            if (!TryGetLayerDropDestination(
                    doc,
                    target,
                    out parentId,
                    out relativeLayerId))
                return false;

            var sortedLayers = GetSortedActiveLayers(doc);
            var layersById = sortedLayers.ToDictionary(layer => layer.Id);
            var requestedIds = new HashSet<Guid>(_draggedNodeIds);
            var movingIds = sortedLayers
                .Where(layer => requestedIds.Contains(layer.Id))
                .Select(layer => layer.Id)
                .ToList();
            if (movingIds.Count == 0)
                return false;

            var movingSet = new HashSet<Guid>(movingIds);
            var siblingsByParent = new Dictionary<Guid, List<Guid>>();
            foreach (var layer in sortedLayers)
            {
                if (movingSet.Contains(layer.Id))
                    continue;

                List<Guid> siblings;
                if (!siblingsByParent.TryGetValue(layer.ParentLayerId, out siblings))
                {
                    siblings = new List<Guid>();
                    siblingsByParent[layer.ParentLayerId] = siblings;
                }
                siblings.Add(layer.Id);
            }

            List<Guid> destinationSiblings;
            if (!siblingsByParent.TryGetValue(parentId, out destinationSiblings))
            {
                destinationSiblings = new List<Guid>();
                siblingsByParent[parentId] = destinationSiblings;
            }

            var insertionIndex = destinationSiblings.Count;
            if (relativeLayerId != Guid.Empty)
            {
                insertionIndex = destinationSiblings.IndexOf(relativeLayerId);
                if (insertionIndex < 0)
                    return false;
                if (target.Placement == OutlinerDropPlacement.After)
                    insertionIndex++;
            }
            destinationSiblings.InsertRange(insertionIndex, movingIds);

            var flattenedIds = new List<Guid>();
            var visited = new HashSet<Guid>();
            Action<Guid> appendChildren = null;
            appendChildren = currentParentId =>
            {
                List<Guid> children;
                if (!siblingsByParent.TryGetValue(currentParentId, out children))
                    return;

                foreach (var childId in children)
                {
                    if (!visited.Add(childId))
                        continue;
                    flattenedIds.Add(childId);
                    appendChildren(childId);
                }
            };
            appendChildren(Guid.Empty);
            foreach (var layer in sortedLayers)
            {
                if (visited.Add(layer.Id))
                {
                    flattenedIds.Add(layer.Id);
                    appendChildren(layer.Id);
                }
            }

            var desiredIndices = flattenedIds
                .Where(layersById.ContainsKey)
                .Select(layerId => layersById[layerId].Index)
                .ToList();
            var currentIndices = sortedLayers.Select(layer => layer.Index).ToList();

            return ModifyDocument(
                doc,
                OutlinerStrings.UndoMoveOrReorderLayers,
                () =>
            {
                var changed = false;
                foreach (var layerId in movingIds)
                {
                    var layer = doc.Layers.FindId(layerId);
                    if (layer == null || layer.ParentLayerId == parentId)
                        continue;

                    layer.ParentLayerId = parentId;
                    if (!doc.Layers.Modify(layer, layerId, true))
                        return changed;
                    changed = true;
                }

                if (!currentIndices.SequenceEqual(desiredIndices))
                {
                    doc.Layers.Sort(desiredIndices);
                    changed = true;
                }
                return changed;
            });
        }

        private static List<Layer> GetSortedActiveLayers(RhinoDoc doc)
        {
            var layers = new List<Layer>();
            var added = new HashSet<int>();
            var sortedIndices = doc.Layers.GetSorted();
            if (sortedIndices != null)
            {
                foreach (var index in sortedIndices)
                {
                    var layer = index >= 0 && index < doc.Layers.Count
                        ? doc.Layers[index]
                        : null;
                    if (layer != null && !layer.IsDeleted && added.Add(index))
                        layers.Add(layer);
                }
            }

            for (var index = 0; index < doc.Layers.Count; index++)
            {
                var layer = doc.Layers[index];
                if (layer != null && !layer.IsDeleted && added.Add(index))
                    layers.Add(layer);
            }
            return layers;
        }
    }
}
