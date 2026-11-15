using System;
using System.Collections.Generic;
using System.Linq;

namespace ObjectManager.Outliner
{
    internal sealed class OutlinerLayerExpansionPlan
    {
        public ISet<Guid> LayerIds { get; } = new HashSet<Guid>();

        public ISet<OutlinerGroupSliceKey> GroupSlices { get; } =
            new HashSet<OutlinerGroupSliceKey>();

        public ISet<Guid> HistoryObjectIds { get; } = new HashSet<Guid>();
    }

    internal static class OutlinerLayerExpansionPlanner
    {
        public static OutlinerLayerExpansionPlan Create(
            OutlinerDocumentModel model,
            IEnumerable<Guid> targetLayerIds,
            IEnumerable<OutlinerGroupSliceKey> availableGroupSlices,
            IEnumerable<Guid> knownHistoryObjectIds)
        {
            var plan = new OutlinerLayerExpansionPlan();
            if (model == null)
                return plan;

            var targets = new HashSet<Guid>((targetLayerIds ??
                Enumerable.Empty<Guid>()).Where(model.LayersById.ContainsKey));
            if (targets.Count == 0)
                return plan;

            foreach (var targetId in targets)
            {
                if (model.ChildLayerIdsByParentId.TryGetValue(
                    targetId,
                    out var childIds))
                {
                    plan.LayerIds.UnionWith(childIds);
                }
            }

            var availableSlices = new HashSet<OutlinerGroupSliceKey>(
                availableGroupSlices ??
                Enumerable.Empty<OutlinerGroupSliceKey>());
            foreach (var slice in availableSlices.Where(
                item => targets.Contains(item.LayerId)))
            {
                var parentGroupId = model.GroupHierarchy.GetParentGroupId(
                    slice.GroupId);
                if (parentGroupId == Guid.Empty ||
                    !availableSlices.Contains(new OutlinerGroupSliceKey(
                        parentGroupId,
                        slice.LayerId)))
                {
                    plan.GroupSlices.Add(slice);
                }
            }

            foreach (var objectId in (knownHistoryObjectIds ??
                Enumerable.Empty<Guid>()).Distinct())
            {
                if (!model.ObjectsById.TryGetValue(
                    objectId,
                    out var objectModel) ||
                    !targets.Contains(objectModel.LayerId) ||
                    model.GroupHierarchy.GetCanonicalContainerId(objectId) !=
                        Guid.Empty)
                {
                    continue;
                }

                plan.HistoryObjectIds.Add(objectId);
            }

            return plan;
        }
    }
}
