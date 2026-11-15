using System;
using System.Collections.Generic;
using System.Linq;

namespace ObjectManager.Outliner
{
    internal sealed class CopyMembershipCandidate
    {
        public Guid Id { get; set; }

        public int Ordinal { get; set; }

        public string MatchKey { get; set; } = string.Empty;

        public IReadOnlyCollection<int> GroupSizes { get; set; } =
            Array.Empty<int>();
    }

    internal static class CopyMembershipMatcher
    {
        public static IList<Guid> RankByMembership(
            IEnumerable<CopyMembershipCandidate> candidates,
            IEnumerable<int> copiedGroupSizes)
        {
            var ordered = (candidates ??
                    Enumerable.Empty<CopyMembershipCandidate>())
                .Where(item => item != null)
                .OrderBy(item => item.Ordinal)
                .ToList();
            var target = Signature(copiedGroupSizes);
            var matching = ordered
                .Where(item => Signature(item.GroupSizes) == target)
                .ToList();

            if (matching.Count == 0)
                return ordered.Select(item => item.Id).ToList();

            return matching
                .Concat(ordered.Where(item => !matching.Contains(item)))
                .Select(item => item.Id)
                .ToList();
        }

        public static IDictionary<Guid, Guid> Reconcile(
            IEnumerable<CopyMembershipCandidate> sources,
            IEnumerable<CopyMembershipCandidate> copies,
            IDictionary<Guid, Guid> existingMap)
        {
            var sourceList = (sources ??
                    Enumerable.Empty<CopyMembershipCandidate>())
                .Where(item => item != null)
                .OrderBy(item => item.Ordinal)
                .ToList();
            var copyList = (copies ??
                    Enumerable.Empty<CopyMembershipCandidate>())
                .Where(item => item != null)
                .OrderBy(item => item.Ordinal)
                .ToList();
            var fallback = new Dictionary<Guid, Guid>(
                existingMap ?? new Dictionary<Guid, Guid>());
            if (sourceList.Count == 0 || sourceList.Count != copyList.Count)
                return fallback;

            var sourcesBySignature = sourceList
                .GroupBy(CombinedSignature)
                .ToDictionary(group => group.Key, group => group.ToList());
            var copiesBySignature = copyList
                .GroupBy(CombinedSignature)
                .ToDictionary(group => group.Key, group => group.ToList());
            if (sourcesBySignature.Count != copiesBySignature.Count ||
                sourcesBySignature.Any(pair =>
                    !copiesBySignature.TryGetValue(pair.Key, out var matches) ||
                    matches.Count != pair.Value.Count))
            {
                return fallback;
            }

            var reconciled = new Dictionary<Guid, Guid>();
            foreach (var pair in sourcesBySignature.OrderBy(item => item.Key))
            {
                var sourceBucket = pair.Value
                    .OrderBy(item => item.Ordinal)
                    .ToList();
                var copyBucket = copiesBySignature[pair.Key]
                    .OrderBy(item => item.Ordinal)
                    .ToList();
                var remainingCopyIds = new HashSet<Guid>(
                    copyBucket.Select(item => item.Id));

                foreach (var source in sourceBucket)
                {
                    if (fallback.TryGetValue(source.Id, out var copiedId) &&
                        remainingCopyIds.Remove(copiedId))
                    {
                        reconciled[source.Id] = copiedId;
                    }
                }

                var remainingSources = sourceBucket
                    .Where(item => !reconciled.ContainsKey(item.Id))
                    .ToList();
                var remainingCopies = copyBucket
                    .Where(item => remainingCopyIds.Contains(item.Id))
                    .ToList();
                for (var index = 0; index < remainingSources.Count; index++)
                {
                    reconciled[remainingSources[index].Id] =
                        remainingCopies[index].Id;
                }
            }

            return reconciled;
        }

        private static string CombinedSignature(CopyMembershipCandidate item)
        {
            return string.Format(
                "{0}\u001f{1}",
                item.MatchKey ?? string.Empty,
                Signature(item.GroupSizes));
        }

        private static string Signature(IEnumerable<int> groupSizes)
        {
            return string.Join(
                ",",
                (groupSizes ?? Enumerable.Empty<int>())
                    .Where(size => size > 0)
                    .OrderBy(size => size));
        }
    }
}
