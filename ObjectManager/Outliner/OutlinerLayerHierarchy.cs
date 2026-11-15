using System;
using System.Collections.Generic;
using System.Linq;

namespace ObjectManager.Outliner
{
    internal static class OutlinerLayerHierarchy
    {
        public static ISet<Guid> GetSubtreeIds(
            OutlinerDocumentModel model,
            IEnumerable<Guid> rootLayerIds)
        {
            var result = new HashSet<Guid>();
            if (model == null)
                return result;

            var pending = new Stack<Guid>((rootLayerIds ??
                Enumerable.Empty<Guid>()).Distinct());
            while (pending.Count > 0)
            {
                var layerId = pending.Pop();
                if (!model.LayersById.ContainsKey(layerId) ||
                    !result.Add(layerId))
                    continue;

                if (!model.ChildLayerIdsByParentId.TryGetValue(
                    layerId,
                    out var childIds))
                {
                    continue;
                }

                foreach (var childId in childIds)
                    pending.Push(childId);
            }

            return result;
        }
    }
}
