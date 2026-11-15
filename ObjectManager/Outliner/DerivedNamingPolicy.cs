using System;
using System.Collections.Generic;
using System.Linq;

namespace ObjectManager.Outliner
{
    internal enum DerivedNamingSourceRule
    {
        SinglePrimary,
        SingleNamedInput
    }

    internal sealed class DerivedNamingRecipe
    {
        public DerivedNamingRecipe(
            string commandName,
            DerivedNamingSourceRule sourceRule,
            int primaryStageIndex = 0)
        {
            CommandName = commandName ?? string.Empty;
            SourceRule = sourceRule;
            PrimaryStageIndex = primaryStageIndex;
        }

        public string CommandName { get; }

        public DerivedNamingSourceRule SourceRule { get; }

        public int PrimaryStageIndex { get; }
    }

    internal sealed class DerivedNamingCandidate
    {
        public string Key { get; set; }

        public string RootName { get; set; }

        public string DisplayName { get; set; }
    }

    internal enum DerivedNamingRelationshipKind
    {
        Difference,
        Union,
        Intersection
    }

    internal sealed class DerivedNamingRelationship
    {
        public DerivedNamingRelationshipKind Kind { get; set; }

        public string RootName { get; set; }

        public string PrimaryName { get; set; }

        public string SecondaryName { get; set; }

        public int AdditionalInputCount { get; set; }
    }

    internal static class DerivedNamingPolicy
    {
        private static readonly IDictionary<string, DerivedNamingRecipe> Recipes =
            new Dictionary<string, DerivedNamingRecipe>(
                StringComparer.OrdinalIgnoreCase)
            {
                { "Pipe", SinglePrimary("Pipe") },
                { "Sweep1", SinglePrimary("Sweep1") },
                { "Extrude", SinglePrimary("Extrude") },
                { "ExtrudeCrv", SinglePrimary("ExtrudeCrv") },
                { "ExtrudeCrvAlongCrv", SinglePrimary("ExtrudeCrvAlongCrv") },
                { "ExtrudeSrf", SinglePrimary("ExtrudeSrf") },
                { "OffsetSrf", SinglePrimary("OffsetSrf") },
                { "Shell", SinglePrimary("Shell") },
                { "Revolve", SinglePrimary("Revolve") },
                { "QuadRemesh", SinglePrimary("QuadRemesh") },
                { "BooleanDifference", SinglePrimary("BooleanDifference") },
                {
                    "BooleanUnion",
                    new DerivedNamingRecipe(
                        "BooleanUnion",
                        DerivedNamingSourceRule.SingleNamedInput)
                },
                {
                    "BooleanIntersection",
                    new DerivedNamingRecipe(
                        "BooleanIntersection",
                        DerivedNamingSourceRule.SingleNamedInput)
                }
            };

        public static bool TracksInputs(string commandName)
        {
            return !string.IsNullOrWhiteSpace(commandName) &&
                Recipes.ContainsKey(commandName);
        }

        public static bool TryResolveRoot(
            string commandName,
            IList<IList<DerivedNamingCandidate>> stages,
            out string rootName)
        {
            rootName = string.Empty;
            if (!Recipes.TryGetValue(
                    commandName ?? string.Empty,
                    out var recipe))
            {
                return false;
            }

            var normalizedStages = stages ??
                (IList<IList<DerivedNamingCandidate>>)
                    new List<IList<DerivedNamingCandidate>>();
            IEnumerable<DerivedNamingCandidate> candidates;
            if (recipe.SourceRule == DerivedNamingSourceRule.SinglePrimary)
            {
                if (recipe.PrimaryStageIndex < 0 ||
                    recipe.PrimaryStageIndex >= normalizedStages.Count)
                {
                    return false;
                }

                candidates = normalizedStages[recipe.PrimaryStageIndex];
            }
            else
            {
                candidates = normalizedStages.SelectMany(stage =>
                    stage ?? Enumerable.Empty<DerivedNamingCandidate>());
            }

            var unique = (candidates ?? Enumerable.Empty<DerivedNamingCandidate>())
                .Where(item => item != null)
                .GroupBy(
                    item => item.Key ?? string.Empty,
                    StringComparer.Ordinal)
                .Select(group => group.First())
                .ToList();
            if (recipe.SourceRule == DerivedNamingSourceRule.SinglePrimary &&
                unique.Count != 1)
            {
                return false;
            }

            var named = unique
                .Where(item => !string.IsNullOrWhiteSpace(item.RootName))
                .ToList();
            if (named.Count != 1)
                return false;

            rootName = named[0].RootName.Trim();
            return rootName.Length > 0;
        }

        public static bool TryResolveRelationship(
            string commandName,
            IList<IList<DerivedNamingCandidate>> stages,
            out DerivedNamingRelationship relationship)
        {
            relationship = null;
            if (string.Equals(
                    commandName,
                    "BooleanDifference",
                    StringComparison.OrdinalIgnoreCase))
            {
                return TryResolveDifference(stages, out relationship);
            }

            DerivedNamingRelationshipKind kind;
            if (string.Equals(
                    commandName,
                    "BooleanUnion",
                    StringComparison.OrdinalIgnoreCase))
            {
                kind = DerivedNamingRelationshipKind.Union;
            }
            else if (string.Equals(
                    commandName,
                    "BooleanIntersection",
                    StringComparison.OrdinalIgnoreCase))
            {
                kind = DerivedNamingRelationshipKind.Intersection;
            }
            else
            {
                return false;
            }

            var inputs = UniqueCandidates(
                stages == null
                    ? Enumerable.Empty<DerivedNamingCandidate>()
                    : stages.SelectMany(stage =>
                        stage ?? Enumerable.Empty<DerivedNamingCandidate>()));
            if (inputs.Count < 2)
                return false;

            var primary = inputs.FirstOrDefault(HasUsableName);
            if (primary == null)
                return false;

            var remaining = inputs
                .Where(item => !ReferenceEquals(item, primary))
                .ToList();
            relationship = CreateRelationship(kind, primary, remaining);
            return relationship != null;
        }

        private static bool TryResolveDifference(
            IList<IList<DerivedNamingCandidate>> stages,
            out DerivedNamingRelationship relationship)
        {
            relationship = null;
            if (stages == null || stages.Count < 2)
                return false;

            var primaryStage = UniqueCandidates(stages[0]);
            if (primaryStage.Count != 1 || !HasUsableName(primaryStage[0]))
                return false;

            var cutters = UniqueCandidates(stages
                .Skip(1)
                .SelectMany(stage =>
                    stage ?? Enumerable.Empty<DerivedNamingCandidate>()))
                .Where(item => !string.Equals(
                    item.Key,
                    primaryStage[0].Key,
                    StringComparison.Ordinal))
                .ToList();
            if (cutters.Count == 0)
                return false;

            relationship = CreateRelationship(
                DerivedNamingRelationshipKind.Difference,
                primaryStage[0],
                cutters);
            return relationship != null;
        }

        private static DerivedNamingRelationship CreateRelationship(
            DerivedNamingRelationshipKind kind,
            DerivedNamingCandidate primary,
            IList<DerivedNamingCandidate> remaining)
        {
            var primaryName = CandidateName(primary);
            if (string.IsNullOrWhiteSpace(primaryName) ||
                remaining == null ||
                remaining.Count == 0)
            {
                return null;
            }

            var secondaryName = remaining.Count == 1
                ? CandidateName(remaining[0])
                : string.Empty;
            return new DerivedNamingRelationship
            {
                Kind = kind,
                RootName = !string.IsNullOrWhiteSpace(primary.RootName)
                    ? primary.RootName.Trim()
                    : primaryName,
                PrimaryName = primaryName,
                SecondaryName = secondaryName,
                AdditionalInputCount = remaining.Count
            };
        }

        private static IList<DerivedNamingCandidate> UniqueCandidates(
            IEnumerable<DerivedNamingCandidate> candidates)
        {
            return (candidates ?? Enumerable.Empty<DerivedNamingCandidate>())
                .Where(item => item != null)
                .GroupBy(
                    item => item.Key ?? string.Empty,
                    StringComparer.Ordinal)
                .Select(group => group.First())
                .ToList();
        }

        private static bool HasUsableName(DerivedNamingCandidate candidate)
        {
            return !string.IsNullOrWhiteSpace(CandidateName(candidate));
        }

        private static string CandidateName(DerivedNamingCandidate candidate)
        {
            if (candidate == null)
                return string.Empty;
            if (!string.IsNullOrWhiteSpace(candidate.RootName))
                return candidate.RootName.Trim();
            return (candidate.DisplayName ?? string.Empty).Trim();
        }

        private static DerivedNamingRecipe SinglePrimary(string commandName)
        {
            return new DerivedNamingRecipe(
                commandName,
                DerivedNamingSourceRule.SinglePrimary);
        }
    }
}
