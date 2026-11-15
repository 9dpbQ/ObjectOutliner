using System;
using System.Collections.Generic;

namespace ObjectManager.Outliner
{
    internal sealed class OutlinerGroupMembershipIndex
    {
        private readonly Dictionary<int, IList<Guid>> _memberIdsByGroupIndex =
            new Dictionary<int, IList<Guid>>();
        private readonly Dictionary<Guid, IList<int>> _groupIndicesByObjectId =
            new Dictionary<Guid, IList<int>>();

        public IDictionary<int, IList<Guid>> MemberIdsByGroupIndex =>
            _memberIdsByGroupIndex;

        public IDictionary<Guid, IList<int>> GroupIndicesByObjectId =>
            _groupIndicesByObjectId;

        public int MembershipCount { get; private set; }

        public void Add(Guid objectId, IEnumerable<int> groupIndices)
        {
            if (objectId == Guid.Empty || groupIndices == null)
                return;

            List<int> indices = null;
            foreach (var groupIndex in groupIndices)
            {
                if (groupIndex < 0)
                    continue;

                if (indices == null)
                    indices = new List<int>();
                if (!indices.Contains(groupIndex))
                    indices.Add(groupIndex);
            }

            if (indices == null || indices.Count == 0)
                return;

            _groupIndicesByObjectId[objectId] = indices;

            foreach (var groupIndex in indices)
            {
                if (!_memberIdsByGroupIndex.TryGetValue(
                    groupIndex,
                    out var memberIds))
                {
                    memberIds = new List<Guid>();
                    _memberIdsByGroupIndex[groupIndex] = memberIds;
                }

                memberIds.Add(objectId);
                MembershipCount++;
            }
        }
    }
}
