using System;
using System.Collections.Generic;
using System.Linq;
using Eto.Drawing;
using Eto.Forms;
using Rhino;
using Rhino.DocObjects;
using ObjectManager.Outliner;

namespace ObjectManager
{
    public sealed partial class ObjectOutlinerPanel
    {
        private enum StateGestureKind
        {
            None,
            Favorite,
            Visibility,
            Lock,
            NewDetailVisibility,
            ModelVisibility
        }

        private struct StateGestureNodeKey : IEquatable<StateGestureNodeKey>
        {
            public StateGestureNodeKey(OutlinerNodeReference node)
            {
                Kind = node == null ? OutlinerNodeKind.Object : node.Kind;
                Id = node?.Id ?? Guid.Empty;
                OwnerId = node?.OwnerId ?? Guid.Empty;
            }

            public OutlinerNodeKind Kind { get; }

            public Guid Id { get; }

            public Guid OwnerId { get; }

            public bool Equals(StateGestureNodeKey other)
            {
                return Kind == other.Kind &&
                    Id == other.Id &&
                    OwnerId == other.OwnerId;
            }

            public override bool Equals(object obj)
            {
                return obj is StateGestureNodeKey &&
                    Equals((StateGestureNodeKey)obj);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    var hashCode = (int)Kind;
                    hashCode = (hashCode * 397) ^ Id.GetHashCode();
                    hashCode = (hashCode * 397) ^ OwnerId.GetHashCode();
                    return hashCode;
                }
            }
        }

        private sealed class StateGestureSession
        {
            public StateGestureSession(
                RhinoDoc document,
                StateGestureKind kind,
                GridColumn column,
                bool desiredState,
                PointF startPoint,
                uint undoRecord)
            {
                Document = document;
                Kind = kind;
                Column = column;
                DesiredState = desiredState;
                StartPoint = startPoint;
                LastPaintY = startPoint.Y;
                UndoRecord = undoRecord;
                VisitedNodes = new HashSet<StateGestureNodeKey>();
                ProcessedObjectIds = new HashSet<Guid>();
                ProcessedGroupIds = new HashSet<Guid>();
                Change = new OutlinerStateChange();
            }

            public RhinoDoc Document { get; }

            public StateGestureKind Kind { get; }

            public GridColumn Column { get; }

            public bool DesiredState { get; }

            public PointF StartPoint { get; }

            public float LastPaintY { get; set; }

            public uint UndoRecord { get; }

            public ISet<StateGestureNodeKey> VisitedNodes { get; }

            public ISet<Guid> ProcessedObjectIds { get; }

            public ISet<Guid> ProcessedGroupIds { get; }

            public OutlinerStateChange Change { get; }

            public bool Changed { get; set; }
        }

        private StateGestureSession _stateGesture;
        private OutlinerNodeReference _referencePointerNode;
        private PointF _referencePointerStartPoint;

        private bool BeginReferencePointer(
            OutlinerNodeReference node,
            PointF location)
        {
            if (node == null || !node.IsObjectReference)
                return false;

            _referencePointerNode = node;
            _referencePointerStartPoint = location;
            _tree.CaptureMouse();
            return true;
        }

        private void ContinueReferencePointer(PointF location)
        {
            if (_referencePointerNode == null)
                return;

            if (Math.Abs(location.X - _referencePointerStartPoint.X) <
                    DragThreshold &&
                Math.Abs(location.Y - _referencePointerStartPoint.Y) <
                    DragThreshold)
            {
                return;
            }

            CancelReferencePointer();
        }

        private void CompleteReferencePointer(
            PointF location,
            bool toggleSelection)
        {
            var pointerNode = _referencePointerNode;
            var startPoint = _referencePointerStartPoint;
            CancelReferencePointer();
            if (pointerNode == null ||
                Math.Abs(location.X - startPoint.X) >= DragThreshold ||
                Math.Abs(location.Y - startPoint.Y) >= DragThreshold)
            {
                return;
            }

            var cell = _tree.GetCellAt(location);
            var releasedNode = (cell?.Item as TreeGridItem)?.Tag as
                OutlinerNodeReference;
            if (cell == null ||
                cell.Column != _nameColumn ||
                releasedNode == null ||
                !releasedNode.IsObjectReference ||
                releasedNode.Kind != pointerNode.Kind ||
                releasedNode.Id != pointerNode.Id ||
                releasedNode.OwnerId != pointerNode.OwnerId)
            {
                return;
            }

            RevealObject(pointerNode.Id, toggleSelection);
        }

        private void CancelReferencePointer()
        {
            _referencePointerNode = null;
            if (_tree.IsMouseCaptured && _stateGesture == null)
                _tree.ReleaseMouseCapture();
        }

        private bool TryBeginStateGesture(
            RhinoDoc doc,
            GridColumn column,
            OutlinerNodeReference clickedNode,
            PointF location)
        {
            if (doc == null ||
                clickedNode == null ||
                !TryGetStateGestureKind(column, clickedNode, out var kind) ||
                (doc.IsCommandRunning &&
                 kind != StateGestureKind.Visibility &&
                 kind != StateGestureKind.Lock) ||
                !TryReadState(doc, clickedNode, kind, out var currentState))
            {
                return false;
            }

            EndStateGesture();
            var undoRecord = doc.BeginUndoRecord(GetStateGestureUndoText(kind));
            var session = new StateGestureSession(
                doc,
                kind,
                column,
                !currentState,
                location,
                undoRecord);
            _stateGesture = session;
            _dragStartPoint = location;
            _tree.CaptureMouse();

            var selectedNodes = _tree.SelectedItems
                .OfType<TreeGridItem>()
                .Select(item => item.Tag as OutlinerNodeReference)
                .Where(node => node != null)
                .ToList();
            var clickedKey = new StateGestureNodeKey(clickedNode);
            var clickedIsSelected = selectedNodes.Any(
                node => new StateGestureNodeKey(node).Equals(clickedKey));
            var initialNodes = clickedIsSelected
                ? selectedNodes.Where(node =>
                    TryGetStateGestureKind(column, node, out var nodeKind) &&
                    nodeKind == kind)
                : new[] { clickedNode };
            ApplyStateGestureNodes(session, initialNodes);
            return true;
        }

        private void ContinueStateGesture(PointF location)
        {
            var session = _stateGesture;
            if (session == null)
                return;

            if (Math.Abs(location.Y - session.StartPoint.Y) < DragThreshold &&
                Math.Abs(location.X - session.StartPoint.X) < DragThreshold)
            {
                return;
            }

            var distance = location.Y - session.LastPaintY;
            var spacing = Math.Max(4f, _tree.RowHeight / 2f);
            var stepCount = Math.Max(
                1,
                (int)Math.Ceiling(Math.Abs(distance) / spacing));
            for (var step = 1; step <= stepCount; step++)
            {
                var y = session.LastPaintY + distance * step / stepCount;
                PaintStateGestureAt(
                    session,
                    new PointF(session.StartPoint.X, y));
            }
            session.LastPaintY = location.Y;
        }

        private void PaintStateGestureAt(
            StateGestureSession session,
            PointF location)
        {
            var cell = _tree.GetCellAt(location);
            var node = (cell?.Item as TreeGridItem)?.Tag as
                OutlinerNodeReference;
            if (cell == null ||
                cell.Column != session.Column ||
                node == null ||
                !TryGetStateGestureKind(
                    session.Column,
                    node,
                    out var kind) ||
                kind != session.Kind)
            {
                return;
            }

            ApplyStateGestureNodes(session, new[] { node });
        }

        private void EndStateGesture()
        {
            var session = _stateGesture;
            _stateGesture = null;
            if (_tree.IsMouseCaptured && _referencePointerNode == null)
                _tree.ReleaseMouseCapture();
            if (session == null)
                return;

            try
            {
                if (session.UndoRecord != 0)
                    session.Document.EndUndoRecord(session.UndoRecord);
            }
            catch
            {
                // The document may already be closing.
            }

            if (!session.Changed)
                return;

            QueueStateRefresh(session.Change);
            session.Document.Views.Redraw();
        }

        private bool IsStateGestureTarget(
            GridColumn column,
            OutlinerNodeReference node)
        {
            return TryGetStateGestureKind(column, node, out _);
        }

        private bool TryGetStateGestureKind(
            GridColumn column,
            OutlinerNodeReference node,
            out StateGestureKind kind)
        {
            kind = StateGestureKind.None;
            if (column == null || node == null)
                return false;

            var isStateNode = node.Kind == OutlinerNodeKind.Layer ||
                node.Kind == OutlinerNodeKind.Object ||
                node.Kind == OutlinerNodeKind.GroupSlice;
            if (!isStateNode)
                return false;

            if (column == _favoriteColumn)
                kind = StateGestureKind.Favorite;
            else if (column == _visibilityColumn)
                kind = StateGestureKind.Visibility;
            else if (column == _lockColumn)
                kind = StateGestureKind.Lock;
            else if (node.Kind == OutlinerNodeKind.Layer &&
                column == _newDetailColumn)
                kind = StateGestureKind.NewDetailVisibility;
            else if (node.Kind == OutlinerNodeKind.Layer &&
                column == _modelVisibilityColumn)
                kind = StateGestureKind.ModelVisibility;

            return kind != StateGestureKind.None;
        }

        private bool TryReadState(
            RhinoDoc doc,
            OutlinerNodeReference node,
            StateGestureKind kind,
            out bool state)
        {
            state = false;
            if (node.Kind == OutlinerNodeKind.Layer)
            {
                var layer = doc.Layers.FindId(node.Id);
                if (layer == null || layer.IsDeleted || layer.IsReference)
                    return false;
                if ((kind == StateGestureKind.Visibility ||
                     kind == StateGestureKind.Lock) &&
                    layer.Index == doc.Layers.CurrentLayerIndex)
                {
                    return false;
                }

                switch (kind)
                {
                    case StateGestureKind.Favorite:
                        var favorite = FavoriteCodec.Read(
                            layer.GetUserString(FavoriteCodec.UserStringKey));
                        state = favorite.IsFavorite;
                        return favorite.CanEdit;
                    case StateGestureKind.Visibility:
                        var inheritedHidden = HasMatchingAncestor(
                            doc,
                            layer,
                            parent => !parent.IsVisible);
                        state = inheritedHidden
                            ? layer.PersistentVisibility
                            : layer.IsVisible;
                        return true;
                    case StateGestureKind.Lock:
                        var inheritedLocked = HasMatchingAncestor(
                            doc,
                            layer,
                            parent => parent.IsLocked);
                        state = inheritedLocked
                            ? layer.GetPersistentLocking()
                            : layer.IsLocked;
                        return true;
                    case StateGestureKind.NewDetailVisibility:
                        state = layer.PerViewportIsVisibleInNewDetails;
                        return true;
                    case StateGestureKind.ModelVisibility:
                        state = layer.ModelIsVisible;
                        return true;
                    default:
                        return false;
                }
            }

            if (node.Kind == OutlinerNodeKind.Object)
            {
                var rhinoObject = doc.Objects.FindId(node.Id);
                if (rhinoObject == null ||
                    rhinoObject.IsDeleted ||
                    rhinoObject.IsReference)
                {
                    return false;
                }

                switch (kind)
                {
                    case StateGestureKind.Favorite:
                        var favorite = FavoriteCodec.Read(
                            rhinoObject.Attributes.GetUserString(
                                FavoriteCodec.UserStringKey));
                        state = favorite.IsFavorite;
                        return favorite.CanEdit;
                    case StateGestureKind.Visibility:
                        state = !rhinoObject.IsHidden;
                        return true;
                    case StateGestureKind.Lock:
                        state = rhinoObject.IsLocked;
                        return true;
                    default:
                        return false;
                }
            }

            if (node.Kind != OutlinerNodeKind.GroupSlice)
                return false;

            if (kind == StateGestureKind.Favorite)
            {
                if (_favoriteStore == null ||
                    _documentModel == null ||
                    !_documentModel.GroupsById.TryGetValue(
                        node.Id,
                        out var groupModel) ||
                    !groupModel.CanEditFavorite)
                {
                    return false;
                }

                state = groupModel.IsFavorite;
                return true;
            }

            var members = GetEditableGroupMembers(doc, node);
            if (members.Count == 0)
                return false;
            if (kind == StateGestureKind.Visibility)
            {
                state = members.All(member => !member.IsHidden);
                return true;
            }
            if (kind == StateGestureKind.Lock)
            {
                state = members.All(member => member.IsLocked);
                return true;
            }
            return false;
        }

        private void ApplyStateGestureNodes(
            StateGestureSession session,
            IEnumerable<OutlinerNodeReference> nodes)
        {
            if (session == null || nodes == null)
                return;

            var targets = new List<OutlinerNodeReference>();
            foreach (var node in nodes)
            {
                if (node == null ||
                    !TryGetStateGestureKind(
                        session.Column,
                        node,
                        out var nodeKind) ||
                    nodeKind != session.Kind ||
                    !session.VisitedNodes.Add(
                        new StateGestureNodeKey(node)))
                {
                    continue;
                }
                targets.Add(node);
            }
            if (targets.Count == 0)
                return;

            var change = new OutlinerStateChange();
            var previousChange = _activeStateChange;
            _activeStateChange = change;
            var changed = false;
            try
            {
                foreach (var node in targets)
                {
                    changed |= ApplyStateGestureNode(
                        session,
                        node,
                        change);
                }
            }
            finally
            {
                _activeStateChange = previousChange;
            }

            if (!changed)
                return;

            session.Changed = true;
            session.Change.Merge(change);
            QueueStateRefresh(change);
            session.Document.Views.Redraw();
        }

        private bool ApplyStateGestureNode(
            StateGestureSession session,
            OutlinerNodeReference node,
            OutlinerStateChange change)
        {
            if (node.Kind == OutlinerNodeKind.Layer)
            {
                return ApplyLayerGestureState(
                    session.Document,
                    node.Id,
                    session.Kind,
                    session.DesiredState,
                    change);
            }

            if (node.Kind == OutlinerNodeKind.Object)
            {
                return ApplyObjectGestureState(
                    session,
                    node.Id,
                    session.Kind,
                    session.DesiredState,
                    change);
            }

            if (node.Kind != OutlinerNodeKind.GroupSlice ||
                !session.ProcessedGroupIds.Add(node.Id))
            {
                return false;
            }

            if (session.Kind == StateGestureKind.Favorite)
            {
                if (_favoriteStore == null ||
                    !_favoriteStore.SetFavorites(
                        session.Document,
                        new[] { node.Id },
                        session.DesiredState))
                {
                    return false;
                }

                change.AddFavoriteGroups(new[] { node.Id });
                return true;
            }

            var changed = false;
            foreach (var member in GetEditableGroupMembers(
                session.Document,
                node))
            {
                changed |= ApplyObjectGestureState(
                    session,
                    member.Id,
                    session.Kind,
                    session.DesiredState,
                    change);
            }
            return changed;
        }

        private bool ApplyLayerGestureState(
            RhinoDoc doc,
            Guid layerId,
            StateGestureKind kind,
            bool desiredState,
            OutlinerStateChange change)
        {
            var layer = doc.Layers.FindId(layerId);
            if (layer == null || layer.IsDeleted || layer.IsReference)
                return false;
            if ((kind == StateGestureKind.Visibility ||
                 kind == StateGestureKind.Lock) &&
                layer.Index == doc.Layers.CurrentLayerIndex)
            {
                return false;
            }

            if (kind == StateGestureKind.Visibility)
            {
                var inheritedHidden = HasMatchingAncestor(
                    doc,
                    layer,
                    parent => !parent.IsVisible);
                if (inheritedHidden)
                {
                    if (layer.PersistentVisibility == desiredState)
                        return false;
                    layer.SetPersistentVisibility(desiredState);
                }
                else
                {
                    if (layer.IsVisible == desiredState &&
                        layer.PersistentVisibility == desiredState)
                    {
                        return false;
                    }
                    layer.IsVisible = desiredState;
                    layer.SetPersistentVisibility(desiredState);
                }

                var changed = doc.Layers.Modify(layer, layer.Id, true);
                if (changed)
                    change.AddLayers(new[] { layer.Id });
                return changed;
            }

            if (kind == StateGestureKind.Lock)
            {
                var inheritedLocked = HasMatchingAncestor(
                    doc,
                    layer,
                    parent => parent.IsLocked);
                var changed = false;
                if (!inheritedLocked && desiredState)
                {
                    changed |= SynchronizeDescendantVisibilityIntent(
                        doc,
                        layer.Id);
                }

                var layerChanged = false;
                if (inheritedLocked)
                {
                    if (layer.GetPersistentLocking() != desiredState)
                    {
                        layer.SetPersistentLocking(desiredState);
                        layerChanged = true;
                    }
                }
                else if (layer.IsLocked != desiredState)
                {
                    layer.IsLocked = desiredState;
                    layerChanged = true;
                }

                if (layerChanged)
                    changed |= doc.Layers.Modify(layer, layer.Id, true);
                if (changed)
                    change.AddLayers(new[] { layer.Id });
                return changed;
            }

            if (kind == StateGestureKind.Favorite)
            {
                var favorite = FavoriteCodec.Read(
                    layer.GetUserString(FavoriteCodec.UserStringKey));
                if (!favorite.CanEdit || favorite.IsFavorite == desiredState)
                    return false;

                var settings = CreateDetachedLayerSettings(layer);
                settings.SetUserString(
                    FavoriteCodec.UserStringKey,
                    FavoriteCodec.Write(desiredState));
                var changed = doc.Layers.Modify(settings, layer.Id, true);
                if (changed)
                    change.AddFavoriteLayers(new[] { layer.Id });
                return changed;
            }

            if (kind == StateGestureKind.NewDetailVisibility)
            {
                if (layer.PerViewportIsVisibleInNewDetails == desiredState)
                    return false;
                layer.PerViewportIsVisibleInNewDetails = desiredState;
            }
            else if (kind == StateGestureKind.ModelVisibility)
            {
                if (layer.ModelIsVisible == desiredState)
                    return false;
                layer.ModelIsVisible = desiredState;
            }
            else
            {
                return false;
            }

            var propertyChanged = doc.Layers.Modify(layer, layer.Id, true);
            if (propertyChanged)
                change.AddLayerProperties(new[] { layer.Id });
            return propertyChanged;
        }

        private bool ApplyObjectGestureState(
            StateGestureSession session,
            Guid objectId,
            StateGestureKind kind,
            bool desiredState,
            OutlinerStateChange change)
        {
            if (!session.ProcessedObjectIds.Add(objectId))
                return false;

            var rhinoObject = session.Document.Objects.FindId(objectId);
            if (rhinoObject == null ||
                rhinoObject.IsDeleted ||
                rhinoObject.IsReference)
            {
                return false;
            }

            bool changed;
            if (kind == StateGestureKind.Visibility)
            {
                if ((!rhinoObject.IsHidden) == desiredState)
                    return false;
                changed = desiredState
                    ? session.Document.Objects.Show(objectId, true)
                    : session.Document.Objects.Hide(objectId, true);
                if (changed)
                    change.AddObjects(new[] { objectId });
                return changed;
            }

            if (kind == StateGestureKind.Lock)
            {
                if (rhinoObject.IsLocked == desiredState)
                    return false;
                changed = desiredState
                    ? session.Document.Objects.Lock(objectId, true)
                    : session.Document.Objects.Unlock(objectId, true);
                if (changed)
                    change.AddObjects(new[] { objectId });
                return changed;
            }

            if (kind != StateGestureKind.Favorite)
                return false;

            var favorite = FavoriteCodec.Read(
                rhinoObject.Attributes.GetUserString(
                    FavoriteCodec.UserStringKey));
            if (!favorite.CanEdit || favorite.IsFavorite == desiredState)
                return false;

            var attributes = rhinoObject.Attributes.Duplicate();
            attributes.SetUserString(
                FavoriteCodec.UserStringKey,
                FavoriteCodec.Write(desiredState));
            changed = session.Document.Objects.ModifyAttributes(
                objectId,
                attributes,
                true);
            if (changed)
                change.AddFavoriteObjects(new[] { objectId });
            return changed;
        }

        private static string GetStateGestureUndoText(StateGestureKind kind)
        {
            switch (kind)
            {
                case StateGestureKind.Visibility:
                    return OutlinerStrings.UndoChangeVisibility;
                case StateGestureKind.Lock:
                    return OutlinerStrings.UndoChangeLock;
                case StateGestureKind.Favorite:
                    return OutlinerStrings.UndoChangeFavorites;
                case StateGestureKind.NewDetailVisibility:
                    return OutlinerStrings.UndoChangeNewDetailVisibility;
                case StateGestureKind.ModelVisibility:
                    return OutlinerStrings.UndoChangeModelVisibility;
                default:
                    return OutlinerStrings.UndoChangeVisibility;
            }
        }
    }
}
