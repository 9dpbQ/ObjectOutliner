using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ObjectManager.Outliner
{
    internal sealed class OutlinerDocumentModel
    {
        private OutlinerGroupHierarchy _groupHierarchy;

        public OutlinerDocumentModel(
            IList<OutlinerLayerModel> layers,
            IList<OutlinerObjectModel> objects,
            IList<OutlinerGroupModel> groups,
            IDictionary<Guid, OutlinerHistoryModel> historyByObjectId,
            IDictionary<Guid, OutlinerBlockDefinitionModel>
                blockDefinitionsById = null)
        {
            Layers = (layers ?? Array.Empty<OutlinerLayerModel>()).ToList();
            Objects = (objects ?? Array.Empty<OutlinerObjectModel>()).ToList();
            Groups = (groups ?? Array.Empty<OutlinerGroupModel>()).ToList();
            HistoryByObjectId = historyByObjectId ??
                new Dictionary<Guid, OutlinerHistoryModel>();
            BlockDefinitionsById = blockDefinitionsById ??
                new Dictionary<Guid, OutlinerBlockDefinitionModel>();
            LayersById = Layers.ToDictionary(item => item.Id);
            LayersByIndex = Layers.ToDictionary(item => item.Index);
            ObjectsById = Objects.ToDictionary(item => item.Id);
            GroupsById = Groups.ToDictionary(item => item.Id);
            GroupsByIndex = Groups.ToDictionary(item => item.Index);
            ObjectsByLayerId = Objects
                .GroupBy(item => item.LayerId)
                .ToDictionary(
                    group => group.Key,
                    group => (IList<OutlinerObjectModel>)group.ToList());
            foreach (var layer in Layers.Where(item => item.ObjectsLoaded))
            {
                layer.TopLevelObjectCount = ObjectsByLayerId.TryGetValue(
                    layer.Id,
                    out var layerObjects)
                        ? layerObjects.Count
                        : 0;
            }
            ChildLayerIdsByParentId = Layers
                .GroupBy(item => item.ParentId)
                .ToDictionary(
                    group => group.Key,
                    group => (IList<Guid>)group.Select(item => item.Id).ToList());
            GroupMembershipCount = Groups.Sum(item => item.MemberIds.Count);
        }

        public IList<OutlinerLayerModel> Layers { get; }

        public IList<OutlinerObjectModel> Objects { get; }

        public IList<OutlinerGroupModel> Groups { get; }

        public IDictionary<Guid, OutlinerHistoryModel> HistoryByObjectId { get; }

        public IDictionary<Guid, OutlinerBlockDefinitionModel>
            BlockDefinitionsById { get; }

        public IDictionary<Guid, OutlinerLayerModel> LayersById { get; }

        public IDictionary<int, OutlinerLayerModel> LayersByIndex { get; }

        public IDictionary<Guid, OutlinerObjectModel> ObjectsById { get; }

        public IDictionary<Guid, OutlinerGroupModel> GroupsById { get; }

        public IDictionary<int, OutlinerGroupModel> GroupsByIndex { get; }

        public IDictionary<Guid, IList<OutlinerObjectModel>> ObjectsByLayerId
        {
            get;
        }

        public IDictionary<Guid, IList<Guid>> ChildLayerIdsByParentId { get; }

        public int GroupMembershipCount { get; private set; }

        public void MergeBlockDefinitions(
            IDictionary<Guid, OutlinerBlockDefinitionModel> definitions)
        {
            foreach (var entry in definitions ??
                new Dictionary<Guid, OutlinerBlockDefinitionModel>())
            {
                if (entry.Key != Guid.Empty && entry.Value != null)
                    BlockDefinitionsById[entry.Key] = entry.Value;
            }
        }

        public OutlinerGroupHierarchy GroupHierarchy
        {
            get
            {
                if (_groupHierarchy == null)
                    _groupHierarchy = OutlinerGroupHierarchy.Create(this);
                return _groupHierarchy;
            }
        }

        public bool TryAddLayer(
            OutlinerLayerModel layer,
            out string failureReason)
        {
            failureReason = string.Empty;
            if (layer == null || layer.Id == Guid.Empty || layer.Index < 0)
            {
                failureReason = "invalid-layer";
                return false;
            }
            if (LayersById.ContainsKey(layer.Id) ||
                LayersByIndex.ContainsKey(layer.Index))
            {
                failureReason = "duplicate-layer";
                return false;
            }
            if (layer.ParentId != Guid.Empty &&
                !LayersById.ContainsKey(layer.ParentId))
            {
                failureReason = "missing-parent-layer";
                return false;
            }

            layer.TopLevelObjectCount = 0;
            Layers.Add(layer);
            LayersById[layer.Id] = layer;
            LayersByIndex[layer.Index] = layer;
            ObjectsByLayerId[layer.Id] = new List<OutlinerObjectModel>();
            if (!ChildLayerIdsByParentId.TryGetValue(
                    layer.ParentId,
                    out var children))
            {
                children = new List<Guid>();
                ChildLayerIdsByParentId[layer.ParentId] = children;
            }
            children.Add(layer.Id);
            if (!ChildLayerIdsByParentId.ContainsKey(layer.Id))
            {
                ChildLayerIdsByParentId[layer.Id] = new List<Guid>();
            }
            return true;
        }

        public bool TryReplaceLayerObjects(
            IDictionary<Guid, IList<OutlinerObjectModel>> replacements,
            out ISet<Guid> removedObjectIds,
            out string failureReason,
            IDictionary<int, OutlinerGroupModel> groupReplacements = null)
        {
            removedObjectIds = new HashSet<Guid>();
            failureReason = string.Empty;
            if (replacements == null || replacements.Count == 0)
            {
                failureReason = "no-dirty-layers";
                return false;
            }

            var dirtyLayerIds = new HashSet<Guid>(replacements.Keys);
            if (dirtyLayerIds.Any(id => !LayersById.ContainsKey(id)))
            {
                failureReason = "missing-layer-model";
                return false;
            }

            var oldObjects = Objects
                .Where(item => dirtyLayerIds.Contains(item.LayerId))
                .ToList();
            var oldById = oldObjects.ToDictionary(item => item.Id);
            var newObjects = replacements
                .SelectMany(entry => entry.Value ??
                    Array.Empty<OutlinerObjectModel>())
                .ToList();
            if (newObjects.Any(item =>
                    item == null ||
                    !dirtyLayerIds.Contains(item.LayerId)) ||
                newObjects.GroupBy(item => item.Id).Any(group =>
                    group.Key == Guid.Empty || group.Count() > 1))
            {
                failureReason = "invalid-layer-capture";
                return false;
            }

            var outsideIds = new HashSet<Guid>(Objects
                .Where(item => !dirtyLayerIds.Contains(item.LayerId))
                .Select(item => item.Id));
            if (newObjects.Any(item => outsideIds.Contains(item.Id)))
            {
                failureReason = "duplicate-object-id";
                return false;
            }

            var newById = newObjects.ToDictionary(item => item.Id);
            var proposedGroupsByIndex = GroupsByIndex.ToDictionary(
                entry => entry.Key,
                entry => entry.Value);
            var allowedChangedGroupIds = new HashSet<Guid>();
            foreach (var entry in groupReplacements ??
                new Dictionary<int, OutlinerGroupModel>())
            {
                if (proposedGroupsByIndex.TryGetValue(
                    entry.Key,
                    out var oldGroup))
                {
                    allowedChangedGroupIds.Add(oldGroup.Id);
                }
                if (entry.Value == null)
                    proposedGroupsByIndex.Remove(entry.Key);
                else
                {
                    allowedChangedGroupIds.Add(entry.Value.Id);
                    proposedGroupsByIndex[entry.Key] = entry.Value;
                }
            }
            var proposedGroupsById = proposedGroupsByIndex.Values.ToDictionary(
                item => item.Id);

            foreach (var oldObject in oldObjects)
            {
                if (!newById.TryGetValue(oldObject.Id, out var replacement))
                {
                    if (oldObject.GroupIds.Any(id =>
                        !allowedChangedGroupIds.Contains(id)))
                    {
                        failureReason = "removed-group-member";
                        return false;
                    }
                    continue;
                }

                var changedMemberships = new HashSet<Guid>(oldObject.GroupIds);
                changedMemberships.SymmetricExceptWith(replacement.GroupIds);
                if (changedMemberships.Any(id =>
                    !allowedChangedGroupIds.Contains(id)))
                {
                    failureReason = "changed-group-membership";
                    return false;
                }
            }
            foreach (var newObject in newObjects)
            {
                if (!oldById.ContainsKey(newObject.Id) &&
                    newObject.GroupIds.Any(id =>
                        !allowedChangedGroupIds.Contains(id)))
                {
                    failureReason = "added-group-member";
                    return false;
                }
            }

            var proposedObjectsById = Objects
                .Where(item => !dirtyLayerIds.Contains(item.LayerId))
                .Concat(newObjects)
                .ToDictionary(item => item.Id);
            if (proposedObjectsById.Values.Any(item => item.GroupIds.Any(
                groupId => !proposedGroupsById.ContainsKey(groupId))))
            {
                failureReason = "unknown-proposed-group";
                return false;
            }
            var changedGroups = (groupReplacements ??
                new Dictionary<int, OutlinerGroupModel>()).Values
                .Where(item => item != null).ToList();
            var actualMembersByGroup = changedGroups.ToDictionary(
                group => group.Id, group => new HashSet<Guid>());
            if (actualMembersByGroup.Count > 0)
            {
                foreach (var item in proposedObjectsById.Values)
                {
                    foreach (var groupId in item.GroupIds)
                    {
                        if (actualMembersByGroup.TryGetValue(groupId, out var members))
                            members.Add(item.Id);
                    }
                }
            }
            foreach (var group in changedGroups)
            {
                var expectedMembers = new HashSet<Guid>(group.MemberIds);
                var actualMembers = actualMembersByGroup[group.Id];
                if (!expectedMembers.SetEquals(actualMembers))
                {
                    failureReason = "incomplete-group-capture";
                    return false;
                }
            }

            var newIds = new HashSet<Guid>(newById.Keys);
            removedObjectIds = new HashSet<Guid>(oldById.Keys.Where(
                id => !newIds.Contains(id)));
            // Compact once; removing scattered entries individually shifts the
            // remaining list repeatedly on large documents.
            var retainedCount = 0;
            for (var index = 0; index < Objects.Count; index++)
            {
                var item = Objects[index];
                if (!dirtyLayerIds.Contains(item.LayerId))
                    Objects[retainedCount++] = item;
            }
            while (Objects.Count > retainedCount)
                Objects.RemoveAt(Objects.Count - 1);
            foreach (var oldObject in oldObjects)
                ObjectsById.Remove(oldObject.Id);

            foreach (var layerId in dirtyLayerIds)
            {
                var layerObjects = replacements.TryGetValue(
                    layerId,
                    out var replacementObjects)
                        ? replacementObjects ??
                            (IList<OutlinerObjectModel>)new List<OutlinerObjectModel>()
                        : new List<OutlinerObjectModel>();
                ObjectsByLayerId[layerId] = layerObjects;
                if (LayersById.TryGetValue(layerId, out var layerModel))
                    layerModel.TopLevelObjectCount = layerObjects.Count;
                foreach (var objectModel in layerObjects)
                {
                    Objects.Add(objectModel);
                    ObjectsById[objectModel.Id] = objectModel;
                }
            }

            foreach (var removedId in removedObjectIds)
                HistoryByObjectId.Remove(removedId);

            foreach (var entry in groupReplacements ??
                new Dictionary<int, OutlinerGroupModel>())
            {
                if (GroupsByIndex.TryGetValue(
                    entry.Key,
                    out var previousGroup))
                {
                    Groups.Remove(previousGroup);
                    GroupsByIndex.Remove(entry.Key);
                    GroupsById.Remove(previousGroup.Id);
                }
                if (entry.Value == null)
                    continue;

                Groups.Add(entry.Value);
                GroupsByIndex[entry.Key] = entry.Value;
                GroupsById[entry.Value.Id] = entry.Value;
            }
            GroupMembershipCount = Groups.Sum(item => item.MemberIds.Count);
            _groupHierarchy = null;
            return true;
        }
    }

    internal sealed class OutlinerLayerModel
    {
        public Guid Id { get; set; }

        public int Index { get; set; }

        public Guid ParentId { get; set; }

        public string Name { get; set; }

        public string FullPath { get; set; }

        public bool DefaultExpanded { get; set; }

        public bool IsCurrent { get; set; }

        public bool OwnVisible { get; set; }

        public bool OwnLocked { get; set; }

        public bool InheritedHidden { get; set; }

        public bool InheritedLocked { get; set; }

        public System.Drawing.Color Color { get; set; }

        public bool HasMaterial { get; set; }

        public string LinetypeName { get; set; }

        public System.Drawing.Color PlotColor { get; set; }

        public string PlotWeightName { get; set; }

        public bool HasCustomSectionStyle { get; set; }

        public bool NewDetailVisible { get; set; }

        public bool ModelVisible { get; set; }

        public bool ObjectsLoaded { get; set; } = true;

        public int TopLevelObjectCount { get; set; } = -1;

        public bool IsFavorite { get; set; }

        public bool CanEditFavorite { get; set; } = true;

        public bool HasDeferredObjects => !ObjectsLoaded;

        public bool IsUnavailable => InheritedHidden || InheritedLocked;
    }

    internal sealed class OutlinerObjectModel
    {
        public string Note { get; set; } = string.Empty;
        public Guid Id { get; set; }

        public uint RuntimeSerialNumber { get; set; }

        public int LayerIndex { get; set; }

        public Guid LayerId { get; set; }

        public Guid BlockDefinitionId { get; set; }

        public int? SortOrder { get; set; }

        public string DisplayName { get; set; }

        public OutlinerResolvedNameKind NameKind { get; set; }

        public bool IsProvisional { get; set; }

        public bool IsUserNamed { get; set; }

        public OutlinerIconKind IconKind { get; set; }

        public bool IsHidden { get; set; }

        public bool IsLocked { get; set; }

        public bool IsReference { get; set; }

        public bool InheritedHidden { get; set; }

        public bool InheritedLocked { get; set; }

        public bool IsFavorite { get; set; }

        public bool CanEditFavorite { get; set; } = true;

        public IList<Guid> GroupIds { get; set; } = new List<Guid>();

        public bool IsUnavailable =>
            IsHidden ||
            IsLocked ||
            IsReference ||
            InheritedHidden ||
            InheritedLocked;
    }

    internal sealed class OutlinerBlockDefinitionModel
    {
        public Guid Id { get; set; }

        public string Name { get; set; }

        public IList<OutlinerBlockComponentModel> Components { get; set; } =
            new List<OutlinerBlockComponentModel>();
    }

    internal sealed class OutlinerBlockComponentModel
    {
        public Guid ObjectId { get; set; }

        public int Index { get; set; }

        public int LayerIndex { get; set; }

        public string LayerPath { get; set; }

        public string DisplayName { get; set; }

        public OutlinerIconKind IconKind { get; set; }
    }

    internal sealed class OutlinerGroupModel
    {
        public string Note { get; set; }

        public Guid Id { get; set; }

        public int Index { get; set; }

        public string Name { get; set; }

        public bool IsFavorite { get; set; }

        public bool CanEditFavorite { get; set; } = true;

        public IList<Guid> MemberIds { get; set; } = new List<Guid>();
    }

    internal sealed class OutlinerHistoryModel
    {
        public Guid ObjectId { get; set; }

        public IList<Guid> ParentIds { get; set; } = new List<Guid>();

        public IList<Guid> ChildIds { get; set; } = new List<Guid>();

        public bool HasRelations => ParentIds.Count > 0 || ChildIds.Count > 0;
    }

    internal sealed class OutlinerViewState
    {
        private string _normalizedSearchText = string.Empty;

        public OutlinerViewState()
        {
            LayerFilter = new OutlinerLayerFilterState();
            ContentFilter = new OutlinerContentFilterState();
            TemporarilyRevealedLayerIds = new HashSet<Guid>();
            TemporarilyRevealedObjectIds = new HashSet<Guid>();
            CompactHistory = true;
        }

        public string SearchText
        {
            get => _normalizedSearchText;
            set => _normalizedSearchText = Normalize(value);
        }

        public bool HasSearch => _normalizedSearchText.Length > 0;

        public OutlinerLayerFilterState LayerFilter { get; }

        public OutlinerContentFilterState ContentFilter { get; }

        public OutlinerDisplayMode DisplayMode { get; set; }

        public bool CompactHistory { get; set; }

        public ISet<Guid> TemporarilyRevealedLayerIds { get; }

        public ISet<Guid> TemporarilyRevealedObjectIds { get; }

        public bool HasLayerFilter => LayerFilter.IsActive;

        public bool HasContentFilter => ContentFilter.IsActive;

        public bool HasFilters => HasLayerFilter || HasContentFilter;

        public bool HasDiscoveryState => HasSearch || HasFilters;

        public bool HasTemporaryFilterReveal =>
            TemporarilyRevealedLayerIds.Count > 0 ||
            TemporarilyRevealedObjectIds.Count > 0;

        public Guid RevealedObjectId { get; set; }

        public bool HasReveal => RevealedObjectId != Guid.Empty;

        public void ClearReveal()
        {
            RevealedObjectId = Guid.Empty;
        }

        public void ClearTemporaryFilterReveal()
        {
            TemporarilyRevealedLayerIds.Clear();
            TemporarilyRevealedObjectIds.Clear();
        }

        public bool Matches(string value)
        {
            return !HasSearch || Normalize(value).Contains(_normalizedSearchText);
        }

        private static string Normalize(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : value.Trim()
                    .Normalize(NormalizationForm.FormKC)
                    .ToUpperInvariant();
        }
    }
}
