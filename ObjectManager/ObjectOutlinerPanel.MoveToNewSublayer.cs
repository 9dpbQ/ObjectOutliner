using System;
using System.Collections.Generic;
using System.Linq;
using Rhino;
using Rhino.DocObjects;
using ObjectManager.Outliner;

namespace ObjectManager
{
    public sealed partial class ObjectOutlinerPanel
    {
        private void ShowMoveToNewSublayer(
            RhinoDoc doc,
            OutlinerNodeReference clickedNode,
            IEnumerable<OutlinerNodeReference> nodes)
        {
            if (doc == null || clickedNode == null)
                return;

            var clickedObject = doc.Objects.FindId(clickedNode.Id);
            if (clickedObject == null ||
                clickedObject.IsDeleted ||
                clickedObject.IsReference)
            {
                return;
            }

            var parentLayer = doc.Layers[clickedObject.Attributes.LayerIndex];
            if (parentLayer == null ||
                parentLayer.IsDeleted ||
                parentLayer.IsReference)
            {
                return;
            }

            var movingIds = GetObjectIdsInVisibleOrder(doc, nodes);
            if (movingIds.Count == 0)
                return;

            MoveObjectsToNewSublayer(doc, parentLayer, movingIds);
        }

        private IList<Guid> GetObjectIdsInVisibleOrder(
            RhinoDoc doc,
            IEnumerable<OutlinerNodeReference> nodes)
        {
            if (doc == null)
                return new List<Guid>();

            var requested = new HashSet<Guid>((nodes ??
                    Enumerable.Empty<OutlinerNodeReference>())
                .Where(node => node != null &&
                    node.Kind == OutlinerNodeKind.Object &&
                    node.IsCanonical &&
                    IsEditableObject(doc, node.Id))
                .Select(node => node.Id));
            var result = _visibleRows
                .Select(item => item.Tag as OutlinerNodeReference)
                .Where(node => node != null &&
                    node.Kind == OutlinerNodeKind.Object &&
                    requested.Remove(node.Id))
                .Select(node => node.Id)
                .ToList();
            result.AddRange(requested.OrderBy(id => id));
            return result;
        }

        private void MoveObjectsToNewSublayer(
            RhinoDoc doc,
            Layer parentLayer,
            IList<Guid> movingIds)
        {
            if (doc == null ||
                parentLayer == null ||
                movingIds == null ||
                movingIds.Count == 0)
            {
                return;
            }

            Guid createdLayerId = Guid.Empty;
            var createdLayerIndex = -1;
            ClearSearchForLayerCreation();
            var changed = ModifyDocument(
                doc,
                OutlinerStrings.UndoMoveToNewSublayer,
                () =>
                {
                    var layer = Layer.GetDefaultLayerProperties();
                    layer.ParentLayerId = parentLayer.Id;
                    layer.Color = parentLayer.Color;
                    createdLayerIndex = doc.Layers.Add(layer);
                    if (createdLayerIndex < 0)
                        return false;

                    var createdLayer = doc.Layers[createdLayerIndex];
                    createdLayerId = createdLayer?.Id ?? Guid.Empty;
                    var movingSet = new HashSet<Guid>(movingIds);
                    var affectedLayerIndices = movingIds
                        .Select(doc.Objects.FindId)
                        .Where(item => item != null)
                        .Select(item => item.Attributes.LayerIndex)
                        .Distinct()
                        .ToList();
                    var desiredByLayer = affectedLayerIndices.ToDictionary(
                        index => index,
                        index => GetOrderedObjectIds(doc, index)
                            .Where(id => !movingSet.Contains(id))
                            .ToList());
                    desiredByLayer[createdLayerIndex] = movingIds.ToList();
                    ApplyObjectLayerOrders(doc, desiredByLayer);
                    RestoreObjectSelection(doc, movingIds);
                    return true;
                },
                false);
            if (!changed || createdLayerId == Guid.Empty)
                return;

            PrepareNewLayer(doc, createdLayerIndex, parentLayer.Id);
            _viewState.TemporarilyRevealedLayerIds.Add(createdLayerId);
            CaptureWorkspaceState();
            QueueTreeRefresh();
        }
    }
}
