using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Rhino;
using Rhino.Commands;
using Rhino.DocObjects;
using Rhino.DocObjects.Tables;

namespace ObjectManager.Outliner
{
    internal sealed class DocumentEventBridge : IDisposable
    {
        private readonly uint _documentSerialNumber;
        private readonly OutlinerStateChange _stateDirtyDuringCommand =
            new OutlinerStateChange();
        private readonly OutlinerStructureChange _structureDirtyDuringCommand =
            new OutlinerStructureChange();
        private bool _commandRunning;
        private long _diagnosticCommandStart;
        private readonly Dictionary<string, long> _diagnosticEvents = new Dictionary<string, long>();

        private void CountDiagnosticEvent(string kind)
        {
            if (!OutlinerDiagnosticMode.IsEnabled || _diagnosticCommandStart == 0)
                return;
            _diagnosticEvents.TryGetValue(kind, out var count);
            _diagnosticEvents[kind] = count + 1;
        }
        private bool _disposed;

        public DocumentEventBridge(uint documentSerialNumber)
        {
            _documentSerialNumber = documentSerialNumber;
            _commandRunning = RhinoDoc
                .FromRuntimeSerialNumber(documentSerialNumber)
                ?.IsCommandRunning == true;

            RhinoDoc.AddRhinoObject += OnRhinoObjectAdded;
            RhinoDoc.DeleteRhinoObject += OnRhinoObjectDeleted;
            RhinoDoc.UndeleteRhinoObject += OnRhinoObjectUndeleted;
            RhinoDoc.PurgeRhinoObject += OnRhinoObjectPurged;
            RhinoDoc.ReplaceRhinoObject += OnRhinoObjectReplaced;
            RhinoDoc.ModifyObjectAttributes += OnObjectAttributesModified;
            RhinoDoc.LayerTableEvent += OnLayerTableChanged;
            RhinoDoc.InstanceDefinitionTableEvent += OnInstanceDefinitionTableChanged;
            RhinoDoc.GroupTableEvent += OnGroupTableChanged;
            RhinoDoc.SelectObjects += OnObjectSelectionChanged;
            RhinoDoc.DeselectObjects += OnObjectSelectionChanged;
            RhinoDoc.DeselectAllObjects += OnAllObjectsDeselected;
            Command.BeginCommand += OnBeginCommand;
            Command.EndCommand += OnEndCommand;
        }

        public event EventHandler<OutlinerStructureChange> StructureChanged;

        public event EventHandler SelectionChanged;

        public event EventHandler<OutlinerStateChange> StateChanged;

        public event EventHandler CurrentLayerChanged;

        public event EventHandler<CommandEventArgs> CommandStarted;

        public event EventHandler<CommandEventArgs> CommandEnded;

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            RhinoDoc.AddRhinoObject -= OnRhinoObjectAdded;
            RhinoDoc.DeleteRhinoObject -= OnRhinoObjectDeleted;
            RhinoDoc.UndeleteRhinoObject -= OnRhinoObjectUndeleted;
            RhinoDoc.PurgeRhinoObject -= OnRhinoObjectPurged;
            RhinoDoc.ReplaceRhinoObject -= OnRhinoObjectReplaced;
            RhinoDoc.ModifyObjectAttributes -= OnObjectAttributesModified;
            RhinoDoc.LayerTableEvent -= OnLayerTableChanged;
            RhinoDoc.InstanceDefinitionTableEvent -= OnInstanceDefinitionTableChanged;
            RhinoDoc.GroupTableEvent -= OnGroupTableChanged;
            RhinoDoc.SelectObjects -= OnObjectSelectionChanged;
            RhinoDoc.DeselectObjects -= OnObjectSelectionChanged;
            RhinoDoc.DeselectAllObjects -= OnAllObjectsDeselected;
            Command.BeginCommand -= OnBeginCommand;
            Command.EndCommand -= OnEndCommand;
            _stateDirtyDuringCommand.Clear();
            _structureDirtyDuringCommand.Clear();
        }

        private void OnRhinoObjectAdded(object sender, RhinoObjectEventArgs e)
        {
            NotifyObjectStructureChanged(
                sender,
                e,
                OutlinerStructureChangeKind.ObjectAdded);
        }

        private void OnRhinoObjectDeleted(object sender, RhinoObjectEventArgs e)
        {
            NotifyObjectStructureChanged(
                sender,
                e,
                OutlinerStructureChangeKind.ObjectDeleted);
        }

        private void OnRhinoObjectUndeleted(object sender, RhinoObjectEventArgs e)
        {
            NotifyObjectStructureChanged(
                sender,
                e,
                OutlinerStructureChangeKind.ObjectUndeleted);
        }

        private void OnRhinoObjectPurged(object sender, RhinoObjectEventArgs e)
        {
            NotifyObjectStructureChanged(
                sender,
                e,
                OutlinerStructureChangeKind.ObjectPurged);
        }

        private void OnRhinoObjectReplaced(object sender, RhinoReplaceObjectEventArgs e)
        {
            var oldObject = e.OldRhinoObject;
            var newObject = e.NewRhinoObject;
            var layers = new[]
            {
                oldObject?.Attributes?.LayerIndex ?? -1,
                newObject?.Attributes?.LayerIndex ?? -1
            };
            var groups = GetGroupIndices(oldObject)
                .Concat(GetGroupIndices(newObject))
                .Distinct()
                .ToArray();
            var change = new OutlinerStructureChange().AddObject(
                OutlinerStructureChangeKind.ObjectReplaced,
                e.ObjectId,
                layers,
                groups,
                groups.Length > 0);
            NotifyStructureChanged(e.Document, change);
        }

        private void OnObjectAttributesModified(object sender, RhinoModifyObjectAttributesEventArgs e)
        {
            if (!IsObservedDocument(e.Document))
                return;

            var oldAttributes = e.OldAttributes;
            var newAttributes = e.NewAttributes;
            var objectId = e.RhinoObject?.Id ?? Guid.Empty;
            if (oldAttributes == null ||
                newAttributes == null ||
                objectId == Guid.Empty)
            {
                NotifyStructureChanged(
                    e.Document,
                    OutlinerStructureChange.Full(
                        OutlinerStructureChangeKind.Unknown));
                return;
            }

            var sameGroups = HaveSameGroups(oldAttributes, newAttributes);
            var oldHasOrder = ObjectOrderStore.TryGetOrder(
                oldAttributes,
                out var oldOrder);
            var newHasOrder = ObjectOrderStore.TryGetOrder(
                newAttributes,
                out var newOrder);
            var structureChanged =
                !string.Equals(oldAttributes.GetUserString(ObjectNote.Key),
                    newAttributes.GetUserString(ObjectNote.Key), StringComparison.Ordinal) ||
                oldAttributes.LayerIndex != newAttributes.LayerIndex ||
                !string.Equals(
                    oldAttributes.Name,
                    newAttributes.Name,
                    StringComparison.Ordinal) ||
                !sameGroups ||
                oldHasOrder != newHasOrder ||
                (oldHasOrder && oldOrder != newOrder);
            if (structureChanged)
            {
                var groups = (oldAttributes.GetGroupList() ?? Array.Empty<int>())
                    .Concat(newAttributes.GetGroupList() ?? Array.Empty<int>())
                    .Distinct()
                    .ToArray();
                var change = new OutlinerStructureChange().AddObject(
                    OutlinerStructureChangeKind.ObjectAttributes,
                    objectId,
                    new[]
                    {
                        oldAttributes.LayerIndex,
                        newAttributes.LayerIndex
                    },
                    groups,
                    !sameGroups ||
                    (oldAttributes.LayerIndex != newAttributes.LayerIndex &&
                     groups.Length > 0));
                NotifyStructureChanged(e.Document, change);
                return;
            }

            var favoriteChanged = !string.Equals(
                oldAttributes.GetUserString(FavoriteCodec.UserStringKey),
                newAttributes.GetUserString(FavoriteCodec.UserStringKey),
                StringComparison.Ordinal);
            if (favoriteChanged)
            {
                NotifyStateChanged(
                    e.Document,
                    new OutlinerStateChange().AddFavoriteObjects(
                        new[] { objectId }));
            }
            else if (oldAttributes.Mode != newAttributes.Mode)
            {
                NotifyStateChanged(
                    e.Document,
                    new OutlinerStateChange().AddObjects(new[] { objectId }));
            }
        }

        private void OnLayerTableChanged(object sender, LayerTableEventArgs e)
        {
            if (e.EventType == LayerTableEventType.Current &&
                IsObservedDocument(e.Document))
            {
                CurrentLayerChanged?.Invoke(this, EventArgs.Empty);
                return;
            }

            var oldLayer = e.OldState;
            var newLayer = e.NewState;
            if (!IsObservedDocument(e.Document))
                return;

            var layerAdded = string.Equals(
                    e.EventType.ToString(),
                    "Added",
                    StringComparison.OrdinalIgnoreCase) ||
                (oldLayer == null &&
                 newLayer != null &&
                 !newLayer.IsDeleted);
            if (layerAdded)
            {
                NotifyStructureChanged(
                    e.Document,
                    new OutlinerStructureChange().AddLayerAddition(
                        e.LayerIndex));
                return;
            }

            if (oldLayer == null ||
                newLayer == null ||
                oldLayer.Id != newLayer.Id ||
                oldLayer.ParentLayerId != newLayer.ParentLayerId ||
                oldLayer.IsDeleted != newLayer.IsDeleted)
            {
                var structureChange = new OutlinerStructureChange().AddLayers(
                    OutlinerStructureChangeKind.LayerTable,
                    new[]
                    {
                        e.LayerIndex,
                        oldLayer?.Index ?? -1,
                        newLayer?.Index ?? -1
                    });
                structureChange.ForceFull(
                    OutlinerStructureChangeKind.LayerTable);
                NotifyStructureChanged(e.Document, structureChange);
                return;
            }

            var change = new OutlinerStateChange();
            if (!string.Equals(oldLayer.Name, newLayer.Name, StringComparison.Ordinal))
                change.AddLayerNames(new[] { newLayer.Id });
            if (oldLayer.IsVisible != newLayer.IsVisible ||
                oldLayer.PersistentVisibility != newLayer.PersistentVisibility ||
                oldLayer.IsLocked != newLayer.IsLocked ||
                oldLayer.GetPersistentLocking() != newLayer.GetPersistentLocking())
            {
                change.AddLayers(new[] { newLayer.Id });
            }
            var favoriteChanged = !string.Equals(
                oldLayer.GetUserString(FavoriteCodec.UserStringKey),
                newLayer.GetUserString(FavoriteCodec.UserStringKey),
                StringComparison.Ordinal);
            if (favoriteChanged)
                change.AddFavoriteLayers(new[] { newLayer.Id });
            else
                change.AddLayerProperties(new[] { newLayer.Id });
            NotifyStateChanged(e.Document, change);
        }

        private void OnInstanceDefinitionTableChanged(
            object sender,
            InstanceDefinitionTableEventArgs e)
        {
            NotifyStructureChanged(
                e.Document,
                OutlinerStructureChange.Full(
                    OutlinerStructureChangeKind.InstanceDefinition));
        }

        private void OnGroupTableChanged(object sender, GroupTableEventArgs e)
        {
            var document = ResolveObservedDocument(e.Document, sender);
            if (!IsObservedDocument(document))
                return;
            if (e.GroupIndex < 0)
            {
                NotifyStructureChanged(
                    document,
                    OutlinerStructureChange.Full(
                        OutlinerStructureChangeKind.GroupTable));
                return;
            }

            var change = new OutlinerStructureChange().AddLayers(
                OutlinerStructureChangeKind.GroupTable,
                Array.Empty<int>());
            change.MarkGroupImpact(new[] { e.GroupIndex });
            NotifyStructureChanged(document, change);
        }

        private void OnObjectSelectionChanged(object sender, RhinoObjectSelectionEventArgs e)
        {
            var eventObjects = e.RhinoObjects ?? Array.Empty<RhinoObject>();
            var document = e.Document ??
                sender as RhinoDoc ??
                eventObjects
                    .Where(item => item != null)
                    .Select(item => item.Document)
                    .FirstOrDefault(item => item != null);
            document = ResolveSelectionDocument(document, eventObjects);
            if (IsObservedDocument(document))
            {
                CountDiagnosticEvent("selection");
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private void OnAllObjectsDeselected(object sender, RhinoDeselectAllObjectsEventArgs e)
        {
            var document = ResolveSelectionDocument(
                e.Document ?? sender as RhinoDoc,
                Array.Empty<RhinoObject>());
            if (IsObservedDocument(document))
            {
                CountDiagnosticEvent("deselectAll");
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private RhinoDoc ResolveSelectionDocument(
            RhinoDoc document,
            RhinoObject[] eventObjects)
        {
            if (document != null)
                return document;

            var observed = RhinoDoc.FromRuntimeSerialNumber(
                _documentSerialNumber);
            if (observed == null)
                return null;

            if ((eventObjects ?? Array.Empty<RhinoObject>())
                .Where(item => item != null)
                .Any(item => observed.Objects.FindId(item.Id) != null))
            {
                return observed;
            }

            return RhinoDoc.ActiveDoc?.RuntimeSerialNumber ==
                _documentSerialNumber
                    ? observed
                    : null;
        }

        private RhinoDoc ResolveObservedDocument(
            RhinoDoc document,
            object sender)
        {
            if (document != null)
                return document;
            if (sender is RhinoDoc senderDocument)
                return senderDocument;

            var observed = RhinoDoc.FromRuntimeSerialNumber(
                _documentSerialNumber);
            return RhinoDoc.ActiveDoc?.RuntimeSerialNumber ==
                _documentSerialNumber
                    ? observed
                    : null;
        }

        private void OnBeginCommand(object sender, CommandEventArgs e)
        {
            if (e.DocumentRuntimeSerialNumber == _documentSerialNumber)
            {
                _diagnosticCommandStart = OutlinerDiagnosticMode.IsEnabled ? Stopwatch.GetTimestamp() : 0;
                _diagnosticEvents.Clear();
                _commandRunning = true;
                CommandStarted?.Invoke(this, e);
            }
        }

        private void OnEndCommand(object sender, CommandEventArgs e)
        {
            if (e.DocumentRuntimeSerialNumber != _documentSerialNumber)
                return;

            if (OutlinerDiagnosticMode.IsEnabled && _diagnosticCommandStart != 0)
                OutlinerPerformanceRecorder.Record(new OutlinerPerformanceSample
                {
                    TimestampUtc = DateTime.UtcNow,
                    DocumentSerial = _documentSerialNumber,
                    Reason = "command-events",
                    Cause = e.CommandEnglishName,
                    TotalMilliseconds = (Stopwatch.GetTimestamp() - _diagnosticCommandStart) * 1000L / Stopwatch.Frequency,
                    Details = new Dictionary<string, long>(_diagnosticEvents)
                });
            _diagnosticCommandStart = 0;
            _diagnosticEvents.Clear();
            _commandRunning = false;
            CommandEnded?.Invoke(this, e);
            FlushPendingStateChange();

            var stateOnlyCommand =
                string.Equals(
                    e.CommandEnglishName,
                    "Isolate",
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    e.CommandEnglishName,
                    "Unisolate",
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    e.CommandEnglishName,
                    "IsolateLock",
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    e.CommandEnglishName,
                    "UnisolateLock",
                    StringComparison.OrdinalIgnoreCase);
            if (stateOnlyCommand)
            {
                _structureDirtyDuringCommand.Clear();
                return;
            }

            var forceHistoryRefresh =
                string.Equals(
                    e.CommandEnglishName,
                    "HistoryPurge",
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    e.CommandEnglishName,
                    "HistoryUpdate",
                    StringComparison.OrdinalIgnoreCase);
            if (forceHistoryRefresh)
            {
                _structureDirtyDuringCommand.ForceFull(
                    OutlinerStructureChangeKind.History);
            }
            if (_structureDirtyDuringCommand.IsEmpty)
                return;

            var change = _structureDirtyDuringCommand.Clone();
            _structureDirtyDuringCommand.Clear();
            StructureChanged?.Invoke(this, change);
        }

        private void NotifyObjectStructureChanged(
            object sender,
            RhinoObjectEventArgs e,
            OutlinerStructureChangeKind kind)
        {
            var rhinoObject = e.TheObject;
            var document = sender as RhinoDoc ?? rhinoObject?.Document;
            var groups = GetGroupIndices(rhinoObject);
            var change = new OutlinerStructureChange().AddObject(
                kind,
                e.ObjectId,
                new[] { rhinoObject?.Attributes?.LayerIndex ?? -1 },
                groups,
                groups.Length > 0);
            NotifyStructureChanged(document, change);
        }

        private void NotifyStructureChanged(
            RhinoDoc document,
            OutlinerStructureChange change)
        {
            if (!IsObservedDocument(document) || change == null || change.IsEmpty)
                return;

            CountDiagnosticEvent("structure_" + change.Kinds);
            if (_commandRunning)
            {
                _structureDirtyDuringCommand.Merge(change);
                return;
            }

            StructureChanged?.Invoke(this, change);
        }

        private void NotifyStateChanged(
            RhinoDoc document,
            OutlinerStateChange change)
        {
            if (!IsObservedDocument(document) || change == null || change.IsEmpty)
                return;

            if (_commandRunning)
            {
                _stateDirtyDuringCommand.Merge(change);
                return;
            }

            StateChanged?.Invoke(this, change);
        }

        private void FlushPendingStateChange()
        {
            if (_stateDirtyDuringCommand.IsEmpty)
                return;

            var change = new OutlinerStateChange();
            change.Merge(_stateDirtyDuringCommand);
            _stateDirtyDuringCommand.Clear();
            StateChanged?.Invoke(this, change);
        }

        private static bool HaveSameGroups(
            ObjectAttributes first,
            ObjectAttributes second)
        {
            var firstGroups = first?.GetGroupList() ?? Array.Empty<int>();
            var secondGroups = second?.GetGroupList() ?? Array.Empty<int>();
            return new HashSet<int>(firstGroups).SetEquals(secondGroups);
        }

        private static int[] GetGroupIndices(RhinoObject rhinoObject)
        {
            return rhinoObject?.Attributes?.GetGroupList() ?? Array.Empty<int>();
        }

        private bool IsObservedDocument(RhinoDoc document)
        {
            return !_disposed &&
                   document != null &&
                   document.RuntimeSerialNumber == _documentSerialNumber;
        }
    }
}
