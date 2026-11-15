using System;
using System.Collections.Generic;
using System.Linq;
using Rhino;
using Rhino.DocObjects;

namespace ObjectManager.Outliner
{
    internal static class OutlinerStateSynchronizer
    {
        public static bool Refresh(
            RhinoDoc doc,
            OutlinerDocumentModel model,
            OutlinerSnapshot snapshot,
            OutlinerIconFactory icons,
            OutlinerIsolateState isolateState,
            OutlinerStateChange change,
            bool allowMissingObjects,
            ICollection<Eto.Forms.TreeGridItem> refreshedItems)
        {
            if (doc == null ||
                model == null ||
                snapshot == null ||
                icons == null ||
                change == null)
            {
                return false;
            }

            var layersById = model.LayersById;
            var objectsById = model.ObjectsById;
            var changedLayerIds = new HashSet<Guid>();
            var changedLayerPropertyIds = new HashSet<Guid>();
            var changedObjectIds = new HashSet<Guid>();
            var changedGroupIds = new HashSet<Guid>();

            foreach (var groupId in change.GroupIds)
            {
                if (!model.GroupsById.TryGetValue(groupId, out var groupModel))
                {
                    if (allowMissingObjects)
                        continue;
                    return false;
                }
                groupModel.IsFavorite = ObjectManagerPlugin.Instance
                    ?.FavoriteStore?.IsFavorite(
                        doc.RuntimeSerialNumber,
                        groupId) == true;
                changedGroupIds.Add(groupId);
            }

            if (change.CurrentLayerChanged)
            {
                var currentLayerIndex = doc.Layers.CurrentLayerIndex;
                foreach (var layer in model.Layers.Where(item =>
                    item.IsCurrent || item.Index == currentLayerIndex))
                {
                    var isCurrent = layer.Index == currentLayerIndex;
                    if (layer.IsCurrent == isCurrent)
                        continue;
                    layer.IsCurrent = isCurrent;
                    changedLayerIds.Add(layer.Id);
                }
            }

            if (change.LayerStateChanged &&
                change.LayerIds.Count > 0 &&
                !RefreshLayerStates(
                    doc,
                    model,
                    layersById,
                    change.LayerIds,
                    changedLayerIds,
                    changedObjectIds,
                    changedGroupIds))
            {
                return false;
            }

            if (change.LayerPropertiesChanged &&
                !RefreshLayerProperties(
                    doc,
                    model,
                    change.LayerNamesChanged
                        ? OutlinerLayerHierarchy.GetSubtreeIds(model, change.LayerIds)
                        : change.LayerIds,
                    changedLayerPropertyIds))
            {
                return false;
            }

            if (change.LayerNamesChanged)
            {
                foreach (var definition in model.BlockDefinitionsById.Values)
                foreach (var component in definition.Components)
                {
                    if (model.LayersByIndex.TryGetValue(component.LayerIndex, out var layer))
                        component.LayerPath = layer.FullPath;
                }
            }

            foreach (var objectId in change.ObjectIds)
            {
                if (!objectsById.TryGetValue(objectId, out var objectModel))
                {
                    if (allowMissingObjects)
                        continue;
                    return false;
                }

                var rhinoObject = doc.Objects.FindId(objectId);
                if (rhinoObject == null || rhinoObject.IsDeleted)
                    return false;
                if (rhinoObject.Attributes.LayerIndex != objectModel.LayerIndex)
                    return false;

                var favorite = FavoriteCodec.Read(
                    rhinoObject.Attributes.GetUserString(
                        FavoriteCodec.UserStringKey));

                if (objectModel.IsHidden == rhinoObject.IsHidden &&
                    objectModel.IsLocked == rhinoObject.IsLocked &&
                    objectModel.IsReference == rhinoObject.IsReference &&
                    objectModel.IsFavorite == favorite.IsFavorite &&
                    objectModel.CanEditFavorite ==
                        (favorite.CanEdit && !rhinoObject.IsReference))
                {
                    continue;
                }

                objectModel.IsHidden = rhinoObject.IsHidden;
                objectModel.IsLocked = rhinoObject.IsLocked;
                objectModel.IsReference = rhinoObject.IsReference;
                objectModel.IsFavorite = favorite.IsFavorite;
                objectModel.CanEditFavorite = favorite.CanEdit &&
                    !rhinoObject.IsReference;
                changedObjectIds.Add(objectId);
                changedGroupIds.UnionWith(objectModel.GroupIds);
            }

            if (change.IsolateChanged)
            {
                isolateState?.RebuildPresentation(model);
                changedLayerIds.UnionWith(model.Layers.Select(item => item.Id));
                changedObjectIds.UnionWith(
                    change.ObjectIds.Where(objectsById.ContainsKey));
                changedGroupIds.UnionWith(model.Groups.Select(item => item.Id));
            }

            foreach (var layerId in changedLayerIds)
            {
                if (snapshot.LayerItems.TryGetValue(layerId, out var item))
                {
                    ApplyLayerState(
                        item,
                        layersById[layerId],
                        icons,
                        isolateState);
                    refreshedItems?.Add(item);
                }
            }

            foreach (var layerId in changedLayerPropertyIds)
            {
                if (snapshot.LayerItems.TryGetValue(layerId, out var item))
                {
                    ApplyLayerProperties(item, layersById[layerId], icons);
                    refreshedItems?.Add(item);
                }
            }

            foreach (var objectId in changedObjectIds)
            {
                var objectModel = objectsById[objectId];
                if (snapshot.ObjectItems.TryGetValue(objectId, out var item))
                {
                    ApplyObjectState(
                        item,
                        objectModel,
                        icons,
                        isolateState,
                        true);
                    refreshedItems?.Add(item);
                }

                if (snapshot.ObjectReferenceItems.TryGetValue(
                    objectId,
                    out var references))
                {
                    foreach (var reference in references)
                    {
                        ApplyObjectState(
                            reference,
                            objectModel,
                            icons,
                            isolateState,
                            false);
                        refreshedItems?.Add(reference);
                    }
                }
            }

            foreach (var groupId in changedGroupIds)
            {
                ApplyGroupState(
                    groupId,
                    model,
                    objectsById,
                    snapshot,
                    icons,
                    isolateState,
                    refreshedItems);
            }

            return true;
        }

        private static bool RefreshLayerProperties(
            RhinoDoc doc,
            OutlinerDocumentModel model,
            IEnumerable<Guid> layerIds,
            ISet<Guid> changedLayerIds)
        {
            foreach (var layerId in layerIds.Distinct())
            {
                if (!model.LayersById.TryGetValue(
                    layerId,
                    out var layerModel))
                {
                    return false;
                }

                var layer = doc.Layers.FindId(layerId);
                if (layer == null ||
                    layer.IsDeleted ||
                    layer.Index != layerModel.Index ||
                    layer.ParentLayerId != layerModel.ParentId)
                {
                    return false;
                }

                layerModel.Name = layer.Name;
                layerModel.FullPath = layer.FullPath;
                OutlinerDocumentReader.ApplyLayerProperties(
                    doc,
                    layer,
                    layerModel);
                changedLayerIds.Add(layerId);
            }
            return true;
        }

        private static bool RefreshLayerStates(
            RhinoDoc doc,
            OutlinerDocumentModel model,
            IDictionary<Guid, OutlinerLayerModel> layersById,
            IEnumerable<Guid> requestedLayerIds,
            ISet<Guid> changedLayerIds,
            ISet<Guid> changedObjectIds,
            ISet<Guid> changedGroupIds)
        {
            var affectedLayerIds = GetAffectedLayerIds(
                model,
                requestedLayerIds);
            if (affectedLayerIds.Count == 0)
                return true;

            var rhinoLayersById = new Dictionary<Guid, Layer>();
            foreach (var layerId in affectedLayerIds)
            {
                var currentLayerId = layerId;
                var visited = new HashSet<Guid>();
                while (currentLayerId != Guid.Empty && visited.Add(currentLayerId))
                {
                    if (!layersById.TryGetValue(
                        currentLayerId,
                        out var currentLayerModel))
                    {
                        return false;
                    }

                    if (!rhinoLayersById.ContainsKey(currentLayerId))
                    {
                        var currentLayer = doc.Layers.FindId(currentLayerId);
                        if (currentLayer == null || currentLayer.IsDeleted)
                            return false;
                        rhinoLayersById[currentLayerId] = currentLayer;
                    }
                    currentLayerId = currentLayerModel.ParentId;
                }
            }

            var states = new Dictionary<Guid, LayerState>();
            foreach (var layerId in affectedLayerIds)
            {
                GetLayerState(
                    rhinoLayersById[layerId],
                    rhinoLayersById,
                    states,
                    new HashSet<Guid>());
            }

            foreach (var layerId in affectedLayerIds)
            {
                var layerModel = layersById[layerId];
                var state = states[layerModel.Id];
                var isCurrent = layerModel.Index == doc.Layers.CurrentLayerIndex;
                if (layerModel.OwnVisible == state.OwnVisible &&
                    layerModel.OwnLocked == state.OwnLocked &&
                    layerModel.InheritedHidden == state.InheritedHidden &&
                    layerModel.InheritedLocked == state.InheritedLocked &&
                    layerModel.IsCurrent == isCurrent)
                {
                    continue;
                }

                layerModel.OwnVisible = state.OwnVisible;
                layerModel.OwnLocked = state.OwnLocked;
                layerModel.InheritedHidden = state.InheritedHidden;
                layerModel.InheritedLocked = state.InheritedLocked;
                layerModel.IsCurrent = isCurrent;
                changedLayerIds.Add(layerModel.Id);
            }

            foreach (var layerId in affectedLayerIds)
            {
                if (!states.TryGetValue(layerId, out var state))
                {
                    return false;
                }

                if (!model.ObjectsByLayerId.TryGetValue(
                    layerId,
                    out var layerObjects))
                {
                    continue;
                }

                var inheritedHidden = !state.EffectiveVisible;
                var inheritedLocked = state.EffectiveLocked;
                foreach (var objectModel in layerObjects)
                {
                    if (objectModel.InheritedHidden == inheritedHidden &&
                        objectModel.InheritedLocked == inheritedLocked)
                    {
                        continue;
                    }

                    objectModel.InheritedHidden = inheritedHidden;
                    objectModel.InheritedLocked = inheritedLocked;
                    changedObjectIds.Add(objectModel.Id);
                    changedGroupIds.UnionWith(objectModel.GroupIds);
                }
            }

            return true;
        }

        private static ISet<Guid> GetAffectedLayerIds(
            OutlinerDocumentModel model,
            IEnumerable<Guid> requestedLayerIds)
        {
            var affected = new HashSet<Guid>();
            var pending = new Queue<Guid>((requestedLayerIds ??
                Enumerable.Empty<Guid>()).Where(id => id != Guid.Empty));
            while (pending.Count > 0)
            {
                var layerId = pending.Dequeue();
                if (!model.LayersById.ContainsKey(layerId) ||
                    !affected.Add(layerId))
                {
                    continue;
                }

                if (!model.ChildLayerIdsByParentId.TryGetValue(
                    layerId,
                    out var childIds))
                {
                    continue;
                }
                foreach (var childId in childIds)
                    pending.Enqueue(childId);
            }
            return affected;
        }

        private static LayerState GetLayerState(
            Layer layer,
            IDictionary<Guid, Layer> layersById,
            IDictionary<Guid, LayerState> states,
            ISet<Guid> visiting)
        {
            if (states.TryGetValue(layer.Id, out var existing))
                return existing;

            LayerState parentState = null;
            if (layer.ParentLayerId != Guid.Empty &&
                visiting.Add(layer.Id) &&
                layersById.TryGetValue(layer.ParentLayerId, out var parent))
            {
                parentState = GetLayerState(parent, layersById, states, visiting);
                visiting.Remove(layer.Id);
            }

            var inheritedHidden = parentState != null &&
                !parentState.EffectiveVisible;
            var inheritedLocked = parentState != null &&
                parentState.EffectiveLocked;
            var state = new LayerState(
                inheritedHidden ? layer.PersistentVisibility : layer.IsVisible,
                inheritedLocked ? layer.GetPersistentLocking() : layer.IsLocked,
                inheritedHidden,
                inheritedLocked);
            states[layer.Id] = state;
            return state;
        }

        private static void ApplyLayerState(
            Eto.Forms.TreeGridItem item,
            OutlinerLayerModel layer,
            OutlinerIconFactory icons,
            OutlinerIsolateState isolateState)
        {
            item.SetValue(
                OutlinerValueIndex.Current,
                layer.IsCurrent ? icons.GetCurrentLayerIcon() : null);
            item.SetValue(
                OutlinerValueIndex.Visibility,
                OutlinerTreeProjector.GetLayerVisibilityIcon(
                    layer,
                    icons,
                    isolateState));
            item.SetValue(
                OutlinerValueIndex.Lock,
                OutlinerTreeProjector.GetLayerLockIcon(
                    layer,
                    icons,
                    isolateState));
            item.SetValue(
                OutlinerValueIndex.IsUnavailable,
                layer.IsUnavailable);
            item.SetValue(OutlinerValueIndex.DimName, !layer.OwnVisible || layer.InheritedHidden);
        }

        private static void ApplyObjectState(
            Eto.Forms.TreeGridItem item,
            OutlinerObjectModel objectModel,
            OutlinerIconFactory icons,
            OutlinerIsolateState isolateState,
            bool canonical)
        {
            item.SetValue(
                OutlinerValueIndex.Icon,
                icons.GetObjectIcon(
                    objectModel.IconKind,
                    objectModel.IsUnavailable));
            if (canonical)
            {
                item.SetValue(
                    OutlinerValueIndex.Favorite,
                    icons.GetFavoriteIcon(
                        objectModel.IsFavorite,
                        !objectModel.CanEditFavorite));
                item.SetValue(
                    OutlinerValueIndex.Visibility,
                    OutlinerTreeProjector.GetObjectVisibilityIcon(
                        objectModel,
                        icons,
                        isolateState));
                item.SetValue(
                    OutlinerValueIndex.Lock,
                    OutlinerTreeProjector.GetObjectLockIcon(
                        objectModel,
                        icons,
                        isolateState));
            }
            item.SetValue(
                OutlinerValueIndex.IsUnavailable,
                objectModel.IsUnavailable);
        }

        private static void ApplyLayerProperties(
            Eto.Forms.TreeGridItem item,
            OutlinerLayerModel layer,
            OutlinerIconFactory icons)
        {
            item.SetValue(OutlinerValueIndex.Name, layer.Name);
            item.SetValue(
                OutlinerValueIndex.Color,
                icons.GetColorSwatch(layer.Color));
            item.SetValue(
                OutlinerValueIndex.Favorite,
                icons.GetFavoriteIcon(
                    layer.IsFavorite,
                    !layer.CanEditFavorite));
            item.SetValue(
                OutlinerValueIndex.Material,
                icons.GetMaterialSwatch(layer.HasMaterial));
            item.SetValue(OutlinerValueIndex.Linetype, layer.LinetypeName);
            item.SetValue(
                OutlinerValueIndex.PlotColor,
                icons.GetColorSwatch(layer.PlotColor));
            item.SetValue(OutlinerValueIndex.PlotWeight, layer.PlotWeightName);
            item.SetValue(
                OutlinerValueIndex.SectionStyle,
                layer.HasCustomSectionStyle
                    ? OutlinerStrings.Custom
                    : OutlinerStrings.Default);
            item.SetValue(
                OutlinerValueIndex.NewDetailVisibility,
                icons.GetNewDetailVisibilityIcon(layer.NewDetailVisible));
            item.SetValue(
                OutlinerValueIndex.ModelVisibility,
                icons.GetModelVisibilityIcon(
                    layer.ModelVisible,
                    !layer.OwnVisible || layer.InheritedHidden));
        }

        private static void ApplyGroupState(
            Guid groupId,
            OutlinerDocumentModel model,
            IDictionary<Guid, OutlinerObjectModel> objectsById,
            OutlinerSnapshot snapshot,
            OutlinerIconFactory icons,
            OutlinerIsolateState isolateState,
            ICollection<Eto.Forms.TreeGridItem> refreshedItems)
        {
            if (!model.GroupsById.TryGetValue(groupId, out var group))
                return;

            var members = group.MemberIds
                .Where(objectsById.ContainsKey)
                .Select(id => objectsById[id])
                .ToList();
            var visibility = OutlinerTreeProjector.GetGroupVisibilityIcon(
                members,
                icons,
                isolateState,
                groupId);
            var locking = OutlinerTreeProjector.GetGroupLockIcon(
                members,
                icons,
                isolateState,
                groupId);

            if (!snapshot.GroupSliceItemsByGroupId.TryGetValue(
                groupId,
                out var groupItems))
            {
                return;
            }

            var dimName = OutlinerTreeProjector.AllMembersHidden(members);
            foreach (var item in groupItems)
            {
                item.SetValue(OutlinerValueIndex.DimName, dimName);
                item.SetValue(
                    OutlinerValueIndex.Favorite,
                    icons.GetFavoriteIcon(
                        group.IsFavorite,
                        !group.CanEditFavorite));
                item.SetValue(OutlinerValueIndex.Visibility, visibility);
                item.SetValue(OutlinerValueIndex.Lock, locking);
                refreshedItems?.Add(item);
            }
        }

        private sealed class LayerState
        {
            public LayerState(
                bool ownVisible,
                bool ownLocked,
                bool inheritedHidden,
                bool inheritedLocked)
            {
                OwnVisible = ownVisible;
                OwnLocked = ownLocked;
                InheritedHidden = inheritedHidden;
                InheritedLocked = inheritedLocked;
            }

            public bool OwnVisible { get; }

            public bool OwnLocked { get; }

            public bool InheritedHidden { get; }

            public bool InheritedLocked { get; }

            public bool EffectiveVisible => OwnVisible && !InheritedHidden;

            public bool EffectiveLocked => OwnLocked || InheritedLocked;
        }
    }
}
