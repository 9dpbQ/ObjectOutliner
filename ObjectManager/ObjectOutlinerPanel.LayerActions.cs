using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using Eto.Drawing;
using Eto.Forms;
using Rhino;
using Rhino.DocObjects;
using Rhino.UI;
using ObjectManager.Outliner;

namespace ObjectManager
{
    public sealed partial class ObjectOutlinerPanel
    {
        private void BeginPendingLayerEdit()
        {
            if (_pendingEditLayerId == Guid.Empty || _snapshot == null)
                return;

            var pendingId = _pendingEditLayerId;
            _pendingEditLayerId = Guid.Empty;
            var visibleItems = new List<TreeGridItem>();
            AddVisibleItems(_snapshot.Roots, visibleItems);
            var row = visibleItems.FindIndex(candidateItem =>
            {
                var node = candidateItem.Tag as OutlinerNodeReference;
                return node != null &&
                       node.Kind == OutlinerNodeKind.Layer &&
                       node.Id == pendingId;
            });
            if (row < 0)
                return;

            var pendingItem = visibleItems[row];
            Application.Instance.AsyncInvoke(() =>
            {
                if (_nameCell != null)
                    BeginNameEdit(pendingItem);
                else
                    _tree.BeginEdit(row, 0);
            });
        }

        private void OnAddLayerClicked(object sender, EventArgs e)
        {
            CreateLayerAtCurrentHierarchy(false);
        }

        private void OnAddSublayerClicked(object sender, EventArgs e)
        {
            CreateSublayerFromSelection(false);
        }

        private void ClearSearchForLayerCreation()
        {
            if (!_viewState.HasSearch && string.IsNullOrEmpty(_searchBox.Text))
                return;

            _searchRefreshTimer.Stop();
            _viewState.SearchText = string.Empty;
            _searchBox.Text = string.Empty;
            _searchRefreshTimer.Stop();
        }

        private void PrepareNewLayer(RhinoDoc doc, int layerIndex, Guid parentId)
        {
            var layer = doc.Layers[layerIndex];
            if (layer == null)
                return;

            if (parentId != Guid.Empty &&
                !_viewState.HasSearch &&
                !_viewState.HasReveal)
                _layerExpansionState[parentId] = true;

            _selectedLayerIds.Clear();
            _selectedLayerIds.Add(layer.Id);
            _primaryLayerId = layer.Id;
            _pendingEditLayerId = layer.Id;
        }

        private void OnDeleteLayerClicked(object sender, EventArgs e)
        {
            DeleteSelectedItems();
        }

        private static int GetLayerDepth(RhinoDoc doc, Layer layer)
        {
            var depth = 0;
            var parentId = layer.ParentLayerId;
            var visited = new HashSet<Guid>();
            while (parentId != Guid.Empty && visited.Add(parentId))
            {
                var parent = doc.Layers.FindId(parentId);
                if (parent == null)
                    break;
                depth++;
                parentId = parent.ParentLayerId;
            }

            return depth;
        }

        private void SetCurrentLayer(
            RhinoDoc doc,
            OutlinerNodeReference node)
        {
            if (doc == null ||
                node == null ||
                node.Kind != OutlinerNodeKind.Layer)
            {
                return;
            }

            var layer = doc.Layers.FindId(node.Id);
            if (layer == null || layer.IsDeleted || layer.IsReference)
                return;

            _primaryLayerId = layer.Id;
            if (doc.Layers.CurrentLayerIndex == layer.Index)
                return;

            var changed = ApplyStateDocumentChange(
                new OutlinerStateChange
                {
                    CurrentLayerChanged = true
                },
                () => doc.Layers.SetCurrentLayerIndex(layer.Index, true));
            if (!changed && doc.Layers.CurrentLayerIndex != layer.Index)
            {
                RhinoApp.WriteLine(
                    OutlinerStrings.CurrentLayerChangeFailed);
            }
        }

        private void ToggleVisibility(RhinoDoc doc, OutlinerNodeReference node)
        {
            if (node.Kind == OutlinerNodeKind.Layer)
            {
                var layer = doc.Layers.FindId(node.Id);
                if (layer == null || layer.Index == doc.Layers.CurrentLayerIndex)
                    return;

                var inheritedHidden = HasMatchingAncestor(
                    doc,
                    layer,
                    parent => !parent.IsVisible);
                var desiredVisibility = inheritedHidden
                    ? !layer.PersistentVisibility
                    : !layer.IsVisible;
                SetLayerVisibility(doc, new[] { node.Id }, desiredVisibility);
                return;
            }

            if (node.Kind == OutlinerNodeKind.GroupSlice)
            {
                ToggleGroupVisibility(doc, node);
                return;
            }

            var rhinoObject = doc.Objects.FindId(node.Id);
            if (rhinoObject == null || rhinoObject.IsReference)
                return;

            ModifyStateDocument(
                doc,
                OutlinerStrings.UndoChangeObjectVisibility,
                new OutlinerStateChange().AddObjects(new[] { node.Id }),
                () => rhinoObject.IsHidden
                    ? doc.Objects.Show(node.Id, true)
                    : doc.Objects.Hide(node.Id, true));
        }

        private void ToggleLock(RhinoDoc doc, OutlinerNodeReference node)
        {
            if (node.Kind == OutlinerNodeKind.Layer)
            {
                var layer = doc.Layers.FindId(node.Id);
                if (layer == null || layer.Index == doc.Layers.CurrentLayerIndex)
                    return;

                var inheritedLocked = HasMatchingAncestor(
                    doc,
                    layer,
                    parent => parent.IsLocked);
                var desiredLocking = inheritedLocked
                    ? !layer.GetPersistentLocking()
                    : !layer.IsLocked;
                SetLayerLocking(doc, new[] { node.Id }, desiredLocking);
                return;
            }

            if (node.Kind == OutlinerNodeKind.GroupSlice)
            {
                ToggleGroupLock(doc, node);
                return;
            }

            var rhinoObject = doc.Objects.FindId(node.Id);
            if (rhinoObject == null || rhinoObject.IsReference)
                return;

            ModifyStateDocument(
                doc,
                OutlinerStrings.UndoChangeObjectLock,
                new OutlinerStateChange().AddObjects(new[] { node.Id }),
                () => rhinoObject.IsLocked
                    ? doc.Objects.Unlock(node.Id, true)
                    : doc.Objects.Lock(node.Id, true));
        }

        private void ToggleGroupVisibility(
            RhinoDoc doc,
            OutlinerNodeReference node)
        {
            var members = GetEditableGroupMembers(doc, node);
            if (members.Count == 0)
                return;

            var showMembers = members.Any(item => item.IsHidden);
            ModifyStateDocument(
                doc,
                OutlinerStrings.UndoChangeGroupVisibility,
                new OutlinerStateChange().AddObjects(
                    members.Select(item => item.Id)),
                () =>
            {
                var changed = false;
                foreach (var member in members)
                {
                    if (showMembers)
                    {
                        if (member.IsHidden)
                            changed |= doc.Objects.Show(member.Id, true);
                    }
                    else if (!member.IsHidden)
                    {
                        changed |= doc.Objects.Hide(member.Id, true);
                    }
                }
                return changed;
            });
        }

        private void ToggleGroupLock(
            RhinoDoc doc,
            OutlinerNodeReference node)
        {
            var members = GetEditableGroupMembers(doc, node);
            if (members.Count == 0)
                return;

            var lockMembers = members.Any(item => !item.IsLocked);
            ModifyStateDocument(
                doc,
                OutlinerStrings.UndoChangeGroupLock,
                new OutlinerStateChange().AddObjects(
                    members.Select(item => item.Id)),
                () =>
            {
                var changed = false;
                foreach (var member in members)
                {
                    if (lockMembers)
                    {
                        if (!member.IsLocked)
                            changed |= doc.Objects.Lock(member.Id, true);
                    }
                    else if (member.IsLocked)
                    {
                        changed |= doc.Objects.Unlock(member.Id, true);
                    }
                }
                return changed;
            });
        }

        private static IList<RhinoObject> GetEditableGroupMembers(
            RhinoDoc doc,
            OutlinerNodeReference node)
        {
            if (doc == null ||
                node == null ||
                node.Kind != OutlinerNodeKind.GroupSlice)
            {
                return new List<RhinoObject>();
            }

            var group = doc.Groups.FindIndex(node.NativeIndex);
            if (group == null || group.IsDeleted || group.Id != node.Id)
                return new List<RhinoObject>();

            return (doc.Groups.GroupMembers(node.NativeIndex) ??
                    Array.Empty<RhinoObject>())
                .Where(item => item != null &&
                    !item.IsDeleted &&
                    !item.IsReference)
                .GroupBy(item => item.Id)
                .Select(items => items.First())
                .ToList();
        }

        private void ChooseLayerColor(
            RhinoDoc doc,
            OutlinerNodeReference node,
            bool plotColor)
        {
            var layer = doc.Layers.FindId(node.Id);
            if (layer == null || layer.IsReference)
                return;

            ChooseLayerColorForTargets(
                doc,
                node.Id,
                new[] { node.Id },
                plotColor);
        }

        private void ChooseLayerMaterial(RhinoDoc doc, OutlinerNodeReference node)
        {
            var layerIds = _selectedLayerIds.ToList();
            if (!layerIds.Contains(node.Id))
                layerIds.Add(node.Id);
            ChooseLayerMaterialForTargets(doc, layerIds);
        }

        private void ChooseLayerLinetype(
            RhinoDoc doc,
            OutlinerNodeReference node,
            PointF location)
        {
            var layer = doc.Layers.FindId(node.Id);
            if (layer == null || layer.IsReference)
                return;

            var menu = new ContextMenu();
            for (var index = 0; index < doc.Linetypes.Count; index++)
            {
                var linetype = doc.Linetypes[index];
                if (linetype == null || linetype.IsDeleted)
                    continue;

                var selectedIndex = index;
                var item = new CheckMenuItem
                {
                    Text = OutlinerStrings.LocalizeLinetypeName(linetype.Name),
                    Checked = index == layer.LinetypeIndex
                };
                item.CheckedChanged += (sender, e) =>
                {
                    if (item.Checked)
                    {
                        ModifyLayers(
                            doc,
                            new[] { node.Id },
                            OutlinerStrings.UndoChangeLayerLinetype,
                            target => target.LinetypeIndex = selectedIndex);
                    }
                };
                menu.Items.Add(item);
            }

            menu.Show(_tree, location);
        }

        private static bool HasMatchingAncestor(
            RhinoDoc doc,
            Layer layer,
            Func<Layer, bool> predicate)
        {
            var parentId = layer.ParentLayerId;
            var visited = new HashSet<Guid>();
            while (parentId != Guid.Empty && visited.Add(parentId))
            {
                var parent = doc.Layers.FindId(parentId);
                if (parent == null)
                    return false;
                if (predicate(parent))
                    return true;
                parentId = parent.ParentLayerId;
            }

            return false;
        }

        private static bool SynchronizeDescendantVisibilityIntent(
            RhinoDoc doc,
            Guid parentLayerId)
        {
            var changed = false;
            foreach (var layer in GetSortedActiveLayers(doc))
            {
                if (layer.IsReference ||
                    !IsLayerDescendantOf(doc, layer.Id, parentLayerId) ||
                    layer.Id == parentLayerId)
                    continue;

                var inheritedHidden = HasMatchingAncestor(
                    doc,
                    layer,
                    parent => !parent.IsVisible);
                var ownVisibility = inheritedHidden
                    ? layer.PersistentVisibility
                    : layer.IsVisible;
                if (layer.PersistentVisibility == ownVisibility)
                    continue;

                layer.SetPersistentVisibility(ownVisibility);
                changed |= doc.Layers.Modify(layer, layer.Id, true);
            }
            return changed;
        }

        private void ChooseLayerPlotWeight(RhinoDoc doc, OutlinerNodeReference node)
        {
            var layer = doc.Layers.FindId(node.Id);
            if (layer == null || layer.IsReference)
                return;

            ChooseLayerPlotWeightForTargets(
                doc,
                node.Id,
                new[] { node.Id });
        }

        private void ShowSectionStyleMenu(
            RhinoDoc doc,
            OutlinerNodeReference node,
            PointF location)
        {
            var layer = doc.Layers.FindId(node.Id);
            if (layer == null || layer.IsReference)
                return;

            var menu = new ContextMenu();
            var useCustom = new ButtonMenuItem
            {
                Text = layer.GetCustomSectionStyle() != null
                    ? OutlinerStrings.ResetToDefaultSectionStyle
                    : OutlinerStrings.CreateCustomSectionStyle
            };
            useCustom.Click += (sender, e) =>
                ToggleLayerSectionStyleForTargets(
                    doc,
                    node.Id,
                    new[] { node.Id });
            menu.Items.Add(useCustom);
            menu.Show(_tree, location);
        }
    }
}
