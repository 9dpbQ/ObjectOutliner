using System;
using System.Collections.Generic;
using System.Linq;

namespace ObjectManager.Outliner
{
    internal static class OutlinerHistoryImpact
    {
        public static ISet<Guid> Resolve(
            IEnumerable<Guid> changedIds,
            IEnumerable<OutlinerHistoryModel> histories)
        {
            var neighbors = new Dictionary<Guid, HashSet<Guid>>();
            foreach (var history in histories)
            {
                if (history == null || history.ObjectId == Guid.Empty)
                    continue;
                foreach (var related in history.ParentIds.Concat(history.ChildIds))
                {
                    if (related == Guid.Empty || related == history.ObjectId)
                        continue;
                    Connect(neighbors, history.ObjectId, related);
                    Connect(neighbors, related, history.ObjectId);
                }
            }

            // Undirected closure includes owners of incoming references, shared
            // inputs, and chained results even when records are one-sided.
            var result = new HashSet<Guid>(changedIds.Where(id => id != Guid.Empty));
            var pending = new Queue<Guid>(result);
            while (pending.Count > 0)
            {
                if (!neighbors.TryGetValue(pending.Dequeue(), out var adjacent))
                    continue;
                foreach (var id in adjacent)
                    if (result.Add(id))
                        pending.Enqueue(id);
            }
            return result;
        }

        private static void Connect(
            IDictionary<Guid, HashSet<Guid>> neighbors, Guid source, Guid target)
        {
            if (!neighbors.TryGetValue(source, out var adjacent))
            {
                adjacent = new HashSet<Guid>();
                neighbors[source] = adjacent;
            }
            adjacent.Add(target);
        }
    }
}
