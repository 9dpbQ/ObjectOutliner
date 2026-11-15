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
        private sealed class ObjectCopySpec
        {
            public Guid SourceId { get; set; }

            public int TargetLayerIndex { get; set; }

            public int Order { get; set; }
        }

        private void PopulateStandardLayerContextMenu(
            ContextMenu menu,
            OutlinerContextTarget target,
            RhinoDoc doc,
            bool editable)
        {
            var layerIds = target.Nodes
                .Select(node => node.Id)
                .Distinct()
                .ToList();
            var editableLayerIds = editable
                ? layerIds.Where(id => IsEditableLayer(doc, id)).ToList()
                : new List<Guid>();
            var clickedLayer = doc?.Layers.FindId(target.ClickedNode.Id);
            var directObjectIds = editable
                ? GetDirectLayerObjectIds(layerIds)
                : new List<Guid>();
            var treeObjectIds = editable
                ? GetLayerTreeObjectIds(doc, layerIds)
                : new List<Guid>();
            var selectedObjectIds = editable
                ? GetEditableSelectedObjectIds(doc)
                : new List<Guid>();

            menu.Items.Add(BuildLayerPropertiesMenu(
                doc,
                target.ClickedNode.Id,
                editableLayerIds,
                editable));
            AddMenuItem(
                menu,
                OutlinerStrings.SelectObjectsOnThisLayer,
                editable && GetSelectableObjectIds(doc, directObjectIds).Count > 0,
                () => SelectObjects(doc, directObjectIds));
            AddMenuItem(
                menu,
                OutlinerStrings.SelectObjectsInLayerTree,
                editable && GetSelectableObjectIds(doc, treeObjectIds).Count > 0,
                () => SelectObjects(doc, treeObjectIds));
            AddMenuItem(
                menu,
                OutlinerStrings.MoveSelectedObjectsToThisLayer,
                editable && clickedLayer != null && !clickedLayer.IsReference &&
                    selectedObjectIds.Count > 0,
                () => MoveSelectedObjectsToLayer(doc, clickedLayer.Id));
            AddMenuItem(
                menu,
                OutlinerStrings.CopySelectedObjectsToThisLayer,
                editable && clickedLayer != null && !clickedLayer.IsReference &&
                    selectedObjectIds.Count > 0,
                () => CopySelectedObjectsToLayer(doc, clickedLayer.Id));

            menu.Items.Add(new SeparatorMenuItem());
            AddMenuItem(
                menu,
                OutlinerStrings.NewLayer,
                editable,
                () => CreateSiblingLayer(
                    doc,
                    target.ClickedNode.Id,
                    false));
            AddMenuItem(
                menu,
                OutlinerStrings.NewSublayer,
                editable && IsEditableLayer(doc, target.ClickedNode.Id),
                () => CreateContextSublayer(doc, target.ClickedNode.Id));
            AddMenuItem(
                menu,
                OutlinerStrings.DuplicateLayer,
                editableLayerIds.Count > 0,
                () => DuplicateContextLayers(doc, editableLayerIds, false));
            AddMenuItem(
                menu,
                OutlinerStrings.DuplicateLayerAndObjects,
                editableLayerIds.Count > 0,
                () => DuplicateContextLayers(doc, editableLayerIds, true));

            menu.Items.Add(new SeparatorMenuItem());
            AddMenuItem(
                menu,
                OutlinerStrings.OneLayerOn,
                editable && IsEditableLayer(doc, target.ClickedNode.Id),
                () => OneLayerOn(doc, target.ClickedNode.Id));
            AddMenuItem(
                menu,
                OutlinerStrings.MatchProperties,
                editableLayerIds.Count > 0 && doc.Layers.ActiveCount > 1,
                () => MatchLayerProperties(doc, editableLayerIds));

            menu.Items.Add(new SeparatorMenuItem());
            AddMenuItem(
                menu,
                OutlinerStrings.Delete,
                editableLayerIds.Any(id =>
                {
                    var layer = doc.Layers.FindId(id);
                    return layer != null && layer.Index != doc.Layers.CurrentLayerIndex;
                }),
                () => DeleteContextLayers(doc, editableLayerIds));
            AddMenuItem(
                menu,
                OutlinerStrings.Rename,
                editable && IsEditableLayer(doc, target.ClickedNode.Id),
                () => BeginNameEdit(target.ClickedItem));
            AddMenuItem(
                menu,
                OutlinerStrings.BatchRename,
                editable && editableLayerIds.Count > 1,
                () => ShowBatchRename(doc, target.Nodes));

            menu.Items.Add(new SeparatorMenuItem());
            var hasExpandableContents = layerIds.Any(id =>
                _snapshot != null &&
                _snapshot.LayerItems.TryGetValue(id, out var item) &&
                item.Children.Count > 0);
            AddMenuItem(
                menu,
                OutlinerStrings.ExpandChildren,
                editable && hasExpandableContents,
                () => SetLayerContentsExpanded(layerIds, true));
            AddMenuItem(
                menu,
                OutlinerStrings.CollapseChildren,
                editable && hasExpandableContents,
                () => SetLayerContentsExpanded(layerIds, false));

            menu.Items.Add(new SeparatorMenuItem());
            AddMenuItem(
                menu,
                OutlinerStrings.ZoomObjects,
                editable && treeObjectIds.Count > 0,
                () => ZoomToObjects(doc, treeObjectIds));
            AddIsolationMenuItems(menu, target, editable);
        }

        private ButtonMenuItem BuildLayerPropertiesMenu(
            RhinoDoc doc,
            Guid clickedLayerId,
            IList<Guid> targetLayerIds,
            bool editable)
        {
            var clickedLayer = doc?.Layers.FindId(clickedLayerId);
            var menu = new ButtonMenuItem
            {
                Text = OutlinerStrings.Properties,
                Enabled = editable && clickedLayer != null && targetLayerIds.Count > 0
            };
            if (!menu.Enabled)
                return menu;

            var mutableStateLayerIds = targetLayerIds
                .Where(id =>
                {
                    var layer = doc.Layers.FindId(id);
                    return layer != null &&
                        layer.Index != doc.Layers.CurrentLayerIndex;
                })
                .ToList();
            var inheritedHidden = HasMatchingAncestor(
                doc,
                clickedLayer,
                parent => !parent.IsVisible);
            var ownVisibility = inheritedHidden
                ? clickedLayer.PersistentVisibility
                : clickedLayer.IsVisible;
            var inheritedLocked = HasMatchingAncestor(
                doc,
                clickedLayer,
                parent => parent.IsLocked);
            var ownLocking = inheritedLocked
                ? clickedLayer.GetPersistentLocking()
                : clickedLayer.IsLocked;
            AddCheckSubMenuItem(
                menu,
                OutlinerStrings.On,
                ownVisibility,
                mutableStateLayerIds.Count > 0,
                value => SetLayerVisibility(doc, mutableStateLayerIds, value));
            AddCheckSubMenuItem(
                menu,
                OutlinerStrings.Lock,
                ownLocking,
                mutableStateLayerIds.Count > 0,
                value => SetLayerLocking(doc, mutableStateLayerIds, value));
            AddSubMenuItem(
                menu,
                OutlinerStrings.ColorDialog,
                true,
                () => ChooseLayerColorForTargets(
                    doc,
                    clickedLayerId,
                    targetLayerIds,
                    false));
            AddSubMenuItem(
                menu,
                OutlinerStrings.MaterialDialog,
                true,
                () => ChooseLayerMaterialForTargets(doc, targetLayerIds));
            AddSubMenuItem(
                menu,
                OutlinerStrings.LinetypeDialog,
                true,
                () => ChooseLayerLinetypeForTargets(
                    doc,
                    clickedLayerId,
                    targetLayerIds));
            AddSubMenuItem(
                menu,
                OutlinerStrings.PrintColorDialog,
                true,
                () => ChooseLayerColorForTargets(
                    doc,
                    clickedLayerId,
                    targetLayerIds,
                    true));
            AddSubMenuItem(
                menu,
                OutlinerStrings.PrintWidthDialog,
                true,
                () => ChooseLayerPlotWeightForTargets(
                    doc,
                    clickedLayerId,
                    targetLayerIds));
            AddSubMenuItem(
                menu,
                OutlinerStrings.SectionStyleDialog,
                true,
                () => ToggleLayerSectionStyleForTargets(
                    doc,
                    clickedLayerId,
                    targetLayerIds));
            AddCheckSubMenuItem(
                menu,
                OutlinerStrings.NewDetailOn,
                clickedLayer.PerViewportIsVisibleInNewDetails,
                true,
                value => SetLayerBooleanProperty(
                    doc,
                    targetLayerIds,
                    OutlinerStrings.UndoChangeNewDetailVisibility,
                    layer => layer.PerViewportIsVisibleInNewDetails = value));
            AddCheckSubMenuItem(
                menu,
                OutlinerStrings.ModelOn,
                clickedLayer.ModelIsVisible,
                true,
                value => SetLayerBooleanProperty(
                    doc,
                    targetLayerIds,
                    OutlinerStrings.UndoChangeModelVisibility,
                    layer => layer.ModelIsVisible = value));
            return menu;
        }

        private static void AddSubMenuItem(
            ButtonMenuItem parent,
            string text,
            bool enabled,
            Action action)
        {
            var item = new ButtonMenuItem
            {
                Text = text,
                Enabled = enabled
            };
            if (enabled && action != null)
                item.Click += (sender, e) => action();
            parent.Items.Add(item);
        }

        private static void AddCheckSubMenuItem(
            ButtonMenuItem parent,
            string text,
            bool isChecked,
            bool enabled,
            Action<bool> action)
        {
            var item = new CheckMenuItem
            {
                Text = text,
                Checked = isChecked,
                Enabled = enabled
            };
            if (enabled && action != null)
            {
                item.CheckedChanged += (sender, e) => action(item.Checked);
            }
            parent.Items.Add(item);
        }

        private List<Guid> GetDirectLayerObjectIds(IEnumerable<Guid> layerIds)
        {
            if (_documentModel == null)
                return new List<Guid>();

            var layerIdSet = new HashSet<Guid>(
                layerIds ?? Enumerable.Empty<Guid>());
            return _documentModel.Objects
                .Where(item => layerIdSet.Contains(item.LayerId))
                .Select(item => item.Id)
                .Distinct()
                .ToList();
        }

        private static List<Guid> GetEditableSelectedObjectIds(RhinoDoc doc)
        {
            if (doc == null)
                return new List<Guid>();

            return doc.Objects.GetSelectedObjects(false, false)
                .Where(item => item != null &&
                    !item.IsDeleted &&
                    !item.IsReference)
                .Select(item => item.Id)
                .Distinct()
                .ToList();
        }

        private void SetLayerVisibility(
            RhinoDoc doc,
            IEnumerable<Guid> layerIds,
            bool visible)
        {
            var targetLayerIds = layerIds
                .Where(id => id != Guid.Empty)
                .Distinct()
                .ToList();
            if (targetLayerIds.Count == 0)
                return;

            ModifyStateDocument(
                doc,
                OutlinerStrings.UndoChangeLayerVisibility,
                new OutlinerStateChange().AddLayers(targetLayerIds),
                () =>
            {
                var changed = false;
                foreach (var layerId in targetLayerIds)
                {
                    var layer = doc.Layers.FindId(layerId);
                    if (layer == null ||
                        layer.IsReference ||
                        layer.Index == doc.Layers.CurrentLayerIndex)
                        continue;

                    var inheritedHidden = HasMatchingAncestor(
                        doc,
                        layer,
                        parent => !parent.IsVisible);
                    if (inheritedHidden)
                    {
                        if (layer.PersistentVisibility == visible)
                            continue;
                        layer.SetPersistentVisibility(visible);
                    }
                    else
                    {
                        if (layer.IsVisible == visible &&
                            layer.PersistentVisibility == visible)
                            continue;
                        layer.IsVisible = visible;
                        layer.SetPersistentVisibility(visible);
                    }
                    changed |= doc.Layers.Modify(layer, layer.Id, true);
                }
                return changed;
            });
        }

        private void SetLayerLocking(
            RhinoDoc doc,
            IEnumerable<Guid> layerIds,
            bool locked)
        {
            var targetLayerIds = layerIds
                .Where(id => id != Guid.Empty)
                .Distinct()
                .ToList();
            if (targetLayerIds.Count == 0)
                return;

            ModifyStateDocument(
                doc,
                OutlinerStrings.UndoChangeLayerLock,
                new OutlinerStateChange().AddLayers(targetLayerIds),
                () =>
            {
                var changed = false;
                foreach (var layerId in targetLayerIds)
                {
                    var layer = doc.Layers.FindId(layerId);
                    if (layer == null ||
                        layer.IsReference ||
                        layer.Index == doc.Layers.CurrentLayerIndex)
                        continue;

                    var inheritedLocked = HasMatchingAncestor(
                        doc,
                        layer,
                        parent => parent.IsLocked);
                    if (!inheritedLocked && locked)
                        changed |= SynchronizeDescendantVisibilityIntent(doc, layer.Id);

                    if (inheritedLocked)
                    {
                        if (layer.GetPersistentLocking() == locked)
                            continue;
                        layer.SetPersistentLocking(locked);
                    }
                    else
                    {
                        if (layer.IsLocked == locked)
                            continue;
                        layer.IsLocked = locked;
                    }
                    changed |= doc.Layers.Modify(layer, layer.Id, true);
                }
                return changed;
            });
        }

        private void ChooseLayerColorForTargets(
            RhinoDoc doc,
            Guid seedLayerId,
            IEnumerable<Guid> layerIds,
            bool plotColor)
        {
            var seed = doc?.Layers.FindId(seedLayerId);
            if (seed == null)
                return;

            var color = plotColor ? seed.PlotColor : seed.Color;
            if (!Rhino.UI.Dialogs.ShowColorDialog(ref color))
                return;

            ModifyLayers(
                doc,
                layerIds,
                plotColor
                    ? OutlinerStrings.UndoChangeLayerPrintColor
                    : OutlinerStrings.UndoChangeLayerColor,
                layer =>
                {
                    if (plotColor)
                        layer.PlotColor = color;
                    else
                        layer.Color = color;
                });
        }

        private void ChooseLayerMaterialForTargets(
            RhinoDoc doc,
            IEnumerable<Guid> layerIds)
        {
            var indices = layerIds
                .Select(doc.Layers.FindId)
                .Where(layer => layer != null && !layer.IsDeleted && !layer.IsReference)
                .Select(layer => layer.Index)
                .Distinct()
                .ToList();
            if (indices.Count > 0 &&
                Rhino.UI.Dialogs.ShowLayerMaterialDialog(doc, indices))
                QueueTreeRefresh();
        }

        private void ChooseLayerLinetypeForTargets(
            RhinoDoc doc,
            Guid seedLayerId,
            IEnumerable<Guid> layerIds)
        {
            var seed = doc?.Layers.FindId(seedLayerId);
            if (seed == null)
                return;

            var index = seed.LinetypeIndex;
            if (!Rhino.UI.Dialogs.ShowSelectLinetypeDialog(ref index, false))
                return;
            ModifyLayers(
                doc,
                layerIds,
                OutlinerStrings.UndoChangeLayerLinetype,
                layer => layer.LinetypeIndex = index);
        }

        private void ChooseLayerPlotWeightForTargets(
            RhinoDoc doc,
            Guid seedLayerId,
            IEnumerable<Guid> layerIds)
        {
            var seed = doc?.Layers.FindId(seedLayerId);
            if (seed == null)
                return;

            var plotWeight = Rhino.UI.Dialogs.ShowPrintWidths(
                OutlinerStrings.PrintWidth,
                OutlinerStrings.PrintWidthPrompt,
                seed.PlotWeight);
            if (!RhinoMath.IsValidDouble(plotWeight) || plotWeight < -1)
                return;
            ModifyLayers(
                doc,
                layerIds,
                OutlinerStrings.UndoChangeLayerPrintWidth,
                layer => layer.PlotWeight = plotWeight);
        }

        private void ToggleLayerSectionStyleForTargets(
            RhinoDoc doc,
            Guid seedLayerId,
            IEnumerable<Guid> layerIds)
        {
            var seed = doc?.Layers.FindId(seedLayerId);
            if (seed == null)
                return;

            var useCustom = seed.GetCustomSectionStyle() == null;
            ModifyLayers(
                doc,
                layerIds,
                OutlinerStrings.UndoChangeLayerSectionStyle,
                layer =>
                {
                    if (useCustom)
                        layer.SetCustomSectionStyle(new SectionStyle());
                    else
                        layer.RemoveCustomSectionStyle();
                });
        }

        private void SetLayerBooleanProperty(
            RhinoDoc doc,
            IEnumerable<Guid> layerIds,
            string undoDescription,
            Action<Layer> update)
        {
            ModifyLayers(doc, layerIds, undoDescription, update);
        }

        private bool ModifyLayers(
            RhinoDoc doc,
            IEnumerable<Guid> layerIds,
            string undoDescription,
            Action<Layer> update)
        {
            var targetLayerIds = (layerIds ?? Enumerable.Empty<Guid>())
                .Where(id => id != Guid.Empty)
                .Distinct()
                .ToList();
            if (targetLayerIds.Count == 0)
                return false;

            return ModifyStateDocument(
                doc,
                undoDescription,
                new OutlinerStateChange().AddLayerProperties(targetLayerIds),
                () =>
            {
                var changed = false;
                foreach (var layerId in targetLayerIds)
                {
                    var layer = doc.Layers.FindId(layerId);
                    if (layer == null || layer.IsDeleted || layer.IsReference)
                        continue;
                    update(layer);
                    changed |= doc.Layers.Modify(layer, layer.Id, true);
                }
                return changed;
            });
        }

        private void MoveSelectedObjectsToLayer(RhinoDoc doc, Guid targetLayerId)
        {
            var targetLayer = doc?.Layers.FindId(targetLayerId);
            var movingIds = GetEditableSelectedObjectIds(doc);
            if (targetLayer == null || targetLayer.IsReference || movingIds.Count == 0)
                return;

            var movingSet = new HashSet<Guid>(movingIds);
            var affectedLayerIndices = movingIds
                .Select(doc.Objects.FindId)
                .Where(item => item != null)
                .Select(item => item.Attributes.LayerIndex)
                .Concat(new[] { targetLayer.Index })
                .Distinct()
                .ToList();
            var desiredByLayer = affectedLayerIndices.ToDictionary(
                index => index,
                index => GetOrderedObjectIds(doc, index)
                    .Where(id => !movingSet.Contains(id))
                    .ToList());
            desiredByLayer[targetLayer.Index].AddRange(movingIds);

            ModifyDocument(doc, OutlinerStrings.UndoChangeObjectLayer, () =>
            {
                var changed = ApplyObjectLayerOrders(doc, desiredByLayer);
                if (changed)
                    RestoreObjectSelection(doc, movingIds);
                return changed;
            }, false);
        }

        private void CopySelectedObjectsToLayer(RhinoDoc doc, Guid targetLayerId)
        {
            var targetLayer = doc?.Layers.FindId(targetLayerId);
            var sourceIds = GetEditableSelectedObjectIds(doc);
            if (targetLayer == null || targetLayer.IsReference || sourceIds.Count == 0)
                return;

            var copiedIds = new List<Guid>();
            ModifyDocument(doc, OutlinerStrings.UndoCopyObjectsToLayer, () =>
            {
                var targetOrder = GetOrderedObjectIds(doc, targetLayer.Index);
                var nextOrder = targetOrder.Count;
                var specs = sourceIds.Select((sourceId, offset) =>
                    new ObjectCopySpec
                    {
                        SourceId = sourceId,
                        TargetLayerIndex = targetLayer.Index,
                        Order = nextOrder + offset
                    });
                copiedIds.AddRange(DuplicateObjectSet(doc, specs));
                if (copiedIds.Count == 0)
                    return false;
                targetOrder.AddRange(copiedIds);
                ApplyObjectLayerOrders(
                    doc,
                    new Dictionary<int, List<Guid>>
                    {
                        { targetLayer.Index, targetOrder }
                    });
                RestoreObjectSelection(doc, copiedIds);
                return true;
            });
        }

        private static bool ApplyObjectLayerOrders(
            RhinoDoc doc,
            IDictionary<int, List<Guid>> desiredByLayer)
        {
            var changed = false;
            foreach (var pair in desiredByLayer)
            {
                for (var order = 0; order < pair.Value.Count; order++)
                {
                    var rhinoObject = doc.Objects.FindId(pair.Value[order]);
                    if (rhinoObject == null || rhinoObject.IsReference)
                        continue;

                    int currentOrder;
                    var hasOrder = ObjectOrderStore.TryGetOrder(
                        rhinoObject.Attributes,
                        out currentOrder);
                    if (rhinoObject.Attributes.LayerIndex == pair.Key &&
                        hasOrder &&
                        currentOrder == order)
                        continue;

                    var attributes = rhinoObject.Attributes.Duplicate();
                    attributes.LayerIndex = pair.Key;
                    ObjectOrderStore.SetOrder(attributes, order);
                    changed |= doc.Objects.ModifyAttributes(
                        rhinoObject.Id,
                        attributes,
                        true);
                }
            }
            return changed;
        }

        private void RestoreObjectSelection(
            RhinoDoc doc,
            IEnumerable<Guid> objectIds)
        {
            ApplyDocumentSelection(doc, objectIds);
        }

        private void DuplicateContextLayers(
            RhinoDoc doc,
            IEnumerable<Guid> layerIds,
            bool includeObjects)
        {
            var sourceLayers = layerIds
                .Select(doc.Layers.FindId)
                .Where(layer => layer != null && !layer.IsDeleted && !layer.IsReference)
                .OrderBy(layer => GetLayerDepth(doc, layer))
                .ThenBy(layer => layer.SortIndex)
                .ToList();
            if (sourceLayers.Count == 0)
                return;

            var copiedLayerIds = new List<Guid>();
            var copiedLayerBySourceId = new Dictionary<Guid, Guid>();
            var changed = ModifyDocument(
                doc,
                includeObjects
                    ? OutlinerStrings.UndoDuplicateLayersAndObjects
                    : OutlinerStrings.UndoDuplicateLayers,
                () =>
                {
                    foreach (var source in sourceLayers)
                    {
                        var parentId = copiedLayerBySourceId.TryGetValue(
                            source.ParentLayerId,
                            out var copiedParentId)
                            ? copiedParentId
                            : source.ParentLayerId;
                        var copy = CreateLayerCopy(doc, source, parentId);
                        var copyIndex = doc.Layers.Add(copy);
                        if (copyIndex < 0)
                            continue;

                        var copiedLayer = doc.Layers[copyIndex];
                        copiedLayerBySourceId[source.Id] = copiedLayer.Id;
                        copiedLayerIds.Add(copiedLayer.Id);
                    }
                    if (includeObjects)
                        DuplicateObjectsForLayerCopies(
                            doc,
                            sourceLayers,
                            copiedLayerBySourceId);
                    return copiedLayerIds.Count > 0;
                });
            if (!changed || copiedLayerIds.Count == 0)
                return;

            _selectedLayerIds.Clear();
            foreach (var copiedId in copiedLayerIds)
                _selectedLayerIds.Add(copiedId);
            _primaryLayerId = copiedLayerIds[0];
            _pendingEditLayerId = copiedLayerIds.Count == 1
                ? copiedLayerIds[0]
                : Guid.Empty;
            QueueTreeRefresh();
        }

        private static Layer CreateLayerCopy(
            RhinoDoc doc,
            Layer source,
            Guid parentId)
        {
            var copy = new Layer();
            copy.CopyAttributesFrom(source);
            copy.ParentLayerId = parentId;
            copy.Name = GetDuplicateLayerName(doc, parentId, source.Name);
            copy.IsVisible = source.IsVisible;
            copy.IsLocked = source.IsLocked;
            copy.SetPersistentVisibility(source.PersistentVisibility);
            copy.SetPersistentLocking(source.GetPersistentLocking());
            copy.PerViewportIsVisibleInNewDetails =
                source.PerViewportIsVisibleInNewDetails;
            copy.ModelIsVisible = source.ModelIsVisible;

            var sectionStyle = source.GetCustomSectionStyle();
            if (sectionStyle != null)
                copy.SetCustomSectionStyle(new SectionStyle(sectionStyle));
            var userStrings = source.GetUserStrings();
            if (userStrings != null)
            {
                foreach (var key in userStrings.AllKeys)
                {
                    if (!string.IsNullOrEmpty(key))
                        copy.SetUserString(key, userStrings[key]);
                }
            }
            return copy;
        }

        private static string GetDuplicateLayerName(
            RhinoDoc doc,
            Guid parentId,
            string sourceName)
        {
            var nameExists = GetSortedActiveLayers(doc).Any(layer =>
                layer.ParentLayerId == parentId &&
                string.Equals(
                    layer.Name,
                    sourceName,
                    StringComparison.OrdinalIgnoreCase));
            return nameExists
                ? GetUniqueSiblingLayerName(
                    doc,
                    parentId,
                    OutlinerStrings.AppendCopySuffix(
                        ObjectNamingService.GetCopySeriesBase(sourceName)))
                : sourceName;
        }

        private static string GetUniqueSiblingLayerName(
            RhinoDoc doc,
            Guid parentId,
            string requestedName)
        {
            var usedNames = new HashSet<string>(
                GetSortedActiveLayers(doc)
                    .Where(layer => layer.ParentLayerId == parentId)
                    .Select(layer => layer.Name),
                StringComparer.OrdinalIgnoreCase);
            if (!usedNames.Contains(requestedName))
                return requestedName;

            var number = 1;
            string candidate;
            do
            {
                candidate = string.Format("{0}.{1:000}", requestedName, number++);
            }
            while (usedNames.Contains(candidate));
            return candidate;
        }

        private static void DuplicateObjectsForLayerCopies(
            RhinoDoc doc,
            IEnumerable<Layer> sourceLayers,
            IDictionary<Guid, Guid> copiedLayerBySourceId)
        {
            var specs = new List<ObjectCopySpec>();
            foreach (var source in sourceLayers)
            {
                if (!copiedLayerBySourceId.TryGetValue(
                    source.Id,
                    out var copiedLayerId))
                    continue;
                var target = doc.Layers.FindId(copiedLayerId);
                if (target == null)
                    continue;

                var sourceObjectIds = GetOrderedObjectIds(doc, source.Index);
                for (var order = 0; order < sourceObjectIds.Count; order++)
                {
                    specs.Add(new ObjectCopySpec
                    {
                        SourceId = sourceObjectIds[order],
                        TargetLayerIndex = target.Index,
                        Order = order
                    });
                }
            }
            DuplicateObjectSet(doc, specs);
        }

        private static List<Guid> DuplicateObjectSet(
            RhinoDoc doc,
            IEnumerable<ObjectCopySpec> specs)
        {
            var copiedIds = new List<Guid>();
            var reservedCopyNames = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            GenericObjectNameAllocator genericNameAllocator = null;
            var copiedMembersByGroupIndex = new Dictionary<int, List<Guid>>();
            var sourceGroupNames = new Dictionary<int, string>();
            var sourceGroupHasUserName = new Dictionary<int, bool>();
            foreach (var spec in specs)
            {
                var source = doc.Objects.FindId(spec.SourceId);
                if (source == null || source.IsDeleted || source.IsReference)
                    continue;

                var sourceGroupIndices = source.Attributes.GetGroupList() ?? new int[0];
                foreach (var groupIndex in sourceGroupIndices)
                {
                    if (!sourceGroupNames.ContainsKey(groupIndex))
                    {
                        var group = doc.Groups.FindIndex(groupIndex);
                        var nativeName = doc.Groups.GroupName(groupIndex);
                        sourceGroupHasUserName[groupIndex] =
                            !string.IsNullOrWhiteSpace(nativeName);
                        sourceGroupNames[groupIndex] =
                            ObjectNamingService.ResolveGroupDisplayName(
                                doc,
                                group,
                                OutlinerStrings.FormatDefaultGroupName(
                                    groupIndex + 1));
                    }
                }

                var copiedId = doc.Objects.Duplicate(source);
                if (copiedId == Guid.Empty)
                    continue;
                var copiedObject = doc.Objects.FindId(copiedId);
                if (copiedObject == null)
                    continue;

                var attributes = copiedObject.Attributes.Duplicate();
                attributes.RemoveFromAllGroups();
                attributes.LayerIndex = spec.TargetLayerIndex;
                ObjectOrderStore.SetOrder(attributes, spec.Order);
                if (!doc.Objects.ModifyAttributes(copiedId, attributes, true))
                {
                    doc.Objects.Delete(copiedId, true);
                    continue;
                }

                var useGenericName =
                    ObjectNamingService.UsesGenericCopyNaming(source);
                var sourceName = useGenericName
                    ? ObjectNamingService.GetGenericNameBase(doc, source)
                    : ObjectNamingService.GetCopySourceBase(doc, source);
                var copyName = useGenericName
                    ? (genericNameAllocator ??
                        (genericNameAllocator =
                            ObjectNamingService.CreateGenericNameAllocator(
                                doc,
                                new[] { copiedId },
                                reservedCopyNames)))
                        .Next(sourceName)
                    : ObjectNamingService.NextCopyName(
                        doc,
                        sourceName,
                        copiedId,
                        reservedCopyNames);
                var lineageRoot = string.Empty;
                if (!useGenericName)
                {
                    ObjectNamingService.TryGetInheritableRoot(
                        source,
                        out lineageRoot);
                }
                if (ObjectNamingService.TryApplyManagedName(
                    doc,
                    copiedId,
                    copyName,
                    "Copy",
                    0,
                    useGenericName
                        ? ObjectNamingService.GenericCopyRole
                        : string.Empty,
                    false,
                    true,
                    lineageRoot))
                {
                    reservedCopyNames.Add(copyName);
                }

                copiedIds.Add(copiedId);
                foreach (var groupIndex in sourceGroupIndices)
                {
                    if (!copiedMembersByGroupIndex.TryGetValue(
                        groupIndex,
                        out var members))
                    {
                        members = new List<Guid>();
                        copiedMembersByGroupIndex[groupIndex] = members;
                    }
                    members.Add(copiedId);
                }
            }

            foreach (var pair in copiedMembersByGroupIndex)
            {
                if (pair.Value.Count == 0)
                    continue;
                var sourceName = sourceGroupNames.TryGetValue(pair.Key, out var name)
                    ? name
                    : string.Empty;
                if (string.IsNullOrWhiteSpace(sourceName))
                    sourceName = OutlinerStrings.FormatDefaultGroupName(
                        pair.Key + 1);
                var copyName = GetUniqueGroupCopyName(doc, sourceName);
                var hasUserName = sourceGroupHasUserName.TryGetValue(
                    pair.Key,
                    out var storedHasUserName) && storedHasUserName;
                var groupIndex = hasUserName
                    ? doc.Groups.Add(copyName, pair.Value)
                    : doc.Groups.Add(pair.Value);
                var group = groupIndex >= 0
                    ? doc.Groups.FindIndex(groupIndex)
                    : null;
                if (group == null || group.IsDeleted)
                    continue;

                ObjectManagerPlugin.Instance?.NameStore?.SetGroup(
                    doc,
                    group.Id,
                    copyName,
                    hasUserName
                        ? OutlinerNameOwnership.UserDerivedNative
                        : OutlinerNameOwnership.ManagedAlias,
                    "Copy",
                    0,
                    string.Empty,
                    hasUserName ? sourceName : string.Empty);
            }
            return copiedIds;
        }

        private static string GetUniqueGroupCopyName(
            RhinoDoc doc,
            string sourceName)
        {
            var sourceBase = ObjectNamingService.GetCopySeriesBase(sourceName);
            var aliases = ObjectNamingService.GetEffectiveGroupNames(doc);
            var number = 1;
            string candidate;
            do
            {
                candidate = OutlinerStrings.FormatNumberedCopyName(
                    sourceBase,
                    number++);
            }
            while (doc.Groups.FindName(candidate) != null ||
                aliases.Contains(candidate));
            return candidate;
        }

        private void MatchLayerProperties(
            RhinoDoc doc,
            IEnumerable<Guid> targetLayerIds)
        {
            var sourceIndex = doc.Layers.CurrentLayerIndex;
            var setCurrent = false;
            if (!Rhino.UI.Dialogs.ShowSelectLayerDialog(
                ref sourceIndex,
                OutlinerStrings.SelectLayerToMatch,
                false,
                false,
                ref setCurrent))
                return;

            var source = sourceIndex >= 0 && sourceIndex < doc.Layers.Count
                ? doc.Layers[sourceIndex]
                : null;
            if (source == null || source.IsDeleted)
                return;

            ModifyDocument(doc, OutlinerStrings.UndoMatchLayerProperties, () =>
            {
                var changed = false;
                foreach (var targetId in targetLayerIds.Distinct())
                {
                    var target = doc.Layers.FindId(targetId);
                    if (target == null || target.IsDeleted || target.IsReference)
                        continue;

                    var name = target.Name;
                    var parentId = target.ParentLayerId;
                    var isCurrent = target.Index == doc.Layers.CurrentLayerIndex;
                    target.CopyAttributesFrom(source);
                    target.Name = name;
                    target.ParentLayerId = parentId;
                    if (isCurrent)
                    {
                        target.IsVisible = true;
                        target.IsLocked = false;
                    }
                    var sectionStyle = source.GetCustomSectionStyle();
                    if (sectionStyle == null)
                        target.RemoveCustomSectionStyle();
                    else
                        target.SetCustomSectionStyle(new SectionStyle(sectionStyle));
                    changed |= doc.Layers.Modify(target, target.Id, true);
                }
                return changed;
            });
        }

        private void DeleteContextLayers(
            RhinoDoc doc,
            IEnumerable<Guid> layerIds)
        {
            var layers = layerIds
                .Select(doc.Layers.FindId)
                .Where(layer => layer != null &&
                    !layer.IsDeleted &&
                    !layer.IsReference &&
                    layer.Index != doc.Layers.CurrentLayerIndex)
                .OrderByDescending(layer => GetLayerDepth(doc, layer))
                .ToList();
            if (layers.Count == 0)
                return;

            var deletedIds = new HashSet<Guid>(layers.Select(layer => layer.Id));
            var changed = ModifyDocument(doc, OutlinerStrings.UndoDeleteLayers, () =>
                DeleteLayersCore(doc, layers) > 0);
            if (!changed)
                return;

            _selectedLayerIds.ExceptWith(deletedIds);
            if (deletedIds.Contains(_primaryLayerId))
                _primaryLayerId = Guid.Empty;
            QueueTreeRefresh();
        }

        private void SetLayerContentsExpanded(
            IEnumerable<Guid> layerIds,
            bool expanded)
        {
            if (_documentModel == null || _snapshot == null)
                return;

            var targets = new HashSet<Guid>((layerIds ??
                Enumerable.Empty<Guid>()).Where(
                    _documentModel.LayersById.ContainsKey));
            if (targets.Count == 0)
                return;

            var plan = OutlinerLayerExpansionPlanner.Create(
                _documentModel,
                targets,
                _snapshot.GroupSliceItems.Keys,
                GetKnownExpandableObjectIds());
            foreach (var layerId in targets)
                _layerExpansionState[layerId] = true;
            foreach (var layerId in plan.LayerIds)
                _layerExpansionState[layerId] = expanded;
            foreach (var groupSlice in plan.GroupSlices)
                SetGroupExpansionState(groupSlice, expanded);
            foreach (var objectId in plan.HistoryObjectIds)
                _historyExpansionState[objectId] = expanded;

            CaptureWorkspaceState();
            RefreshProjection();
        }
    }
}
