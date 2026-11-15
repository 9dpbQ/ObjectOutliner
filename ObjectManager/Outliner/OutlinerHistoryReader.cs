using System;
using System.Collections.Generic;
using System.Linq;
using Rhino;

namespace ObjectManager.Outliner
{
    internal static class OutlinerHistoryReader
    {
        public static bool TryRead(
            RhinoDoc doc,
            Guid objectId,
            out OutlinerHistoryModel history,
            out int queryCount)
        {
            history = null;
            queryCount = 0;
            if (doc == null || objectId == Guid.Empty)
                return false;

            if (doc.Objects.HistoryRecordCount <= 0)
                return true;

            var rhinoObject = doc.Objects.FindId(objectId);
            if (rhinoObject == null || rhinoObject.IsDeleted)
                return false;

            var parents = DistinctIds(rhinoObject.HistoryParents());
            var children = DistinctIds(rhinoObject.HistoryChildren());
            queryCount = 2;
            if (parents.Count == 0 && children.Count == 0)
                return true;

            history = new OutlinerHistoryModel
            {
                ObjectId = objectId,
                ParentIds = parents,
                ChildIds = children
            };
            return true;
        }

        private static IList<Guid> DistinctIds(IEnumerable<Guid> ids)
        {
            if (ids == null)
                return new List<Guid>();

            var seen = new HashSet<Guid>();
            return ids
                .Where(id => id != Guid.Empty && seen.Add(id))
                .ToList();
        }
    }
}
