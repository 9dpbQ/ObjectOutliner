using System;
using System.Collections.Generic;
using System.Linq;

namespace ObjectManager.Outliner
{
    internal static class OutlinerHistoryTraversal
    {
        public static IList<Guid> GetKnownInputs(
            IDictionary<Guid, OutlinerHistoryModel> historyByObjectId,
            IEnumerable<Guid> resultIds)
        {
            var roots = new HashSet<Guid>((resultIds ?? Enumerable.Empty<Guid>())
                .Where(id => id != Guid.Empty));
            var visited = new HashSet<Guid>(roots);
            var pending = new Queue<Guid>(roots);
            var inputs = new List<Guid>();

            while (pending.Count > 0)
            {
                var resultId = pending.Dequeue();
                if (historyByObjectId == null ||
                    !historyByObjectId.TryGetValue(
                        resultId,
                        out var history) ||
                    history == null)
                {
                    continue;
                }

                foreach (var parentId in history.ParentIds
                    .Where(id => id != Guid.Empty))
                {
                    if (!visited.Add(parentId))
                        continue;
                    inputs.Add(parentId);
                    pending.Enqueue(parentId);
                }
            }

            return inputs;
        }
    }
}
