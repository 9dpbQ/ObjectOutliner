using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Eto.Drawing;
using Eto.Forms;

namespace ObjectManager.Outliner
{
    internal sealed class OutlinerLayerProjection
    {
        public OutlinerLayerProjection()
        {
            Roots = new TreeGridItemCollection();
            ObjectItems = new Dictionary<Guid, TreeGridItem>();
            GroupSliceItems =
                new Dictionary<OutlinerGroupSliceKey, TreeGridItem>();
        }

        public TreeGridItemCollection Roots { get; }

        public IDictionary<Guid, TreeGridItem> ObjectItems { get; }

        public IDictionary<OutlinerGroupSliceKey, TreeGridItem> GroupSliceItems
        {
            get;
        }

        public int VisibleObjectCount { get; set; }
    }

    internal sealed class OutlinerSnapshot
    {
        private sealed class TreeGridItemReferenceComparer : IEqualityComparer<TreeGridItem>
        {
            public bool Equals(TreeGridItem first, TreeGridItem second)
            {
                return ReferenceEquals(first, second);
            }

            public int GetHashCode(TreeGridItem item)
            {
                return item == null ? 0 : RuntimeHelpers.GetHashCode(item);
            }
        }

        public OutlinerSnapshot(
            TreeGridItemCollection roots,
            IDictionary<Guid, TreeGridItem> layerItems,
            IDictionary<Guid, TreeGridItem> objectItems,
            IDictionary<OutlinerGroupSliceKey, TreeGridItem> groupSliceItems,
            OutlinerGroupHierarchy groupHierarchy,
            int layerCount,
            int objectCount)
        {
            Roots = roots;
            LayerItems = layerItems;
            ObjectItems = objectItems;
            GroupSliceItems = groupSliceItems;
            GroupSliceItemsByGroupId = groupSliceItems
                .GroupBy(entry => entry.Key.GroupId)
                .ToDictionary(
                    group => group.Key,
                    group => (IList<TreeGridItem>)group
                        .Select(entry => entry.Value)
                        .ToList());
            GroupHierarchy = groupHierarchy;
            LayerCount = layerCount;
            ObjectCount = objectCount;
            Items = CollectItems(roots);
            ObjectReferenceItems = Items
                .Select(item => new
                {
                    Item = item,
                    Node = item.Tag as OutlinerNodeReference
                })
                .Where(entry => entry.Node != null && entry.Node.IsObjectReference)
                .GroupBy(entry => entry.Node.Id)
                .ToDictionary(
                    group => group.Key,
                    group => (IList<TreeGridItem>)group
                        .Select(entry => entry.Item)
                        .ToList());
        }

        public TreeGridItemCollection Roots { get; }

        public IDictionary<Guid, TreeGridItem> LayerItems { get; }

        public IDictionary<Guid, TreeGridItem> ObjectItems { get; }

        public IDictionary<OutlinerGroupSliceKey, TreeGridItem> GroupSliceItems { get; }

        public IDictionary<Guid, IList<TreeGridItem>> GroupSliceItemsByGroupId
        {
            get;
        }

        public OutlinerGroupHierarchy GroupHierarchy { get; private set; }

        public int LayerCount { get; private set; }

        public int ObjectCount { get; private set; }

        public ISet<TreeGridItem> Items { get; }

        public IDictionary<Guid, IList<TreeGridItem>> ObjectReferenceItems { get; }

        public bool Contains(TreeGridItem item)
        {
            return item != null && Items.Contains(item);
        }

        public void RegisterDescendants(TreeGridItem parent)
        {
            if (parent == null)
                return;

            var addedItems = new HashSet<TreeGridItem>(
                new TreeGridItemReferenceComparer());
            AddItems(parent.Children, addedItems);
            foreach (var item in addedItems)
            {
                if (!Items.Add(item))
                    continue;
                var node = item.Tag as OutlinerNodeReference;
                if (node == null || !node.IsObjectReference)
                    continue;

                if (!ObjectReferenceItems.TryGetValue(
                    node.Id,
                    out var references))
                {
                    references = new List<TreeGridItem>();
                    ObjectReferenceItems[node.Id] = references;
                }
                references.Add(item);
            }
        }

        public void UnregisterDescendants(TreeGridItem parent)
        {
            if (parent == null)
                return;

            foreach (var child in parent.Children
                .OfType<TreeGridItem>()
                .ToList())
            {
                UnregisterItem(child);
            }
        }

        public bool ReplaceLayerContent(
            Guid layerId,
            OutlinerLayerProjection projection)
        {
            if (projection == null ||
                !LayerItems.TryGetValue(layerId, out var layerItem))
            {
                return false;
            }

            var childLayers = layerItem.Children
                .OfType<TreeGridItem>()
                .Where(item =>
                {
                    var node = item.Tag as OutlinerNodeReference;
                    return node != null && node.Kind == OutlinerNodeKind.Layer;
                })
                .ToList();
            var previousContent = layerItem.Children
                .OfType<TreeGridItem>()
                .Except(childLayers)
                .ToList();

            foreach (var item in previousContent)
                UnregisterItem(item);

            layerItem.Children.Clear();
            foreach (var value in projection.Roots)
            {
                if (!(value is TreeGridItem item))
                    continue;
                layerItem.Children.Add(item);
                RegisterItem(item);
            }
            foreach (var item in childLayers)
                layerItem.Children.Add(item);

            layerItem.SetValue(
                OutlinerValueIndex.Count,
                projection.VisibleObjectCount);

            ObjectCount = ObjectItems.Count;
            return true;
        }

        public bool TryAddLayer(
            OutlinerLayerModel layer,
            TreeGridItem layerItem)
        {
            if (layer == null ||
                layerItem == null ||
                LayerItems.ContainsKey(layer.Id))
            {
                return false;
            }

            if (layer.ParentId == Guid.Empty)
            {
                Roots.Add(layerItem);
            }
            else if (LayerItems.TryGetValue(
                layer.ParentId,
                out var parentItem))
            {
                parentItem.Children.Add(layerItem);
            }
            else
            {
                return false;
            }

            LayerItems[layer.Id] = layerItem;
            Items.Add(layerItem);
            LayerCount = LayerItems.Count;
            return true;
        }

        public void SetGroupHierarchy(OutlinerGroupHierarchy hierarchy)
        {
            if (hierarchy != null)
                GroupHierarchy = hierarchy;
        }

        private void RegisterItem(TreeGridItem item)
        {
            if (item == null || !Items.Add(item))
                return;

            var node = item.Tag as OutlinerNodeReference;
            if (node != null)
            {
                if (node.Kind == OutlinerNodeKind.Object)
                {
                    ObjectItems[node.Id] = item;
                }
                else if (node.Kind == OutlinerNodeKind.GroupSlice)
                {
                    var key = new OutlinerGroupSliceKey(node.Id, node.OwnerId);
                    GroupSliceItems[key] = item;
                    if (!GroupSliceItemsByGroupId.TryGetValue(
                        node.Id,
                        out var slices))
                    {
                        slices = new List<TreeGridItem>();
                        GroupSliceItemsByGroupId[node.Id] = slices;
                    }
                    slices.Add(item);
                }
                else if (node.IsObjectReference)
                {
                    if (!ObjectReferenceItems.TryGetValue(
                        node.Id,
                        out var references))
                    {
                        references = new List<TreeGridItem>();
                        ObjectReferenceItems[node.Id] = references;
                    }
                    references.Add(item);
                }
            }

            foreach (var child in item.Children.OfType<TreeGridItem>())
                RegisterItem(child);
        }

        private void UnregisterItem(TreeGridItem item)
        {
            if (item == null)
                return;

            foreach (var child in item.Children.OfType<TreeGridItem>().ToList())
                UnregisterItem(child);

            var node = item.Tag as OutlinerNodeReference;
            if (node != null)
            {
                if (node.Kind == OutlinerNodeKind.Object &&
                    ObjectItems.TryGetValue(node.Id, out var objectItem) &&
                    ReferenceEquals(objectItem, item))
                {
                    ObjectItems.Remove(node.Id);
                }
                else if (node.Kind == OutlinerNodeKind.GroupSlice)
                {
                    var key = new OutlinerGroupSliceKey(node.Id, node.OwnerId);
                    if (GroupSliceItems.TryGetValue(key, out var groupItem) &&
                        ReferenceEquals(groupItem, item))
                    {
                        GroupSliceItems.Remove(key);
                    }
                    if (GroupSliceItemsByGroupId.TryGetValue(
                        node.Id,
                        out var slices))
                    {
                        slices.Remove(item);
                        if (slices.Count == 0)
                            GroupSliceItemsByGroupId.Remove(node.Id);
                    }
                }
                else if (node.IsObjectReference &&
                    ObjectReferenceItems.TryGetValue(
                        node.Id,
                        out var references))
                {
                    references.Remove(item);
                    if (references.Count == 0)
                        ObjectReferenceItems.Remove(node.Id);
                }
            }
            Items.Remove(item);
        }

        private static ISet<TreeGridItem> CollectItems(
            TreeGridItemCollection roots)
        {
            var items = new HashSet<TreeGridItem>(
                new TreeGridItemReferenceComparer());
            AddItems(roots, items);
            return items;
        }

        private static void AddItems(
            TreeGridItemCollection source,
            ISet<TreeGridItem> destination)
        {
            if (source == null)
                return;

            foreach (var value in source)
            {
                var item = value as TreeGridItem;
                if (item == null || !destination.Add(item))
                    continue;
                AddItems(item.Children, destination);
            }
        }
    }

    internal static class OutlinerTreeProjector
    {
        public static OutlinerSnapshot Project(
            OutlinerDocumentModel model,
            OutlinerIconFactory icons,
            IDictionary<Guid, bool> layerExpansionState,
            IDictionary<OutlinerGroupSliceKey, bool> groupExpansionState,
            IDictionary<Guid, bool> historyExpansionState,
            OutlinerViewState viewState,
            OutlinerIsolateState isolateState,
            ISet<Guid> selectedObjectIds = null,
            IDictionary<OutlinerGroupSliceKey, int> groupOrders = null)
        {
            var layersById = model.LayersById;
            var layersByIndex = model.LayersByIndex;
            var objectsById = model.ObjectsById;
            var groupHierarchy = model.GroupHierarchy;
            var includedLayerIds = new HashSet<Guid>();
            var includedObjectIds = new HashSet<Guid>();
            var includedGroupIds = new HashSet<Guid>();
            var temporaryExpandedLayerIds = new HashSet<Guid>();
            var temporaryExpandedGroupIds = new HashSet<Guid>();
            var historyStackPlan = viewState.CompactHistory
                ? OutlinerHistoryStackPlanner.Create(model)
                : null;
            BuildVisibleSets(
                model,
                layersById,
                layersByIndex,
                objectsById,
                groupHierarchy,
                viewState,
                selectedObjectIds,
                includedLayerIds,
                includedObjectIds,
                includedGroupIds,
                temporaryExpandedLayerIds,
                temporaryExpandedGroupIds);
            var visibleObjectCountsByLayer = CreateVisibleObjectCountsByLayer(
                model,
                viewState,
                includedObjectIds);

            var itemsByLayerIndex = new Dictionary<int, TreeGridItem>();
            var itemsByLayerId = new Dictionary<Guid, TreeGridItem>();
            var itemsByObjectId = new Dictionary<Guid, TreeGridItem>();
            var itemsByGroupSlice = new Dictionary<OutlinerGroupSliceKey, TreeGridItem>();
            var roots = new TreeGridItemCollection();

            foreach (var layer in model.Layers.Where(
                layer => includedLayerIds.Contains(layer.Id)))
            {
                var item = CreateLayerItem(
                    layer,
                    icons,
                    layerExpansionState,
                    isolateState,
                    temporaryExpandedLayerIds.Contains(layer.Id),
                    visibleObjectCountsByLayer.TryGetValue(
                        layer.Id,
                        out var visibleCount) ? visibleCount : 0);
                itemsByLayerIndex[layer.Index] = item;
                itemsByLayerId[layer.Id] = item;
            }

            foreach (var layer in model.Layers.Where(
                layer => includedLayerIds.Contains(layer.Id)))
            {
                var item = itemsByLayerIndex[layer.Index];
                if (layer.ParentId == Guid.Empty ||
                    !itemsByLayerId.ContainsKey(layer.ParentId))
                {
                    roots.Add(item);
                }
            }

            foreach (var layer in model.Layers.Where(
                layer => includedLayerIds.Contains(layer.Id)))
            {
                if (!itemsByLayerIndex.TryGetValue(
                    layer.Index,
                    out var layerItem))
                {
                    continue;
                }

                if (viewState.DisplayMode == OutlinerDisplayMode.LayersOnly)
                    continue;

                var projection = ProjectLayerContent(
                    model,
                    layer,
                    icons,
                    groupExpansionState,
                    historyExpansionState,
                    isolateState,
                    includedObjectIds,
                    includedGroupIds,
                    temporaryExpandedGroupIds,
                    historyStackPlan,
                    viewState.DisplayMode == OutlinerDisplayMode.All,
                    groupOrders);
                foreach (var value in projection.Roots)
                    layerItem.Children.Add(value);
                foreach (var entry in projection.ObjectItems)
                    itemsByObjectId[entry.Key] = entry.Value;
                foreach (var entry in projection.GroupSliceItems)
                    itemsByGroupSlice[entry.Key] = entry.Value;
            }

            foreach (var layer in model.Layers.Where(
                layer => includedLayerIds.Contains(layer.Id)))
            {
                if (layer.ParentId == Guid.Empty ||
                    !itemsByLayerId.TryGetValue(layer.ParentId, out var parent))
                {
                    continue;
                }

                parent.Children.Add(itemsByLayerIndex[layer.Index]);
            }

            var layerOnlyFastPath =
                viewState.DisplayMode == OutlinerDisplayMode.LayersOnly &&
                !viewState.HasSearch &&
                !viewState.ContentFilter.IsActive;
            var projectedObjectCount = includedObjectIds.Count;
            if (layerOnlyFastPath)
            {
                var matchingObjectCount = model.Layers
                    .Where(layer => viewState.LayerFilter.Matches(layer, model))
                    .Sum(layer => (long)Math.Max(0, layer.TopLevelObjectCount));
                projectedObjectCount = (int)Math.Min(
                    int.MaxValue,
                    matchingObjectCount);
            }
            return new OutlinerSnapshot(
                roots,
                itemsByLayerId,
                itemsByObjectId,
                itemsByGroupSlice,
                groupHierarchy,
                itemsByLayerId.Count,
                projectedObjectCount);
        }

        internal static TreeGridItem CreateLayerItem(
            OutlinerLayerModel layer,
            OutlinerIconFactory icons,
            IDictionary<Guid, bool> layerExpansionState,
            OutlinerIsolateState isolateState,
            bool temporarilyExpanded = false,
            int visibleObjectCount = 0)
        {
            if (layer == null || icons == null)
                return null;

            bool expanded;
            if (temporarilyExpanded)
                expanded = true;
            else if (layerExpansionState == null ||
                     !layerExpansionState.TryGetValue(layer.Id, out expanded))
                expanded = layer.DefaultExpanded;

            var item = CreateTreeItem(
                null,
                layer.Name,
                false,
                layer.IsCurrent ? icons.GetCurrentLayerIcon() : null,
                GetLayerVisibilityIcon(layer, icons, isolateState),
                GetLayerLockIcon(layer, icons, isolateState),
                icons.GetColorSwatch(layer.Color),
                icons.GetMaterialSwatch(layer.HasMaterial),
                layer.LinetypeName,
                icons.GetColorSwatch(layer.PlotColor),
                layer.PlotWeightName,
                layer.HasCustomSectionStyle
                    ? OutlinerStrings.Custom
                    : OutlinerStrings.Default,
                icons.GetNewDetailVisibilityIcon(layer.NewDetailVisible),
                icons.GetModelVisibilityIcon(
                    layer.ModelVisible,
                    !layer.OwnVisible || layer.InheritedHidden),
                layer.IsUnavailable);
            item.SetValue(OutlinerValueIndex.DimName, !layer.OwnVisible || layer.InheritedHidden);
            item.SetValue(
                OutlinerValueIndex.Favorite,
                icons.GetFavoriteIcon(
                    layer.IsFavorite,
                    !layer.CanEditFavorite));
            item.SetValue(
                OutlinerValueIndex.Count,
                Math.Max(0, visibleObjectCount));
            item.Expanded = expanded;
            item.Tag = OutlinerNodeReference.ForLayer(layer.Id, layer.Index);
            item.SetValue(OutlinerValueIndex.Zoom, icons.GetZoomIcon());
            return item;
        }

        internal static OutlinerLayerProjection ProjectLayerContent(
            OutlinerDocumentModel model,
            OutlinerLayerModel layer,
            OutlinerIconFactory icons,
            IDictionary<OutlinerGroupSliceKey, bool> groupExpansionState,
            IDictionary<Guid, bool> historyExpansionState,
            OutlinerIsolateState isolateState,
            ISet<Guid> includedObjectIds = null,
            ISet<Guid> includedGroupIds = null,
            ISet<Guid> temporaryExpandedGroupIds = null,
            OutlinerHistoryStackPlan historyStackPlan = null,
            bool includeObjectRows = true,
            IDictionary<OutlinerGroupSliceKey, int> groupOrders = null)
        {
            var projection = new OutlinerLayerProjection();
            if (model == null || layer == null || icons == null)
                return projection;

            if (layer.HasDeferredObjects)
            {
                var deferredItem = CreateRelationshipItem(
                    null,
                    OutlinerStrings.ObjectsExpandToLoad,
                    true);
                deferredItem.Tag = OutlinerNodeReference.ForDeferredLayerObjects(
                    layer.Id,
                    layer.Index);
                projection.Roots.Add(deferredItem);
                return projection;
            }

            var candidates = model.ObjectsByLayerId.TryGetValue(
                layer.Id, out var indexedObjects)
                    ? indexedObjects
                    : Array.Empty<OutlinerObjectModel>();
            var layerObjects = SortObjects(candidates
                .Where(item => includedObjectIds == null ||
                     includedObjectIds.Contains(item.Id)))
                .ToList();
            projection.VisibleObjectCount = layerObjects.Count;
            if (layerObjects.Count == 0)
                return projection;
            var visibleObjectIds = new HashSet<Guid>(
                layerObjects.Select(item => item.Id));

            var groupHierarchy = model.GroupHierarchy;
            var groupIdsInLayer = new HashSet<Guid>(layerObjects
                .SelectMany(item => item.GroupIds)
                .Where(id => includedGroupIds == null ||
                    includedGroupIds.Contains(id)));
            var groupsInLayer = model.Groups
                .Where(item => groupIdsInLayer.Contains(item.Id))
                .OrderBy(item => TryGetGroupOrder(
                    groupOrders,
                    item.Id,
                    layer.Id,
                    out _) ? 0 : 1)
                .ThenBy(item => TryGetGroupOrder(
                    groupOrders,
                    item.Id,
                    layer.Id,
                    out var order) ? order : int.MaxValue)
                .ThenBy(item => item.Index)
                .ToList();

            foreach (var group in groupsInLayer)
            {
                var activeMembers = group.MemberIds
                    .Select(id => model.ObjectsById.TryGetValue(
                        id,
                        out var member)
                            ? member
                            : null)
                    .Where(member => member != null)
                    .ToList();
                var activeLocalMemberCount = activeMembers.Count(
                    member => member.LayerId == layer.Id);
                if (activeLocalMemberCount == 0)
                    continue;

                var participatingLayerCount = activeMembers
                    .Select(member => member.LayerId)
                    .Distinct()
                    .Count();
                var displayName = participatingLayerCount > 1
                    ? string.Format(
                        "{0} ({1}/{2})",
                        group.Name,
                        activeLocalMemberCount,
                        activeMembers.Count)
                    : group.Name;
                var groupItem = CreateRelationshipItem(
                    icons.GetGroupIcon(),
                    displayName,
                    false);
                groupItem.SetValue(OutlinerValueIndex.Note, icons.GetNoteIcon(!string.IsNullOrEmpty(group.Note)));
                groupItem.SetValue(OutlinerValueIndex.Zoom, icons.GetZoomIcon());
                groupItem.SetValue(OutlinerValueIndex.DimName, AllMembersHidden(activeMembers));
                groupItem.SetValue(
                    OutlinerValueIndex.Visibility,
                    GetGroupVisibilityIcon(
                        activeMembers,
                        icons,
                        isolateState,
                        group.Id));
                groupItem.SetValue(
                    OutlinerValueIndex.Lock,
                    GetGroupLockIcon(
                        activeMembers,
                        icons,
                        isolateState,
                        group.Id));
                groupItem.SetValue(
                    OutlinerValueIndex.Favorite,
                    icons.GetFavoriteIcon(
                        group.IsFavorite,
                        !group.CanEditFavorite));
                groupItem.SetValue(
                    OutlinerValueIndex.Count,
                    group.MemberIds.Count(visibleObjectIds.Contains));

                var sliceKey = new OutlinerGroupSliceKey(group.Id, layer.Id);
                bool groupExpanded;
                if (temporaryExpandedGroupIds != null &&
                    temporaryExpandedGroupIds.Contains(group.Id))
                {
                    groupExpanded = true;
                }
                else if (groupExpansionState == null ||
                         !groupExpansionState.TryGetValue(
                             sliceKey,
                             out groupExpanded))
                {
                    groupExpanded = true;
                }

                groupItem.Expanded = groupExpanded;
                groupItem.Tag = OutlinerNodeReference.ForGroupSlice(
                    group.Id,
                    layer.Id,
                    layer.Index,
                    group.Index);
                projection.GroupSliceItems[sliceKey] = groupItem;
            }

            foreach (var group in groupsInLayer)
            {
                var sliceKey = new OutlinerGroupSliceKey(group.Id, layer.Id);
                if (!projection.GroupSliceItems.TryGetValue(
                    sliceKey,
                    out var groupItem))
                {
                    continue;
                }

                var parentId = groupHierarchy.GetParentGroupId(group.Id);
                if (parentId != Guid.Empty &&
                    projection.GroupSliceItems.TryGetValue(
                        new OutlinerGroupSliceKey(parentId, layer.Id),
                        out var parentItem))
                {
                    parentItem.Children.Add(groupItem);
                }
                else
                {
                    projection.Roots.Add(groupItem);
                }
            }

            if (!includeObjectRows)
                return projection;

            foreach (var group in groupsInLayer)
            {
                if (!projection.GroupSliceItems.TryGetValue(
                    new OutlinerGroupSliceKey(group.Id, layer.Id),
                    out var groupItem))
                {
                    continue;
                }

                foreach (var member in SortObjects(layerObjects.Where(
                    item => item.GroupIds.Contains(group.Id))))
                {
                    var containerId = groupHierarchy.GetCanonicalContainerId(
                        member.Id);
                    if (containerId == group.Id)
                    {
                        var objectItem = CreateObjectItem(
                            model,
                            member,
                            model.ObjectsById,
                            model.LayersByIndex,
                            icons,
                            historyExpansionState,
                            containerId,
                            isolateState,
                            historyStackPlan);
                        if (!ShouldStackObject(
                                member.Id,
                                visibleObjectIds,
                                historyStackPlan))
                        {
                            groupItem.Children.Add(objectItem);
                        }
                        projection.ObjectItems[member.Id] = objectItem;
                    }
                    else if (!groupHierarchy.IsAncestorOrSelf(
                        group.Id,
                        containerId))
                    {
                        groupItem.Children.Add(CreateObjectReferenceItem(
                            member,
                            member.DisplayName,
                            icons,
                            OutlinerNodeReference.ForGroupMemberReference(
                                member.Id,
                                group.Id,
                                member.LayerIndex,
                                group.Index)));
                    }
                }
            }

            foreach (var entry in layerObjects.Where(item =>
                groupHierarchy.GetCanonicalContainerId(item.Id) == Guid.Empty ||
                !projection.GroupSliceItems.ContainsKey(
                    new OutlinerGroupSliceKey(
                        groupHierarchy.GetCanonicalContainerId(item.Id),
                        layer.Id))))
            {
                var objectItem = CreateObjectItem(
                    model,
                    entry,
                    model.ObjectsById,
                    model.LayersByIndex,
                    icons,
                    historyExpansionState,
                    Guid.Empty,
                    isolateState,
                    historyStackPlan);
                if (!ShouldStackObject(
                        entry.Id,
                        visibleObjectIds,
                        historyStackPlan))
                {
                    projection.Roots.Add(objectItem);
                }
                projection.ObjectItems[entry.Id] = objectItem;
            }

            AttachHistoryStackItems(
                projection,
                historyStackPlan,
                historyExpansionState);

            return projection;
        }

        private static bool TryGetGroupOrder(
            IDictionary<OutlinerGroupSliceKey, int> groupOrders,
            Guid groupId,
            Guid layerId,
            out int order)
        {
            order = 0;
            return groupOrders != null && groupOrders.TryGetValue(
                new OutlinerGroupSliceKey(groupId, layerId),
                out order);
        }

        private static IDictionary<Guid, int> CreateVisibleObjectCountsByLayer(
            OutlinerDocumentModel model,
            OutlinerViewState viewState,
            ISet<Guid> includedObjectIds)
        {
            if (model == null || viewState == null)
                return new Dictionary<Guid, int>();

            if (!viewState.HasSearch && !viewState.ContentFilter.IsActive)
            {
                return model.Layers
                    .Where(layer => viewState.LayerFilter.Matches(layer, model))
                    .ToDictionary(
                        layer => layer.Id,
                        layer => Math.Max(0, layer.TopLevelObjectCount));
            }

            var included = includedObjectIds ?? new HashSet<Guid>();
            return model.Objects
                .Where(item => included.Contains(item.Id))
                .GroupBy(item => item.LayerId)
                .ToDictionary(group => group.Key, group => group.Count());
        }

        private static TreeGridItem CreateObjectItem(
            OutlinerDocumentModel model,
            OutlinerObjectModel entry,
            IDictionary<Guid, OutlinerObjectModel> objectsById,
            IDictionary<int, OutlinerLayerModel> layersByIndex,
            OutlinerIconFactory icons,
            IDictionary<Guid, bool> historyExpansionState,
            Guid containerId,
            OutlinerIsolateState isolateState,
            OutlinerHistoryStackPlan historyStackPlan)
        {
            var item = CreateTreeItem(
                icons.GetObjectIcon(entry.IconKind, entry.IsUnavailable),
                entry.DisplayName,
                entry.IsProvisional,
                null,
                GetObjectVisibilityIcon(entry, icons, isolateState),
                GetObjectLockIcon(entry, icons, isolateState),
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                entry.IsUnavailable);
            item.SetValue(
                OutlinerValueIndex.Favorite,
                icons.GetFavoriteIcon(
                    entry.IsFavorite,
                    !entry.CanEditFavorite));
            item.Tag = OutlinerNodeReference.ForObject(
                entry.Id,
                entry.LayerIndex,
                containerId);
            item.SetValue(OutlinerValueIndex.Note, icons.GetNoteIcon(!string.IsNullOrEmpty(entry.Note)));
            item.SetValue(OutlinerValueIndex.Zoom, icons.GetZoomIcon());
            ApplyObjectRelationships(
                item,
                entry,
                model,
                icons,
                historyExpansionState,
                historyStackPlan == null
                    ? null
                    : new HashSet<Guid>(
                        historyStackPlan.GetInputs(entry.Id)),
                historyStackPlan != null);
            return item;
        }

        internal static bool ApplyObjectRelationships(
            TreeGridItem item,
            OutlinerObjectModel entry,
            OutlinerDocumentModel model,
            OutlinerIconFactory icons,
            IDictionary<Guid, bool> historyExpansionState,
            ISet<Guid> suppressedHistoryParentIds = null,
            bool omitHistoryChildren = false)
        {
            if (item == null || entry == null || model == null || icons == null)
                return false;

            item.Children.Clear();
            var expanded = historyExpansionState != null &&
                historyExpansionState.TryGetValue(
                    entry.Id,
                    out var storedExpanded) &&
                storedExpanded;
            AddBlockDefinitionComponents(
                item,
                entry,
                model,
                icons,
                expanded);
            if (model.HistoryByObjectId.TryGetValue(
                entry.Id,
                out var history))
            {
                AddHistorySection(
                    item,
                    history.ParentIds.Where(id =>
                        suppressedHistoryParentIds == null ||
                        !suppressedHistoryParentIds.Contains(id)),
                    entry,
                    OutlinerHistorySectionKind.Parents,
                    OutlinerStrings.HistoryParents,
                    model.ObjectsById,
                    model.LayersByIndex,
                    icons);
                if (!omitHistoryChildren)
                {
                    AddHistorySection(
                        item,
                        history.ChildIds,
                        entry,
                        OutlinerHistorySectionKind.Children,
                        OutlinerStrings.HistoryChildren,
                        model.ObjectsById,
                        model.LayersByIndex,
                        icons);
                }
            }

            if (item.Children.Count == 0)
            {
                item.Expanded = false;
                return false;
            }

            item.Expanded = expanded;
            return true;
        }

        private static bool ShouldStackObject(
            Guid objectId,
            ISet<Guid> visibleObjectIds,
            OutlinerHistoryStackPlan historyStackPlan)
        {
            return historyStackPlan != null &&
                historyStackPlan.TryGetResult(objectId, out var resultId) &&
                visibleObjectIds.Contains(resultId);
        }

        private static void AttachHistoryStackItems(
            OutlinerLayerProjection projection,
            OutlinerHistoryStackPlan historyStackPlan,
            IDictionary<Guid, bool> historyExpansionState)
        {
            if (projection == null || historyStackPlan == null)
                return;

            foreach (var resultEntry in projection.ObjectItems.ToList())
            {
                var inputItems = historyStackPlan.GetInputs(resultEntry.Key)
                    .Where(projection.ObjectItems.ContainsKey)
                    .Select(id => projection.ObjectItems[id])
                    .ToList();
                if (inputItems.Count == 0)
                    continue;

                var existingChildren = resultEntry.Value.Children
                    .OfType<TreeGridItem>()
                    .ToList();
                resultEntry.Value.Children.Clear();
                foreach (var inputItem in inputItems)
                    resultEntry.Value.Children.Add(inputItem);
                foreach (var child in existingChildren)
                    resultEntry.Value.Children.Add(child);

                resultEntry.Value.Expanded = historyExpansionState != null &&
                    historyExpansionState.TryGetValue(
                        resultEntry.Key,
                        out var expanded) &&
                    expanded;
            }
        }

        private static void AddBlockDefinitionComponents(
            TreeGridItem objectItem,
            OutlinerObjectModel instance,
            OutlinerDocumentModel model,
            OutlinerIconFactory icons,
            bool expanded)
        {
            if (instance.BlockDefinitionId == Guid.Empty ||
                !model.BlockDefinitionsById.TryGetValue(
                    instance.BlockDefinitionId,
                    out var definition))
            {
                return;
            }

            if (definition.Components.Count == 0)
                return;

            if (!expanded)
            {
                var placeholder = CreateRelationshipItem(
                    null,
                    OutlinerStrings.FormatBlockComponents(
                        definition.Components.Count),
                    false);
                placeholder.Tag =
                    OutlinerNodeReference.ForBlockDefinitionComponent(
                        Guid.Empty,
                        instance.Id,
                        -1,
                        -1);
                objectItem.Children.Add(placeholder);
                return;
            }

            foreach (var component in definition.Components)
            {
                var label = string.IsNullOrWhiteSpace(component.LayerPath)
                    ? component.DisplayName
                    : string.Format(
                        "{0}  [{1}]",
                        component.DisplayName,
                        component.LayerPath);
                var item = CreateRelationshipItem(
                    icons.GetObjectIcon(component.IconKind, false),
                    label,
                    false);
                item.Tag = OutlinerNodeReference.ForBlockDefinitionComponent(
                    component.ObjectId,
                    instance.Id,
                    component.LayerIndex,
                    component.Index);
                objectItem.Children.Add(item);
            }
        }

        internal static object GetLayerVisibilityIcon(
            OutlinerLayerModel layer,
            OutlinerIconFactory icons,
            OutlinerIsolateState isolateState)
        {
            var visualState = isolateState == null
                ? OutlinerIsolateVisualState.None
                : isolateState.GetLayerState(
                    layer.Id,
                    OutlinerIsolationMode.Visibility);
            if (visualState != OutlinerIsolateVisualState.None)
                return GetIsolateIcon(visualState, icons);

            return layer.IsCurrent
                ? null
                : icons.GetVisibilityIcon(layer.OwnVisible, layer.InheritedHidden);
        }

        internal static object GetLayerLockIcon(
            OutlinerLayerModel layer,
            OutlinerIconFactory icons,
            OutlinerIsolateState isolateState)
        {
            var visualState = isolateState == null
                ? OutlinerIsolateVisualState.None
                : isolateState.GetLayerState(
                    layer.Id,
                    OutlinerIsolationMode.Lock);
            if (visualState != OutlinerIsolateVisualState.None)
                return GetIsolateLockIcon(visualState, icons);

            return layer.IsCurrent
                ? null
                : icons.GetLockIcon(
                    layer.OwnLocked,
                    layer.InheritedLocked);
        }

        internal static Image GetObjectVisibilityIcon(
            OutlinerObjectModel item,
            OutlinerIconFactory icons,
            OutlinerIsolateState isolateState)
        {
            var visualState = isolateState == null
                ? OutlinerIsolateVisualState.None
                : isolateState.GetObjectState(
                    item.Id,
                    OutlinerIsolationMode.Visibility);
            return visualState == OutlinerIsolateVisualState.None
                ? icons.GetVisibilityIcon(!item.IsHidden, item.InheritedHidden)
                : GetIsolateIcon(visualState, icons);
        }

        internal static Image GetObjectLockIcon(
            OutlinerObjectModel item,
            OutlinerIconFactory icons,
            OutlinerIsolateState isolateState)
        {
            var visualState = isolateState == null
                ? OutlinerIsolateVisualState.None
                : isolateState.GetObjectState(
                    item.Id,
                    OutlinerIsolationMode.Lock);
            return visualState == OutlinerIsolateVisualState.None
                ? icons.GetLockIcon(item.IsLocked, item.InheritedLocked)
                : GetIsolateLockIcon(visualState, icons);
        }

        internal static bool AllMembersHidden(IList<OutlinerObjectModel> members) =>
            members.Count > 0 && members.All(item => item.IsHidden || item.InheritedHidden);

        internal static Image GetGroupVisibilityIcon(
            IList<OutlinerObjectModel> members,
            OutlinerIconFactory icons)
        {
            if (members == null || members.Count == 0)
                return null;

            if (members.All(item => item.IsHidden))
                return icons.GetVisibilityIcon(false);

            var isMixedOrInherited = members.Any(item => item.IsHidden) ||
                members.Any(item => item.InheritedHidden);
            return icons.GetVisibilityIcon(true, isMixedOrInherited);
        }

        internal static Image GetGroupVisibilityIcon(
            IList<OutlinerObjectModel> members,
            OutlinerIconFactory icons,
            OutlinerIsolateState isolateState,
            Guid groupId)
        {
            var visualState = isolateState == null
                ? OutlinerIsolateVisualState.None
                : isolateState.GetGroupState(
                    groupId,
                    OutlinerIsolationMode.Visibility);
            return visualState == OutlinerIsolateVisualState.None
                ? GetGroupVisibilityIcon(members, icons)
                : GetIsolateIcon(visualState, icons);
        }

        internal static Image GetGroupLockIcon(
            IList<OutlinerObjectModel> members,
            OutlinerIconFactory icons)
        {
            if (members == null || members.Count == 0)
                return null;

            if (members.All(item => item.IsLocked))
                return icons.GetLockIcon(true);

            var isMixedOrInherited = members.Any(item => item.IsLocked) ||
                members.Any(item => item.InheritedLocked);
            return icons.GetLockIcon(false, isMixedOrInherited);
        }

        internal static Image GetGroupLockIcon(
            IList<OutlinerObjectModel> members,
            OutlinerIconFactory icons,
            OutlinerIsolateState isolateState,
            Guid groupId)
        {
            var visualState = isolateState == null
                ? OutlinerIsolateVisualState.None
                : isolateState.GetGroupState(
                    groupId,
                    OutlinerIsolationMode.Lock);
            return visualState == OutlinerIsolateVisualState.None
                ? GetGroupLockIcon(members, icons)
                : GetIsolateLockIcon(visualState, icons);
        }

        internal static Image GetIsolateIcon(
            OutlinerIsolateVisualState state,
            OutlinerIconFactory icons)
        {
            return state == OutlinerIsolateVisualState.Target
                ? icons.GetIsolateTargetIcon()
                : icons.GetIsolateSuppressedIcon();
        }

        internal static Image GetIsolateLockIcon(
            OutlinerIsolateVisualState state,
            OutlinerIconFactory icons)
        {
            return state == OutlinerIsolateVisualState.Target
                ? icons.GetIsolateLockTargetIcon()
                : icons.GetIsolateLockSuppressedIcon();
        }

        private static void AddHistorySection(
            TreeGridItem objectItem,
            IEnumerable<Guid> relationIds,
            OutlinerObjectModel source,
            OutlinerHistorySectionKind sectionKind,
            string sectionName,
            IDictionary<Guid, OutlinerObjectModel> objectsById,
            IDictionary<int, OutlinerLayerModel> layersByIndex,
            OutlinerIconFactory icons)
        {
            var targets = relationIds
                .Select(id => new
                {
                    Id = id,
                    Target = objectsById.TryGetValue(id, out var target)
                        ? target
                        : null
                })
                .ToList();
            if (targets.Count == 0)
                return;

            var section = CreateRelationshipItem(
                null,
                string.Format("{0} ({1})", sectionName, targets.Count),
                false);
            section.Tag = OutlinerNodeReference.ForHistorySection(
                source.Id,
                source.LayerIndex,
                sectionKind);
            section.Expanded = true;
            foreach (var entry in targets)
            {
                var target = entry.Target;
                if (target == null)
                {
                    var missingItem = CreateRelationshipItem(
                        null,
                        string.Format(
                            OutlinerStrings.MissingObject + " [{0}]",
                            entry.Id.ToString("N").Substring(0, 8)),
                        true);
                    missingItem.Tag = OutlinerNodeReference.ForHistoryReference(
                        entry.Id,
                        source.Id,
                        -1,
                        sectionKind);
                    section.Children.Add(missingItem);
                    continue;
                }

                OutlinerLayerModel layer;
                var layerPath = layersByIndex.TryGetValue(target.LayerIndex, out layer)
                    ? layer.FullPath
                    : string.Empty;
                var label = string.IsNullOrWhiteSpace(layerPath)
                    ? target.DisplayName
                    : string.Format("{0}  [{1}]", target.DisplayName, layerPath);
                section.Children.Add(CreateObjectReferenceItem(
                    target,
                    label,
                    icons,
                    OutlinerNodeReference.ForHistoryReference(
                        target.Id,
                        source.Id,
                        target.LayerIndex,
                        sectionKind)));
            }
            objectItem.Children.Add(section);
        }

        private static TreeGridItem CreateObjectReferenceItem(
            OutlinerObjectModel target,
            string label,
            OutlinerIconFactory icons,
            OutlinerNodeReference node)
        {
            var item = CreateRelationshipItem(
                icons.GetObjectIcon(target.IconKind, target.IsUnavailable),
                label,
                target.IsUnavailable);
            item.Tag = node;
            item.SetValue(OutlinerValueIndex.Note, icons.GetNoteIcon(!string.IsNullOrEmpty(target.Note)));
            item.SetValue(OutlinerValueIndex.Zoom, icons.GetZoomIcon());
            return item;
        }

        private static TreeGridItem CreateRelationshipItem(
            object icon,
            string name,
            bool unavailable)
        {
            return CreateTreeItem(
                icon,
                name,
                false,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                unavailable);
        }

        private static IOrderedEnumerable<OutlinerObjectModel> SortObjects(
            IEnumerable<OutlinerObjectModel> objects)
        {
            return objects
                .OrderBy(item => item.SortOrder.HasValue ? 0 : 1)
                .ThenBy(item => item.SortOrder ?? int.MaxValue)
                .ThenBy(item => item.RuntimeSerialNumber);
        }

        private static void BuildVisibleSets(
            OutlinerDocumentModel model,
            IDictionary<Guid, OutlinerLayerModel> layersById,
            IDictionary<int, OutlinerLayerModel> layersByIndex,
            IDictionary<Guid, OutlinerObjectModel> objectsById,
            OutlinerGroupHierarchy groupHierarchy,
            OutlinerViewState viewState,
            ISet<Guid> selectedObjectIds,
            ISet<Guid> includedLayerIds,
            ISet<Guid> includedObjectIds,
            ISet<Guid> includedGroupIds,
            ISet<Guid> temporaryExpandedLayerIds,
            ISet<Guid> temporaryExpandedGroupIds)
        {
            var filteredLayerIds = new HashSet<Guid>(model.Layers
                .Where(layer => viewState.LayerFilter.Matches(layer, model))
                .Select(layer => layer.Id));

            if (viewState.DisplayMode == OutlinerDisplayMode.LayersOnly &&
                !viewState.HasSearch &&
                !viewState.ContentFilter.IsActive)
            {
                includedLayerIds.UnionWith(filteredLayerIds);
                foreach (var layerId in filteredLayerIds)
                    AddAncestors(layerId, layersById, includedLayerIds);
                foreach (var layerId in viewState.TemporarilyRevealedLayerIds)
                {
                    if (layersById.ContainsKey(layerId))
                    {
                        AddPathToRoot(
                            layerId,
                            layersById,
                            includedLayerIds,
                            temporaryExpandedLayerIds);
                    }
                }
                foreach (var objectId in viewState.TemporarilyRevealedObjectIds)
                {
                    AddObjectPath(
                        objectId,
                        objectsById,
                        layersById,
                        includedLayerIds,
                        temporaryExpandedLayerIds);
                }
                if (viewState.HasReveal)
                {
                    AddObjectPath(
                        viewState.RevealedObjectId,
                        objectsById,
                        layersById,
                        includedLayerIds,
                        temporaryExpandedLayerIds);
                }
                return;
            }

            var filteredObjectIds = new HashSet<Guid>(model.Objects
                .Where(item =>
                    filteredLayerIds.Contains(item.LayerId) &&
                    viewState.ContentFilter.MatchesObject(
                        item,
                        selectedObjectIds))
                .Select(item => item.Id));
            var candidateLayerIds = new HashSet<Guid>();
            var candidateObjectIds = new HashSet<Guid>();
            var candidateGroupIds = new HashSet<Guid>();

            if (viewState.ContentFilter.PreservesLayerRows)
            {
                candidateLayerIds.UnionWith(filteredLayerIds);
                candidateObjectIds.UnionWith(filteredObjectIds);
            }
            else
            {
                var favoriteLayerIds = new HashSet<Guid>(model.Layers
                    .Where(layer =>
                        filteredLayerIds.Contains(layer.Id) &&
                        layer.IsFavorite)
                    .Select(layer => layer.Id));
                foreach (var layer in model.Layers)
                {
                    if (filteredLayerIds.Contains(layer.Id) &&
                        IsWithinMatchedLayerSubtree(
                            layer.Id,
                            favoriteLayerIds,
                            layersById))
                    {
                        candidateLayerIds.Add(layer.Id);
                    }
                }

                candidateObjectIds.UnionWith(model.Objects
                    .Where(item =>
                        filteredObjectIds.Contains(item.Id) &&
                        (item.IsFavorite ||
                         candidateLayerIds.Contains(item.LayerId)))
                    .Select(item => item.Id));

                foreach (var group in model.Groups.Where(item =>
                    item.IsFavorite))
                {
                    var currentId = group.Id;
                    var visited = new HashSet<Guid>();
                    while (currentId != Guid.Empty && visited.Add(currentId))
                    {
                        candidateGroupIds.Add(currentId);
                        currentId = groupHierarchy.GetParentGroupId(currentId);
                    }
                    candidateObjectIds.UnionWith(
                        group.MemberIds.Where(filteredObjectIds.Contains));
                }

                foreach (var objectId in candidateObjectIds)
                {
                    if (!objectsById.TryGetValue(objectId, out var objectModel))
                        continue;
                    candidateGroupIds.UnionWith(objectModel.GroupIds);
                }
            }

            if (!viewState.HasSearch)
            {
                includedLayerIds.UnionWith(candidateLayerIds);
                includedObjectIds.UnionWith(candidateObjectIds);
                includedGroupIds.UnionWith(candidateGroupIds);
                foreach (var layerId in candidateLayerIds)
                    AddAncestors(layerId, layersById, includedLayerIds);
                if (viewState.ContentFilter.FavoritesOnly ||
                    viewState.ContentFilter.HasObjectCriteria)
                {
                    foreach (var objectId in candidateObjectIds)
                    {
                        AddObjectPath(
                            objectId,
                            objectsById,
                            layersById,
                            includedLayerIds,
                            temporaryExpandedLayerIds);
                        AddCanonicalGroupPath(
                            objectId,
                            groupHierarchy,
                            temporaryExpandedGroupIds);
                    }
                    foreach (var groupId in candidateGroupIds)
                    {
                        AddGroupPath(
                            groupId,
                            groupHierarchy,
                            temporaryExpandedGroupIds);
                    }
                }
            }
            else
            {
                var searchableLayerIds =
                    viewState.ContentFilter.FavoritesOnly
                        ? candidateLayerIds
                        : filteredLayerIds;
                var matchingLayerIds = new HashSet<Guid>(model.Layers
                    .Where(layer =>
                        searchableLayerIds.Contains(layer.Id) &&
                        viewState.Matches(layer.Name))
                    .Select(layer => layer.Id));
                var matchingObjectIds = new HashSet<Guid>(model.Objects
                    .Where(item =>
                        candidateObjectIds.Contains(item.Id) &&
                        viewState.Matches(item.DisplayName))
                    .Select(item => item.Id));
                var matchingGroups = model.Groups
                    .Where(group =>
                        viewState.Matches(group.Name) &&
                        (!viewState.ContentFilter.FavoritesOnly ||
                         candidateGroupIds.Contains(group.Id)) &&
                        group.MemberIds.Any(candidateObjectIds.Contains))
                    .ToList();

                foreach (var group in matchingGroups)
                {
                    includedGroupIds.Add(group.Id);
                    AddGroupPath(
                        group.Id,
                        groupHierarchy,
                        temporaryExpandedGroupIds);
                    foreach (var memberId in group.MemberIds)
                    {
                        if (candidateObjectIds.Contains(memberId))
                            matchingObjectIds.Add(memberId);
                    }
                }

                foreach (var layer in model.Layers)
                {
                    if (searchableLayerIds.Contains(layer.Id) &&
                        IsWithinMatchedLayerSubtree(
                        layer.Id,
                        matchingLayerIds,
                        layersById))
                        includedLayerIds.Add(layer.Id);
                }

                foreach (var item in model.Objects)
                {
                    if (!candidateObjectIds.Contains(item.Id))
                        continue;

                    if (matchingObjectIds.Contains(item.Id) ||
                        includedLayerIds.Contains(item.LayerId))
                        includedObjectIds.Add(item.Id);
                }

                foreach (var objectId in matchingObjectIds)
                {
                    AddObjectPath(
                        objectId,
                        objectsById,
                        layersById,
                        includedLayerIds,
                        temporaryExpandedLayerIds);
                    OutlinerObjectModel matchingObject;
                    if (objectsById.TryGetValue(objectId, out matchingObject))
                    {
                        AddCanonicalGroupPath(
                            matchingObject.Id,
                            groupHierarchy,
                            temporaryExpandedGroupIds);
                    }
                }

                foreach (var layerId in matchingLayerIds)
                {
                    includedLayerIds.Add(layerId);
                    AddPathToRoot(
                        layerId,
                        layersById,
                        includedLayerIds,
                        temporaryExpandedLayerIds);
                }

                foreach (var layerId in includedLayerIds.ToList())
                    AddAncestors(layerId, layersById, includedLayerIds);
            }

            foreach (var layerId in viewState.TemporarilyRevealedLayerIds)
            {
                if (!layersById.ContainsKey(layerId))
                    continue;

                includedLayerIds.Add(layerId);
                AddPathToRoot(
                    layerId,
                    layersById,
                    includedLayerIds,
                    temporaryExpandedLayerIds);
            }

            foreach (var objectId in viewState.TemporarilyRevealedObjectIds)
            {
                if (!objectsById.ContainsKey(objectId))
                    continue;

                includedObjectIds.Add(objectId);
                AddObjectPath(
                    objectId,
                    objectsById,
                    layersById,
                    includedLayerIds,
                    temporaryExpandedLayerIds);
                AddCanonicalGroupPath(
                    objectId,
                    groupHierarchy,
                    temporaryExpandedGroupIds);
            }

            if (viewState.HasReveal && objectsById.ContainsKey(viewState.RevealedObjectId))
            {
                includedObjectIds.Add(viewState.RevealedObjectId);
                AddObjectPath(
                    viewState.RevealedObjectId,
                    objectsById,
                    layersById,
                    includedLayerIds,
                    temporaryExpandedLayerIds);
                AddCanonicalGroupPath(
                    viewState.RevealedObjectId,
                    groupHierarchy,
                    temporaryExpandedGroupIds);
            }

            foreach (var objectId in includedObjectIds)
            {
                OutlinerObjectModel item;
                if (!objectsById.TryGetValue(objectId, out item))
                    continue;
                foreach (var groupId in item.GroupIds)
                    includedGroupIds.Add(groupId);
            }
        }

        private static void AddObjectPath(
            Guid objectId,
            IDictionary<Guid, OutlinerObjectModel> objectsById,
            IDictionary<Guid, OutlinerLayerModel> layersById,
            ISet<Guid> includedLayerIds,
            ISet<Guid> temporaryExpandedLayerIds)
        {
            OutlinerObjectModel item;
            if (!objectsById.TryGetValue(objectId, out item) || item.LayerId == Guid.Empty)
                return;
            AddPathToRoot(
                item.LayerId,
                layersById,
                includedLayerIds,
                temporaryExpandedLayerIds);
        }

        private static void AddCanonicalGroupPath(
            Guid objectId,
            OutlinerGroupHierarchy hierarchy,
            ISet<Guid> expandedGroupIds)
        {
            foreach (var groupId in hierarchy.GetCanonicalGroupPath(objectId))
                expandedGroupIds.Add(groupId);
        }

        private static void AddGroupPath(
            Guid groupId,
            OutlinerGroupHierarchy hierarchy,
            ISet<Guid> expandedGroupIds)
        {
            var currentId = groupId;
            var visited = new HashSet<Guid>();
            while (currentId != Guid.Empty && visited.Add(currentId))
            {
                expandedGroupIds.Add(currentId);
                currentId = hierarchy.GetParentGroupId(currentId);
            }
        }

        private static bool IsWithinMatchedLayerSubtree(
            Guid layerId,
            ISet<Guid> matchingLayerIds,
            IDictionary<Guid, OutlinerLayerModel> layersById)
        {
            var currentId = layerId;
            var visited = new HashSet<Guid>();
            while (currentId != Guid.Empty && visited.Add(currentId))
            {
                if (matchingLayerIds.Contains(currentId))
                    return true;
                OutlinerLayerModel layer;
                if (!layersById.TryGetValue(currentId, out layer))
                    break;
                currentId = layer.ParentId;
            }
            return false;
        }

        private static void AddPathToRoot(
            Guid layerId,
            IDictionary<Guid, OutlinerLayerModel> layersById,
            ISet<Guid> includedLayerIds,
            ISet<Guid> expandedLayerIds)
        {
            var currentId = layerId;
            var visited = new HashSet<Guid>();
            while (currentId != Guid.Empty && visited.Add(currentId))
            {
                includedLayerIds.Add(currentId);
                expandedLayerIds.Add(currentId);
                OutlinerLayerModel layer;
                if (!layersById.TryGetValue(currentId, out layer))
                    break;
                currentId = layer.ParentId;
            }
        }

        private static void AddAncestors(
            Guid layerId,
            IDictionary<Guid, OutlinerLayerModel> layersById,
            ISet<Guid> includedLayerIds)
        {
            OutlinerLayerModel layer;
            if (!layersById.TryGetValue(layerId, out layer))
                return;

            var currentId = layer.ParentId;
            var visited = new HashSet<Guid>();
            while (currentId != Guid.Empty && visited.Add(currentId))
            {
                includedLayerIds.Add(currentId);
                if (!layersById.TryGetValue(currentId, out layer))
                    break;
                currentId = layer.ParentId;
            }
        }

        private static TreeGridItem CreateTreeItem(params object[] values)
        {
            // Passing an untyped null directly to TreeGridItem can select its
            // children-first overload and fail at runtime with AddRange(null).
            var extendedValues = new object[
                Math.Max(values.Length, OutlinerValueIndex.DimName + 1)];
            Array.Copy(values, extendedValues, values.Length);
            extendedValues[OutlinerValueIndex.ContainsSelectedDescendant] = false;
            return new TreeGridItem(extendedValues);
        }
    }
}
