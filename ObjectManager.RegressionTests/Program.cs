using System;
using System.Collections.Generic;
using System.Linq;
using ObjectManager.Outliner;

namespace ObjectManager.RegressionTests
{
    internal static class Program
    {
        private static int Main()
        {
            try
            {
                AvoidsRepeatedNativeDeselectionAndOversizedRefresh();
                ReconcilesOnlyChangedCopyGroups();
                RecordsDiagnosticContextWithoutChangingActions();
                AllocatesSharedCopyNames();
                MatchesNestedGroupsByExactMemberSet();
                DoesNotConfuseEqualSizeGroups();
                MatchesDuplicateMemberSetsDeterministically();
                PreservesFullGroupBeforeRemovingPartialAncestor();
                MatchesPartialGroupForRemovalWhenNoCompleteGroupExists();
                LeavesIncompleteGroupsUnmatched();
                ReconcilesReverseOrderedNestedGroupMembers();
                RanksMembershipWithoutDroppingFallbackCandidates();
                BuildsGroupMembershipsInOnePass();
                PreservesIncrementalStateChangeKinds();
                PreservesLayerRenameImpactWhenCoalescing();
                ResolvesHistoryImpactAcrossSharedInputsAndCycles();
                CoalescesStructuralChangesWithoutLosingDirtyLayers();
                CoalescesLayerAdditionsWithoutLosingTheirIdentity();
                ReplacesDirtyLayerObjectsAtomically();
                CompactsInterleavedLayerObjectsWithoutChangingSurvivors();
                ReplacesNativeObjectWithoutTouchingUnrelatedLayers();
                LeavesModelUntouchedWhenReplacementValidationFails();
                RejectsUnsafeGroupedObjectRemoval();
                ReplacesImpactedGroupMembershipsAtomically();
                FiltersCurrentAndSelectedLayerTrees();
                DoesNotTreatDeferredLayersAsEmpty();
                ResolvesCompleteLayerSubtrees();
                PlansOnlyDirectLayerContentsForExpansion();
                PlansOnlyDirectGroupContentsForExpansion();
                FiltersObjectsByType();
                CombinesSelectedAndUserNamedObjectFilters();
                ParsesFavoriteMetadataDefensively();
                KeepsNewerSelectionOperationsProtected();
                LetsUserSelectionOverrideProgrammaticWrites();
                ReleasesExpansionProtectionWithoutACompletionEvent();
                KeepsUnrootedAutomaticNamesAsAliases();
                PromotesUserRootedAutomaticNamesToNativeNames();
                InheritsANameFromOnePrimaryInput();
                RejectsAmbiguousPrimaryInputs();
                UsesTheOnlyNamedBooleanInput();
                RejectsMultipleNamedBooleanInputs();
                DescribesBooleanDifferenceInputs();
                DescribesBooleanUnionInputsCompactly();
                RejectsDifferenceWithoutASeparateCutterStage();
                StacksAUniqueHistoryInputInTheSameContainer();
                KeepsSharedHistoryInputsCanonical();
                KeepsFavoriteHistoryInputsCanonical();
                TraversesKnownHistoryInputsRecursivelyWithoutDuplicates();
                SharesBlockDefinitionsAcrossInstances();
                FormatsBatchNamesWithPaddedNumbers();
                ReplacesBatchNameTextIgnoringCase();
                AddsBatchNameTextWithoutChangingOrder();
                ReordersMultipleGroupsWithoutLosingTheirRelativeOrder();
                TogglesDiagnosticsOnlyWhenExplicitlyRequested();
                PreservesNoteTextAndCombinesNoteFilters();
                Console.WriteLine("Object Manager regression tests passed.");
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception.Message);
                return 1;
            }
        }

        private static void ResolvesHistoryImpactAcrossSharedInputsAndCycles()
        {
            var input = Guid.NewGuid();
            var result = Guid.NewGuid();
            var sibling = Guid.NewGuid();
            var next = Guid.NewGuid();
            var unrelated = Guid.NewGuid();
            var impact = OutlinerHistoryImpact.Resolve(new[] { result }, new[]
            {
                new OutlinerHistoryModel { ObjectId = result, ParentIds = new[] { input } },
                new OutlinerHistoryModel { ObjectId = sibling, ParentIds = new[] { input } },
                new OutlinerHistoryModel { ObjectId = next, ParentIds = new[] { sibling },
                    ChildIds = new[] { result } },
                new OutlinerHistoryModel { ObjectId = unrelated }
            });
            Assert(impact.SetEquals(new[] { input, result, sibling, next }),
                "History closure missed shared or chained dependencies, or included unrelated objects.");
            Assert(OutlinerHistoryImpact.Resolve(new[] { input }, new[]
            {
                new OutlinerHistoryModel { ObjectId = result, ParentIds = new[] { input } }
            }).SetEquals(new[] { input, result }),
                "Deleted inputs must still reach incoming reference owners.");
            Assert(OutlinerHistoryImpact.Resolve(new[] { unrelated },
                Array.Empty<OutlinerHistoryModel>()).SetEquals(new[] { unrelated }),
                "Objects without history lost their ordinary update scope.");
        }

        private static void PreservesLayerRenameImpactWhenCoalescing()
        {
            var root = CreateLayer(1);
            var child = CreateLayer(2);
            child.ParentId = root.Id;
            var unrelated = CreateLayer(3);
            var model = CreateDocumentModel(
                new[] { root, child, unrelated }, Array.Empty<OutlinerObjectModel>());
            var change = new OutlinerStateChange();
            change.Merge(new OutlinerStateChange().AddLayerNames(new[] { root.Id }));
            change.Merge(new OutlinerStateChange { CurrentLayerChanged = true });
            Assert(change.LayerNamesChanged && change.LayerPropertiesChanged &&
                change.CurrentLayerChanged && !change.IsEmpty,
                "Coalescing lost layer rename impact.");
            Assert(OutlinerLayerHierarchy.GetSubtreeIds(model, change.LayerIds)
                .SetEquals(new[] { root.Id, child.Id }),
                "Rename affected the wrong layer subtree.");
            change.Clear();
            Assert(change.IsEmpty && !change.LayerNamesChanged &&
                change.LayerIds.Count == 0,
                "Clearing left stale layer rename impact.");
        }

        private static void KeepsUnrootedAutomaticNamesAsAliases()
        {
            Assert(
                OutlinerNameOwnershipPolicy.ResolveAutomaticName(string.Empty) ==
                    OutlinerNameOwnership.ManagedAlias,
                "An unrooted automatic name was promoted to a Rhino name.");
        }

        private static void PreservesNoteTextAndCombinesNoteFilters()
        {
            Assert(ObjectNote.Normalize(" \r\n\t") == string.Empty,
                "Whitespace-only notes must count as absent.");
            Assert(ObjectNote.Normalize("  column\r\ncheck\rnext  ") == "  column\ncheck\nnext  ",
                "Note normalization lost indentation or line breaks.");
            var item = new OutlinerObjectModel { Id = Guid.NewGuid(), Note = "Review" };
            var filter = new OutlinerContentFilterState { NotesOnly = true, SelectedObjectsOnly = true };
            Assert(!filter.MatchesObject(item, new HashSet<Guid>()),
                "Note filter bypassed the selected-only filter.");
            var selected = new HashSet<Guid> { item.Id };
            Assert(filter.MatchesObject(item, selected), "Annotated selected object was excluded.");
            item.Note = string.Empty;
            Assert(!filter.MatchesObject(item, selected), "Unannotated object passed note filter.");
            filter.Reset();
            Assert(!filter.IsActive && filter.MatchesObject(item, selected),
                "Reset retained the note filter.");
        }

        private static void TogglesDiagnosticsOnlyWhenExplicitlyRequested()
        {
            OutlinerDiagnosticMode.SetEnabled(false);
            Assert(
                !OutlinerDiagnosticMode.IsEnabled,
                "Diagnostics were enabled before an explicit request.");
            Assert(
                OutlinerDiagnosticMode.Toggle() &&
                OutlinerDiagnosticMode.IsEnabled,
                "Diagnostics did not turn on explicitly.");
            Assert(
                !OutlinerDiagnosticMode.Toggle() &&
                !OutlinerDiagnosticMode.IsEnabled,
                "Diagnostics did not turn off explicitly.");
        }

        private static void FormatsBatchNamesWithPaddedNumbers()
        {
            var preview = BatchRenamePlanner.CreatePreview(
                new[] { "A", "B", "C" },
                new BatchRenameOptions
                {
                    Mode = BatchRenameMode.Format,
                    FormatText = "Column",
                    StartNumber = 7,
                    DigitCount = 3,
                    Separator = "."
                });

            Assert(
                preview.Select(item => item.ProposedName).SequenceEqual(
                    new[] { "Column.007", "Column.008", "Column.009" }),
                "Batch format mode did not generate stable padded numbers.");
        }

        private static void ReplacesBatchNameTextIgnoringCase()
        {
            var preview = BatchRenamePlanner.CreatePreview(
                new[] { "PIPE_A", "Pipe_B" },
                new BatchRenameOptions
                {
                    Mode = BatchRenameMode.Replace,
                    FindText = "pipe",
                    ReplacementText = "Rail",
                    MatchCase = false
                });

            Assert(
                preview.Select(item => item.ProposedName).SequenceEqual(
                    new[] { "Rail_A", "Rail_B" }),
                "Batch replace mode did not honor case-insensitive matching.");
        }

        private static void AddsBatchNameTextWithoutChangingOrder()
        {
            var preview = BatchRenamePlanner.CreatePreview(
                new[] { "A", "B" },
                new BatchRenameOptions
                {
                    Mode = BatchRenameMode.Add,
                    AddedText = "old_",
                    TextPlacement = BatchRenamePlacement.Before
                });

            Assert(
                preview.Select(item => item.ProposedName).SequenceEqual(
                    new[] { "old_A", "old_B" }),
                "Batch add mode did not preserve the input order.");
        }

        private static void ReordersMultipleGroupsWithoutLosingTheirRelativeOrder()
        {
            var first = Guid.NewGuid();
            var second = Guid.NewGuid();
            var third = Guid.NewGuid();
            var fourth = Guid.NewGuid();
            var reordered = GroupOrderPlanner.MoveBeforeOrAfter(
                new[] { first, second, third, fourth },
                new[] { second, third },
                fourth,
                true);

            Assert(
                reordered.SequenceEqual(new[] { first, fourth, second, third }),
                "Group reordering lost the relative order of a multi-row drag.");
        }

        private static void PromotesUserRootedAutomaticNamesToNativeNames()
        {
            Assert(
                OutlinerNameOwnershipPolicy.ResolveAutomaticName(
                    "Pipe source") ==
                    OutlinerNameOwnership.UserDerivedNative,
                "A user-rooted automatic name was not promoted to a Rhino name.");
        }

        private static void InheritsANameFromOnePrimaryInput()
        {
            var stages = NamingStages(
                new DerivedNamingCandidate
                {
                    Key = "rail",
                    RootName = "Handrail"
                });

            Assert(
                DerivedNamingPolicy.TryResolveRoot(
                    "Pipe",
                    stages,
                    out var root) &&
                root == "Handrail",
                "Pipe did not inherit its single named rail.");
        }

        private static void RejectsAmbiguousPrimaryInputs()
        {
            var stages = NamingStages(
                new DerivedNamingCandidate { Key = "a", RootName = "A" },
                new DerivedNamingCandidate { Key = "b", RootName = "B" });

            Assert(
                !DerivedNamingPolicy.TryResolveRoot(
                    "BooleanDifference",
                    stages,
                    out _),
                "BooleanDifference inherited an ambiguous base set.");
        }

        private static void UsesTheOnlyNamedBooleanInput()
        {
            var stages = new List<IList<DerivedNamingCandidate>>
            {
                new List<DerivedNamingCandidate>
                {
                    new DerivedNamingCandidate
                    {
                        Key = "body",
                        RootName = "Body"
                    }
                },
                new List<DerivedNamingCandidate>
                {
                    new DerivedNamingCandidate { Key = "tool" }
                }
            };

            Assert(
                DerivedNamingPolicy.TryResolveRoot(
                    "BooleanUnion",
                    stages,
                    out var root) &&
                root == "Body",
                "BooleanUnion did not use its only named input.");
        }

        private static void RejectsMultipleNamedBooleanInputs()
        {
            var stages = new List<IList<DerivedNamingCandidate>>
            {
                new List<DerivedNamingCandidate>
                {
                    new DerivedNamingCandidate { Key = "a", RootName = "A" }
                },
                new List<DerivedNamingCandidate>
                {
                    new DerivedNamingCandidate { Key = "b", RootName = "B" }
                }
            };

            Assert(
                !DerivedNamingPolicy.TryResolveRoot(
                    "BooleanIntersection",
                    stages,
                    out _),
                "BooleanIntersection inherited one of several named inputs.");
        }

        private static void DescribesBooleanDifferenceInputs()
        {
            var stages = new List<IList<DerivedNamingCandidate>>
            {
                new List<DerivedNamingCandidate>
                {
                    new DerivedNamingCandidate
                    {
                        Key = "body",
                        RootName = "Body"
                    }
                },
                new List<DerivedNamingCandidate>
                {
                    new DerivedNamingCandidate
                    {
                        Key = "hole",
                        DisplayName = "Hole"
                    }
                }
            };

            Assert(
                DerivedNamingPolicy.TryResolveRelationship(
                    "BooleanDifference",
                    stages,
                    out var relationship) &&
                relationship.Kind == DerivedNamingRelationshipKind.Difference &&
                relationship.PrimaryName == "Body" &&
                relationship.SecondaryName == "Hole" &&
                relationship.AdditionalInputCount == 1,
                "BooleanDifference did not retain its base/cutter relationship.");
        }

        private static void DescribesBooleanUnionInputsCompactly()
        {
            var stages = NamingStages(
                new DerivedNamingCandidate
                {
                    Key = "body",
                    RootName = "Body"
                },
                new DerivedNamingCandidate
                {
                    Key = "a",
                    DisplayName = "A"
                },
                new DerivedNamingCandidate
                {
                    Key = "b",
                    DisplayName = "B"
                },
                new DerivedNamingCandidate
                {
                    Key = "c",
                    DisplayName = "C"
                });

            Assert(
                DerivedNamingPolicy.TryResolveRelationship(
                    "BooleanUnion",
                    stages,
                    out var relationship) &&
                relationship.PrimaryName == "Body" &&
                relationship.AdditionalInputCount == 3 &&
                string.IsNullOrEmpty(relationship.SecondaryName),
                "BooleanUnion did not abbreviate its additional inputs.");
        }

        private static void RejectsDifferenceWithoutASeparateCutterStage()
        {
            Assert(
                !DerivedNamingPolicy.TryResolveRelationship(
                    "BooleanDifference",
                    NamingStages(
                        new DerivedNamingCandidate
                        {
                            Key = "body",
                            RootName = "Body"
                        },
                        new DerivedNamingCandidate
                        {
                            Key = "tool",
                            RootName = "Tool"
                        }),
                    out _),
                "BooleanDifference guessed a base from one ambiguous stage.");
        }

        private static void StacksAUniqueHistoryInputInTheSameContainer()
        {
            var layerId = Guid.NewGuid();
            var inputId = Guid.NewGuid();
            var resultId = Guid.NewGuid();
            var model = HistoryModel(layerId, inputId, resultId, false);

            var plan = OutlinerHistoryStackPlanner.Create(model);
            Assert(
                plan.TryGetResult(inputId, out var stackedResultId) &&
                stackedResultId == resultId,
                "A unique same-layer History input was not stacked.");
        }

        private static void KeepsSharedHistoryInputsCanonical()
        {
            var layerId = Guid.NewGuid();
            var inputId = Guid.NewGuid();
            var firstResultId = Guid.NewGuid();
            var secondResultId = Guid.NewGuid();
            var model = HistoryModel(layerId, inputId, firstResultId, false);
            model.Objects.Add(new OutlinerObjectModel
            {
                Id = secondResultId,
                LayerId = layerId,
                LayerIndex = 0
            });
            model.ObjectsById[secondResultId] = model.Objects.Last();
            model.HistoryByObjectId[secondResultId] = new OutlinerHistoryModel
            {
                ObjectId = secondResultId,
                ParentIds = new List<Guid> { inputId }
            };

            var plan = OutlinerHistoryStackPlanner.Create(model);
            Assert(
                !plan.TryGetResult(inputId, out _),
                "A History input shared by two results was hidden in one stack.");
        }

        private static void KeepsFavoriteHistoryInputsCanonical()
        {
            var layerId = Guid.NewGuid();
            var inputId = Guid.NewGuid();
            var resultId = Guid.NewGuid();
            var model = HistoryModel(layerId, inputId, resultId, true);

            var plan = OutlinerHistoryStackPlanner.Create(model);
            Assert(
                !plan.TryGetResult(inputId, out _),
                "A favorite History input was removed from its canonical row.");
        }

        private static void TraversesKnownHistoryInputsRecursivelyWithoutDuplicates()
        {
            var resultId = Guid.NewGuid();
            var firstInputId = Guid.NewGuid();
            var secondInputId = Guid.NewGuid();
            var sharedInputId = Guid.NewGuid();
            var history = new Dictionary<Guid, OutlinerHistoryModel>
            {
                {
                    resultId,
                    new OutlinerHistoryModel
                    {
                        ObjectId = resultId,
                        ParentIds = new List<Guid>
                        {
                            firstInputId,
                            secondInputId
                        }
                    }
                },
                {
                    firstInputId,
                    new OutlinerHistoryModel
                    {
                        ObjectId = firstInputId,
                        ParentIds = new List<Guid> { sharedInputId }
                    }
                },
                {
                    secondInputId,
                    new OutlinerHistoryModel
                    {
                        ObjectId = secondInputId,
                        ParentIds = new List<Guid> { sharedInputId }
                    }
                },
                {
                    sharedInputId,
                    new OutlinerHistoryModel
                    {
                        ObjectId = sharedInputId,
                        ParentIds = new List<Guid> { resultId }
                    }
                }
            };

            var inputs = OutlinerHistoryTraversal.GetKnownInputs(
                history,
                new[] { resultId });
            Assert(
                inputs.SequenceEqual(new[]
                {
                    firstInputId,
                    secondInputId,
                    sharedInputId
                }),
                "Known History inputs were not traversed recursively and uniquely.");
        }

        private static OutlinerDocumentModel HistoryModel(
            Guid layerId,
            Guid inputId,
            Guid resultId,
            bool favoriteInput)
        {
            return new OutlinerDocumentModel(
                new List<OutlinerLayerModel>
                {
                    new OutlinerLayerModel { Id = layerId, Index = 0 }
                },
                new List<OutlinerObjectModel>
                {
                    new OutlinerObjectModel
                    {
                        Id = inputId,
                        LayerId = layerId,
                        LayerIndex = 0,
                        IsFavorite = favoriteInput
                    },
                    new OutlinerObjectModel
                    {
                        Id = resultId,
                        LayerId = layerId,
                        LayerIndex = 0
                    }
                },
                new List<OutlinerGroupModel>(),
                new Dictionary<Guid, OutlinerHistoryModel>
                {
                    {
                        resultId,
                        new OutlinerHistoryModel
                        {
                            ObjectId = resultId,
                            ParentIds = new List<Guid> { inputId }
                        }
                    }
                });
        }

        private static IList<IList<DerivedNamingCandidate>> NamingStages(
            params DerivedNamingCandidate[] candidates)
        {
            return new List<IList<DerivedNamingCandidate>>
            {
                (candidates ?? Array.Empty<DerivedNamingCandidate>()).ToList()
            };
        }

        private static void SharesBlockDefinitionsAcrossInstances()
        {
            var definitionId = Guid.NewGuid();
            var definition = new OutlinerBlockDefinitionModel
            {
                Id = definitionId,
                Name = "Chair",
                Components = new List<OutlinerBlockComponentModel>
                {
                    new OutlinerBlockComponentModel
                    {
                        ObjectId = Guid.NewGuid(),
                        DisplayName = "Seat"
                    }
                }
            };
            var instances = new[]
            {
                new OutlinerObjectModel
                {
                    Id = Guid.NewGuid(),
                    BlockDefinitionId = definitionId
                },
                new OutlinerObjectModel
                {
                    Id = Guid.NewGuid(),
                    BlockDefinitionId = definitionId
                }
            };
            var model = new OutlinerDocumentModel(
                Array.Empty<OutlinerLayerModel>(),
                instances,
                Array.Empty<OutlinerGroupModel>(),
                new Dictionary<Guid, OutlinerHistoryModel>(),
                new Dictionary<Guid, OutlinerBlockDefinitionModel>
                {
                    [definitionId] = definition
                });

            Assert(
                model.Objects.Count == 2 &&
                model.BlockDefinitionsById.Count == 1 &&
                ReferenceEquals(
                    model.BlockDefinitionsById[definitionId],
                    definition),
                "Repeated block instances duplicated their definition model.");

            var componentNode =
                OutlinerNodeReference.ForBlockDefinitionComponent(
                    definition.Components[0].ObjectId,
                    instances[0].Id,
                    0,
                    0);
            Assert(
                componentNode.IsReadOnlyRelationship &&
                !componentNode.IsObjectReference &&
                !componentNode.IsCanonical,
                "A block definition component became an editable object row.");
        }

        private static void KeepsNewerSelectionOperationsProtected()
        {
            var gate = new OutlinerSelectionEventGate();
            var staleGeneration = gate.BeginTreeWrite();
            var currentGeneration = gate.BeginTreeWrite();

            Assert(
                !gate.CompleteTreeWrite(staleGeneration),
                "A stale tree update released a newer selection operation.");
            Assert(
                gate.TreeWritePending,
                "A newer tree update was no longer protected.");
            Assert(
                gate.CompleteTreeWrite(currentGeneration) &&
                !gate.TreeWritePending,
                "The current tree update did not complete normally.");
        }

        private static void LetsUserSelectionOverrideProgrammaticWrites()
        {
            var gate = new OutlinerSelectionEventGate();
            var generation = gate.BeginTreeWrite();

            gate.BeginUserInput();

            Assert(
                !gate.SuppressTreeSelection,
                "User input remained blocked by a programmatic tree write.");
            Assert(
                !gate.CompleteTreeWrite(generation),
                "A canceled tree write was completed after user input.");
        }

        private static void ReleasesExpansionProtectionWithoutACompletionEvent()
        {
            var gate = new OutlinerSelectionEventGate();
            var generation = gate.BeginExpansion();

            Assert(
                gate.SuppressTreeSelection,
                "Expansion did not protect the current Rhino selection.");
            Assert(
                gate.CompleteExpansion(generation) &&
                !gate.SuppressTreeSelection,
                "Expansion protection could not be released independently.");
        }

        private static void BuildsGroupMembershipsInOnePass()
        {
            var first = Guid.NewGuid();
            var second = Guid.NewGuid();
            var third = Guid.NewGuid();
            var index = new OutlinerGroupMembershipIndex();

            index.Add(first, new[] { 4, 2, 4 });
            index.Add(second, new[] { 2 });
            index.Add(third, null);

            Assert(
                index.MembershipCount == 3,
                "Duplicate native group indices were counted more than once.");
            Assert(
                index.MemberIdsByGroupIndex[2].SequenceEqual(
                    new[] { first, second }),
                "Group members did not preserve object enumeration order.");
            Assert(
                index.MemberIdsByGroupIndex[4].SequenceEqual(new[] { first }),
                "A group received the wrong object members.");
            Assert(
                !index.GroupIndicesByObjectId.ContainsKey(third),
                "An ungrouped object was unnecessarily indexed.");
        }

        private static void PreservesIncrementalStateChangeKinds()
        {
            var stateLayerId = Guid.NewGuid();
            var propertyLayerId = Guid.NewGuid();
            var objectId = Guid.NewGuid();
            var groupId = Guid.NewGuid();
            var pending = new OutlinerStateChange();
            pending.Merge(new OutlinerStateChange().AddLayers(
                new[] { stateLayerId }));
            pending.Merge(new OutlinerStateChange().AddLayerProperties(
                new[] { propertyLayerId }));
            pending.Merge(new OutlinerStateChange
            {
                CurrentLayerChanged = true,
                IsolateChanged = true
            }.AddObjects(new[] { objectId }));
            pending.Merge(new OutlinerStateChange().AddFavoriteGroups(
                new[] { groupId }));

            Assert(pending.LayerStateChanged,
                "Layer state changes were lost while coalescing refreshes.");
            Assert(pending.LayerPropertiesChanged,
                "Layer property changes were lost while coalescing refreshes.");
            Assert(pending.CurrentLayerChanged && pending.IsolateChanged,
                "Current-layer or Isolate state was lost while coalescing.");
            Assert(pending.LayerIds.SetEquals(
                    new[] { stateLayerId, propertyLayerId }),
                "Coalescing changed the affected layer ids.");
            Assert(pending.ObjectIds.SetEquals(new[] { objectId }),
                "Coalescing changed the affected object ids.");
            Assert(pending.FavoritesChanged &&
                pending.GroupIds.SetEquals(new[] { groupId }),
                "Favorite group changes were lost while coalescing refreshes.");

            pending.Clear();
            Assert(pending.IsEmpty,
                "Clearing a coalesced state change left pending work behind.");
        }

        private static void CoalescesStructuralChangesWithoutLosingDirtyLayers()
        {
            var firstObjectId = Guid.NewGuid();
            var secondObjectId = Guid.NewGuid();
            var pending = new OutlinerStructureChange();
            pending.Merge(new OutlinerStructureChange().AddObject(
                OutlinerStructureChangeKind.ObjectAdded,
                firstObjectId,
                new[] { 2 },
                Array.Empty<int>()));
            pending.Merge(new OutlinerStructureChange().AddObject(
                OutlinerStructureChangeKind.ObjectDeleted,
                secondObjectId,
                new[] { 5 },
                new[] { 7 },
                true));

            Assert(
                pending.Kinds.HasFlag(
                    OutlinerStructureChangeKind.ObjectAdded) &&
                pending.Kinds.HasFlag(
                    OutlinerStructureChangeKind.ObjectDeleted),
                "Structural event kinds were lost while coalescing.");
            Assert(
                pending.ObjectIds.SetEquals(
                    new[] { firstObjectId, secondObjectId }),
                "Structural object ids were lost while coalescing.");
            Assert(pending.LayerIndices.SetEquals(new[] { 2, 5 }),
                "Dirty layer indices were lost while coalescing.");
            Assert(pending.GroupIndices.SetEquals(new[] { 7 }) &&
                pending.HasGroupImpact,
                "Group impact was lost while coalescing.");

            var forced = pending.Clone().ForceFull(
                OutlinerStructureChangeKind.LayerTable);
            Assert(forced.RequiresFullRefresh,
                "A forced full refresh was not preserved.");
            Assert(!pending.RequiresFullRefresh,
                "Cloning a structure change mutated the source.");
        }

        private static void CoalescesLayerAdditionsWithoutLosingTheirIdentity()
        {
            var pending = new OutlinerStructureChange();
            pending.Merge(
                new OutlinerStructureChange().AddLayerAddition(3));
            pending.Merge(
                new OutlinerStructureChange().AddLayerAddition(8));

            Assert(
                pending.Kinds == OutlinerStructureChangeKind.LayerTable,
                "Layer additions introduced an unrelated structure kind.");
            Assert(
                pending.LayerIndices.SetEquals(new[] { 3, 8 }) &&
                pending.AddedLayerIndices.SetEquals(new[] { 3, 8 }),
                "Coalescing lost the identity of newly added layers.");

            var clone = pending.Clone();
            pending.Clear();
            Assert(
                clone.AddedLayerIndices.SetEquals(new[] { 3, 8 }),
                "Cloning lost newly added layer indices.");
            Assert(
                pending.IsEmpty && pending.AddedLayerIndices.Count == 0,
                "Clearing left newly added layers pending.");
        }

        private static void CompactsInterleavedLayerObjectsWithoutChangingSurvivors()
        {
            var dirty = CreateLayer(1);
            var untouched = CreateLayer(2);
            var objects = Enumerable.Range(0, 20000)
                .Select(index => CreateObject(index % 2 == 0 ? dirty : untouched,
                    "Object" + index)).ToList();
            var survivors = objects.Where(item => item.LayerId == untouched.Id).ToList();
            var model = CreateDocumentModel(new[] { dirty, untouched }, objects);
            var added = CreateObject(dirty, "Added");
            Assert(model.TryReplaceLayerObjects(
                new Dictionary<Guid, IList<OutlinerObjectModel>>
                {
                    [dirty.Id] = new[] { added }
                }, out var removed, out var failure),
                "Interleaved replacement failed: " + failure);
            Assert(model.Objects.SequenceEqual(survivors.Concat(new[] { added })),
                "Compaction changed survivor order or identity.");
            Assert(removed.Count == 10000 && model.ObjectsById.Count == 10001,
                "Compaction lost object-index consistency.");
            Assert(model.ObjectsByLayerId[untouched.Id].SequenceEqual(survivors) &&
                model.ObjectsByLayerId[dirty.Id].Single() == added,
                "Compaction changed layer-index consistency.");
        }

        private static void ReplacesDirtyLayerObjectsAtomically()
        {
            var firstLayer = CreateLayer(1);
            var secondLayer = CreateLayer(2);
            var movingObject = CreateObject(firstLayer, "Moving");
            var stationaryObject = CreateObject(secondLayer, "Stationary");
            var model = CreateDocumentModel(
                new[] { firstLayer, secondLayer },
                new[] { movingObject, stationaryObject });
            var movedObject = CreateObject(secondLayer, "Moved", movingObject.Id);

            var replaced = model.TryReplaceLayerObjects(
                new Dictionary<Guid, IList<OutlinerObjectModel>>
                {
                    [firstLayer.Id] = new List<OutlinerObjectModel>(),
                    [secondLayer.Id] = new List<OutlinerObjectModel>
                    {
                        stationaryObject,
                        movedObject
                    }
                },
                out var removedIds,
                out var failureReason);

            Assert(replaced,
                "A safe dirty-layer replacement failed: " + failureReason);
            Assert(removedIds.Count == 0,
                "A layer move was incorrectly reported as object removal.");
            Assert(model.ObjectsById[movingObject.Id].LayerId == secondLayer.Id,
                "The moved object was not committed to its destination layer.");
            Assert(model.ObjectsByLayerId[firstLayer.Id].Count == 0 &&
                model.ObjectsByLayerId[secondLayer.Id].Count == 2,
                "Dirty-layer indexes were not replaced atomically.");
        }

        private static void ReplacesNativeObjectWithoutTouchingUnrelatedLayers()
        {
            var dirtyLayer = CreateLayer(1);
            var untouchedLayer = CreateLayer(2);
            var original = CreateObject(dirtyLayer, "Original");
            var untouched = CreateObject(untouchedLayer, "Untouched");
            var model = CreateDocumentModel(
                new[] { dirtyLayer, untouchedLayer },
                new[] { original, untouched });
            var replacement = CreateObject(
                dirtyLayer,
                "Replacement",
                Guid.NewGuid());

            var replaced = model.TryReplaceLayerObjects(
                new Dictionary<Guid, IList<OutlinerObjectModel>>
                {
                    [dirtyLayer.Id] = new List<OutlinerObjectModel>
                    {
                        replacement
                    }
                },
                out var removedIds,
                out var failureReason);

            Assert(replaced,
                "A native object replacement failed: " + failureReason);
            Assert(removedIds.SetEquals(new[] { original.Id }),
                "The replaced native object was not reported as removed.");
            Assert(!model.ObjectsById.ContainsKey(original.Id) &&
                model.ObjectsById.ContainsKey(replacement.Id),
                "The replacement did not update the object index.");
            Assert(model.ObjectsById.TryGetValue(
                    untouched.Id,
                    out var untouchedAfter) &&
                ReferenceEquals(untouched, untouchedAfter) &&
                model.ObjectsByLayerId[untouchedLayer.Id].Count == 1,
                "Replacing one layer disturbed an unrelated layer.");
        }

        private static void LeavesModelUntouchedWhenReplacementValidationFails()
        {
            var layer = CreateLayer(1);
            var groupedObject = CreateObject(layer, "Grouped");
            var group = new OutlinerGroupModel
            {
                Id = Guid.NewGuid(),
                Index = 4,
                Name = "Group",
                MemberIds = new List<Guid> { groupedObject.Id }
            };
            groupedObject.GroupIds.Add(group.Id);
            var model = CreateDocumentModel(
                new[] { layer },
                new[] { groupedObject },
                new[] { group });
            var invalidReplacement = CreateObject(
                layer,
                "Invalid replacement",
                groupedObject.Id);

            var replaced = model.TryReplaceLayerObjects(
                new Dictionary<Guid, IList<OutlinerObjectModel>>
                {
                    [layer.Id] = new List<OutlinerObjectModel>
                    {
                        invalidReplacement
                    }
                },
                out var removedIds,
                out var failureReason);

            Assert(!replaced && failureReason == "changed-group-membership",
                "An incomplete replacement did not fail safely.");
            Assert(removedIds.Count == 0 &&
                ReferenceEquals(model.ObjectsById[groupedObject.Id], groupedObject) &&
                model.GroupsById.ContainsKey(group.Id) &&
                model.GroupMembershipCount == 1,
                "A rejected replacement partially mutated the cached model.");
        }

        private static void RejectsUnsafeGroupedObjectRemoval()
        {
            var layer = CreateLayer(1);
            var groupedObject = CreateObject(layer, "Grouped");
            var group = new OutlinerGroupModel
            {
                Id = Guid.NewGuid(),
                Index = 4,
                Name = "Group",
                MemberIds = new List<Guid> { groupedObject.Id }
            };
            groupedObject.GroupIds.Add(group.Id);
            var model = CreateDocumentModel(
                new[] { layer },
                new[] { groupedObject },
                new[] { group });

            var replaced = model.TryReplaceLayerObjects(
                new Dictionary<Guid, IList<OutlinerObjectModel>>
                {
                    [layer.Id] = new List<OutlinerObjectModel>()
                },
                out var removedIds,
                out var failureReason);

            Assert(!replaced && failureReason == "removed-group-member",
                "A grouped-object removal did not request a full refresh.");
            Assert(removedIds.Count == 0 &&
                model.ObjectsById.ContainsKey(groupedObject.Id),
                "A rejected replacement mutated the cached document model.");
        }

        private static void ReplacesImpactedGroupMembershipsAtomically()
        {
            var layer = CreateLayer(1);
            var first = CreateObject(layer, "First");
            var second = CreateObject(layer, "Second");
            var group = new OutlinerGroupModel
            {
                Id = Guid.NewGuid(),
                Index = 4,
                Name = "Group",
                MemberIds = new List<Guid> { first.Id }
            };
            first.GroupIds.Add(group.Id);
            var model = CreateDocumentModel(
                new[] { layer },
                new[] { first, second },
                new[] { group });
            Assert(model.GroupHierarchy.GetCanonicalContainerId(first.Id) ==
                group.Id,
                "The initial group hierarchy was not built for the test.");

            var newFirst = CreateObject(layer, "First", first.Id);
            var newSecond = CreateObject(layer, "Second", second.Id);
            newSecond.GroupIds.Add(group.Id);
            var replacementGroup = new OutlinerGroupModel
            {
                Id = group.Id,
                Index = group.Index,
                Name = group.Name,
                MemberIds = new List<Guid> { second.Id }
            };
            var replaced = model.TryReplaceLayerObjects(
                new Dictionary<Guid, IList<OutlinerObjectModel>>
                {
                    [layer.Id] = new List<OutlinerObjectModel>
                    {
                        newFirst,
                        newSecond
                    }
                },
                out var removedIds,
                out var failureReason,
                new Dictionary<int, OutlinerGroupModel>
                {
                    [group.Index] = replacementGroup
                });

            Assert(replaced,
                "An impacted group replacement failed: " + failureReason);
            Assert(removedIds.Count == 0 && model.GroupMembershipCount == 1,
                "The group replacement changed unrelated model counts.");
            Assert(model.GroupHierarchy.GetCanonicalContainerId(first.Id) ==
                    Guid.Empty &&
                model.GroupHierarchy.GetCanonicalContainerId(second.Id) ==
                    group.Id,
                "The cached group hierarchy was not rebuilt after replacement.");
        }

        private static void FiltersCurrentAndSelectedLayerTrees()
        {
            var root = CreateLayer(1);
            var current = CreateLayer(2);
            var descendant = CreateLayer(3);
            var unrelated = CreateLayer(4);
            current.ParentId = root.Id;
            current.IsCurrent = true;
            descendant.ParentId = current.Id;
            foreach (var layer in new[] { root, current, descendant, unrelated })
            {
                layer.OwnVisible = true;
                layer.TopLevelObjectCount = 0;
            }

            var model = CreateDocumentModel(
                new[] { root, current, descendant, unrelated },
                new List<OutlinerObjectModel>());
            var filter = new OutlinerLayerFilterState
            {
                Scope = OutlinerLayerScopeMode.CurrentLayerTree
            };

            Assert(!filter.Matches(root, model),
                "Current-layer scope included an ancestor as a direct match.");
            Assert(filter.Matches(current, model) &&
                filter.Matches(descendant, model),
                "Current-layer scope did not include its complete subtree.");
            Assert(!filter.Matches(unrelated, model),
                "Current-layer scope included an unrelated layer.");

            filter.Scope = OutlinerLayerScopeMode.SelectedLayerTrees;
            filter.SelectedLayerIds.Add(root.Id);
            Assert(filter.Matches(root, model) &&
                filter.Matches(current, model) &&
                filter.Matches(descendant, model),
                "Selected-layer scope did not include descendant layers.");
            Assert(!filter.Matches(unrelated, model),
                "Selected-layer scope included an unrelated layer.");
        }

        private static void DoesNotTreatDeferredLayersAsEmpty()
        {
            var layer = CreateLayer(1);
            layer.OwnVisible = true;
            layer.ObjectsLoaded = false;
            layer.TopLevelObjectCount = -1;
            var model = CreateDocumentModel(
                new[] { layer },
                new List<OutlinerObjectModel>());
            var filter = new OutlinerLayerFilterState
            {
                Occupancy = OutlinerLayerOccupancyFilter.Empty
            };

            Assert(!filter.Matches(layer, model),
                "An unloaded layer with unknown occupancy was treated as empty.");
            layer.TopLevelObjectCount = 0;
            Assert(filter.Matches(layer, model),
                "A known empty layer did not match the empty-layer filter.");
        }

        private static void FiltersObjectsByType()
        {
            var layer = CreateLayer(1);
            var item = CreateObject(layer, "Surface");
            item.IconKind = OutlinerIconKind.Surface;
            var filter = new OutlinerContentFilterState();

            Assert(filter.MatchesObjectType(item),
                "The default object-type filter excluded an object.");
            filter.ObjectTypes.Add(OutlinerIconKind.Surface);

            Assert(filter.MatchesObjectType(item),
                "A matching object type was filtered out.");
            Assert(filter.PreservesLayerRows,
                "An object-type filter unexpectedly removed layer rows.");
            item.IconKind = OutlinerIconKind.Curve;
            Assert(!filter.MatchesObjectType(item),
                "Object-type filtering included the wrong shape category.");

            filter.Reset();
            Assert(filter.MatchesObjectType(item),
                "Reset did not restore all object types.");
            filter.FavoritesOnly = true;
            Assert(filter.IsActive && filter.ActiveConditionCount == 1,
                "Favorites-only did not participate in filter state.");
            Assert(!filter.PreservesLayerRows,
                "Favorites-only stopped filtering non-favorite layer rows.");
            filter.Reset();
            Assert(!filter.FavoritesOnly && !filter.IsActive,
                "Reset did not clear the favorites-only filter.");
        }

        private static void CombinesSelectedAndUserNamedObjectFilters()
        {
            var layer = CreateLayer(1);
            var item = CreateObject(layer, "Handrail");
            item.IconKind = OutlinerIconKind.Curve;
            item.IsUserNamed = true;
            var selectedIds = new HashSet<Guid> { item.Id };
            var filter = new OutlinerContentFilterState
            {
                SelectedObjectsOnly = true,
                UserNamedOnly = true
            };
            filter.ObjectTypes.Add(OutlinerIconKind.Curve);

            Assert(filter.MatchesObject(item, selectedIds),
                "Combined selected, user-named, and type filters rejected a match.");

            selectedIds.Clear();
            Assert(!filter.MatchesObject(item, selectedIds),
                "Selected-only included an unselected object.");

            selectedIds.Add(item.Id);
            item.IsUserNamed = false;
            Assert(!filter.MatchesObject(item, selectedIds),
                "User-named-only included a generic automatic name.");

            filter.Reset();
            Assert(filter.MatchesObject(item, selectedIds) &&
                !filter.SelectedObjectsOnly &&
                !filter.UserNamedOnly,
                "Reset did not clear object discovery conditions.");
        }

        private static void ParsesFavoriteMetadataDefensively()
        {
            var missing = FavoriteCodec.Read(null);
            var favorite = FavoriteCodec.Read("1:true");
            var notFavorite = FavoriteCodec.Read("1:false");
            var future = FavoriteCodec.Read("2:true");
            var invalid = FavoriteCodec.Read("favorite");

            Assert(!missing.IsFavorite && missing.CanEdit,
                "Missing favorite metadata was not editable by default.");
            Assert(favorite.IsFavorite && favorite.CanEdit,
                "Supported favorite metadata was not read correctly.");
            Assert(!notFavorite.IsFavorite && notFavorite.CanEdit,
                "Supported false favorite metadata was not read correctly.");
            Assert(future.IsFavorite && !future.CanEdit,
                "Future favorite metadata was not preserved read-only.");
            Assert(!invalid.IsFavorite && !invalid.CanEdit,
                "Malformed favorite metadata was treated as writable.");
            Assert(FavoriteCodec.Write(true) == "1:true" &&
                FavoriteCodec.Write(false) == null,
                "Favorite metadata serialization changed unexpectedly.");
        }

        private static OutlinerDocumentModel CreateDocumentModel(
            IList<OutlinerLayerModel> layers,
            IList<OutlinerObjectModel> objects,
            IList<OutlinerGroupModel> groups = null)
        {
            return new OutlinerDocumentModel(
                layers,
                objects,
                groups ?? new List<OutlinerGroupModel>(),
                new Dictionary<Guid, OutlinerHistoryModel>());
        }

        private static void ResolvesCompleteLayerSubtrees()
        {
            var root = CreateLayer(1);
            var child = CreateLayer(2);
            var grandchild = CreateLayer(3);
            var unrelated = CreateLayer(4);
            child.ParentId = root.Id;
            grandchild.ParentId = child.Id;
            var model = CreateDocumentModel(
                new[] { root, child, grandchild, unrelated },
                new List<OutlinerObjectModel>());

            var subtree = OutlinerLayerHierarchy.GetSubtreeIds(
                model,
                new[] { root.Id });
            Assert(
                subtree.SetEquals(new[]
                {
                    root.Id,
                    child.Id,
                    grandchild.Id
                }),
                "Layer subtree expansion missed a descendant or included an unrelated layer.");

            var unknown = OutlinerLayerHierarchy.GetSubtreeIds(
                model,
                new[] { Guid.NewGuid() });
            Assert(unknown.Count == 0,
                "An unknown layer ID was retained in the expansion plan.");
        }

        private static void PlansOnlyDirectLayerContentsForExpansion()
        {
            var root = CreateLayer(1);
            var child = CreateLayer(2);
            var grandchild = CreateLayer(3);
            child.ParentId = root.Id;
            grandchild.ParentId = child.Id;

            var nestedMember = CreateObject(root, "Nested member");
            var outerOnlyMember = CreateObject(root, "Outer member");
            var directHistoryObject = CreateObject(root, "Direct history");
            var outerGroup = new OutlinerGroupModel
            {
                Id = Guid.NewGuid(),
                Index = 10,
                Name = "Outer",
                MemberIds = new List<Guid>
                {
                    nestedMember.Id,
                    outerOnlyMember.Id
                }
            };
            var innerGroup = new OutlinerGroupModel
            {
                Id = Guid.NewGuid(),
                Index = 11,
                Name = "Inner",
                MemberIds = new List<Guid> { nestedMember.Id }
            };
            nestedMember.GroupIds.Add(outerGroup.Id);
            nestedMember.GroupIds.Add(innerGroup.Id);
            outerOnlyMember.GroupIds.Add(outerGroup.Id);
            var model = CreateDocumentModel(
                new[] { root, child, grandchild },
                new[]
                {
                    nestedMember,
                    outerOnlyMember,
                    directHistoryObject
                },
                new[] { outerGroup, innerGroup });
            var outerSlice = new OutlinerGroupSliceKey(
                outerGroup.Id,
                root.Id);
            var innerSlice = new OutlinerGroupSliceKey(
                innerGroup.Id,
                root.Id);

            var plan = OutlinerLayerExpansionPlanner.Create(
                model,
                new[] { root.Id },
                new[] { outerSlice, innerSlice },
                new[] { directHistoryObject.Id, nestedMember.Id });

            Assert(plan.LayerIds.SetEquals(new[] { child.Id }),
                "Layer expansion changed its target or a grandchild, or missed a direct child.");
            Assert(plan.GroupSlices.SetEquals(new[] { outerSlice }),
                "Layer expansion included a nested group slice.");
            Assert(plan.HistoryObjectIds.SetEquals(
                    new[] { directHistoryObject.Id }),
                "Layer expansion included History below a nested group.");
        }

        private static void PlansOnlyDirectGroupContentsForExpansion()
        {
            var layer = CreateLayer(1);
            var nestedMember = CreateObject(layer, "Nested member");
            var innerMember = CreateObject(layer, "Inner member");
            var outerMember = CreateObject(layer, "Outer member");
            var outerGroup = new OutlinerGroupModel
            {
                Id = Guid.NewGuid(),
                Index = 20,
                Name = "Outer",
                MemberIds = new List<Guid>
                {
                    nestedMember.Id,
                    innerMember.Id,
                    outerMember.Id
                }
            };
            var innerGroup = new OutlinerGroupModel
            {
                Id = Guid.NewGuid(),
                Index = 21,
                Name = "Inner",
                MemberIds = new List<Guid>
                {
                    nestedMember.Id,
                    innerMember.Id
                }
            };
            var grandchildGroup = new OutlinerGroupModel
            {
                Id = Guid.NewGuid(),
                Index = 22,
                Name = "Grandchild",
                MemberIds = new List<Guid> { nestedMember.Id }
            };
            nestedMember.GroupIds.Add(outerGroup.Id);
            nestedMember.GroupIds.Add(innerGroup.Id);
            nestedMember.GroupIds.Add(grandchildGroup.Id);
            innerMember.GroupIds.Add(outerGroup.Id);
            innerMember.GroupIds.Add(innerGroup.Id);
            outerMember.GroupIds.Add(outerGroup.Id);
            var model = CreateDocumentModel(
                new[] { layer },
                new[] { nestedMember, innerMember, outerMember },
                new[] { outerGroup, innerGroup, grandchildGroup });
            var outerSlice = new OutlinerGroupSliceKey(
                outerGroup.Id,
                layer.Id);
            var innerSlice = new OutlinerGroupSliceKey(
                innerGroup.Id,
                layer.Id);
            var grandchildSlice = new OutlinerGroupSliceKey(
                grandchildGroup.Id,
                layer.Id);

            var plan = OutlinerGroupExpansionPlanner.Create(
                model,
                new[] { outerSlice },
                new[] { outerSlice, innerSlice, grandchildSlice },
                new[] { outerMember.Id, nestedMember.Id });

            Assert(plan.TargetSlices.SetEquals(new[] { outerSlice }),
                "Group expansion lost its exact target slice.");
            Assert(plan.GroupSlices.SetEquals(new[] { innerSlice }),
                "Group expansion changed a grandchild or missed a direct child group.");
            Assert(plan.HistoryObjectIds.SetEquals(new[] { outerMember.Id }),
                "Group expansion included History below a nested child group.");
        }

        private static OutlinerLayerModel CreateLayer(int index)
        {
            return new OutlinerLayerModel
            {
                Id = Guid.NewGuid(),
                Index = index,
                Name = "Layer " + index,
                ObjectsLoaded = true
            };
        }

        private static OutlinerObjectModel CreateObject(
            OutlinerLayerModel layer,
            string name,
            Guid id = default(Guid))
        {
            return new OutlinerObjectModel
            {
                Id = id == Guid.Empty ? Guid.NewGuid() : id,
                LayerId = layer.Id,
                LayerIndex = layer.Index,
                DisplayName = name,
                GroupIds = new List<Guid>()
            };
        }

        private static void AvoidsRepeatedNativeDeselectionAndOversizedRefresh()
        {
            Assert(OutlinerRefreshPolicy.ReplaceSelection(0, 24), "24 removals must use one native write.");
            Assert(OutlinerRefreshPolicy.ReplaceSelection(1, 2), "Mixed changes with multiple removals must use one write.");
            Assert(!OutlinerRefreshPolicy.ReplaceSelection(1, 0), "Keep single-row addition incremental.");
            Assert(!OutlinerRefreshPolicy.ReplaceSelection(0, 1), "Keep single-row removal incremental.");
            Assert(OutlinerRefreshPolicy.ReplaceSelection(128, 0), "Preserve large selection batching.");
            Assert(OutlinerRefreshPolicy.UseIncrementalLayers(17, 20000, 198788), "A small subset across 17 layers should not force full refresh.");
            Assert(!OutlinerRefreshPolicy.UseIncrementalLayers(17, 180000, 198788), "Retain fallback for broad object changes.");
            Assert(!OutlinerRefreshPolicy.UseIncrementalLayers(65, 100, 198788), "Bound the number of native subtree reloads.");
            Assert(OutlinerRefreshPolicy.UseIncrementalLayers(8, 10000, 10000), "Preserve the existing small-layer path.");
        }

        private static void ReconcilesOnlyChangedCopyGroups()
        {
            var revision = new CopyGroupRevision();
            Assert(revision.NeedsReconciliation, "Initial batch requires reconciliation.");
            revision.Reconciled(revision.Current);
            Assert(!revision.NeedsReconciliation, "Completion callbacks without changes should reuse reconciliation.");
            revision.Changed();
            var started = revision.Current;
            revision.Changed();
            revision.Reconciled(started);
            Assert(revision.NeedsReconciliation, "Changes arriving during reconciliation must not be lost.");
            revision.Reconciled(revision.Current);
            Assert(!revision.NeedsReconciliation, "The latest revision can be completed.");
        }

        private static void RecordsDiagnosticContextWithoutChangingActions()
        {
            OutlinerDiagnosticMode.SetEnabled(false);
            Assert(OutlinerDiagnosticScope.Start("disabled", 1) == null, "Disabled scopes must not allocate.");
            OutlinerDiagnosticMode.SetEnabled(true);
            var outer = OutlinerDiagnosticScope.Start("outer", 17);
            var id = OutlinerDiagnosticScope.CurrentId;
            var inner = OutlinerDiagnosticScope.Start("inner", 18);
            Assert(id > 0 && OutlinerDiagnosticScope.CurrentId == id, "Nested scopes share a dispatch ID.");
            Assert(inner.Metrics.MeasureDetail("value", () => 42) == 42, "Measurement must preserve results.");
            var threw = false;
            try { inner.Metrics.MeasureDetail("failure", () => { throw new InvalidOperationException(); }); }
            catch (InvalidOperationException) { threw = true; }
            Assert(threw, "Measurement must preserve exceptions.");
            OutlinerDiagnosticMode.SetEnabled(false);
            inner.Dispose();
            Assert(OutlinerDiagnosticScope.CurrentDocument == 17, "Nested scope restores document context.");
            outer.Dispose();
            Assert(OutlinerDiagnosticScope.CurrentId == 0, "Dispatch scope must not leak.");
            var json = OutlinerPerformanceRecorder.ToJson(new OutlinerPerformanceSample
            {
                TraceId = id,
                DocumentSerial = 17,
                Reason = "test\"reason",
                Details = new Dictionary<string, long> { { "queueWaitUs", 1234 } }
            });
            using var parsed = System.Text.Json.JsonDocument.Parse(json);
            Assert(parsed.RootElement.GetProperty("traceId").GetInt64() == id, "Trace ID must be serialized.");
            Assert(parsed.RootElement.GetProperty("detail_queueWaitUs").GetInt64() == 1234, "Detailed timings must be serialized.");
        }

        private static void AllocatesSharedCopyNames()
        {
            var enumerations = 0;
            IEnumerable<string> ExistingNames()
            {
                enumerations++;
                yield return "Curve.001";
                yield return "CURVE.003";
                yield return "Column Copy.001";
            }
            var allocator = new GenericObjectNameAllocator(ExistingNames());
            Assert(allocator.Next("Curve") == "Curve.002", "Fill the first available number.");
            Assert(allocator.Next("curve") == "curve.004", "Names must be case-insensitively unique.");
            Assert(allocator.Next("Column Copy") == "Column Copy.002", "Named copies share reservations.");
            var allocated = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < 10000; i++)
                Assert(allocated.Add(allocator.Next("Curve")), "Array copies must have unique names.");
            Assert(enumerations == 1, "Existing names must only be enumerated once.");
            Assert(allocator.Next(" ") == "Object.001", "Empty base names retain their fallback.");
            var fresh = new GenericObjectNameAllocator(new[] { "Curve.001" });
            Assert(fresh.Next("Curve") == "Curve.002", "Independent operations must not share reservations.");
        }

        private static void MatchesNestedGroupsByExactMemberSet()
        {
            var first = Guid.NewGuid();
            var second = Guid.NewGuid();
            var third = Guid.NewGuid();
            var expectations = new[]
            {
                Expect("A", 1, first, second, third),
                Expect("B", 2, first, second),
                Expect("B2", 3, first),
                Expect("C", 4, third)
            };
            var candidates = new[]
            {
                Candidate(103, first),
                Candidate(101, first, second, third),
                Candidate(104, third),
                Candidate(102, first, second)
            };

            var result = CopyGroupMatcher.Match(expectations, candidates);

            AssertMatch(result, "A", 101);
            AssertMatch(result, "B", 102);
            AssertMatch(result, "B2", 103);
            AssertMatch(result, "C", 104);
        }

        private static void DoesNotConfuseEqualSizeGroups()
        {
            var first = Guid.NewGuid();
            var second = Guid.NewGuid();
            var third = Guid.NewGuid();
            var fourth = Guid.NewGuid();
            var result = CopyGroupMatcher.Match(
                new[]
                {
                    Expect("Left", 1, first, second),
                    Expect("Right", 2, third, fourth)
                },
                new[]
                {
                    Candidate(10, third, fourth),
                    Candidate(11, first, second)
                });

            AssertMatch(result, "Left", 11);
            AssertMatch(result, "Right", 10);
        }

        private static void MatchesDuplicateMemberSetsDeterministically()
        {
            var first = Guid.NewGuid();
            var second = Guid.NewGuid();
            var result = CopyGroupMatcher.Match(
                new[]
                {
                    Expect("Earlier", 1, first, second),
                    Expect("Later", 2, first, second)
                },
                new[]
                {
                    Candidate(20, first, second),
                    Candidate(21, first, second)
                });

            AssertMatch(result, "Earlier", 20);
            AssertMatch(result, "Later", 21);
        }

        private static void LeavesIncompleteGroupsUnmatched()
        {
            var first = Guid.NewGuid();
            var second = Guid.NewGuid();
            var result = CopyGroupMatcher.Match(
                new[] { Expect("Complete", 1, first, second) },
                new[] { Candidate(30, first) });

            Assert(result.Count == 0, "A partial group was incorrectly matched.");
        }

        private static void PreservesFullGroupBeforeRemovingPartialAncestor()
        {
            var first = Guid.NewGuid();
            var second = Guid.NewGuid();
            var partialAncestor = Expect("Partial ancestor", 1, first, second);
            partialAncestor.Preserve = false;
            var completeChild = Expect("Complete child", 2, first, second);
            completeChild.Preserve = true;

            var result = CopyGroupMatcher.Match(
                new[] { partialAncestor, completeChild },
                new[] { Candidate(40, first, second) });

            AssertMatch(result, "Complete child", 40);
            Assert(
                result.All(item => item.Expectation.Key != "Partial ancestor"),
                "A partial ancestor took precedence over a complete child group.");
        }

        private static void MatchesPartialGroupForRemovalWhenNoCompleteGroupExists()
        {
            var copiedMember = Guid.NewGuid();
            var partialGroup = Expect("Partial group", 1, copiedMember);
            partialGroup.Preserve = false;

            var result = CopyGroupMatcher.Match(
                new[] { partialGroup },
                new[] { Candidate(41, copiedMember) });

            AssertMatch(result, "Partial group", 41);
        }

        private static void ReconcilesReverseOrderedNestedGroupMembers()
        {
            var sourceOuter = Guid.NewGuid();
            var sourceChild1 = Guid.NewGuid();
            var sourceChild2 = Guid.NewGuid();
            var sourceChild3 = Guid.NewGuid();
            var copiedChild1 = Guid.NewGuid();
            var copiedChild2 = Guid.NewGuid();
            var copiedChild3 = Guid.NewGuid();
            var copiedOuter = Guid.NewGuid();
            var sources = new[]
            {
                Membership(sourceOuter, 0, 4),
                Membership(sourceChild1, 1, 3, 4),
                Membership(sourceChild2, 2, 3, 4),
                Membership(sourceChild3, 3, 3, 4)
            };
            var copies = new[]
            {
                Membership(copiedChild1, 0, 3, 4),
                Membership(copiedChild2, 1, 3, 4),
                Membership(copiedChild3, 2, 3, 4),
                Membership(copiedOuter, 3, 4)
            };
            var existing = new Dictionary<Guid, Guid>
            {
                { sourceOuter, copiedChild1 },
                { sourceChild1, copiedChild2 },
                { sourceChild2, copiedChild3 },
                { sourceChild3, copiedOuter }
            };

            var reconciled = CopyMembershipMatcher.Reconcile(
                sources,
                copies,
                existing);

            Assert(
                reconciled[sourceOuter] == copiedOuter,
                "The outer-only member was not restored after reverse-order Paste.");
            var copiedChildren = new HashSet<Guid>
            {
                copiedChild1,
                copiedChild2,
                copiedChild3
            };
            Assert(
                copiedChildren.SetEquals(new[]
                {
                    reconciled[sourceChild1],
                    reconciled[sourceChild2],
                    reconciled[sourceChild3]
                }),
                "Nested child members were not kept in the copied child group.");
        }

        private static CopyMembershipCandidate Membership(
            Guid id,
            int ordinal,
            params int[] groupSizes)
        {
            return new CopyMembershipCandidate
            {
                Id = id,
                Ordinal = ordinal,
                MatchKey = "Brep\u001f",
                GroupSizes = groupSizes
            };
        }

        private static void RanksMembershipWithoutDroppingFallbackCandidates()
        {
            var outer = Membership(Guid.NewGuid(), 0, 4);
            var child = Membership(Guid.NewGuid(), 1, 3, 4);

            var ranked = CopyMembershipMatcher.RankByMembership(
                new[] { outer, child },
                new[] { 3, 4 });

            Assert(
                ranked.SequenceEqual(new[] { child.Id, outer.Id }),
                "Membership ranking dropped the batch fallback candidate.");
        }

        private static CopyGroupExpectation Expect(
            string key,
            int sourceGroupIndex,
            params Guid[] memberIds)
        {
            return new CopyGroupExpectation
            {
                Key = key,
                SourceGroupIndex = sourceGroupIndex,
                BatchIndex = 0,
                Preserve = true,
                MemberIds = memberIds
            };
        }

        private static CopyGroupCandidate Candidate(
            int groupIndex,
            params Guid[] memberIds)
        {
            return new CopyGroupCandidate
            {
                GroupIndex = groupIndex,
                MemberIds = memberIds
            };
        }

        private static void AssertMatch(
            IEnumerable<CopyGroupMatch> matches,
            string key,
            int groupIndex)
        {
            Assert(
                matches.Any(item =>
                    item.Expectation.Key == key &&
                    item.Candidate.GroupIndex == groupIndex),
                string.Format(
                    "Expected {0} to match group {1}.",
                    key,
                    groupIndex));
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }
    }
}
