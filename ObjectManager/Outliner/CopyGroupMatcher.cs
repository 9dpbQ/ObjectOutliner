using System;
using System.Collections.Generic;
using System.Linq;

namespace ObjectManager.Outliner
{
    internal sealed class CopyGroupExpectation
    {
        public string Key { get; set; }

        public int SourceGroupIndex { get; set; }

        public int BatchIndex { get; set; }

        public bool Preserve { get; set; }

        public IReadOnlyCollection<Guid> MemberIds { get; set; } =
            Array.Empty<Guid>();
    }

    internal sealed class CopyGroupCandidate
    {
        public int GroupIndex { get; set; }

        public IReadOnlyCollection<Guid> MemberIds { get; set; } =
            Array.Empty<Guid>();
    }

    internal sealed class CopyGroupMatch
    {
        public CopyGroupExpectation Expectation { get; set; }

        public CopyGroupCandidate Candidate { get; set; }
    }

    internal static class CopyGroupMatcher
    {
        public static IList<CopyGroupMatch> Match(
            IEnumerable<CopyGroupExpectation> expectations,
            IEnumerable<CopyGroupCandidate> candidates)
        {
            var remaining = (expectations ??
                    Enumerable.Empty<CopyGroupExpectation>())
                .Where(item =>
                    item != null &&
                    item.MemberIds != null &&
                    item.MemberIds.Count > 0)
                .OrderBy(item => item.BatchIndex)
                .ThenByDescending(item => item.Preserve)
                .ThenBy(item => item.SourceGroupIndex)
                .ToList();
            var matches = new List<CopyGroupMatch>();

            foreach (var candidate in (candidates ??
                    Enumerable.Empty<CopyGroupCandidate>())
                .Where(item =>
                    item != null &&
                    item.MemberIds != null &&
                    item.MemberIds.Count > 0)
                .OrderBy(item => item.GroupIndex))
            {
                var candidateIds = new HashSet<Guid>(candidate.MemberIds);
                var expectation = remaining.FirstOrDefault(item =>
                    candidateIds.SetEquals(item.MemberIds));
                if (expectation == null)
                    continue;

                matches.Add(new CopyGroupMatch
                {
                    Expectation = expectation,
                    Candidate = candidate
                });
                remaining.Remove(expectation);
            }

            return matches;
        }
    }
}
