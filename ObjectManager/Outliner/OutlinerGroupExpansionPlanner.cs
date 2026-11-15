using System;
using System.Collections.Generic;
using System.Linq;

namespace ObjectManager.Outliner
{
    internal sealed class OutlinerGroupExpansionPlan
    {
        public ISet<OutlinerGroupSliceKey> TargetSlices { get; } =
            new HashSet<OutlinerGroupSliceKey>();

        public ISet<OutlinerGroupSliceKey> GroupSlices { get; } =
            new HashSet<OutlinerGroupSliceKey>();

        public ISet<Guid> HistoryObjectIds { get; } = new HashSet<Guid>();
    }

    internal static class OutlinerGroupExpansionPlanner
    {
        public static OutlinerGroupExpansionPlan Create(
            OutlinerDocumentModel model,
            IEnumerable<OutlinerGroupSliceKey> targetGroupSlices,
            IEnumerable<OutlinerGroupSliceKey> availableGroupSlices,
            IEnumerable<Guid> knownHistoryObjectIds)
        {
            var plan = new OutlinerGroupExpansionPlan();
            if (model == null)
                return plan;

            var availableSlices = new HashSet<OutlinerGroupSliceKey>(
                availableGroupSlices ??
                Enumerable.Empty<OutlinerGroupSliceKey>());
            plan.TargetSlices.UnionWith((targetGroupSlices ??
                Enumerable.Empty<OutlinerGroupSliceKey>()).Where(slice =>
                    model.GroupsById.ContainsKey(slice.GroupId) &&
                    model.LayersById.ContainsKey(slice.LayerId) &&
                    availableSlices.Contains(slice)));
            if (plan.TargetSlices.Count == 0)
                return plan;

            foreach (var slice in availableSlices)
            {
                var parentId = model.GroupHierarchy.GetParentGroupId(
                    slice.GroupId);
                if (parentId != Guid.Empty &&
                    plan.TargetSlices.Contains(new OutlinerGroupSliceKey(
                        parentId,
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
                    out var objectModel))
                {
                    continue;
                }

                var containerId = model.GroupHierarchy
                    .GetCanonicalContainerId(objectId);
                if (containerId != Guid.Empty &&
                    plan.TargetSlices.Contains(new OutlinerGroupSliceKey(
                        containerId,
                        objectModel.LayerId)))
                {
                    plan.HistoryObjectIds.Add(objectId);
                }
            }

            return plan;
        }
    }
}
