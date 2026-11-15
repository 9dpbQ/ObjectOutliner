using System;
using System.Collections.Generic;
using System.Linq;
using Rhino;
using Rhino.DocObjects;

namespace ObjectManager.Outliner
{
    internal static class OutlinerDocumentReader
    {
        public static OutlinerDocumentModel Read(
            RhinoDoc doc,
            IDictionary<Guid, string> provisionalNames,
            OutlinerRefreshMetrics metrics = null)
        {
            List<Layer> layers = null;
            IDictionary<int, Layer> layersByIndex = null;
            IDictionary<Guid, LayerState> layerStates = null;
            List<OutlinerLayerModel> layerModels = null;
            Measure(metrics, OutlinerRefreshStage.Layers, () =>
            {
                layers = GetActiveLayers(doc);
                var layersById = layers.ToDictionary(layer => layer.Id);
                layersByIndex = layers.ToDictionary(layer => layer.Index);
                layerStates = CreateLayerStates(layers, layersById);
                layerModels = CreateLayerEntries(doc, layers, layerStates);
            });
            var objects = Measure(
                metrics,
                OutlinerRefreshStage.ObjectEnumeration,
                () => GetTopLevelObjects(doc));
            var objectModels = Measure(
                metrics,
                OutlinerRefreshStage.ObjectPresentation,
                () => CreateObjectEntries(
                    doc,
                    objects,
                    provisionalNames,
                    layersByIndex,
                    layerStates));
            var groupModels = Measure(
                metrics,
                OutlinerRefreshStage.Groups,
                () => CreateGroupEntries(doc, objects, objectModels));
            var blockDefinitions = ReadBlockDefinitions(doc, objectModels);
            var historyByObjectId = Measure(
                metrics,
                OutlinerRefreshStage.History,
                () => new Dictionary<Guid, OutlinerHistoryModel>());

            return new OutlinerDocumentModel(
                layerModels,
                objectModels,
                groupModels,
                historyByObjectId,
                blockDefinitions);
        }

        public static OutlinerDocumentModel ReadLayersOnly(RhinoDoc doc)
        {
            return ReadLayers(doc, null, new HashSet<int>());
        }

        internal static OutlinerLayerModel ReadLayer(RhinoDoc doc, int layerIndex)
        {
            if (doc == null || layerIndex < 0)
                return null;

            var layers = GetActiveLayers(doc);
            var layersById = layers.ToDictionary(layer => layer.Id);
            var layerStates = CreateLayerStates(layers, layersById);
            return CreateLayerEntries(doc, layers, layerStates)
                .FirstOrDefault(layer => layer.Index == layerIndex);
        }

        public static OutlinerDocumentModel ReadLayers(
            RhinoDoc doc,
            IDictionary<Guid, string> provisionalNames,
            ISet<int> loadedLayerIndices,
            OutlinerRefreshMetrics metrics = null)
        {
            var loadedIndices = loadedLayerIndices ?? new HashSet<int>();
            List<Layer> layers = null;
            IDictionary<int, Layer> layersByIndex = null;
            IDictionary<Guid, LayerState> layerStates = null;
            List<OutlinerLayerModel> layerModels = null;
            Measure(metrics, OutlinerRefreshStage.Layers, () =>
            {
                layers = GetActiveLayers(doc);
                var layersById = layers.ToDictionary(layer => layer.Id);
                layersByIndex = layers.ToDictionary(layer => layer.Index);
                layerStates = CreateLayerStates(layers, layersById);
                layerModels = CreateLayerEntries(
                    doc,
                    layers,
                    layerStates,
                    loadedIndices);
            });
            var objects = Measure(
                metrics,
                OutlinerRefreshStage.ObjectEnumeration,
                () => GetTopLevelObjects(doc, loadedIndices));
            var objectModels = Measure(
                metrics,
                OutlinerRefreshStage.ObjectPresentation,
                () => CreateObjectEntries(
                    doc,
                    objects,
                    provisionalNames ?? new Dictionary<Guid, string>(),
                    layersByIndex,
                    layerStates));
            var groupModels = Measure(
                metrics,
                OutlinerRefreshStage.Groups,
                () => CreateGroupEntries(doc, objects, objectModels));
            var blockDefinitions = ReadBlockDefinitions(doc, objectModels);
            return new OutlinerDocumentModel(
                layerModels,
                objectModels,
                groupModels,
                new Dictionary<Guid, OutlinerHistoryModel>(),
                blockDefinitions);
        }

        public static int CountTopLevelObjects(RhinoDoc doc)
        {
            return doc.Objects.ObjectCount(CreateObjectEnumeratorSettings());
        }

        public static IDictionary<int, int> CountTopLevelObjectsByLayer(
            RhinoDoc doc)
        {
            var counts = new Dictionary<int, int>();
            if (doc == null)
                return counts;

            foreach (var item in doc.Objects.GetObjectList(
                CreateObjectEnumeratorSettings()))
            {
                if (item == null || item.IsDeleted)
                    continue;

                var layerIndex = item.Attributes.LayerIndex;
                counts[layerIndex] = counts.TryGetValue(
                    layerIndex,
                    out var count)
                        ? count + 1
                        : 1;
            }
            return counts;
        }

        internal static bool TryReadLayerObjects(
            RhinoDoc doc,
            OutlinerDocumentModel model,
            IEnumerable<int> layerIndices,
            IDictionary<Guid, string> provisionalNames,
            out IDictionary<Guid, IList<OutlinerObjectModel>> objectsByLayerId,
            out string failureReason,
            IDictionary<int, OutlinerGroupModel> groupsByIndex = null)
        {
            objectsByLayerId = new Dictionary<Guid, IList<OutlinerObjectModel>>();
            failureReason = string.Empty;
            if (doc == null || model == null)
            {
                failureReason = "missing-document-model";
                return false;
            }

            var dirtyIndices = new HashSet<int>((layerIndices ??
                Enumerable.Empty<int>()).Where(index => index >= 0));
            if (dirtyIndices.Count == 0)
            {
                failureReason = "no-dirty-layers";
                return false;
            }

            var dirtyLayers = new Dictionary<int, OutlinerLayerModel>();
            foreach (var layerIndex in dirtyIndices)
            {
                if (!model.LayersByIndex.TryGetValue(
                    layerIndex,
                    out var layerModel))
                {
                    failureReason = "missing-layer-model";
                    return false;
                }

                var nativeLayer = doc.Layers[layerIndex];
                if (nativeLayer == null ||
                    nativeLayer.IsDeleted ||
                    nativeLayer.Id != layerModel.Id)
                {
                    failureReason = "changed-layer-table";
                    return false;
                }
                dirtyLayers[layerIndex] = layerModel;
                objectsByLayerId[layerModel.Id] =
                    new List<OutlinerObjectModel>();
            }

            var groupLookup = groupsByIndex ?? model.GroupsByIndex;
            var nameRecords = ObjectManagerPlugin.Instance?.NameStore
                ?.GetObjectSnapshot(doc.RuntimeSerialNumber);
            var usedNames = new HashSet<string>(model.Objects
                .Where(item => !dirtyIndices.Contains(item.LayerIndex))
                .Select(item => item.DisplayName)
                .Where(name => !string.IsNullOrWhiteSpace(name)),
                StringComparer.OrdinalIgnoreCase);
            var nativeObjects = GetTopLevelObjects(doc, dirtyIndices);
            foreach (var rhinoObject in nativeObjects)
            {
                if (ObjectNamingService.TryResolveName(
                        rhinoObject,
                        nameRecords,
                        out var resolvedName))
                {
                    usedNames.Add(resolvedName.Name);
                }
            }

            var nextNumberByBaseName = new Dictionary<string, int>(
                StringComparer.OrdinalIgnoreCase);
            foreach (var rhinoObject in nativeObjects)
            {
                if (!dirtyLayers.TryGetValue(
                    rhinoObject.Attributes.LayerIndex,
                    out var layerModel))
                {
                    failureReason = "object-outside-dirty-layers";
                    return false;
                }

                var presentation = ObjectPresentation.FromObject(
                    rhinoObject,
                    doc.ModelAbsoluteTolerance,
                    doc.ModelAngleToleranceRadians);
                var hasResolvedName = ObjectNamingService.TryResolveName(
                    rhinoObject,
                    nameRecords,
                    out var resolvedName);
                var provisional = !hasResolvedName;
                string displayName;
                if (hasResolvedName)
                {
                    provisionalNames?.Remove(rhinoObject.Id);
                    displayName = resolvedName.Name;
                }
                else if (provisionalNames != null &&
                         provisionalNames.TryGetValue(
                             rhinoObject.Id,
                             out displayName) &&
                         IsProvisionalNameForBase(
                             displayName,
                             presentation.BaseName) &&
                         !usedNames.Contains(displayName))
                {
                    usedNames.Add(displayName);
                }
                else
                {
                    displayName = NextAvailableName(
                        presentation.BaseName,
                        usedNames,
                        nextNumberByBaseName);
                    if (provisionalNames != null)
                        provisionalNames[rhinoObject.Id] = displayName;
                }

                var groupIds = new List<Guid>();
                foreach (var groupIndex in rhinoObject.Attributes
                    .GetGroupList() ?? Array.Empty<int>())
                {
                    if (!groupLookup.TryGetValue(
                        groupIndex,
                        out var groupModel))
                    {
                        failureReason = "unknown-group-membership";
                        return false;
                    }
                    if (!groupIds.Contains(groupModel.Id))
                        groupIds.Add(groupModel.Id);
                }

                var hasOrder = ObjectOrderStore.TryGetOrder(
                    rhinoObject,
                    out var sortOrder);
                var favorite = FavoriteCodec.Read(
                    rhinoObject.Attributes.GetUserString(
                        FavoriteCodec.UserStringKey));
                objectsByLayerId[layerModel.Id].Add(
                    new OutlinerObjectModel
                    {
                        Note = ObjectNote.Normalize(rhinoObject.Attributes.GetUserString(ObjectNote.Key)),
                        Id = rhinoObject.Id,
                        RuntimeSerialNumber = rhinoObject.RuntimeSerialNumber,
                        LayerIndex = layerModel.Index,
                        LayerId = layerModel.Id,
                        SortOrder = hasOrder ? (int?)sortOrder : null,
                        DisplayName = displayName,
                        NameKind = hasResolvedName
                            ? resolvedName.Kind
                            : OutlinerResolvedNameKind.Ephemeral,
                        IsProvisional = provisional,
                        IsUserNamed = hasResolvedName &&
                            resolvedName.IsUserNamed,
                        IconKind = presentation.IconKind,
                        IsHidden = rhinoObject.IsHidden,
                        IsLocked = rhinoObject.IsLocked,
                        IsReference = rhinoObject.IsReference,
                        IsFavorite = favorite.IsFavorite,
                        CanEditFavorite = favorite.CanEdit &&
                            !rhinoObject.IsReference,
                        InheritedHidden = !layerModel.OwnVisible ||
                            layerModel.InheritedHidden,
                        InheritedLocked = layerModel.OwnLocked ||
                            layerModel.InheritedLocked,
                        GroupIds = groupIds
                    });
            }
            return true;
        }

        internal static bool TryReadGroups(
            RhinoDoc doc,
            OutlinerDocumentModel model,
            IEnumerable<int> groupIndices,
            out IDictionary<int, OutlinerGroupModel> replacements,
            out string failureReason)
        {
            replacements = new Dictionary<int, OutlinerGroupModel>();
            failureReason = string.Empty;
            if (doc == null || model == null)
            {
                failureReason = "missing-document-model";
                return false;
            }

            var indices = new HashSet<int>((groupIndices ??
                Enumerable.Empty<int>()).Where(index => index >= 0));
            if (indices.Count == 0)
            {
                failureReason = "no-dirty-groups";
                return false;
            }

            var favoriteGroupIds = ObjectManagerPlugin.Instance?.FavoriteStore
                ?.GetFavorites(doc.RuntimeSerialNumber) ?? new HashSet<Guid>();
            foreach (var index in indices)
            {
                var nativeGroup = doc.Groups.FindIndex(index);
                if (nativeGroup == null || nativeGroup.IsDeleted)
                {
                    replacements[index] = null;
                    continue;
                }

                var name = ObjectNamingService.ResolveGroupDisplayName(
                    doc,
                    nativeGroup,
                    OutlinerStrings.FormatDefaultGroupName(index + 1));
                var memberIds = (doc.Groups.GroupMembers(index) ??
                    Array.Empty<RhinoObject>())
                    .Where(item => item != null && !item.IsDeleted)
                    .Select(item => item.Id)
                    .Where(id => id != Guid.Empty)
                    .Distinct()
                    .ToList();
                if (memberIds.Count == 0)
                {
                    replacements[index] = null;
                    continue;
                }
                replacements[index] = new OutlinerGroupModel
                {
                    Id = nativeGroup.Id,
                    Note = ObjectNote.Normalize(nativeGroup.GetUserString(ObjectNote.Key)),
                    Index = index,
                    Name = name,
                    IsFavorite = favoriteGroupIds.Contains(nativeGroup.Id),
                    MemberIds = memberIds
                };
            }
            return true;
        }

        private static List<OutlinerLayerModel> CreateLayerEntries(
            RhinoDoc doc,
            IEnumerable<Layer> layers,
            IDictionary<Guid, LayerState> layerStates,
            ISet<int> loadedLayerIndices = null)
        {
            return layers.Select(layer =>
            {
                var state = layerStates[layer.Id];
                var model = new OutlinerLayerModel
                {
                    Id = layer.Id,
                    Index = layer.Index,
                    ParentId = layer.ParentLayerId,
                    Name = layer.Name,
                    FullPath = layer.FullPath,
                    DefaultExpanded = layer.IsExpanded,
                    IsCurrent = layer.Index == doc.Layers.CurrentLayerIndex,
                    OwnVisible = state.OwnVisible,
                    OwnLocked = state.OwnLocked,
                    InheritedHidden = state.InheritedHidden,
                    InheritedLocked = state.InheritedLocked,
                    ObjectsLoaded = loadedLayerIndices == null ||
                        loadedLayerIndices.Contains(layer.Index)
                };
                ApplyLayerProperties(doc, layer, model);
                return model;
            }).ToList();
        }

        internal static void ApplyLayerProperties(
            RhinoDoc doc,
            Layer layer,
            OutlinerLayerModel model)
        {
            if (doc == null || layer == null || model == null)
                return;

            model.Color = layer.Color;
            model.HasMaterial = layer.RenderMaterial != null ||
                layer.RenderMaterialIndex >= 0;
            model.LinetypeName = GetLinetypeName(doc, layer);
            model.PlotColor = layer.PlotColor;
            model.PlotWeightName = GetPlotWeightName(layer.PlotWeight);
            model.HasCustomSectionStyle = layer.GetCustomSectionStyle() != null;
            model.NewDetailVisible = layer.PerViewportIsVisibleInNewDetails;
            model.ModelVisible = layer.ModelIsVisible;
            var favorite = FavoriteCodec.Read(
                layer.GetUserString(FavoriteCodec.UserStringKey));
            model.IsFavorite = favorite.IsFavorite;
            model.CanEditFavorite = favorite.CanEdit && !layer.IsReference;
        }

        private static IDictionary<Guid, LayerState> CreateLayerStates(
            IEnumerable<Layer> layers,
            IDictionary<Guid, Layer> layersById)
        {
            var states = new Dictionary<Guid, LayerState>();
            foreach (var layer in layers)
                GetOrCreateLayerState(layer, layersById, states, new HashSet<Guid>());
            return states;
        }

        private static LayerState GetOrCreateLayerState(
            Layer layer,
            IDictionary<Guid, Layer> layersById,
            IDictionary<Guid, LayerState> states,
            ISet<Guid> visiting)
        {
            LayerState existing;
            if (states.TryGetValue(layer.Id, out existing))
                return existing;

            LayerState parentState = null;
            Layer parent;
            if (layer.ParentLayerId != Guid.Empty &&
                visiting.Add(layer.Id) &&
                layersById.TryGetValue(layer.ParentLayerId, out parent))
            {
                parentState = GetOrCreateLayerState(parent, layersById, states, visiting);
                visiting.Remove(layer.Id);
            }

            var inheritedHidden = parentState != null && !parentState.EffectiveVisible;
            var inheritedLocked = parentState != null && parentState.EffectiveLocked;
            var ownVisible = inheritedHidden ? layer.PersistentVisibility : layer.IsVisible;
            var ownLocked = inheritedLocked ? layer.GetPersistentLocking() : layer.IsLocked;
            var state = new LayerState(
                ownVisible,
                ownLocked,
                inheritedHidden,
                inheritedLocked);
            states[layer.Id] = state;
            return state;
        }

        private static string GetLinetypeName(RhinoDoc doc, Layer layer)
        {
            var linetype = layer.LinetypeIndex >= 0 && layer.LinetypeIndex < doc.Linetypes.Count
                ? doc.Linetypes[layer.LinetypeIndex]
                : null;
            return OutlinerStrings.LocalizeLinetypeName(linetype?.Name);
        }

        private static string GetPlotWeightName(double plotWeight)
        {
            if (plotWeight < 0)
                return OutlinerStrings.NoPrint;
            if (plotWeight == 0)
                return OutlinerStrings.Default;
            return string.Format("{0:0.##} mm", plotWeight);
        }

        private static List<Layer> GetActiveLayers(RhinoDoc doc)
        {
            var layers = new List<Layer>();
            var addedIndices = new HashSet<int>();
            var sortedIndices = doc.Layers.GetSorted();
            if (sortedIndices != null)
            {
                foreach (var index in sortedIndices)
                {
                    var layer = index >= 0 && index < doc.Layers.Count
                        ? doc.Layers[index]
                        : null;
                    if (layer != null && !layer.IsDeleted && addedIndices.Add(index))
                        layers.Add(layer);
                }
            }

            for (var index = 0; index < doc.Layers.Count; index++)
            {
                var layer = doc.Layers[index];
                if (layer != null && !layer.IsDeleted && addedIndices.Add(index))
                    layers.Add(layer);
            }

            return layers;
        }

        private static List<RhinoObject> GetTopLevelObjects(RhinoDoc doc)
        {
            return doc.Objects
                .GetObjectList(CreateObjectEnumeratorSettings())
                .Where(item => item != null && !item.IsDeleted)
                .OrderBy(item => item.RuntimeSerialNumber)
                .ToList();
        }

        private static List<RhinoObject> GetTopLevelObjects(
            RhinoDoc doc,
            IEnumerable<int> layerIndices)
        {
            var objectsById = new Dictionary<Guid, RhinoObject>();
            foreach (var layerIndex in layerIndices.Distinct())
            {
                var settings = CreateObjectEnumeratorSettings();
                settings.LayerIndexFilter = layerIndex;
                foreach (var item in doc.Objects.GetObjectList(settings))
                {
                    if (item != null && !item.IsDeleted)
                        objectsById[item.Id] = item;
                }
            }

            return objectsById.Values
                .OrderBy(item => item.RuntimeSerialNumber)
                .ToList();
        }

        private static ObjectEnumeratorSettings CreateObjectEnumeratorSettings()
        {
            return new ObjectEnumeratorSettings
            {
                ActiveObjects = true,
                DeletedObjects = false,
                HiddenObjects = true,
                IdefObjects = false,
                IncludeGrips = false,
                IncludeLights = true,
                IncludePhantoms = false,
                LockedObjects = true,
                NormalObjects = true,
                ReferenceObjects = true
            };
        }

        private static List<OutlinerObjectModel> CreateObjectEntries(
            RhinoDoc doc,
            IList<RhinoObject> objects,
            IDictionary<Guid, string> provisionalNames,
            IDictionary<int, Layer> layersByIndex,
            IDictionary<Guid, LayerState> layerStates)
        {
            var nameRecords = ObjectManagerPlugin.Instance?.NameStore
                ?.GetObjectSnapshot(doc.RuntimeSerialNumber);
            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var rhinoObject in objects)
            {
                if (ObjectNamingService.TryResolveName(
                        rhinoObject,
                        nameRecords,
                        out var resolvedName))
                {
                    usedNames.Add(resolvedName.Name);
                }
            }

            var nextNumberByBaseName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var entries = new List<OutlinerObjectModel>(objects.Count);

            foreach (var rhinoObject in objects)
            {
                var presentation = ObjectPresentation.FromObject(
                    rhinoObject,
                    doc.ModelAbsoluteTolerance,
                    doc.ModelAngleToleranceRadians);
                var hasResolvedName = ObjectNamingService.TryResolveName(
                    rhinoObject,
                    nameRecords,
                    out var resolvedName);
                var provisional = !hasResolvedName;
                string displayName;
                if (hasResolvedName)
                {
                    provisionalNames.Remove(rhinoObject.Id);
                    displayName = resolvedName.Name;
                }
                else if (provisionalNames.TryGetValue(rhinoObject.Id, out displayName) &&
                         IsProvisionalNameForBase(displayName, presentation.BaseName) &&
                         !usedNames.Contains(displayName))
                {
                    usedNames.Add(displayName);
                }
                else
                {
                    displayName = NextAvailableName(
                        presentation.BaseName,
                        usedNames,
                        nextNumberByBaseName);
                    provisionalNames[rhinoObject.Id] = displayName;
                }

                int sortOrder;
                var hasSortOrder = ObjectOrderStore.TryGetOrder(rhinoObject, out sortOrder);
                Layer objectLayer;
                layersByIndex.TryGetValue(
                    rhinoObject.Attributes.LayerIndex,
                    out objectLayer);
                LayerState objectLayerState = null;
                var hasLayerState = objectLayer != null &&
                    layerStates.TryGetValue(objectLayer.Id, out objectLayerState);
                var favorite = FavoriteCodec.Read(
                    rhinoObject.Attributes.GetUserString(
                        FavoriteCodec.UserStringKey));
                entries.Add(new OutlinerObjectModel
                {
                    Note = ObjectNote.Normalize(rhinoObject.Attributes.GetUserString(ObjectNote.Key)),
                    Id = rhinoObject.Id,
                    RuntimeSerialNumber = rhinoObject.RuntimeSerialNumber,
                    LayerIndex = rhinoObject.Attributes.LayerIndex,
                    LayerId = objectLayer?.Id ?? Guid.Empty,
                    BlockDefinitionId =
                        (rhinoObject as InstanceObject)
                            ?.InstanceDefinition?.Id ?? Guid.Empty,
                    SortOrder = hasSortOrder ? (int?)sortOrder : null,
                    DisplayName = displayName,
                    NameKind = hasResolvedName
                        ? resolvedName.Kind
                        : OutlinerResolvedNameKind.Ephemeral,
                    IsProvisional = provisional,
                    IsUserNamed = hasResolvedName &&
                        resolvedName.IsUserNamed,
                    IconKind = presentation.IconKind,
                    IsHidden = rhinoObject.IsHidden,
                    IsLocked = rhinoObject.IsLocked,
                    IsReference = rhinoObject.IsReference,
                    IsFavorite = favorite.IsFavorite,
                    CanEditFavorite = favorite.CanEdit &&
                        !rhinoObject.IsReference,
                    InheritedHidden = hasLayerState &&
                        !objectLayerState.EffectiveVisible,
                    InheritedLocked = hasLayerState &&
                        objectLayerState.EffectiveLocked
                });
            }

            return entries;
        }

        internal static IDictionary<Guid, OutlinerBlockDefinitionModel>
            ReadBlockDefinitions(
                RhinoDoc doc,
                IEnumerable<OutlinerObjectModel> objectModels)
        {
            var definitions = new Dictionary<Guid, OutlinerBlockDefinitionModel>();
            if (doc == null)
                return definitions;

            foreach (var definitionId in (objectModels ??
                    Enumerable.Empty<OutlinerObjectModel>())
                .Select(item => item?.BlockDefinitionId ?? Guid.Empty)
                .Where(id => id != Guid.Empty)
                .Distinct())
            {
                var definition = doc.InstanceDefinitions.FindId(definitionId);
                if (definition == null || definition.IsDeleted)
                    continue;

                var definitionObjects = definition.GetObjects() ??
                    Array.Empty<RhinoObject>();
                var usedNames = new HashSet<string>(
                    definitionObjects
                        .Where(item => item != null)
                        .Select(item => item.Attributes.Name)
                        .Where(name => !string.IsNullOrWhiteSpace(name)),
                    StringComparer.OrdinalIgnoreCase);
                var nextNumberByBaseName = new Dictionary<string, int>(
                    StringComparer.OrdinalIgnoreCase);
                var components = new List<OutlinerBlockComponentModel>();
                for (var index = 0; index < definitionObjects.Length; index++)
                {
                    var component = definitionObjects[index];
                    if (component == null || component.IsDeleted)
                        continue;

                    var presentation = ObjectPresentation.FromObject(
                        component,
                        doc.ModelAbsoluteTolerance,
                        doc.ModelAngleToleranceRadians);
                    var storedName = component.Attributes.Name;
                    var displayName = string.IsNullOrWhiteSpace(storedName)
                        ? NextAvailableName(
                            presentation.BaseName,
                            usedNames,
                            nextNumberByBaseName)
                        : storedName.Trim();
                    var layerIndex = component.Attributes.LayerIndex;
                    var layer = layerIndex >= 0 && layerIndex < doc.Layers.Count
                        ? doc.Layers[layerIndex]
                        : null;
                    components.Add(new OutlinerBlockComponentModel
                    {
                        ObjectId = component.Id,
                        Index = index,
                        LayerIndex = layerIndex,
                        LayerPath = layer?.FullPath ?? layer?.Name ?? string.Empty,
                        DisplayName = displayName,
                        IconKind = presentation.IconKind
                    });
                }

                definitions[definition.Id] = new OutlinerBlockDefinitionModel
                {
                    Id = definition.Id,
                    Name = definition.Name ?? string.Empty,
                    Components = components
                };
            }

            return definitions;
        }

        private static List<OutlinerGroupModel> CreateGroupEntries(
            RhinoDoc doc,
            IList<RhinoObject> objects,
            IList<OutlinerObjectModel> objectModels)
        {
            var memberships = new OutlinerGroupMembershipIndex();
            foreach (var rhinoObject in objects)
            {
                memberships.Add(
                    rhinoObject.Id,
                    rhinoObject.Attributes.GetGroupList());
            }

            var groups = new List<OutlinerGroupModel>();
            var groupsByIndex = new Dictionary<int, OutlinerGroupModel>();
            var favoriteGroupIds = ObjectManagerPlugin.Instance?.FavoriteStore
                ?.GetFavorites(doc.RuntimeSerialNumber) ?? new HashSet<Guid>();
            foreach (var entry in memberships.MemberIdsByGroupIndex
                .OrderBy(item => item.Key))
            {
                var index = entry.Key;
                var group = doc.Groups.FindIndex(index);
                if (group == null || group.IsDeleted)
                    continue;

                var memberIds = entry.Value;
                if (memberIds.Count == 0)
                    continue;

                var name = ObjectNamingService.ResolveGroupDisplayName(
                    doc,
                    group,
                    OutlinerStrings.FormatDefaultGroupName(index + 1));

                var model = new OutlinerGroupModel
                {
                    Id = group.Id,
                    Note = ObjectNote.Normalize(group.GetUserString(ObjectNote.Key)),
                    Index = index,
                    Name = name,
                    IsFavorite = favoriteGroupIds.Contains(group.Id),
                    MemberIds = memberIds
                };
                groups.Add(model);
                groupsByIndex[index] = model;
            }

            foreach (var objectModel in objectModels)
            {
                if (!memberships.GroupIndicesByObjectId.TryGetValue(
                    objectModel.Id,
                    out var groupIndices))
                {
                    continue;
                }

                objectModel.GroupIds = groupIndices
                    .Where(groupsByIndex.ContainsKey)
                    .Select(index => groupsByIndex[index].Id)
                    .Distinct()
                    .ToList();
            }

            return groups;
        }

        private static T Measure<T>(
            OutlinerRefreshMetrics metrics,
            OutlinerRefreshStage stage,
            Func<T> action)
        {
            return metrics == null ? action() : metrics.Measure(stage, action);
        }

        private static void Measure(
            OutlinerRefreshMetrics metrics,
            OutlinerRefreshStage stage,
            Action action)
        {
            if (metrics == null)
                action();
            else
                metrics.Measure(stage, action);
        }

        private static string NextAvailableName(
            string baseName,
            ISet<string> usedNames,
            IDictionary<string, int> nextNumberByBaseName)
        {
            int nextNumber;
            if (!nextNumberByBaseName.TryGetValue(baseName, out nextNumber))
                nextNumber = 1;

            string candidate;
            do
            {
                candidate = string.Format("{0}.{1:000}", baseName, nextNumber);
                nextNumber++;
            }
            while (usedNames.Contains(candidate));

            nextNumberByBaseName[baseName] = nextNumber;
            usedNames.Add(candidate);
            return candidate;
        }

        private static bool IsProvisionalNameForBase(string name, string baseName)
        {
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(baseName))
                return false;

            var prefix = baseName + ".";
            if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                name.Length != prefix.Length + 3)
                return false;

            return name.Skip(prefix.Length).All(char.IsDigit);
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
