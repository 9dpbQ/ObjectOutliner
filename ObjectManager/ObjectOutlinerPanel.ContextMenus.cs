using System;
using System.Collections.Generic;
using System.Linq;
using Eto.Drawing;
using Eto.Forms;
using Rhino;
using Rhino.Commands;
using Rhino.DocObjects;
using Rhino.Geometry;
using ObjectManager.Outliner;

namespace ObjectManager
{
    public sealed partial class ObjectOutlinerPanel
    {
        private sealed class OutlinerContextTarget
        {
            public TreeGridItem ClickedItem { get; set; }

            public OutlinerNodeReference ClickedNode { get; set; }

            public IList<OutlinerNodeReference> Nodes { get; set; }
        }

        private sealed class OutlinerIsolationCommandRequest
        {
            public OutlinerIsolationCommandRequest(
                OutlinerIsolationMode mode,
                OutlinerIsolateRequest request)
            {
                Mode = mode;
                Request = request;
            }

            public OutlinerIsolationMode Mode { get; }

            public OutlinerIsolateRequest Request { get; }
        }

        private readonly OutlinerIsolateState _isolateState =
            new OutlinerIsolateState();
        private readonly HashSet<Guid> _pendingIsolateRefreshObjectIds =
            new HashSet<Guid>();
        private OutlinerIsolationCommandRequest _pendingIsolationRequest;
        private OutlinerIsolationCommandRequest _pendingRetargetRequest;
        private bool _isolationLifecycleEventsAttached;

        private void HandleTreeAlternateClick(PointF location)
        {
            _nameCell?.CancelActiveEdit();
            var cell = _tree.GetCellAt(location);
            var item = cell?.Item as TreeGridItem;
            var node = item?.Tag as OutlinerNodeReference;
            var isolationMode = cell?.Column == _visibilityColumn
                ? OutlinerIsolationMode.Visibility
                : cell?.Column == _lockColumn
                    ? OutlinerIsolationMode.Lock
                    : OutlinerIsolationMode.None;
            var stateValueIndex = isolationMode == OutlinerIsolationMode.Visibility
                ? OutlinerValueIndex.Visibility
                : OutlinerValueIndex.Lock;
            if (cell != null &&
                item != null &&
                node != null &&
                isolationMode != OutlinerIsolationMode.None &&
                item.Values != null &&
                item.Values.Length > stateValueIndex &&
                item.Values[stateValueIndex] != null &&
                CanIsolateNodeKind(node.Kind))
            {
                var target = ResolveContextTarget(item, node);
                if (_isolateState.IsExplicitTarget(node, isolationMode))
                {
                    RequestEndIsolation(null);
                    return;
                }

                var request = CreateIsolateRequest(target);
                if (request != null && request.ObjectIds.Count > 0)
                    StartIsolation(request, isolationMode);
                return;
            }

            if (item == null || node == null)
            {
                ShowBlankContextMenu(location);
                return;
            }

            ShowNodeContextMenu(ResolveContextTarget(item, node), location);
        }

        private static bool CanIsolateNodeKind(OutlinerNodeKind kind)
        {
            return kind == OutlinerNodeKind.Layer ||
                kind == OutlinerNodeKind.GroupSlice ||
                kind == OutlinerNodeKind.Object;
        }

        private OutlinerContextTarget ResolveContextTarget(
            TreeGridItem clickedItem,
            OutlinerNodeReference clickedNode)
        {
            var clickedIsSelected = _tree.SelectedItems
                .OfType<TreeGridItem>()
                .Any(item => ReferenceEquals(item, clickedItem));
            var nodes = clickedIsSelected
                ? _tree.SelectedItems
                    .OfType<TreeGridItem>()
                    .Select(item => item.Tag as OutlinerNodeReference)
                    .Where(node => node != null && node.Kind == clickedNode.Kind)
                    .GroupBy(node => node.Id)
                    .Select(group => group.First())
                    .ToList()
                : new List<OutlinerNodeReference> { clickedNode };

            if (clickedNode.IsReadOnlyRelationship)
            {
                nodes.Clear();
                nodes.Add(clickedNode);
            }

            return new OutlinerContextTarget
            {
                ClickedItem = clickedItem,
                ClickedNode = clickedNode,
                Nodes = nodes
            };
        }

        private void ShowNodeContextMenu(
            OutlinerContextTarget target,
            PointF location)
        {
            var menu = new ContextMenu();
            var doc = RhinoDoc.FromRuntimeSerialNumber(_documentSerialNumber);
            var editable = doc != null && !doc.IsCommandRunning;
            switch (target.ClickedNode.Kind)
            {
                case OutlinerNodeKind.Layer:
                    BuildLayerContextMenu(menu, target, doc, editable);
                    break;
                case OutlinerNodeKind.GroupSlice:
                    BuildGroupContextMenu(menu, target, doc, editable);
                    break;
                case OutlinerNodeKind.Object:
                    BuildObjectContextMenu(menu, target, doc, editable);
                    break;
                case OutlinerNodeKind.GroupMemberReference:
                case OutlinerNodeKind.HistoryReference:
                    AddMenuItem(
                        menu,
                        OutlinerStrings.ZoomOriginal,
                        editable && doc.Objects.FindId(target.ClickedNode.Id) != null,
                        () => ZoomToObjects(doc, new[] { target.ClickedNode.Id }));
                    break;
            }

            AddExitIsolateFallback(menu, editable, target.ClickedNode);
            if (menu.Items.Count > 0)
                menu.Show(_tree, location);
        }

        private void BuildLayerContextMenu(
            ContextMenu menu,
            OutlinerContextTarget target,
            RhinoDoc doc,
            bool editable)
        {
            PopulateStandardLayerContextMenu(menu, target, doc, editable);
        }

        private void BuildGroupContextMenu(
            ContextMenu menu,
            OutlinerContextTarget target,
            RhinoDoc doc,
            bool editable)
        {
            var objectIds = editable
                ? GetGroupObjectIds(target.Nodes.Select(node => node.Id))
                : new List<Guid>();
            AddMenuItem(
                menu,
                OutlinerStrings.ZoomMembers,
                editable && objectIds.Count > 0,
                () => ZoomToObjects(doc, objectIds));
            AddIsolationMenuItems(menu, target, editable);
            menu.Items.Add(new SeparatorMenuItem());
            AddMenuItem(
                menu,
                OutlinerStrings.Ungroup,
                editable && target.Nodes.Any(node =>
                    _documentModel != null &&
                    _documentModel.GroupsById.ContainsKey(node.Id)),
                () => UngroupContextGroups(
                    doc,
                    target.Nodes.Select(node => node.Id)));
            AddMenuItem(
                menu,
                OutlinerStrings.Rename,
                editable,
                () => BeginNameEdit(target.ClickedItem));
            AddMenuItem(
                menu,
                OutlinerStrings.BatchRename,
                editable && target.Nodes.Count > 1,
                () => ShowBatchRename(doc, target.Nodes));

            menu.Items.Add(new SeparatorMenuItem());
            var groupSlices = target.Nodes
                .Where(node => node.Kind == OutlinerNodeKind.GroupSlice)
                .Select(node => new OutlinerGroupSliceKey(
                    node.Id,
                    node.OwnerId))
                .Distinct()
                .ToList();
            var hasExpandableContents = groupSlices.Any(key =>
                _snapshot != null &&
                _snapshot.GroupSliceItems.TryGetValue(key, out var item) &&
                item.Children.Count > 0);
            AddMenuItem(
                menu,
                OutlinerStrings.ExpandChildren,
                editable && hasExpandableContents,
                () => SetGroupContentsExpanded(groupSlices, true));
            AddMenuItem(
                menu,
                OutlinerStrings.CollapseChildren,
                editable && hasExpandableContents,
                () => SetGroupContentsExpanded(groupSlices, false));
        }

        private void BuildObjectContextMenu(
            ContextMenu menu,
            OutlinerContextTarget target,
            RhinoDoc doc,
            bool editable)
        {
            var objectIds = target.Nodes.Select(node => node.Id).Distinct().ToList();
            var editableObjectNodes = target.Nodes
                .Where(node => node.IsCanonical &&
                    IsEditableObject(doc, node.Id))
                .ToList();
            AddMenuItem(
                menu,
                OutlinerStrings.Zoom,
                editable && objectIds.Any(id => doc.Objects.FindId(id) != null),
                () => ZoomToObjects(doc, objectIds));
            AddIsolationMenuItems(menu, target, editable);
            var historyRootIds = editableObjectNodes
                .Select(node => node.Id)
                .Distinct()
                .ToList();
            var historyInputIds = GetKnownHistoryInputObjectIds(
                doc,
                historyRootIds);
            if (historyInputIds.Count > 0)
            {
                menu.Items.Add(new SeparatorMenuItem());
                AddMenuItem(
                    menu,
                    OutlinerStrings.HideHistoryInputs,
                    editable && historyInputIds.Any(id =>
                    {
                        var item = doc.Objects.FindId(id);
                        return item != null && !item.IsHidden;
                    }),
                    () => SetHistoryInputVisibility(
                        doc,
                        historyRootIds,
                        false));
                AddMenuItem(
                    menu,
                    OutlinerStrings.ShowHistoryInputs,
                    editable && historyInputIds.Any(id =>
                    {
                        var item = doc.Objects.FindId(id);
                        return item != null && item.IsHidden;
                    }),
                    () => SetHistoryInputVisibility(
                        doc,
                        historyRootIds,
                        true));
            }
            if (editableObjectNodes.Count > 1)
            {
                menu.Items.Add(new SeparatorMenuItem());
                AddMenuItem(
                    menu,
                    OutlinerStrings.GroupSelectedObjects,
                    editable,
                    () => GroupSelectedObjectNodes(
                        doc,
                        editableObjectNodes));
            }
            AddMenuItem(
                menu,
                OutlinerStrings.MoveToNewSublayer,
                editable && editableObjectNodes.Count > 0,
                () => ShowMoveToNewSublayer(
                    doc,
                    target.ClickedNode,
                    editableObjectNodes));
            AddMenuItem(
                menu,
                OutlinerStrings.Rename,
                editable &&
                    !IsUnavailable(target.ClickedItem) &&
                    IsEditableObject(doc, target.ClickedNode.Id),
                () => BeginNameEdit(target.ClickedItem));
            AddMenuItem(
                menu,
                OutlinerStrings.BatchRename,
                editable && editableObjectNodes.Count > 1,
                () => ShowBatchRename(doc, editableObjectNodes));
        }

        private IList<Guid> GetKnownHistoryInputObjectIds(
            RhinoDoc doc,
            IEnumerable<Guid> resultIds)
        {
            if (doc == null || _documentModel == null)
                return Array.Empty<Guid>();

            return OutlinerHistoryTraversal.GetKnownInputs(
                    _documentModel.HistoryByObjectId,
                    resultIds)
                .Where(id =>
                {
                    var item = doc.Objects.FindId(id);
                    return item != null &&
                        !item.IsDeleted &&
                        !item.IsReference;
                })
                .ToList();
        }

        private void SetHistoryInputVisibility(
            RhinoDoc doc,
            IEnumerable<Guid> resultIds,
            bool visible)
        {
            var targetIds = GetKnownHistoryInputObjectIds(doc, resultIds)
                .Where(id =>
                {
                    var item = doc.Objects.FindId(id);
                    return item != null && (!item.IsHidden) != visible;
                })
                .ToList();
            if (targetIds.Count == 0)
                return;

            var change = new OutlinerStateChange().AddObjects(targetIds);
            ModifyStateDocument(
                doc,
                visible
                    ? OutlinerStrings.UndoShowHistoryInputs
                    : OutlinerStrings.UndoHideHistoryInputs,
                change,
                () =>
                {
                    var changed = false;
                    foreach (var objectId in targetIds)
                    {
                        changed |= visible
                            ? doc.Objects.Show(objectId, true)
                            : doc.Objects.Hide(objectId, true);
                    }
                    return changed;
                });
        }

        private void AddIsolationMenuItems(
            ContextMenu menu,
            OutlinerContextTarget target,
            bool editable)
        {
            AddIsolationMenuItem(
                menu,
                target,
                editable,
                OutlinerIsolationMode.Visibility);
            AddIsolationMenuItem(
                menu,
                target,
                editable,
                OutlinerIsolationMode.Lock);
        }

        private void AddIsolationMenuItem(
            ContextMenu menu,
            OutlinerContextTarget target,
            bool editable,
            OutlinerIsolationMode mode)
        {
            var isLockMode = mode == OutlinerIsolationMode.Lock;
            if (_isolateState.IsExplicitTarget(target.ClickedNode, mode))
            {
                AddMenuItem(
                    menu,
                    isLockMode
                        ? OutlinerStrings.ExitIsolateLock
                        : OutlinerStrings.ExitIsolate,
                    editable,
                    () => RequestEndIsolation(null));
                return;
            }

            var request = editable ? CreateIsolateRequest(target) : null;
            AddMenuItem(
                menu,
                isLockMode
                    ? OutlinerStrings.IsolateLock
                    : OutlinerStrings.Isolate,
                request != null && request.ObjectIds.Count > 0,
                () => StartIsolation(request, mode));
        }

        private void AddExitIsolateFallback(
            ContextMenu menu,
            bool editable,
            OutlinerNodeReference clickedNode)
        {
            if (!_isolateState.IsActive ||
                _isolateState.IsExplicitTarget(
                    clickedNode,
                    _isolateState.Mode))
                return;

            if (menu.Items.Count > 0)
                menu.Items.Add(new SeparatorMenuItem());
            AddMenuItem(
                menu,
                _isolateState.Mode == OutlinerIsolationMode.Lock
                    ? OutlinerStrings.ExitIsolateLock
                    : OutlinerStrings.ExitIsolate,
                editable,
                () => RequestEndIsolation(null));
        }

        private void ShowBlankContextMenu(PointF location)
        {
            var menu = new ContextMenu();
            var doc = RhinoDoc.FromRuntimeSerialNumber(_documentSerialNumber);
            var editable = doc != null && !doc.IsCommandRunning;
            AddMenuItem(
                menu,
                OutlinerStrings.NewLayer,
                editable,
                () => CreateRootLayer(doc, false));
            menu.Items.Add(new SeparatorMenuItem());
            AddMenuItem(
                menu,
                OutlinerStrings.ExpandAll,
                editable,
                () => SetAllExpanded(true));
            AddMenuItem(
                menu,
                OutlinerStrings.CollapseAll,
                editable,
                () => SetAllExpanded(false));
            AddExitIsolateFallback(menu, editable, null);
            menu.Show(_tree, location);
        }

        private static void AddMenuItem(
            ContextMenu menu,
            string text,
            bool enabled,
            Action action)
        {
            var item = new ButtonMenuItem
            {
                Text = text,
                Enabled = enabled
            };
            if (enabled && action != null)
                item.Click += (sender, e) => action();
            menu.Items.Add(item);
        }

        private OutlinerIsolateRequest CreateIsolateRequest(
            OutlinerContextTarget target)
        {
            var doc = GetEditableDocument();
            if (doc == null || target == null || target.ClickedNode == null)
                return null;

            OutlinerIsolateTargetKind kind;
            List<Guid> objectIds;
            switch (target.ClickedNode.Kind)
            {
                case OutlinerNodeKind.Layer:
                    kind = OutlinerIsolateTargetKind.Layer;
                    objectIds = GetLayerTreeObjectIds(
                        doc,
                        target.Nodes.Select(node => node.Id));
                    break;
                case OutlinerNodeKind.GroupSlice:
                    kind = OutlinerIsolateTargetKind.Group;
                    objectIds = GetGroupObjectIds(
                        target.Nodes.Select(node => node.Id));
                    break;
                case OutlinerNodeKind.Object:
                    kind = OutlinerIsolateTargetKind.Object;
                    objectIds = target.Nodes.Select(node => node.Id).Distinct().ToList();
                    break;
                default:
                    return null;
            }

            return new OutlinerIsolateRequest(
                kind,
                target.Nodes.Select(node => node.Id),
                objectIds);
        }

        private void StartIsolation(
            OutlinerIsolateRequest request,
            OutlinerIsolationMode mode)
        {
            var doc = GetEditableDocument();
            if (doc == null ||
                request == null ||
                request.ObjectIds.Count == 0 ||
                mode == OutlinerIsolationMode.None)
                return;

            if (_isolateState.IsActive)
            {
                RequestEndIsolation(
                    new OutlinerIsolationCommandRequest(mode, request));
                return;
            }

            var selectableObjectIds = GetSelectableObjectIds(
                doc,
                request.ObjectIds);
            if (selectableObjectIds.Count == 0)
                return;
            var stagedRequest = new OutlinerIsolateRequest(
                request.Kind,
                request.NodeIds,
                selectableObjectIds);

            var previousSelection = _selectionController.Read(doc);
            _pendingIsolationRequest =
                new OutlinerIsolationCommandRequest(mode, stagedRequest);
            _workspaceStore?.StageIsolationRequest(
                doc.RuntimeSerialNumber,
                mode,
                stagedRequest);
            _syncingDocumentSelection = true;
            try
            {
                var staged = _selectionController.Apply(
                    doc,
                    stagedRequest.ObjectIds);
                if (!staged.FullyApplied)
                {
                    _pendingIsolationRequest = null;
                    QueueSelectionRefresh();
                    return;
                }
                doc.Views.Redraw();
            }
            finally
            {
                _syncingDocumentSelection = false;
            }

            if (RhinoApp.RunScript(
                doc.RuntimeSerialNumber,
                GetStartIsolationCommand(mode),
                false))
                return;

            _pendingIsolationRequest = null;
            _syncingDocumentSelection = true;
            try
            {
                _selectionController.Apply(doc, previousSelection);
                doc.Views.Redraw();
            }
            finally
            {
                _syncingDocumentSelection = false;
            }
            QueueSelectionRefresh();
        }

        private void RequestEndIsolation(
            OutlinerIsolationCommandRequest retargetRequest)
        {
            var doc = GetEditableDocument();
            if (doc == null || !_isolateState.IsActive)
                return;

            _pendingRetargetRequest = retargetRequest;
            if (!RhinoApp.RunScript(
                doc.RuntimeSerialNumber,
                GetEndIsolationCommand(_isolateState.Mode),
                false))
                _pendingRetargetRequest = null;
        }

        private void OnDocumentCommandStarted(object sender, CommandEventArgs e)
        {
            if (e == null ||
                e.DocumentRuntimeSerialNumber != _documentSerialNumber)
                return;

            UpdateToolbarButtonState();

            OutlinerIsolationMode mode;
            if (TryGetStartIsolationMode(e, out mode))
            {
                var doc = RhinoDoc.FromRuntimeSerialNumber(_documentSerialNumber);
                var pending = _pendingIsolationRequest;
                var request = pending != null && pending.Mode == mode
                    ? pending.Request
                    : _workspaceStore?.PeekIsolationRequest(
                        _documentSerialNumber,
                        mode);
                _pendingIsolationRequest = null;
                _isolateState.BeginNativeIsolation(doc, request, mode);
                return;
            }

            if (_pendingIsolationRequest != null)
                _pendingIsolationRequest = null;
        }

        private void OnDocumentCommandEnded(object sender, CommandEventArgs e)
        {
            if (e == null ||
                e.DocumentRuntimeSerialNumber != _documentSerialNumber)
                return;

            ClearCommandInputPointer();
            Application.Instance.AsyncInvoke(() =>
            {
                if (!_isClosing)
                    UpdateToolbarButtonState();
            });

            OutlinerIsolationMode mode;
            if (TryGetStartIsolationMode(e, out mode))
            {
                var doc = RhinoDoc.FromRuntimeSerialNumber(_documentSerialNumber);
                _isolateState.CompleteNativeIsolation(
                    doc,
                    e.CommandResult == Result.Success);
                _workspaceStore?.UpdateIsolation(
                    doc,
                    _isolateState.CreateSnapshot(),
                    _selectionController.Read(doc));
                _pendingIsolateRefreshObjectIds.UnionWith(
                    _isolateState.GetAffectedObjectIds());
                QueueIsolateStateRefresh();
                return;
            }

            if (!TryGetEndIsolationMode(e, out mode) ||
                !_isolateState.IsActive ||
                _isolateState.Mode != mode)
                return;

            if (e.CommandResult != Result.Success)
            {
                _pendingRetargetRequest = null;
                _pendingIsolateRefreshObjectIds.Clear();
                QueueTreeRefresh();
                return;
            }

            _pendingIsolateRefreshObjectIds.UnionWith(
                _isolateState.GetAffectedObjectIds());
            _isolateState.Clear();
            var endDoc = RhinoDoc.FromRuntimeSerialNumber(_documentSerialNumber);
            _workspaceStore?.UpdateIsolation(
                endDoc,
                null,
                _selectionController.Read(endDoc));
            var retarget = _pendingRetargetRequest;
            _pendingRetargetRequest = null;
            if (retarget != null)
            {
                Application.Instance.AsyncInvoke(() =>
                {
                    if (!_isClosing)
                        StartIsolation(retarget.Request, retarget.Mode);
                });
            }
            else
            {
                QueueIsolateStateRefresh();
            }
        }

        private void QueueIsolateStateRefresh()
        {
            var change = new OutlinerStateChange
            {
                IsolateChanged = true
            };
            change.AddObjects(_pendingIsolateRefreshObjectIds);
            _pendingIsolateRefreshObjectIds.Clear();
            QueueStateRefresh(change);
        }

        private static bool IsCommand(CommandEventArgs e, string englishName)
        {
            return e != null && string.Equals(
                e.CommandEnglishName,
                englishName,
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryGetStartIsolationMode(
            CommandEventArgs e,
            out OutlinerIsolationMode mode)
        {
            if (IsCommand(e, "Isolate"))
            {
                mode = OutlinerIsolationMode.Visibility;
                return true;
            }
            if (IsCommand(e, "IsolateLock"))
            {
                mode = OutlinerIsolationMode.Lock;
                return true;
            }

            mode = OutlinerIsolationMode.None;
            return false;
        }

        private static bool TryGetEndIsolationMode(
            CommandEventArgs e,
            out OutlinerIsolationMode mode)
        {
            if (IsCommand(e, "Unisolate"))
            {
                mode = OutlinerIsolationMode.Visibility;
                return true;
            }
            if (IsCommand(e, "UnisolateLock"))
            {
                mode = OutlinerIsolationMode.Lock;
                return true;
            }

            mode = OutlinerIsolationMode.None;
            return false;
        }

        private static string GetStartIsolationCommand(
            OutlinerIsolationMode mode)
        {
            return mode == OutlinerIsolationMode.Lock
                ? "_IsolateLock"
                : "_Isolate";
        }

        private static string GetEndIsolationCommand(
            OutlinerIsolationMode mode)
        {
            return mode == OutlinerIsolationMode.Lock
                ? "_UnisolateLock"
                : "_Unisolate";
        }

        private void ResetIsolateTracking()
        {
            _pendingIsolationRequest = null;
            _pendingRetargetRequest = null;
            _pendingIsolateRefreshObjectIds.Clear();
            _isolateState.Clear();
        }

        private void InitializeIsolationLifecycle()
        {
            if (_isolationLifecycleEventsAttached)
                return;

            _isolationLifecycleEventsAttached = true;
            Rhino.Commands.Command.BeginCommand += OnDocumentCommandStarted;
            Rhino.Commands.Command.EndCommand += OnDocumentCommandEnded;
            RhinoDoc.BeginSaveDocument += OnBeginSaveDocument;
            RhinoDoc.CloseDocument += OnIsolationDocumentClosed;
        }

        private void DisposeIsolationLifecycle()
        {
            if (!_isolationLifecycleEventsAttached)
                return;

            _isolationLifecycleEventsAttached = false;
            Rhino.Commands.Command.BeginCommand -= OnDocumentCommandStarted;
            Rhino.Commands.Command.EndCommand -= OnDocumentCommandEnded;
            RhinoDoc.BeginSaveDocument -= OnBeginSaveDocument;
            RhinoDoc.CloseDocument -= OnIsolationDocumentClosed;
        }

        private void OnBeginSaveDocument(
            object sender,
            DocumentSaveEventArgs e)
        {
            if (IsIsolationLifecycleDocument(e) && !e.ExportSelected)
                CaptureWorkspaceState();
        }

        private void OnIsolationDocumentClosed(
            object sender,
            DocumentEventArgs e)
        {
            if (!IsIsolationLifecycleDocument(e))
                return;

            ResetIsolateTracking();
        }

        private bool IsIsolationLifecycleDocument(DocumentEventArgs e)
        {
            if (e == null)
                return false;
            if (e.DocumentSerialNumber != 0)
                return e.DocumentSerialNumber == _documentSerialNumber;
            return e.Document?.RuntimeSerialNumber == _documentSerialNumber;
        }

        private List<Guid> GetLayerTreeObjectIds(
            RhinoDoc doc,
            IEnumerable<Guid> rootLayerIds)
        {
            if (doc == null || _documentModel == null)
                return new List<Guid>();

            var roots = new HashSet<Guid>(rootLayerIds ?? Enumerable.Empty<Guid>());
            var layerIds = new HashSet<Guid>(_documentModel.Layers
                .Where(layer => roots.Any(root =>
                    IsLayerDescendantOf(doc, layer.Id, root)))
                .Select(layer => layer.Id));
            return _documentModel.Objects
                .Where(item => layerIds.Contains(item.LayerId))
                .Select(item => item.Id)
                .Distinct()
                .ToList();
        }

        private List<Guid> GetGroupObjectIds(IEnumerable<Guid> groupIds)
        {
            return (groupIds ?? Enumerable.Empty<Guid>())
                .Distinct()
                .SelectMany(GetGroupMemberIds)
                .Distinct()
                .ToList();
        }

        private List<Guid> GetSelectableObjectIds(
            RhinoDoc doc,
            IEnumerable<Guid> objectIds)
        {
            return _selectionController
                .GetSelectableIds(doc, objectIds)
                .ToList();
        }

        private void SelectObjects(RhinoDoc doc, IEnumerable<Guid> objectIds)
        {
            if (doc == null || doc.IsCommandRunning)
                return;

            var selectableIds = GetSelectableObjectIds(doc, objectIds);
            ApplyDocumentSelection(doc, selectableIds);
            SynchronizeSelectionFromDocument();
        }

        private static void ZoomToObjects(RhinoDoc doc, IEnumerable<Guid> objectIds)
        {
            var view = doc?.Views.ActiveView;
            if (view == null)
                return;

            var box = BoundingBox.Unset;
            foreach (var id in (objectIds ?? Enumerable.Empty<Guid>()).Distinct())
            {
                var rhinoObject = doc.Objects.FindId(id);
                if (rhinoObject == null || rhinoObject.IsDeleted)
                    continue;

                var objectBox = rhinoObject.Geometry == null
                    ? BoundingBox.Unset
                    : rhinoObject.Geometry.GetBoundingBox(true);
                if (!objectBox.IsValid)
                    continue;
                if (box.IsValid)
                    box.Union(objectBox);
                else
                    box = objectBox;
            }

            if (box.IsValid && view.ActiveViewport.ZoomBoundingBox(box))
                view.Redraw();
        }

        private static bool IsEditableLayer(RhinoDoc doc, Guid layerId)
        {
            var layer = doc?.Layers.FindId(layerId);
            return layer != null && !layer.IsDeleted && !layer.IsReference;
        }

        private static bool IsEditableObject(RhinoDoc doc, Guid objectId)
        {
            var rhinoObject = doc?.Objects.FindId(objectId);
            return rhinoObject != null &&
                !rhinoObject.IsDeleted &&
                !rhinoObject.IsReference;
        }

        private void CreateContextSublayer(RhinoDoc doc, Guid parentLayerId)
        {
            CreateSublayer(doc, parentLayerId, false);
        }

        private void OneLayerOn(RhinoDoc doc, Guid layerId)
        {
            var target = doc?.Layers.FindId(layerId);
            if (target == null || target.IsDeleted || target.IsReference)
                return;

            var visibleLayerIds = new HashSet<Guid>();
            var currentId = target.Id;
            while (currentId != Guid.Empty && visibleLayerIds.Add(currentId))
            {
                var layer = doc.Layers.FindId(currentId);
                if (layer == null)
                    break;
                currentId = layer.ParentLayerId;
            }

            ModifyDocument(doc, OutlinerStrings.UndoOneLayerOn, () =>
            {
                var changed = doc.Layers.SetCurrentLayerIndex(target.Index, true);
                foreach (var layer in GetSortedActiveLayers(doc))
                {
                    if (layer.IsReference)
                        continue;

                    var desiredVisibility = visibleLayerIds.Contains(layer.Id);
                    if (layer.IsVisible == desiredVisibility &&
                        layer.PersistentVisibility == desiredVisibility)
                        continue;

                    layer.IsVisible = desiredVisibility;
                    layer.SetPersistentVisibility(desiredVisibility);
                    changed |= doc.Layers.Modify(layer, layer.Id, true);
                }
                return changed;
            });
        }

        private void SetAllExpanded(bool expanded)
        {
            if (_documentModel == null || _snapshot == null)
                return;

            foreach (var layer in _documentModel.Layers)
                _layerExpansionState[layer.Id] = expanded;
            foreach (var key in _snapshot.GroupSliceItems.Keys)
                SetGroupExpansionState(key, expanded);
            foreach (var objectId in GetKnownExpandableObjectIds())
                _historyExpansionState[objectId] = expanded;

            CaptureWorkspaceState();
            RefreshProjection();
        }

        private void SetGroupContentsExpanded(
            IEnumerable<OutlinerGroupSliceKey> groupSlices,
            bool expanded)
        {
            if (_documentModel == null || _snapshot == null)
                return;

            var plan = OutlinerGroupExpansionPlanner.Create(
                _documentModel,
                groupSlices,
                _snapshot.GroupSliceItems.Keys,
                GetKnownExpandableObjectIds());
            if (plan.TargetSlices.Count == 0)
                return;

            foreach (var targetSlice in plan.TargetSlices)
                SetGroupExpansionState(targetSlice, true);
            foreach (var childSlice in plan.GroupSlices)
                SetGroupExpansionState(childSlice, expanded);
            foreach (var objectId in plan.HistoryObjectIds)
                _historyExpansionState[objectId] = expanded;

            CaptureWorkspaceState();
            RefreshProjection();
        }

        private IEnumerable<Guid> GetKnownExpandableObjectIds()
        {
            if (_documentModel == null)
                return Enumerable.Empty<Guid>();

            return _documentModel.HistoryByObjectId.Keys
                .Concat(_documentModel.Objects
                    .Where(item => item.BlockDefinitionId != Guid.Empty)
                    .Select(item => item.Id))
                .Distinct()
                .ToList();
        }
    }
}
