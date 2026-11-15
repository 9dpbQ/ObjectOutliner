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
        private void OnTreeCellClick(object sender, GridCellMouseEventArgs e)
        {
            if (e.Buttons != MouseButtons.Primary)
                return;

            var hitCell = _tree.GetCellAt(e.Location);
            var clickedColumn = hitCell?.Column ?? e.GridColumn;
            var item = hitCell?.Item as TreeGridItem ?? e.Item as TreeGridItem;
            var node = item?.Tag as OutlinerNodeReference;
            if (node == null)
                return;

            _primaryNameEditTarget = NameEditTarget.FromNode(node);

            var doc = RhinoDoc.FromRuntimeSerialNumber(_documentSerialNumber);
            if (doc == null)
                return;

            if (IsStateGestureTarget(clickedColumn, node))
                return;

            var isStateAction =
                (clickedColumn == _currentColumn &&
                 node.Kind == OutlinerNodeKind.Layer);
            if (doc.IsCommandRunning && !isStateAction)
                return;

            if (node.IsReadOnlyRelationship)
                return;

            if (node.Kind == OutlinerNodeKind.Object && IsUnavailable(item))
                return;

            if (node.Kind == OutlinerNodeKind.Layer && clickedColumn == _colorColumn)
            {
                ChooseLayerColor(doc, node, false);
                return;
            }

            if (node.Kind == OutlinerNodeKind.Layer && clickedColumn == _materialColumn)
            {
                ChooseLayerMaterial(doc, node);
                return;
            }

            if (node.Kind == OutlinerNodeKind.Layer && clickedColumn == _linetypeColumn)
            {
                ChooseLayerLinetype(doc, node, e.Location);
                return;
            }

            if (node.Kind == OutlinerNodeKind.Layer && clickedColumn == _plotColorColumn)
            {
                ChooseLayerColor(doc, node, true);
                return;
            }

            if (node.Kind == OutlinerNodeKind.Layer && clickedColumn == _plotWeightColumn)
            {
                ChooseLayerPlotWeight(doc, node);
                return;
            }

            if (node.Kind == OutlinerNodeKind.Layer && clickedColumn == _sectionStyleColumn)
            {
                ShowSectionStyleMenu(doc, node, e.Location);
                return;
            }

            if (node.Kind == OutlinerNodeKind.Layer &&
                clickedColumn == _currentColumn)
            {
                SetCurrentLayer(doc, node);
            }
        }

        private void OnTreeCellDoubleClick(object sender, GridCellMouseEventArgs e)
        {
            if (e.Buttons != MouseButtons.Primary)
                return;

            if (_nameCell == null)
                return;

            var hitCell = _tree.GetCellAt(e.Location);
            var item = hitCell?.Item as TreeGridItem ?? e.Item as TreeGridItem;
            if (hitCell == null || hitCell.Column != _nameColumn || item == null)
                return;

            if (BeginNameEdit(item))
                e.Handled = true;
        }

        private void OnTreeKeyDown(object sender, KeyEventArgs e)
        {
            if ((e.Key == Keys.Enter || e.Key == Keys.Escape) &&
                _nameCell != null &&
                _nameCell.IsHandlingCompositionCommandKey)
            {
                return;
            }

            if (_nameCell == null || !_nameCell.HasActiveEdit)
            {
                _selectionEventGate.BeginUserInput();
                BeginKeyboardExpansionSuppression(e.Key);
            }

            if ((e.Key == Keys.Enter || e.Key == Keys.Escape) &&
                _nameCell != null &&
                (_nameCell.IsHandlingEditCommandKey || _nameCell.HasActiveEdit))
            {
                if (_nameCell.HasActiveEdit)
                {
                    if (e.Key == Keys.Enter)
                        _nameCell.CommitActiveEdit();
                    else
                        _nameCell.CancelActiveEdit();
                }
                e.Handled = true;
                return;
            }

            if (e.Key != Keys.Enter || (_nameCell != null && _nameCell.HasActiveEdit))
                return;

            var selectedItems = new HashSet<TreeGridItem>(
                _tree.SelectedItems.OfType<TreeGridItem>());
            var selectedItem = ResolveNameEditItem(_primaryNameEditTarget);
            if (selectedItem == null || !selectedItems.Contains(selectedItem))
                selectedItem = _tree.SelectedItem as TreeGridItem;
            if (selectedItem != null && BeginNameEdit(selectedItem))
                e.Handled = true;
        }

        private void OnTreeCellEdited(object sender, GridViewCellEventArgs e)
        {
            if (_nameCell != null || e.GridColumn != _nameColumn)
                return;

            var item = e.Item as TreeGridItem;
            CommitNameEdit(
                item,
                item == null ? null : Convert.ToString(item.Values[1]));
        }

        private void OnNameCellEditCommitted(
            object sender,
            OutlinerNameEditEventArgs e)
        {
            CommitNameEdit(_activeNameEditTarget, e.Item, e.Text);
            _activeNameEditTarget = null;
        }

        private void OnNameCellEditEnded(object sender, EventArgs e)
        {
            _activeNameEditTarget = null;
            QueueSelectionRefresh();
        }

        private void CommitNameEdit(TreeGridItem item, string requestedName)
        {
            CommitNameEdit(NameEditTarget.FromItem(item), item, requestedName);
        }

        private void CommitNameEdit(
            NameEditTarget target,
            TreeGridItem fallbackItem,
            string requestedName)
        {
            var item = ResolveNameEditItem(target) ?? fallbackItem;
            var node = item?.Tag as OutlinerNodeReference;
            var doc = RhinoDoc.FromRuntimeSerialNumber(_documentSerialNumber);
            if (node == null || doc == null || doc.IsCommandRunning)
                return;

            if (!node.IsCanonical && node.Kind != OutlinerNodeKind.GroupSlice)
            {
                QueueTreeRefresh();
                return;
            }

            var newName = requestedName?.Trim();
            if (node.Kind == OutlinerNodeKind.Layer)
                RenameLayer(doc, item, node, newName);
            else if (node.Kind == OutlinerNodeKind.Object)
                RenameObject(doc, item, node, newName);
            else if (node.Kind == OutlinerNodeKind.GroupSlice)
                RenameGroup(doc, item, node, newName);
        }

        private bool BeginNameEdit(TreeGridItem item)
        {
            var node = item?.Tag as OutlinerNodeReference;
            var doc = RhinoDoc.FromRuntimeSerialNumber(_documentSerialNumber);
            if (node == null ||
                doc == null ||
                doc.IsCommandRunning ||
                (node.Kind == OutlinerNodeKind.Object && IsUnavailable(item)) ||
                (!node.IsCanonical && node.Kind != OutlinerNodeKind.GroupSlice))
            {
                return false;
            }

            var target = NameEditTarget.FromNode(node);
            if (target == null)
                return false;

            var generation = ++_nameEditGeneration;
            Application.Instance.AsyncInvoke(() =>
            {
                if (_isClosing || generation != _nameEditGeneration)
                    return;

                var currentItem = ResolveNameEditItem(target);
                if (currentItem != null)
                    BeginResolvedNameEdit(currentItem, target);
            });
            return true;
        }

        private bool BeginResolvedNameEdit(
            TreeGridItem item,
            NameEditTarget target)
        {
            var node = item?.Tag as OutlinerNodeReference;
            var doc = RhinoDoc.FromRuntimeSerialNumber(_documentSerialNumber);
            if (node == null ||
                doc == null ||
                doc.IsCommandRunning ||
                (node.Kind == OutlinerNodeKind.Object && IsUnavailable(item)) ||
                (!node.IsCanonical && node.Kind != OutlinerNodeKind.GroupSlice))
            {
                return false;
            }

            string editName;
            if (node.Kind == OutlinerNodeKind.GroupSlice)
            {
                editName = _documentModel?.Groups.FirstOrDefault(
                    candidate => candidate.Id == node.Id)?.Name;
            }
            else
            {
                editName = Convert.ToString(item.Values[OutlinerValueIndex.Name]);
            }

            if (_nameCell != null)
            {
                var editing = _nameCell.TryBeginEdit(item, editName ?? string.Empty);
                if (editing)
                    _activeNameEditTarget = target;
                return editing;
            }

            if (_snapshot == null)
                return false;

            var visibleItems = new List<TreeGridItem>();
            AddVisibleItems(_snapshot.Roots, visibleItems);
            var row = visibleItems.IndexOf(item);
            if (row < 0)
                return false;

            _tree.BeginEdit(row, 0);
            return true;
        }

        private TreeGridItem ResolveNameEditItem(NameEditTarget target)
        {
            if (target == null || _snapshot == null)
                return null;

            if (target.Kind == OutlinerNodeKind.Layer)
            {
                _snapshot.LayerItems.TryGetValue(target.Id, out var layerItem);
                return layerItem;
            }

            if (target.Kind == OutlinerNodeKind.Object)
            {
                _snapshot.ObjectItems.TryGetValue(target.Id, out var objectItem);
                return objectItem;
            }

            if (target.Kind == OutlinerNodeKind.GroupSlice)
            {
                _snapshot.GroupSliceItems.TryGetValue(
                    new OutlinerGroupSliceKey(target.Id, target.OwnerId),
                    out var groupItem);
                return groupItem;
            }

            return null;
        }

        private void OnTreeCellEditing(object sender, GridViewCellEventArgs e)
        {
            if (_nameCell != null || e.GridColumn != _nameColumn)
                return;

            var item = e.Item as TreeGridItem;
            var node = item?.Tag as OutlinerNodeReference;
            if (node == null || node.Kind != OutlinerNodeKind.GroupSlice)
                return;

            var group = _documentModel?.Groups.FirstOrDefault(
                candidate => candidate.Id == node.Id);
            if (group != null)
                item.SetValue(1, group.Name);
        }
    }
}
