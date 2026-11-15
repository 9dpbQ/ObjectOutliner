using System;
using System.Collections.Generic;
using System.Linq;
using Rhino;
using Rhino.DocObjects;

namespace ObjectManager.Outliner
{
    internal enum OutlinerIsolationMode
    {
        None,
        Visibility,
        Lock
    }

    internal enum OutlinerIsolateTargetKind
    {
        None,
        Layer,
        Group,
        Object
    }

    internal enum OutlinerIsolateVisualState
    {
        None,
        Target,
        Suppressed
    }

    internal sealed class OutlinerIsolateRequest
    {
        public OutlinerIsolateRequest(
            OutlinerIsolateTargetKind kind,
            IEnumerable<Guid> nodeIds,
            IEnumerable<Guid> objectIds)
        {
            Kind = kind;
            NodeIds = new HashSet<Guid>(nodeIds ?? Enumerable.Empty<Guid>());
            ObjectIds = new HashSet<Guid>(objectIds ?? Enumerable.Empty<Guid>());
        }

        public OutlinerIsolateTargetKind Kind { get; }

        public HashSet<Guid> NodeIds { get; }

        public HashSet<Guid> ObjectIds { get; }
    }

    internal sealed class OutlinerIsolateSnapshot
    {
        public OutlinerIsolateSnapshot(
            OutlinerIsolationMode mode,
            OutlinerIsolateTargetKind targetKind,
            IEnumerable<Guid> targetNodeIds,
            IEnumerable<Guid> targetObjectIds,
            IEnumerable<Guid> suppressedObjectIds)
        {
            Mode = mode;
            TargetKind = targetKind;
            TargetNodeIds = new HashSet<Guid>(
                targetNodeIds ?? Enumerable.Empty<Guid>());
            TargetObjectIds = new HashSet<Guid>(
                targetObjectIds ?? Enumerable.Empty<Guid>());
            SuppressedObjectIds = new HashSet<Guid>(
                suppressedObjectIds ?? Enumerable.Empty<Guid>());
        }

        public OutlinerIsolationMode Mode { get; }

        public OutlinerIsolateTargetKind TargetKind { get; }

        public HashSet<Guid> TargetNodeIds { get; }

        public HashSet<Guid> TargetObjectIds { get; }

        public HashSet<Guid> SuppressedObjectIds { get; }

        public bool IsActive =>
            Mode != OutlinerIsolationMode.None &&
            TargetKind != OutlinerIsolateTargetKind.None &&
            TargetObjectIds.Count > 0;

        public OutlinerIsolateSnapshot Clone()
        {
            return new OutlinerIsolateSnapshot(
                Mode,
                TargetKind,
                TargetNodeIds,
                TargetObjectIds,
                SuppressedObjectIds);
        }

        public OutlinerIsolateRequest CreateRequest()
        {
            return IsActive
                ? new OutlinerIsolateRequest(
                    TargetKind,
                    TargetNodeIds,
                    TargetObjectIds)
                : null;
        }
    }

    internal sealed class OutlinerIsolateState
    {
        private readonly HashSet<Guid> _targetNodeIds = new HashSet<Guid>();
        private readonly HashSet<Guid> _targetObjectIds = new HashSet<Guid>();
        private readonly HashSet<Guid> _stateBeforeCommand = new HashSet<Guid>();
        private readonly HashSet<Guid> _suppressedObjectIds = new HashSet<Guid>();
        private readonly HashSet<Guid> _targetLayerContext = new HashSet<Guid>();
        private readonly HashSet<Guid> _suppressedLayerContext = new HashSet<Guid>();
        private readonly HashSet<Guid> _targetGroupContext = new HashSet<Guid>();
        private readonly HashSet<Guid> _suppressedGroupContext = new HashSet<Guid>();

        public bool IsActive { get; private set; }

        public bool IsCapturing { get; private set; }

        public OutlinerIsolateTargetKind TargetKind { get; private set; }

        public OutlinerIsolationMode Mode { get; private set; }

        public IEnumerable<Guid> GetAffectedObjectIds()
        {
            return _targetObjectIds
                .Concat(_suppressedObjectIds)
                .Distinct()
                .ToList();
        }

        public OutlinerIsolateRequest CreateRequest()
        {
            if (!IsActive ||
                Mode == OutlinerIsolationMode.None ||
                TargetKind == OutlinerIsolateTargetKind.None ||
                _targetObjectIds.Count == 0)
                return null;

            return new OutlinerIsolateRequest(
                TargetKind,
                _targetNodeIds,
                _targetObjectIds);
        }

        public OutlinerIsolateSnapshot CreateSnapshot()
        {
            return !IsActive
                ? null
                : new OutlinerIsolateSnapshot(
                    Mode,
                    TargetKind,
                    _targetNodeIds,
                    _targetObjectIds,
                    _suppressedObjectIds);
        }

        public void Restore(OutlinerIsolateSnapshot snapshot)
        {
            Clear();
            if (snapshot == null || !snapshot.IsActive)
                return;

            Mode = snapshot.Mode;
            TargetKind = snapshot.TargetKind;
            _targetNodeIds.UnionWith(snapshot.TargetNodeIds);
            _targetObjectIds.UnionWith(snapshot.TargetObjectIds);
            _suppressedObjectIds.UnionWith(snapshot.SuppressedObjectIds);
            IsActive = true;
        }

        public bool MatchesDocument(RhinoDoc doc)
        {
            if (!IsActive || doc == null)
                return false;

            foreach (var objectId in _suppressedObjectIds)
            {
                var item = doc.Objects.FindId(objectId);
                if (item == null || item.IsDeleted)
                    continue;
                if (Mode == OutlinerIsolationMode.Visibility && !item.IsHidden)
                    return false;
                if (Mode == OutlinerIsolationMode.Lock && !item.IsLocked)
                    return false;
            }
            return true;
        }

        public void BeginNativeIsolation(
            RhinoDoc doc,
            OutlinerIsolateRequest request,
            OutlinerIsolationMode mode)
        {
            Clear();
            IsCapturing = true;
            Mode = mode;
            TargetKind = request?.Kind ?? OutlinerIsolateTargetKind.Object;

            if (request != null)
            {
                _targetNodeIds.UnionWith(request.NodeIds);
                _targetObjectIds.UnionWith(request.ObjectIds);
            }
            else if (doc != null)
            {
                _targetObjectIds.UnionWith(
                    doc.Objects.GetSelectedObjects(false, false).Select(item => item.Id));
                _targetNodeIds.UnionWith(_targetObjectIds);
            }

            _stateBeforeCommand.UnionWith(GetAffectedStateObjectIds(doc, mode));
        }

        public void CompleteNativeIsolation(RhinoDoc doc, bool succeeded)
        {
            if (!IsCapturing)
                return;

            IsCapturing = false;
            if (!succeeded)
            {
                Clear();
                return;
            }

            _suppressedObjectIds.UnionWith(
                GetAffectedStateObjectIds(doc, Mode));
            _suppressedObjectIds.ExceptWith(_stateBeforeCommand);
            IsActive = true;
        }

        public void Validate(RhinoDoc doc)
        {
            if (!IsActive || doc == null)
                return;

            _targetObjectIds.RemoveWhere(id => doc.Objects.FindId(id) == null);
            _suppressedObjectIds.RemoveWhere(id => doc.Objects.FindId(id) == null);
        }

        public void ReleaseManuallyChangedObject(Guid id)
        {
            _suppressedObjectIds.Remove(id);
        }

        public void RebuildPresentation(OutlinerDocumentModel model)
        {
            _targetLayerContext.Clear();
            _suppressedLayerContext.Clear();
            _targetGroupContext.Clear();
            _suppressedGroupContext.Clear();
            if (!IsActive || model == null)
                return;

            var layersById = model.Layers.ToDictionary(item => item.Id);
            var objectsById = model.Objects.ToDictionary(item => item.Id);
            AddLayerContexts(_targetObjectIds, objectsById, layersById, _targetLayerContext);
            AddLayerContexts(_suppressedObjectIds, objectsById, layersById, _suppressedLayerContext);

            foreach (var group in model.Groups)
            {
                if (group.MemberIds.Any(_targetObjectIds.Contains))
                    _targetGroupContext.Add(group.Id);
                if (group.MemberIds.Any(_suppressedObjectIds.Contains))
                    _suppressedGroupContext.Add(group.Id);
            }
        }

        public OutlinerIsolateVisualState GetLayerState(
            Guid layerId,
            OutlinerIsolationMode mode)
        {
            if (!IsActive || Mode != mode)
                return OutlinerIsolateVisualState.None;
            if (TargetKind == OutlinerIsolateTargetKind.Layer &&
                _targetNodeIds.Contains(layerId))
                return OutlinerIsolateVisualState.Target;
            if (_targetLayerContext.Contains(layerId))
                return OutlinerIsolateVisualState.None;
            return _suppressedLayerContext.Contains(layerId)
                ? OutlinerIsolateVisualState.Suppressed
                : OutlinerIsolateVisualState.None;
        }

        public OutlinerIsolateVisualState GetGroupState(
            Guid groupId,
            OutlinerIsolationMode mode)
        {
            if (!IsActive || Mode != mode)
                return OutlinerIsolateVisualState.None;
            if (TargetKind == OutlinerIsolateTargetKind.Group &&
                _targetNodeIds.Contains(groupId))
                return OutlinerIsolateVisualState.Target;
            if (_targetGroupContext.Contains(groupId))
                return OutlinerIsolateVisualState.None;
            return _suppressedGroupContext.Contains(groupId)
                ? OutlinerIsolateVisualState.Suppressed
                : OutlinerIsolateVisualState.None;
        }

        public OutlinerIsolateVisualState GetObjectState(
            Guid objectId,
            OutlinerIsolationMode mode)
        {
            if (!IsActive || Mode != mode)
                return OutlinerIsolateVisualState.None;
            if (TargetKind == OutlinerIsolateTargetKind.Object &&
                _targetNodeIds.Contains(objectId))
                return OutlinerIsolateVisualState.Target;
            return _suppressedObjectIds.Contains(objectId)
                ? OutlinerIsolateVisualState.Suppressed
                : OutlinerIsolateVisualState.None;
        }

        public bool IsExplicitTarget(
            OutlinerNodeReference node,
            OutlinerIsolationMode mode)
        {
            if (!IsActive ||
                Mode != mode ||
                node == null ||
                !_targetNodeIds.Contains(node.Id))
                return false;

            return (TargetKind == OutlinerIsolateTargetKind.Layer &&
                    node.Kind == OutlinerNodeKind.Layer) ||
                (TargetKind == OutlinerIsolateTargetKind.Group &&
                 node.Kind == OutlinerNodeKind.GroupSlice) ||
                (TargetKind == OutlinerIsolateTargetKind.Object &&
                 node.Kind == OutlinerNodeKind.Object);
        }

        public void Clear()
        {
            IsActive = false;
            IsCapturing = false;
            TargetKind = OutlinerIsolateTargetKind.None;
            Mode = OutlinerIsolationMode.None;
            _targetNodeIds.Clear();
            _targetObjectIds.Clear();
            _stateBeforeCommand.Clear();
            _suppressedObjectIds.Clear();
            _targetLayerContext.Clear();
            _suppressedLayerContext.Clear();
            _targetGroupContext.Clear();
            _suppressedGroupContext.Clear();
        }

        private static void AddLayerContexts(
            IEnumerable<Guid> objectIds,
            IDictionary<Guid, OutlinerObjectModel> objectsById,
            IDictionary<Guid, OutlinerLayerModel> layersById,
            ISet<Guid> destination)
        {
            foreach (var objectId in objectIds)
            {
                OutlinerObjectModel objectModel;
                if (!objectsById.TryGetValue(objectId, out objectModel))
                    continue;

                var layerId = objectModel.LayerId;
                var visited = new HashSet<Guid>();
                while (layerId != Guid.Empty && visited.Add(layerId))
                {
                    destination.Add(layerId);
                    OutlinerLayerModel layer;
                    if (!layersById.TryGetValue(layerId, out layer))
                        break;
                    layerId = layer.ParentId;
                }
            }
        }

        private static IEnumerable<Guid> GetHiddenObjectIds(RhinoDoc doc)
        {
            if (doc == null)
                return Enumerable.Empty<Guid>();

            var settings = new ObjectEnumeratorSettings
            {
                ActiveObjects = true,
                DeletedObjects = false,
                HiddenObjects = true,
                IdefObjects = false,
                IncludeGrips = false,
                IncludeLights = true,
                IncludePhantoms = false,
                LockedObjects = true,
                NormalObjects = true,
                ReferenceObjects = true
            };
            return doc.Objects.GetObjectList(settings)
                .Where(item => item != null && item.IsHidden)
                .Select(item => item.Id)
                .ToList();
        }

        private static IEnumerable<Guid> GetAffectedStateObjectIds(
            RhinoDoc doc,
            OutlinerIsolationMode mode)
        {
            if (mode == OutlinerIsolationMode.Visibility)
                return GetHiddenObjectIds(doc);
            if (mode != OutlinerIsolationMode.Lock || doc == null)
                return Enumerable.Empty<Guid>();

            var settings = new ObjectEnumeratorSettings
            {
                ActiveObjects = true,
                DeletedObjects = false,
                HiddenObjects = true,
                IdefObjects = false,
                IncludeGrips = false,
                IncludeLights = true,
                IncludePhantoms = false,
                LockedObjects = true,
                NormalObjects = true,
                ReferenceObjects = true
            };
            return doc.Objects.GetObjectList(settings)
                .Where(item => item != null && item.IsLocked)
                .Select(item => item.Id)
                .ToList();
        }
    }
}
