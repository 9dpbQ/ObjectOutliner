using System;
using System.Collections.Generic;
using System.Linq;

namespace ObjectManager.Outliner
{
    internal enum OutlinerLayerScopeMode
    {
        All,
        CurrentLayerTree,
        SelectedLayerTrees
    }

    internal enum OutlinerLayerVisibilityFilter
    {
        All,
        On,
        Off
    }

    internal enum OutlinerLockFilter
    {
        All,
        Unlocked,
        Locked
    }

    internal enum OutlinerLayerOccupancyFilter
    {
        All,
        WithObjects,
        Empty
    }

    internal enum OutlinerDisplayMode
    {
        All,
        Structure,
        LayersOnly
    }

    internal sealed class OutlinerLayerFilterState
    {
        public OutlinerLayerScopeMode Scope { get; set; }

        public OutlinerLayerVisibilityFilter Visibility { get; set; }

        public OutlinerLockFilter Lock { get; set; }

        public OutlinerLayerOccupancyFilter Occupancy { get; set; }

        public HashSet<Guid> SelectedLayerIds { get; } = new HashSet<Guid>();

        public bool IsActive =>
            Scope != OutlinerLayerScopeMode.All ||
            Visibility != OutlinerLayerVisibilityFilter.All ||
            Lock != OutlinerLockFilter.All ||
            Occupancy != OutlinerLayerOccupancyFilter.All;

        public int ActiveConditionCount =>
            (Scope == OutlinerLayerScopeMode.All ? 0 : 1) +
            (Visibility == OutlinerLayerVisibilityFilter.All ? 0 : 1) +
            (Lock == OutlinerLockFilter.All ? 0 : 1) +
            (Occupancy == OutlinerLayerOccupancyFilter.All ? 0 : 1);

        public void Reset()
        {
            Scope = OutlinerLayerScopeMode.All;
            Visibility = OutlinerLayerVisibilityFilter.All;
            Lock = OutlinerLockFilter.All;
            Occupancy = OutlinerLayerOccupancyFilter.All;
            SelectedLayerIds.Clear();
        }

        public bool Matches(
            OutlinerLayerModel layer,
            OutlinerDocumentModel model)
        {
            if (layer == null || model == null || !MatchesScope(layer, model))
                return false;

            var effectivelyVisible = layer.OwnVisible && !layer.InheritedHidden;
            if (Visibility == OutlinerLayerVisibilityFilter.On &&
                !effectivelyVisible)
            {
                return false;
            }
            if (Visibility == OutlinerLayerVisibilityFilter.Off &&
                effectivelyVisible)
            {
                return false;
            }

            var effectivelyLocked = layer.OwnLocked || layer.InheritedLocked;
            if (Lock == OutlinerLockFilter.Locked && !effectivelyLocked)
                return false;
            if (Lock == OutlinerLockFilter.Unlocked && effectivelyLocked)
                return false;

            if (Occupancy == OutlinerLayerOccupancyFilter.WithObjects &&
                layer.TopLevelObjectCount <= 0)
            {
                return false;
            }
            if (Occupancy == OutlinerLayerOccupancyFilter.Empty &&
                layer.TopLevelObjectCount != 0)
            {
                return false;
            }

            return true;
        }

        public bool MatchesScope(
            OutlinerLayerModel layer,
            OutlinerDocumentModel model)
        {
            if (layer == null || model == null)
                return false;
            if (Scope == OutlinerLayerScopeMode.All)
                return true;

            if (Scope == OutlinerLayerScopeMode.CurrentLayerTree)
            {
                var current = model.Layers.FirstOrDefault(item => item.IsCurrent);
                return current != null && IsDescendantOrSelf(
                    layer.Id,
                    current.Id,
                    model.LayersById);
            }

            return SelectedLayerIds.Any(rootId => IsDescendantOrSelf(
                layer.Id,
                rootId,
                model.LayersById));
        }

        private static bool IsDescendantOrSelf(
            Guid layerId,
            Guid rootId,
            IDictionary<Guid, OutlinerLayerModel> layersById)
        {
            var currentId = layerId;
            var visited = new HashSet<Guid>();
            while (currentId != Guid.Empty && visited.Add(currentId))
            {
                if (currentId == rootId)
                    return true;
                if (!layersById.TryGetValue(currentId, out var layer))
                    break;
                currentId = layer.ParentId;
            }
            return false;
        }
    }

    internal sealed class OutlinerContentFilterState
    {
        public ISet<OutlinerIconKind> ObjectTypes { get; } =
            new HashSet<OutlinerIconKind>();

        public bool FavoritesOnly { get; set; }

        public bool SelectedObjectsOnly { get; set; }

        public bool UserNamedOnly { get; set; }
        public bool NotesOnly { get; set; }

        public bool IsActive =>
            ObjectTypes.Count > 0 ||
            FavoritesOnly ||
            SelectedObjectsOnly ||
            UserNamedOnly || NotesOnly;

        public bool HasObjectCriteria =>
            ObjectTypes.Count > 0 ||
            SelectedObjectsOnly ||
            UserNamedOnly || NotesOnly;

        public bool PreservesLayerRows => !FavoritesOnly;

        public int ActiveConditionCount =>
            ObjectTypes.Count +
            (FavoritesOnly ? 1 : 0) +
            (SelectedObjectsOnly ? 1 : 0) +
            (UserNamedOnly ? 1 : 0) + (NotesOnly ? 1 : 0);

        public void Reset()
        {
            ObjectTypes.Clear();
            FavoritesOnly = false;
            SelectedObjectsOnly = false;
            UserNamedOnly = false;
            NotesOnly = false;
        }

        public bool MatchesObjectType(OutlinerObjectModel item)
        {
            return item != null &&
                (ObjectTypes.Count == 0 ||
                 ObjectTypes.Contains(item.IconKind));
        }

        public bool MatchesObject(
            OutlinerObjectModel item,
            ISet<Guid> selectedObjectIds)
        {
            if (!MatchesObjectType(item))
                return false;
            if (UserNamedOnly && !item.IsUserNamed)
                return false;
            if (NotesOnly && string.IsNullOrWhiteSpace(item.Note))
                return false;
            if (SelectedObjectsOnly &&
                (selectedObjectIds == null ||
                 !selectedObjectIds.Contains(item.Id)))
            {
                return false;
            }

            return true;
        }
    }
}
