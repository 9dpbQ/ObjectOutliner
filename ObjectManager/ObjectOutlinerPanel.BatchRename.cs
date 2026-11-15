using System;
using System.Collections.Generic;
using System.Linq;
using Eto.Forms;
using Rhino;
using Rhino.DocObjects;
using ObjectManager.Outliner;

namespace ObjectManager
{
    public sealed partial class ObjectOutlinerPanel
    {
        private sealed class BatchRenameTarget
        {
            public OutlinerNodeReference Node { get; set; }

            public string Name { get; set; }

            public int DisplayOrder { get; set; }
        }

        private sealed class BatchRenameChange
        {
            public BatchRenameTarget Target { get; set; }

            public string Name { get; set; }
        }

        private void ShowBatchRename(
            RhinoDoc doc,
            IEnumerable<OutlinerNodeReference> nodes)
        {
            var targets = GetBatchRenameTargets(doc, nodes);
            if (doc == null || targets.Count < 2)
                return;

            var dialog = new BatchRenameDialog(targets.Select(item => item.Name));
            if (!dialog.ShowModal(_tree.ParentWindow))
                return;

            var preview = BatchRenamePlanner.CreatePreview(
                targets.Select(item => item.Name),
                dialog.Options);
            if (!BatchRenamePlanner.IsValid(preview, dialog.Options))
                return;

            var names = preview
                .Select(item => item.ProposedName?.Trim() ?? string.Empty)
                .ToList();
            if (!ValidateBatchRename(doc, targets, names))
            {
                MessageBox.Show(
                    this,
                    OutlinerStrings.BatchRenameInvalid,
                    MessageBoxType.Information);
                return;
            }

            if (!ApplyBatchRename(doc, targets, names))
            {
                MessageBox.Show(
                    this,
                    OutlinerStrings.BatchRenameInvalid,
                    MessageBoxType.Information);
            }
        }

        private IList<BatchRenameTarget> GetBatchRenameTargets(
            RhinoDoc doc,
            IEnumerable<OutlinerNodeReference> nodes)
        {
            if (doc == null || _snapshot == null)
                return new List<BatchRenameTarget>();

            var candidates = (nodes ?? Enumerable.Empty<OutlinerNodeReference>())
                .Where(node => node != null)
                .GroupBy(node => node.Id)
                .Select(group => group.First())
                .ToList();
            if (candidates.Count == 0 ||
                candidates.Any(node => node.Kind != candidates[0].Kind))
            {
                return new List<BatchRenameTarget>();
            }

            var visibleItems = new List<TreeGridItem>();
            AddVisibleItems(_snapshot.Roots, visibleItems);
            var visibleOrder = visibleItems
                .Select((item, index) => new { item, index })
                .ToDictionary(entry => entry.item, entry => entry.index);
            var result = new List<BatchRenameTarget>();
            foreach (var node in candidates)
            {
                var item = ResolveNameEditItem(NameEditTarget.FromNode(node));
                if (item == null)
                    continue;

                string name = null;
                if (node.Kind == OutlinerNodeKind.Layer)
                {
                    var layer = doc.Layers.FindId(node.Id);
                    if (layer == null || layer.IsDeleted || layer.IsReference)
                        continue;
                    name = layer.Name;
                }
                else if (node.Kind == OutlinerNodeKind.GroupSlice)
                {
                    var group = doc.Groups.FindIndex(node.NativeIndex);
                    if (group == null ||
                        group.IsDeleted ||
                        group.Id != node.Id)
                    {
                        continue;
                    }
                    name = _documentModel?.GroupsById.TryGetValue(
                        node.Id,
                        out var groupModel) == true
                            ? groupModel.Name
                            : doc.Groups.GroupName(node.NativeIndex);
                }
                else if (node.Kind == OutlinerNodeKind.Object)
                {
                    var rhinoObject = doc.Objects.FindId(node.Id);
                    if (rhinoObject == null ||
                        rhinoObject.IsDeleted ||
                        rhinoObject.IsReference ||
                        IsUnavailable(item))
                    {
                        continue;
                    }
                    name = GetObjectDisplayName(rhinoObject);
                }
                else
                {
                    continue;
                }

                result.Add(new BatchRenameTarget
                {
                    Node = node,
                    Name = name ?? string.Empty,
                    DisplayOrder = visibleOrder.TryGetValue(item, out var order)
                        ? order
                        : int.MaxValue
                });
            }
            return result
                .OrderBy(item => item.DisplayOrder)
                .ThenBy(item => item.Node.NativeIndex)
                .ToList();
        }

        private bool ValidateBatchRename(
            RhinoDoc doc,
            IList<BatchRenameTarget> targets,
            IList<string> names)
        {
            if (doc == null ||
                targets == null ||
                names == null ||
                targets.Count != names.Count ||
                names.Any(string.IsNullOrWhiteSpace))
            {
                return false;
            }

            var kind = targets[0].Node.Kind;
            if (kind == OutlinerNodeKind.Object)
                return true;

            var targetIds = new HashSet<Guid>(targets.Select(item => item.Node.Id));
            if (kind == OutlinerNodeKind.GroupSlice)
            {
                if (names.Distinct(StringComparer.CurrentCultureIgnoreCase).Count() !=
                    names.Count)
                {
                    return false;
                }

                var occupiedNames = new HashSet<string>(
                    (_documentModel?.Groups ?? new List<OutlinerGroupModel>())
                        .Where(group => !targetIds.Contains(group.Id))
                        .Select(group => group.Name),
                    StringComparer.CurrentCultureIgnoreCase);
                return names.All(name => !occupiedNames.Contains(name));
            }

            if (kind != OutlinerNodeKind.Layer ||
                names.Any(name => !ModelComponent.IsValidComponentName(name)))
            {
                return false;
            }

            var proposed = targets
                .Select((target, index) => new
                {
                    Layer = doc.Layers.FindId(target.Node.Id),
                    Name = names[index]
                })
                .ToList();
            if (proposed.Any(item => item.Layer == null))
                return false;

            if (proposed
                .GroupBy(item => item.Layer.ParentLayerId)
                .Any(group => group
                    .Select(item => item.Name)
                    .Distinct(StringComparer.CurrentCultureIgnoreCase)
                    .Count() != group.Count()))
            {
                return false;
            }

            var activeLayers = GetSortedActiveLayers(doc)
                .Where(layer => !targetIds.Contains(layer.Id))
                .ToList();
            return proposed.All(item => !activeLayers.Any(layer =>
                layer.ParentLayerId == item.Layer.ParentLayerId &&
                string.Equals(
                    layer.Name,
                    item.Name,
                    StringComparison.CurrentCultureIgnoreCase)));
        }

        private bool ApplyBatchRename(
            RhinoDoc doc,
            IList<BatchRenameTarget> targets,
            IList<string> names)
        {
            var changes = targets
                .Select((target, index) => new BatchRenameChange
                {
                    Target = target,
                    Name = names[index]
                })
                .Where(item => !string.Equals(
                    item.Target.Name,
                    item.Name,
                    StringComparison.Ordinal))
                .ToList();
            if (changes.Count == 0)
                return false;

            return ModifyDocument(
                doc,
                OutlinerStrings.UndoBatchRename,
                () =>
                {
                    if (changes[0].Target.Node.Kind == OutlinerNodeKind.Object)
                    {
                        var changed = false;
                        foreach (var change in changes)
                        {
                            var rhinoObject = doc.Objects.FindId(
                                change.Target.Node.Id);
                            if (rhinoObject == null || rhinoObject.IsReference)
                                continue;
                            var attributes = rhinoObject.Attributes.Duplicate();
                            attributes.Name = change.Name;
                            ObjectNamingService.ClearManagedName(attributes);
                            changed |= doc.Objects.ModifyAttributes(
                                rhinoObject.Id,
                                attributes,
                                true);
                        }
                        return changed;
                    }

                    if (changes[0].Target.Node.Kind == OutlinerNodeKind.Layer)
                        return RenameLayersInTwoPhases(doc, changes);
                    return RenameGroupsInTwoPhases(doc, changes);
                },
                false);
        }

        private bool RenameLayersInTwoPhases(
            RhinoDoc doc,
            IEnumerable<BatchRenameChange> changes)
        {
            var entries = changes.ToList();
            var changed = false;
            foreach (var entry in entries)
            {
                var layer = doc.Layers.FindId(entry.Target.Node.Id);
                if (layer == null)
                    return false;
                var temporary = CreateDetachedLayerSettings(layer);
                temporary.Name = "ObjectOutlinerTemp_" + Guid.NewGuid().ToString("N");
                if (!doc.Layers.Modify(temporary, layer.Id, true))
                    return false;
                changed = true;
            }
            foreach (var entry in entries)
            {
                var layer = doc.Layers.FindId(entry.Target.Node.Id);
                if (layer == null)
                    return false;
                var renamed = CreateDetachedLayerSettings(layer);
                renamed.Name = entry.Name;
                if (!doc.Layers.Modify(renamed, layer.Id, true))
                    return false;
            }
            return changed;
        }

        private bool RenameGroupsInTwoPhases(
            RhinoDoc doc,
            IEnumerable<BatchRenameChange> changes)
        {
            var entries = changes.ToList();
            var changed = false;
            foreach (var entry in entries)
            {
                var index = entry.Target.Node.NativeIndex;
                if (!doc.Groups.ChangeGroupName(
                        index,
                        "ObjectOutlinerTemp_" + Guid.NewGuid().ToString("N")))
                {
                    return false;
                }
                changed = true;
            }
            foreach (var entry in entries)
            {
                if (!doc.Groups.ChangeGroupName(
                        entry.Target.Node.NativeIndex,
                        entry.Name))
                {
                    return false;
                }
            }
            return changed;
        }
    }
}
