using System;
using System.Collections.Generic;
using System.Linq;

namespace ObjectManager.Outliner
{
    internal static class GroupOrderPlanner
    {
        public static IList<Guid> MoveBeforeOrAfter(
            IEnumerable<Guid> currentOrder,
            IEnumerable<Guid> movedIds,
            Guid targetId,
            bool after)
        {
            var order = (currentOrder ?? Enumerable.Empty<Guid>())
                .Where(id => id != Guid.Empty)
                .Distinct()
                .ToList();
            var moved = new HashSet<Guid>((movedIds ?? Enumerable.Empty<Guid>())
                .Where(order.Contains));
            if (moved.Count == 0 || targetId == Guid.Empty || moved.Contains(targetId))
                return order;

            var movingInDisplayOrder = order.Where(moved.Contains).ToList();
            order.RemoveAll(moved.Contains);
            var targetIndex = order.IndexOf(targetId);
            if (targetIndex < 0)
                return order;
            if (after)
                targetIndex++;
            order.InsertRange(targetIndex, movingInDisplayOrder);
            return order;
        }
    }
}
