using System;
using System.Collections.Generic;
using System.Linq;

namespace ObjectManager.Outliner
{
    internal sealed class OutlinerGroupHierarchy
    {
        private readonly IDictionary<Guid, Guid> _parentByGroupId;
        private readonly IDictionary<Guid, Guid> _containerByObjectId;
        private readonly IDictionary<Guid, IList<Guid>> _pathByObjectId;

        private OutlinerGroupHierarchy(
            IDictionary<Guid, Guid> parentByGroupId,
            IDictionary<Guid, Guid> containerByObjectId,
            IDictionary<Guid, IList<Guid>> pathByObjectId)
        {
            _parentByGroupId = parentByGroupId;
            _containerByObjectId = containerByObjectId;
            _pathByObjectId = pathByObjectId;
        }

        public static OutlinerGroupHierarchy Create(OutlinerDocumentModel model)
        {
            var objectsById = model.ObjectsById;
            var groupsById = model.GroupsById;
            var memberSets = model.Groups.ToDictionary(
                group => group.Id,
                group => new HashSet<Guid>(group.MemberIds.Where(objectsById.ContainsKey)));
            var singleLayerGroups = model.Groups
                .Select(group => new
                {
                    Group = group,
                    Members = memberSets[group.Id],
                    LayerIds = memberSets[group.Id]
                        .Select(id => objectsById[id].LayerId)
                        .Distinct()
                        .ToList()
                })
                .Where(item => item.Members.Count > 0 && item.LayerIds.Count == 1)
                .GroupBy(item => item.LayerIds[0]);

            var parentByGroupId = new Dictionary<Guid, Guid>();
            foreach (var layerGroups in singleLayerGroups)
            {
                var candidates = layerGroups.ToList();
                foreach (var child in candidates)
                {
                    var supersets = candidates
                        .Where(parent =>
                            parent.Group.Id != child.Group.Id &&
                            parent.Members.Count > child.Members.Count &&
                            parent.Members.IsSupersetOf(child.Members))
                        .ToList();
                    if (supersets.Count == 0)
                        continue;

                    var smallestCount = supersets.Min(item => item.Members.Count);
                    var smallest = supersets
                        .Where(item => item.Members.Count == smallestCount)
                        .ToList();
                    if (smallest.Count == 1)
                        parentByGroupId[child.Group.Id] = smallest[0].Group.Id;
                }
            }

            var containerByObjectId = new Dictionary<Guid, Guid>();
            var pathByObjectId = new Dictionary<Guid, IList<Guid>>();
            foreach (var item in model.Objects)
            {
                var memberships = item.GroupIds
                    .Where(groupsById.ContainsKey)
                    .Distinct()
                    .ToList();
                var containerId = FindCanonicalContainer(
                    memberships,
                    parentByGroupId);
                containerByObjectId[item.Id] = containerId;
                pathByObjectId[item.Id] = BuildPath(containerId, parentByGroupId);
            }

            return new OutlinerGroupHierarchy(
                parentByGroupId,
                containerByObjectId,
                pathByObjectId);
        }

        public Guid GetParentGroupId(Guid groupId)
        {
            Guid parentId;
            return _parentByGroupId.TryGetValue(groupId, out parentId)
                ? parentId
                : Guid.Empty;
        }

        public Guid GetCanonicalContainerId(Guid objectId)
        {
            Guid containerId;
            return _containerByObjectId.TryGetValue(objectId, out containerId)
                ? containerId
                : Guid.Empty;
        }

        public IList<Guid> GetCanonicalGroupPath(Guid objectId)
        {
            IList<Guid> path;
            return _pathByObjectId.TryGetValue(objectId, out path)
                ? path
                : Array.Empty<Guid>();
        }

        public bool IsAncestorOrSelf(Guid possibleAncestorId, Guid groupId)
        {
            if (possibleAncestorId == Guid.Empty || groupId == Guid.Empty)
                return false;

            var currentId = groupId;
            var visited = new HashSet<Guid>();
            while (currentId != Guid.Empty && visited.Add(currentId))
            {
                if (currentId == possibleAncestorId)
                    return true;
                currentId = GetParentGroupId(currentId);
            }
            return false;
        }

        private static Guid FindCanonicalContainer(
            IList<Guid> memberships,
            IDictionary<Guid, Guid> parentByGroupId)
        {
            if (memberships.Count == 0)
                return Guid.Empty;

            var membershipSet = new HashSet<Guid>(memberships);
            var leaves = memberships.Where(groupId => !memberships.Any(otherId =>
                otherId != groupId &&
                IsAncestor(groupId, otherId, parentByGroupId))).ToList();
            if (leaves.Count == 1)
                return leaves[0];

            HashSet<Guid> commonAncestors = null;
            foreach (var leafId in leaves)
            {
                var ancestors = new HashSet<Guid>(
                    BuildPath(leafId, parentByGroupId).Where(membershipSet.Contains));
                if (commonAncestors == null)
                    commonAncestors = ancestors;
                else
                    commonAncestors.IntersectWith(ancestors);
            }

            if (commonAncestors == null || commonAncestors.Count == 0)
                return Guid.Empty;

            return commonAncestors
                .OrderByDescending(id => GetDepth(id, parentByGroupId))
                .First();
        }

        private static bool IsAncestor(
            Guid possibleAncestorId,
            Guid groupId,
            IDictionary<Guid, Guid> parentByGroupId)
        {
            Guid currentId;
            if (!parentByGroupId.TryGetValue(groupId, out currentId))
                return false;

            var visited = new HashSet<Guid>();
            while (currentId != Guid.Empty && visited.Add(currentId))
            {
                if (currentId == possibleAncestorId)
                    return true;
                if (!parentByGroupId.TryGetValue(currentId, out currentId))
                    break;
            }
            return false;
        }

        private static IList<Guid> BuildPath(
            Guid groupId,
            IDictionary<Guid, Guid> parentByGroupId)
        {
            var path = new List<Guid>();
            var currentId = groupId;
            var visited = new HashSet<Guid>();
            while (currentId != Guid.Empty && visited.Add(currentId))
            {
                path.Add(currentId);
                Guid parentId;
                currentId = parentByGroupId.TryGetValue(currentId, out parentId)
                    ? parentId
                    : Guid.Empty;
            }
            path.Reverse();
            return path;
        }

        private static int GetDepth(
            Guid groupId,
            IDictionary<Guid, Guid> parentByGroupId)
        {
            var depth = 0;
            var currentId = groupId;
            var visited = new HashSet<Guid>();
            while (currentId != Guid.Empty && visited.Add(currentId))
            {
                depth++;
                Guid parentId;
                currentId = parentByGroupId.TryGetValue(currentId, out parentId)
                    ? parentId
                    : Guid.Empty;
            }
            return depth;
        }
    }
}
