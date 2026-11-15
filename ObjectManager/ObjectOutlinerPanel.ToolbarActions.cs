using System;
using System.Collections.Generic;
using System.Linq;
using Eto.Forms;
using Rhino;
using Rhino.DocObjects;
using ObjectManager.Outliner;

namespace ObjectManager
{
    public sealed partial class ObjectOutlinerPanel
    {
        private sealed class DeleteSelection
        {
            public IList<Layer> Layers { get; } = new List<Layer>();

            public IList<OutlinerGroupModel> Groups { get; } =
                new List<OutlinerGroupModel>();

            public IList<RhinoObject> Objects { get; } =
                new List<RhinoObject>();

            public bool HasTargets =>
                Layers.Count > 0 || Groups.Count > 0 || Objects.Count > 0;
        }

        private static readonly object LayerColorRandomSync = new object();
        private static readonly Random LayerColorRandom = new Random();
        private static readonly System.Drawing.Color[] RandomLayerColors =
        {
            System.Drawing.Color.FromArgb(222, 172, 46),
            System.Drawing.Color.FromArgb(32, 157, 146),
            System.Drawing.Color.FromArgb(45, 132, 197),
            System.Drawing.Color.FromArgb(53, 154, 91),
            System.Drawing.Color.FromArgb(224, 126, 44),
            System.Drawing.Color.FromArgb(213, 84, 73),
            System.Drawing.Color.FromArgb(103, 116, 130),
            System.Drawing.Color.FromArgb(173, 79, 146),
            System.Drawing.Color.FromArgb(64, 103, 187),
            System.Drawing.Color.FromArgb(185, 74, 74),
            System.Drawing.Color.FromArgb(218, 167, 34),
            System.Drawing.Color.FromArgb(105, 117, 91)
        };

        private void OnAddLayerAlternateMouseDown(
            object sender,
            MouseEventArgs e)
        {
            CreateLayerAtCurrentHierarchy(true);
        }

        private void OnAddSublayerAlternateMouseDown(
            object sender,
            MouseEventArgs e)
        {
            CreateSublayerFromSelection(true);
        }

        private void CreateLayerAtCurrentHierarchy(bool randomColor)
        {
            var doc = GetEditableDocument();
            if (doc == null)
                return;

            var anchor = ResolveNewLayerAnchor(doc);
            if (anchor == null)
                return;

            CreateLayer(
                doc,
                anchor.ParentLayerId,
                null,
                randomColor,
                OutlinerStrings.UndoNewLayer);
        }

        private Layer ResolveNewLayerAnchor(RhinoDoc doc)
        {
            if (doc == null)
                return null;

            var selectedLayers = GetSelectedEditableLayers(doc);
            if (selectedLayers.Count > 0)
            {
                var primary = selectedLayers.FirstOrDefault(
                    layer => layer.Id == _primaryLayerId);
                return primary ?? selectedLayers[0];
            }

            var current = doc.Layers.CurrentLayer;
            return current != null && !current.IsDeleted
                ? current
                : null;
        }

        private void CreateSiblingLayer(
            RhinoDoc doc,
            Guid siblingLayerId,
            bool randomColor)
        {
            var sibling = doc?.Layers.FindId(siblingLayerId);
            if (sibling == null || sibling.IsDeleted)
                return;

            CreateLayer(
                doc,
                sibling.ParentLayerId,
                null,
                randomColor,
                OutlinerStrings.UndoNewLayer);
        }

        private void CreateRootLayer(RhinoDoc doc, bool randomColor)
        {
            CreateLayer(
                doc,
                Guid.Empty,
                null,
                randomColor,
                OutlinerStrings.UndoNewLayer);
        }

        private void CreateSublayerFromSelection(bool randomColor)
        {
            var doc = GetEditableDocument();
            if (doc == null)
                return;

            var parents = GetSelectedEditableLayers(doc);
            if (parents.Count != 1)
                return;

            CreateSublayer(doc, parents[0].Id, randomColor);
        }

        private void CreateSublayer(
            RhinoDoc doc,
            Guid parentLayerId,
            bool randomColor)
        {
            var parent = doc?.Layers.FindId(parentLayerId);
            if (parent == null || parent.IsDeleted || parent.IsReference)
                return;

            CreateLayer(
                doc,
                parent.Id,
                parent.Color,
                randomColor,
                OutlinerStrings.UndoNewSublayer);
        }

        private void CreateLayer(
            RhinoDoc doc,
            Guid parentLayerId,
            System.Drawing.Color? inheritedColor,
            bool randomColor,
            string undoDescription)
        {
            if (doc == null)
                return;

            using (var diagnostic = OutlinerDiagnosticScope.Start("create-layer", doc.RuntimeSerialNumber))
            {
                ClearSearchForLayerCreation();
                var layer = Layer.GetDefaultLayerProperties();
                layer.ParentLayerId = parentLayerId;
                if (randomColor)
                    layer.Color = NextRandomLayerColor();
                else if (inheritedColor.HasValue)
                    layer.Color = inheritedColor.Value;

                diagnostic?.Checkpoint("prepare");
                var undoRecord = doc.BeginUndoRecord(undoDescription);
                int layerIndex;
                try
                {
                    layerIndex = doc.Layers.Add(layer);
                }
                finally
                {
                    if (undoRecord != 0)
                        doc.EndUndoRecord(undoRecord);
                }

                diagnostic?.Checkpoint("nativeLayerAndUndo");
                if (layerIndex >= 0)
                    PrepareNewLayer(doc, layerIndex, parentLayerId);
                diagnostic?.Checkpoint("prepareTreeAndEdit");
            }
        }

        private static System.Drawing.Color NextRandomLayerColor()
        {
            lock (LayerColorRandomSync)
            {
                return RandomLayerColors[
                    LayerColorRandom.Next(RandomLayerColors.Length)];
            }
        }

        private IList<Layer> GetSelectedEditableLayers(RhinoDoc doc)
        {
            return GetSelectedEditableLayers(doc, GetSelectedCanonicalNodes());
        }

        private static IList<Layer> GetSelectedEditableLayers(
            RhinoDoc doc,
            IEnumerable<OutlinerNodeReference> selectedNodes)
        {
            if (doc == null)
                return new List<Layer>();

            return (selectedNodes ?? Enumerable.Empty<OutlinerNodeReference>())
                .Where(node => node.Kind == OutlinerNodeKind.Layer)
                .Select(node => doc.Layers.FindId(node.Id))
                .Where(layer => layer != null &&
                    !layer.IsDeleted &&
                    !layer.IsReference)
                .GroupBy(layer => layer.Id)
                .Select(group => group.First())
                .ToList();
        }

        private IList<OutlinerNodeReference> GetSelectedCanonicalNodes()
        {
            if (_snapshot == null)
                return new List<OutlinerNodeReference>();

            return _tree.SelectedItems
                .OfType<TreeGridItem>()
                .Where(_snapshot.Contains)
                .Select(item => item.Tag as OutlinerNodeReference)
                .Where(node => node != null &&
                    (node.IsCanonical ||
                     node.Kind == OutlinerNodeKind.GroupSlice))
                .ToList();
        }

        private DeleteSelection ResolveDeleteSelection(RhinoDoc doc)
        {
            var result = new DeleteSelection();
            if (doc == null || _documentModel == null)
                return result;

            var nodes = GetSelectedCanonicalNodes();
            var objectIds = new HashSet<Guid>(nodes
                .Where(node => node.Kind == OutlinerNodeKind.Object)
                .Select(node => node.Id));
            var groupIds = new HashSet<Guid>(nodes
                .Where(node => node.Kind == OutlinerNodeKind.GroupSlice)
                .Select(node => node.Id));

            foreach (var groupId in groupIds)
            {
                if (!_documentModel.GroupsById.TryGetValue(
                    groupId,
                    out var group))
                {
                    continue;
                }

                result.Groups.Add(group);
                foreach (var memberId in group.MemberIds)
                    objectIds.Add(memberId);
            }

            foreach (var objectId in objectIds)
            {
                var rhinoObject = doc.Objects.FindId(objectId);
                if (rhinoObject != null &&
                    !rhinoObject.IsDeleted &&
                    !rhinoObject.IsReference)
                {
                    result.Objects.Add(rhinoObject);
                }
            }

            foreach (var layer in nodes
                .Where(node => node.Kind == OutlinerNodeKind.Layer)
                .Select(node => doc.Layers.FindId(node.Id))
                .Where(layer => layer != null &&
                    !layer.IsDeleted &&
                    !layer.IsReference &&
                    layer.Index != doc.Layers.CurrentLayerIndex)
                .GroupBy(layer => layer.Id)
                .Select(group => group.First())
                .OrderByDescending(layer => GetLayerDepth(doc, layer)))
            {
                result.Layers.Add(layer);
            }

            return result;
        }

        private void DeleteSelectedItems()
        {
            var doc = GetEditableDocument();
            var targets = ResolveDeleteSelection(doc);
            if (doc == null || !targets.HasTargets)
                return;

            var objectIds = new HashSet<Guid>(
                targets.Objects.Select(item => item.Id));
            var changed = ModifyDocument(
                doc,
                OutlinerStrings.UndoDeleteSelectedItems,
                () =>
                {
                    var operationChanged = false;
                    foreach (var rhinoObject in targets.Objects)
                    {
                        operationChanged |= doc.Objects.Delete(
                            rhinoObject,
                            true,
                            true);
                    }

                    foreach (var group in targets.Groups)
                    {
                        var allMembersDeleted = group.MemberIds.All(id =>
                        {
                            var member = doc.Objects.FindId(id);
                            return member == null || member.IsDeleted;
                        });
                        if (allMembersDeleted)
                            operationChanged |= doc.Groups.Delete(group.Index);
                    }

                    operationChanged |= DeleteLayersCore(
                        doc,
                        targets.Layers) > 0;
                    return operationChanged;
                });

            if (!changed)
                return;

            if (_deleteButton != null)
                _deleteButton.Enabled = false;
            _selectedObjectIds.ExceptWith(objectIds);
            _selectedLayerIds.ExceptWith(
                targets.Layers.Select(layer => layer.Id));
            if (targets.Layers.Any(layer => layer.Id == _primaryLayerId))
                _primaryLayerId = Guid.Empty;
        }

        private static int DeleteLayersCore(
            RhinoDoc doc,
            IEnumerable<Layer> layers)
        {
            if (doc == null)
                return 0;

            var indices = (layers ?? Enumerable.Empty<Layer>())
                .Where(layer => layer != null &&
                    !layer.IsDeleted &&
                    !layer.IsReference &&
                    layer.Index != doc.Layers.CurrentLayerIndex)
                .Select(layer => layer.Index)
                .Distinct()
                .ToList();
            return indices.Count == 0
                ? 0
                : doc.Layers.Delete(indices, false);
        }

        private void UpdateToolbarButtonState()
        {
            var doc = GetEditableDocument();
            var editable = doc != null;
            var selectedNodes = editable &&
                (_addSublayerButton != null || _deleteButton != null)
                    ? GetSelectedCanonicalNodes()
                    : new List<OutlinerNodeReference>();
            if (_addLayerButton != null)
                _addLayerButton.Enabled = editable;
            if (_addSublayerButton != null)
            {
                _addSublayerButton.Enabled = editable &&
                    GetSelectedEditableLayers(doc, selectedNodes).Count == 1;
            }
            if (_deleteButton != null)
            {
                _deleteButton.Enabled = editable &&
                    HasDeletableSelectedNode(doc, selectedNodes);
            }
        }

        private bool HasDeletableSelectedNode(
            RhinoDoc doc,
            IEnumerable<OutlinerNodeReference> selectedNodes)
        {
            if (doc == null || _documentModel == null)
                return false;

            foreach (var node in selectedNodes ??
                Enumerable.Empty<OutlinerNodeReference>())
            {
                if (node.Kind == OutlinerNodeKind.Object &&
                    IsEditableObject(doc, node.Id))
                {
                    return true;
                }

                if (node.Kind == OutlinerNodeKind.GroupSlice &&
                    _documentModel.GroupsById.TryGetValue(
                        node.Id,
                        out var group) &&
                    group.MemberIds.Count > 0)
                {
                    return true;
                }

                if (node.Kind != OutlinerNodeKind.Layer)
                    continue;
                var layer = doc.Layers.FindId(node.Id);
                if (layer != null &&
                    !layer.IsDeleted &&
                    !layer.IsReference &&
                    layer.Index != doc.Layers.CurrentLayerIndex)
                {
                    return true;
                }
            }

            return false;
        }

        private void GroupSelectedObjectNodes(
            RhinoDoc doc,
            IEnumerable<OutlinerNodeReference> nodes)
        {
            var objectIds = (nodes ?? Enumerable.Empty<OutlinerNodeReference>())
                .Where(node => node != null &&
                    node.Kind == OutlinerNodeKind.Object &&
                    node.IsCanonical &&
                    IsEditableObject(doc, node.Id))
                .Select(node => node.Id)
                .Distinct()
                .ToList();
            if (objectIds.Count < 2)
                return;

            var groupName = GetUniqueDefaultGroupName(doc);
            ModifyDocument(
                doc,
                OutlinerStrings.UndoGroupSelectedObjects,
                () => doc.Groups.Add(groupName, objectIds) >= 0);
        }

        private static string GetUniqueDefaultGroupName(RhinoDoc doc)
        {
            for (var number = 1; ; number++)
            {
                var candidate = OutlinerStrings.FormatDefaultGroupName(number);
                if (doc.Groups.FindName(candidate) == null)
                    return candidate;
            }
        }

        private void UngroupContextGroups(
            RhinoDoc doc,
            IEnumerable<Guid> groupIds)
        {
            if (doc == null || _documentModel == null)
                return;

            var groups = (groupIds ?? Enumerable.Empty<Guid>())
                .Distinct()
                .Select(id => _documentModel.GroupsById.TryGetValue(
                    id,
                    out var group) ? group : null)
                .Where(group => group != null)
                .ToList();
            if (groups.Count == 0)
                return;

            var removals = new Dictionary<Guid, IList<int>>();
            foreach (var group in groups)
            {
                foreach (var memberId in group.MemberIds)
                {
                    if (!removals.TryGetValue(memberId, out var groupIndices))
                    {
                        groupIndices = new List<int>();
                        removals[memberId] = groupIndices;
                    }
                    if (!groupIndices.Contains(group.Index))
                        groupIndices.Add(group.Index);
                }
            }

            RemoveObjectsFromGroups(
                doc,
                removals,
                OutlinerStrings.UndoUngroup);
        }
    }
}
