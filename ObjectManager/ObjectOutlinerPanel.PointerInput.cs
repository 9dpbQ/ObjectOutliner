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
        private void OnTreeMouseDown(object sender, MouseEventArgs e)
        {
            EndStateGesture();
            CancelReferencePointer();
            ClearPotentialExpansionGesture();
            SetLinkCursor(false);
            _dragSourceNode = null;
            ClearCommandInputPointer();
            _blockedPointerNode = null;
            _blockedPointerColumn = null;
            if (e.Buttons == MouseButtons.Middle)
            {
                e.Handled = _treePanAdapter.HandlesPointerInput ||
                    BeginTreePan(e.Location);
                return;
            }
            if (e.Buttons == MouseButtons.Alternate)
            {
                HandleTreeAlternateClick(e.Location);
                e.Handled = true;
                return;
            }

            if (e.Buttons != MouseButtons.Primary)
                return;

            _selectionEventGate.BeginUserInput();

            var cell = _tree.GetCellAt(e.Location);
            var item = cell?.Item as TreeGridItem;
            var node = item?.Tag as OutlinerNodeReference;
            if (cell == null ||
                node == null)
                return;

            if (cell.Column == _noteColumn || cell.Column == _zoomColumn)
            {
                _blockedPointerNode = node;
                _blockedPointerColumn = cell.Column;
                _dragStartPoint = e.Location;
                e.Handled = true;
                return;
            }

            if (cell.Column == _nameColumn &&
                node.IsObjectReference &&
                BeginReferencePointer(node, e.Location))
            {
                e.Handled = true;
                return;
            }

            if (node.Kind == OutlinerNodeKind.BlockDefinitionComponent)
            {
                e.Handled = true;
                return;
            }

            var doc = RhinoDoc.FromRuntimeSerialNumber(_documentSerialNumber);
            if (TryBeginStateGesture(doc, cell.Column, node, e.Location))
            {
                e.Handled = true;
                return;
            }

            if (node.Kind == OutlinerNodeKind.Layer &&
                cell.Column == _currentColumn)
            {
                _blockedPointerNode = node;
                _blockedPointerColumn = cell.Column;
                _dragStartPoint = e.Location;
                e.Handled = true;
                return;
            }

            if (node.Kind == OutlinerNodeKind.Object && IsUnavailable(item))
            {
                _blockedPointerNode = node;
                _blockedPointerColumn = cell.Column;
                _dragStartPoint = e.Location;
                e.Handled = true;
                return;
            }

            var commandInputObjectIds =
                cell.Column == _nameColumn && doc != null
                    ? GetCommandInputObjectIds(node)
                    : Array.Empty<Guid>();
            if (commandInputObjectIds.Count > 0 &&
                ((node.Kind == OutlinerNodeKind.Object && node.IsCanonical) ||
                 node.Kind == OutlinerNodeKind.GroupSlice))
            {
                var probe = _commandInputBridge.Inspect(doc);
                if (probe.ShouldTrace)
                {
                    var objectName = item.Values != null &&
                        item.Values.Length > OutlinerValueIndex.Name
                        ? Convert.ToString(
                            item.Values[OutlinerValueIndex.Name])
                        : string.Empty;
                    var traceId = _commandInputBridge.TracePointer(
                        doc,
                        node.Id,
                        objectName,
                        probe,
                        node.Kind == OutlinerNodeKind.GroupSlice
                            ? "group"
                            : "object",
                        commandInputObjectIds.Count);
                    if (probe.HasActiveGetter)
                    {
                        _commandInputPointerNode = node;
                        _commandInputPointerTraceId = traceId;
                        _dragStartPoint = e.Location;
                        _tree.CaptureMouse();
                        e.Handled = true;
                        return;
                    }

                    UpdateStatusText();
                    _status.Text += "  |  command input: no active GetObject";
                }
            }

            if (cell.Column == _nameColumn && item.Children.Count > 0)
                _potentialExpansionItem = item;

            if (cell.Column != _nameColumn)
                return;

            if (node.Kind != OutlinerNodeKind.Layer &&
                node.Kind != OutlinerNodeKind.Object &&
                node.Kind != OutlinerNodeKind.GroupSlice)
                return;

            _dragSourceNode = node;
            _dragStartPoint = e.Location;
        }

        private void OnTreeMouseMove(object sender, MouseEventArgs e)
        {
            UpdateNoteToolTip(e.Location);
            if (_treePanActive)
            {
                if (!_treePanAdapter.HandlesPointerInput)
                    ContinueTreePan(e.Location);
                e.Handled = true;
                return;
            }

            if (_stateGesture != null)
            {
                if (e.Buttons == MouseButtons.Primary)
                    ContinueStateGesture(e.Location);
                else
                    EndStateGesture();
                e.Handled = true;
                return;
            }

            if (_referencePointerNode != null)
            {
                if (e.Buttons == MouseButtons.Primary)
                    ContinueReferencePointer(e.Location);
                else
                    CancelReferencePointer();
                e.Handled = true;
                return;
            }

            if (e.Buttons == MouseButtons.None)
            {
                UpdateLinkCursor(e.Location);
                return;
            }

            SetLinkCursor(false);
            if (_commandInputPointerNode != null &&
                e.Buttons == MouseButtons.Primary)
            {
                if (!IsWithinDragThreshold(e.Location))
                    ClearCommandInputPointer();
                e.Handled = true;
                return;
            }

            if (_blockedPointerNode != null && e.Buttons == MouseButtons.Primary)
            {
                if (IsWithinDragThreshold(e.Location))
                    return;

                _blockedPointerNode = null;
                _blockedPointerColumn = null;
                e.Handled = true;
                return;
            }

            if (_dragSourceNode == null || e.Buttons != MouseButtons.Primary)
                return;

            if (IsWithinDragThreshold(e.Location))
                return;

            var doc = GetEditableDocument();
            if (doc == null)
            {
                _dragSourceNode = null;
                return;
            }

            var sourceNode = _dragSourceNode;
            ClearPotentialExpansionGesture();
            if (!IsDraggableNode(doc, sourceNode))
            {
                _dragSourceNode = null;
                return;
            }

            List<Guid> selectedIds;
            List<OutlinerNodeReference> selectedGroupNodes = null;
            if (sourceNode.Kind == OutlinerNodeKind.GroupSlice)
            {
                selectedGroupNodes = _tree.SelectedItems
                    .OfType<TreeGridItem>()
                    .Select(item => item.Tag as OutlinerNodeReference)
                    .Where(node =>
                        node != null &&
                        node.Kind == OutlinerNodeKind.GroupSlice)
                    .GroupBy(node =>
                        new OutlinerGroupSliceKey(node.Id, node.OwnerId))
                    .Select(group => group.First())
                    .ToList();
                var sourceIsSelected = selectedGroupNodes.Any(node =>
                    node.Id == sourceNode.Id &&
                    node.OwnerId == sourceNode.OwnerId);
                if (!sourceIsSelected)
                    selectedGroupNodes = new List<OutlinerNodeReference> { sourceNode };
                if (selectedGroupNodes.Any(node => !IsDraggableNode(doc, node)))
                {
                    _dragSourceNode = null;
                    return;
                }
                selectedIds = selectedGroupNodes
                    .Select(node => node.Id)
                    .Distinct()
                    .ToList();
            }
            else
            {
                var selectedNodes = _tree.SelectedItems
                    .OfType<TreeGridItem>()
                    .Select(item => item.Tag as OutlinerNodeReference)
                    .Where(node => node != null && node.Kind == sourceNode.Kind)
                    .ToList();
                if (selectedNodes.Any(node => !IsDraggableNode(doc, node)))
                {
                    _dragSourceNode = null;
                    return;
                }

                selectedIds = selectedNodes
                    .Select(node => node.Id)
                    .Distinct()
                    .ToList();
                if (!selectedIds.Contains(sourceNode.Id))
                {
                    selectedIds.Clear();
                    selectedIds.Add(sourceNode.Id);
                }
            }

            if (sourceNode.Kind == OutlinerNodeKind.Layer)
                selectedIds = NormalizeDraggedLayerIds(doc, selectedIds);

            _dragSourceNode = null;
            _draggedNodeKind = sourceNode.Kind;
            _draggedNodeIds = selectedIds;
            _draggedGroupNodes = sourceNode.Kind == OutlinerNodeKind.GroupSlice
                ? selectedGroupNodes
                : null;
            var dataType = sourceNode.Kind == OutlinerNodeKind.Object
                ? ObjectDragDataType
                : sourceNode.Kind == OutlinerNodeKind.Layer
                    ? LayerDragDataType
                    : GroupDragDataType;
            var data = new DataObject
            {
                Text = sourceNode.Kind == OutlinerNodeKind.GroupSlice
                    ? string.Join(",", selectedGroupNodes.Select(node =>
                        node.Id.ToString("D") + ":" +
                        node.OwnerId.ToString("D")))
                    : string.Join(",", selectedIds)
            };
            data.SetString(data.Text, dataType);

            try
            {
                _tree.DoDragDrop(data, DragEffects.Move);
            }
            catch (Exception exception)
            {
                ClearDragState();
                RhinoApp.WriteLine("Object Outliner could not start drag:");
                RhinoApp.WriteLine(exception.ToString());
            }

            e.Handled = true;
        }

        private void OnTreeMouseLeave(object sender, MouseEventArgs e)
        {
            if (!_treePanActive)
                SetLinkCursor(false);
        }

        private void OnNativeTreePanStarted()
        {
            if (_isClosing || _treePanActive)
                return;

            _selectionEventGate.BeginUserInput();
            _linkCursorActive = false;
            _tree.Cursor = Cursors.Default;
            _treePanActive = true;
            SetTreePanCursor(true);
        }

        private void OnNativeTreePanEnded()
        {
            if (!_treePanActive)
                return;

            _treePanActive = false;
            SetTreePanCursor(false);
        }

        private bool BeginTreePan(PointF location)
        {
            if (_treePanActive || _nameCell?.HasActiveEdit == true)
                return false;

            if (!_treePanAdapter.TryBegin())
                return false;

            _selectionEventGate.BeginUserInput();
            SetLinkCursor(false);
            _treePanActive = true;
            _treePanStartScreenPoint = _tree.PointToScreen(location);
            SetTreePanCursor(true);
            _tree.CaptureMouse();
            return true;
        }

        private void ContinueTreePan(PointF location)
        {
            if (!_treePanActive)
                return;

            var screenPoint = _tree.PointToScreen(location);
            _treePanAdapter.Pan(
                screenPoint.X - _treePanStartScreenPoint.X,
                screenPoint.Y - _treePanStartScreenPoint.Y);
        }

        private void EndTreePan()
        {
            _treePanActive = false;
            _treePanAdapter.End();
            if (_tree.IsMouseCaptured)
                _tree.ReleaseMouseCapture();
            SetTreePanCursor(false);
        }

        private void SetTreePanCursor(bool active)
        {
            if (_treePanAdapter.HandlesPointerInput)
            {
                if (!active)
                    _tree.Cursor = Cursors.Default;
                return;
            }

            _tree.Cursor = active ? Cursors.Move : Cursors.Default;
        }

        private void UpdateLinkCursor(PointF location)
        {
            var cell = _tree.GetCellAt(location);
            var item = cell?.Item as TreeGridItem;
            var node = item?.Tag as OutlinerNodeReference;
            SetLinkCursor(
                cell != null &&
                cell.Column == _nameColumn &&
                node != null &&
                node.IsObjectReference &&
                !IsUnavailable(item));
        }

        private void SetLinkCursor(bool active)
        {
            if (_linkCursorActive == active)
                return;

            _linkCursorActive = active;
            _tree.Cursor = active ? Cursors.Pointer : Cursors.Default;
        }

        private void OnTreeMouseUp(object sender, MouseEventArgs e)
        {
            if (_treePanActive)
            {
                if (_treePanAdapter.HandlesPointerInput)
                {
                    e.Handled = true;
                    return;
                }

                EndTreePan();
                e.Handled = true;
                return;
            }

            if (_stateGesture != null)
            {
                EndStateGesture();
                e.Handled = true;
                return;
            }

            if (_referencePointerNode != null)
            {
                CompleteReferencePointer(
                    e.Location,
                    e.Modifiers.HasFlag(Keys.Application));
                e.Handled = true;
                return;
            }

            var commandInputNode = _commandInputPointerNode;
            var commandInputTraceId = _commandInputPointerTraceId;
            if (commandInputNode != null)
            {
                ClearCommandInputPointer();
                e.Handled = true;
                if (!IsWithinDragThreshold(e.Location))
                    return;

                var cell = _tree.GetCellAt(e.Location);
                var releasedNode = (cell?.Item as TreeGridItem)?.Tag as
                    OutlinerNodeReference;
                if (cell == null ||
                    cell.Column != _nameColumn ||
                    releasedNode == null ||
                    releasedNode.Kind != commandInputNode.Kind ||
                    releasedNode.Id != commandInputNode.Id ||
                    releasedNode.OwnerId != commandInputNode.OwnerId)
                {
                    return;
                }

                AppendCommandInput(commandInputNode, commandInputTraceId);
                return;
            }

            var applyDeferredSelection =
                _potentialExpansionItem != null &&
                !_potentialExpansionOccurred &&
                _deferredPointerSelectionChange;
            ClearPotentialExpansionGesture();
            var blockedNode = _blockedPointerNode;
            var blockedColumn = _blockedPointerColumn;
            _dragSourceNode = null;
            _blockedPointerNode = null;
            _blockedPointerColumn = null;
            try
            {
                if (blockedNode == null)
                    return;

                e.Handled = true;
                if (!IsWithinDragThreshold(e.Location))
                    return;

                var cell = _tree.GetCellAt(e.Location);
                var item = cell?.Item as TreeGridItem;
                var releasedNode = item?.Tag as OutlinerNodeReference;
                if (cell == null ||
                    cell.Column != blockedColumn ||
                    releasedNode == null ||
                    releasedNode.Kind != blockedNode.Kind ||
                    releasedNode.Id != blockedNode.Id)
                    return;

                var doc = RhinoDoc.FromRuntimeSerialNumber(_documentSerialNumber);
                if (doc == null)
                    return;
                if (doc.IsCommandRunning &&
                    (blockedColumn == _favoriteColumn || blockedColumn == _noteColumn))
                    return;

                if (blockedColumn == _visibilityColumn)
                    ToggleVisibility(doc, blockedNode);
                else if (blockedColumn == _lockColumn)
                    ToggleLock(doc, blockedNode);
                else if (blockedColumn == _favoriteColumn)
                    ToggleFavorite(doc, blockedNode);
                else if (blockedColumn == _noteColumn)
                    OpenNote(blockedNode, e.Location);
                else if (blockedColumn == _zoomColumn && !doc.IsCommandRunning)
                {
                    var ids = ResolveContextTarget(item, blockedNode).Nodes.Select(node => node.Id);
                    if (blockedNode.Kind == OutlinerNodeKind.Layer)
                        ZoomToObjects(doc, GetLayerTreeObjectIds(doc, ids.ToList()));
                    else if (blockedNode.Kind == OutlinerNodeKind.GroupSlice)
                        ZoomToObjects(doc, GetGroupObjectIds(ids));
                    else if (blockedNode.Kind == OutlinerNodeKind.Object || blockedNode.IsObjectReference)
                        ZoomToObjects(doc, ids);
                }
                else if (blockedColumn == _currentColumn)
                    SetCurrentLayer(doc, blockedNode);
            }
            finally
            {
                if (applyDeferredSelection)
                    ApplyTreeSelectionToDocument();
            }
        }

        private void ClearPotentialExpansionGesture()
        {
            _potentialExpansionItem = null;
            _potentialExpansionOccurred = false;
            _deferredPointerSelectionChange = false;
        }

        private void ClearCommandInputPointer()
        {
            if (_commandInputPointerNode == null)
                return;

            _commandInputPointerNode = null;
            _commandInputPointerTraceId = 0;
            if (_tree.IsMouseCaptured)
                _tree.ReleaseMouseCapture();
        }

        private IList<Guid> GetCommandInputObjectIds(
            OutlinerNodeReference node)
        {
            if (node == null)
                return Array.Empty<Guid>();
            if (node.Kind == OutlinerNodeKind.Object && node.IsCanonical)
                return new[] { node.Id };
            if (node.Kind != OutlinerNodeKind.GroupSlice ||
                _documentModel == null ||
                !_documentModel.GroupsById.TryGetValue(
                    node.Id,
                    out var group))
            {
                return Array.Empty<Guid>();
            }

            return group.MemberIds
                .Where(id => id != Guid.Empty)
                .Distinct()
                .ToList();
        }

        private void AppendCommandInput(
            OutlinerNodeReference node,
            int traceId)
        {
            var doc = RhinoDoc.FromRuntimeSerialNumber(_documentSerialNumber);
            var objectIds = GetCommandInputObjectIds(node);
            OutlinerCommandInputResult result;
            if (node != null &&
                node.Kind == OutlinerNodeKind.GroupSlice &&
                _documentModel != null &&
                _documentModel.GroupsById.TryGetValue(node.Id, out var group))
            {
                result = _commandInputBridge.TrySubmitGroup(
                    doc,
                    group.Id,
                    group.Index,
                    objectIds,
                    traceId);
            }
            else
            {
                var objectId = objectIds.FirstOrDefault();
                result = _commandInputBridge.TrySubmit(
                    doc,
                    objectId,
                    traceId);
            }

            if (result.Status != OutlinerCommandInputStatus.Dispatched)
            {
                UpdateStatusText();
                _status.Text += "  |  command input: " + result.Reason;
            }

            Application.Instance.AsyncInvoke(() =>
            {
                if (_isClosing)
                    return;

                QueueSelectionRefresh();
                var currentDocument = RhinoDoc.FromRuntimeSerialNumber(
                    _documentSerialNumber);
                if (currentDocument != null)
                    RhinoApp.SetFocusToMainWindow(currentDocument);
            });
        }

        private static bool IsSameTreeNode(TreeGridItem first, TreeGridItem second)
        {
            if (ReferenceEquals(first, second))
                return true;

            var firstNode = first?.Tag as OutlinerNodeReference;
            var secondNode = second?.Tag as OutlinerNodeReference;
            return firstNode != null &&
                secondNode != null &&
                firstNode.Kind == secondNode.Kind &&
                firstNode.Id == secondNode.Id &&
                firstNode.OwnerId == secondNode.OwnerId;
        }

        private bool IsWithinDragThreshold(PointF location)
        {
            return Math.Abs(location.X - _dragStartPoint.X) < DragThreshold &&
                Math.Abs(location.Y - _dragStartPoint.Y) < DragThreshold;
        }
    }
}
