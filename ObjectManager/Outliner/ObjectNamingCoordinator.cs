using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Eto.Forms;
using Rhino;
using Rhino.Commands;
using Rhino.DocObjects;
using Rhino.DocObjects.Tables;
using Rhino.Input.Custom;

namespace ObjectManager.Outliner
{
    internal sealed class ObjectNamingCoordinator : IDisposable
    {
        private sealed class CopyNameCache
        {
            public uint UndoSerial;
            public GenericObjectNameAllocator Allocator;
        }

        private readonly Dictionary<uint, CopyNameCache> _copyNames =
            new Dictionary<uint, CopyNameCache>();
        private readonly Dictionary<string, List<RhinoObject>> _presentationTransforms =
            new Dictionary<string, List<RhinoObject>>();

        private sealed class InputStage
        {
            public InputStage(uint serialNumber)
            {
                SerialNumber = serialNumber;
            }

            public uint SerialNumber { get; }

            public IList<Guid> ObjectIds { get; } = new List<Guid>();

            public void Add(IEnumerable<Guid> objectIds)
            {
                foreach (var objectId in objectIds)
                {
                    if (objectId != Guid.Empty && !ObjectIds.Contains(objectId))
                        ObjectIds.Add(objectId);
                }
            }
        }

        private sealed class InputObjectSnapshot
        {
            public Guid Id { get; set; }

            public ObjectType ObjectType { get; set; }

            public string RootName { get; set; }

            public string DisplayName { get; set; }
        }

        private sealed class NamingCommandSession
        {
            public NamingCommandSession(
                uint documentSerialNumber,
                string commandName,
                bool isCopy,
                bool capturesClipboard,
                bool tracksInputs,
                IEnumerable<Guid> preselectedObjectIds)
            {
                DocumentSerialNumber = documentSerialNumber;
                CommandName = commandName;
                IsCopy = isCopy;
                CapturesClipboard = capturesClipboard;
                TracksInputs = tracksInputs;
                PreselectedObjectIds = new List<Guid>(
                    preselectedObjectIds ?? Enumerable.Empty<Guid>());
            }

            public uint DocumentSerialNumber { get; }

            public string CommandName { get; }

            public bool IsCopy { get; }

            public bool CapturesClipboard { get; }

            public bool TracksInputs { get; }

            public IList<Guid> PreselectedObjectIds { get; }

            public IList<InputStage> InputStages { get; } = new List<InputStage>();

            public IDictionary<Guid, InputObjectSnapshot> InputObjects { get; } =
                new Dictionary<Guid, InputObjectSnapshot>();

            public IList<Guid> OutputIds { get; } = new List<Guid>();

            public ISet<string> ReservedNames { get; } =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            public int OperationNumber { get; set; }

            public string OperationBaseName { get; set; }

            public string OperationRootName { get; set; }

            public bool InputsNamed { get; set; }

            public CopyOperationSession CopyOperation { get; set; }

            public bool CommandEnded { get; set; }

            public InputStage GetOrCreateStage(uint serialNumber)
            {
                var existing = InputStages.FirstOrDefault(
                    candidate => candidate.SerialNumber == serialNumber);
                if (existing != null)
                    return existing;

                var stage = new InputStage(serialNumber);
                InputStages.Add(stage);
                return stage;
            }
        }

        private sealed class CopyGroupSource
        {
            public int Index { get; set; }

            public Guid Id { get; set; }

            public string DisplayName { get; set; }

            public bool HasUserName { get; set; }

            public IList<Guid> MemberIds { get; set; } = new List<Guid>();

            public IList<Guid> CopiedMemberIds { get; set; } = new List<Guid>();

            public bool PreserveOnCopy { get; set; }

            public IDictionary<string, bool> ExpansionByLayerPath { get; set; } =
                new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        }

        private sealed class CopyObjectSource
        {
            public int Ordinal { get; set; }

            public Guid Id { get; set; }

            public string BaseName { get; set; }

            public string StoredName { get; set; }

            public ObjectType ObjectType { get; set; }

            public bool UseGenericName { get; set; }

            public string LineageRoot { get; set; }
        }

        private sealed class CopyBatch
        {
            public int Index { get; set; }

            public IDictionary<Guid, Guid> ObjectMap { get; } =
                new Dictionary<Guid, Guid>();

            public IList<Guid> CopiedObjectIds { get; } = new List<Guid>();

            public IDictionary<Guid, string> OriginalNames { get; } =
                new Dictionary<Guid, string>();

            public bool IsComplete(int sourceCount)
            {
                return sourceCount > 0 && ObjectMap.Count == sourceCount;
            }
        }

        private sealed class CopyOperationSession
        {
            public CopyOperationSession(
                uint documentSerialNumber,
                string commandName,
                string transformEventId = null)
            {
                DocumentSerialNumber = documentSerialNumber;
                SourceDocumentSerialNumber = documentSerialNumber;
                CommandName = commandName;
                TransformEventId = transformEventId ?? string.Empty;
            }

            public uint DocumentSerialNumber { get; }

            public uint SourceDocumentSerialNumber { get; set; }

            public string CommandName { get; }

            public string TransformEventId { get; }

            public bool RequiresStoredNameMatch { get; set; }

            public uint UndoRecordSerialNumber { get; set; }

            public bool CompletionObserved { get; set; }
            public CopyGroupRevision GroupRevision { get; } = new CopyGroupRevision();

            public IList<CopyObjectSource> Sources { get; } =
                new List<CopyObjectSource>();

            public IList<CopyGroupSource> SourceGroups { get; } =
                new List<CopyGroupSource>();

            public IList<CopyBatch> Batches { get; } = new List<CopyBatch>();

            public ISet<Guid> AddedObjectIds { get; } = new HashSet<Guid>();

            public ISet<Guid> ObservedObjectIds { get; } = new HashSet<Guid>();

            public IList<Guid> PendingObjectIds { get; } = new List<Guid>();

            public ISet<int> PendingGroupIndices { get; } = new HashSet<int>();

            public ISet<string> MatchedExpectationKeys { get; } =
                new HashSet<string>(StringComparer.Ordinal);

            public ISet<string> ReservedNames { get; } =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            public GenericObjectNameAllocator GenericNameAllocator { get; set; }
        }

        private static readonly IDictionary<string, string> SemanticCommands =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "Arc", "Arc" },
                { "BooleanDifference", "BooleanDifference" },
                { "BooleanIntersection", "BooleanIntersection" },
                { "BooleanUnion", "BooleanUnion" },
                { "Box", "Box" },
                { "Circle", "Circle" },
                { "Cone", "Cone" },
                { "Contour", "Contour" },
                { "Cylinder", "Cylinder" },
                { "EdgeSrf", "EdgeSrf" },
                { "Ellipse", "Ellipse" },
                { "Extrude", "Extrude" },
                { "ExtrudeCrv", "ExtrudeCrv" },
                { "ExtrudeCrvAlongCrv", "ExtrudeCrvAlongCrv" },
                { "ExtrudeSrf", "ExtrudeSrf" },
                { "Line", "Line" },
                { "Loft", "Loft" },
                { "NetworkSrf", "NetworkSrf" },
                { "OffsetSrf", "OffsetSrf" },
                { "Patch", "Patch" },
                { "Pipe", "Pipe" },
                { "PlanarSrf", "PlanarSrf" },
                { "Polyline", "Polyline" },
                { "QuadRemesh", "QuadRemesh" },
                { "RailRevolve", "RailRevolve" },
                { "Rectangle", "Rectangle" },
                { "Revolve", "Revolve" },
                { "Section", "Section" },
                { "Shell", "Shell" },
                { "Sphere", "Sphere" },
                { "SubDLoft", "SubDLoft" },
                { "Sweep1", "Sweep1" },
                { "Sweep2", "Sweep2" },
                { "Torus", "Torus" }
            };

        private static readonly ISet<string> CopyCommands =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Copy",
                "Duplicate",
                "Paste"
            };

        private static readonly ISet<string> ClipboardCaptureCommands =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "CopyToClipboard"
            };

        private readonly IDictionary<uint, NamingCommandSession> _sessions =
            new Dictionary<uint, NamingCommandSession>();
        private readonly IDictionary<uint, CopyOperationSession> _transformCopies =
            new Dictionary<uint, CopyOperationSession>();
        private readonly DocumentLoadMonitor _documentLoadMonitor;
        private readonly OutlinerRelationshipHub _relationshipHub;
        private readonly ISet<string> _reportedErrors =
            new HashSet<string>(StringComparer.Ordinal);
        private readonly UITimer _inputTimer;
        private CopyOperationSession _clipboardSnapshot;
        private bool _mutating;
        private bool _disposed;

        public ObjectNamingCoordinator(
            DocumentLoadMonitor documentLoadMonitor,
            OutlinerRelationshipHub relationshipHub = null)
        {
            _documentLoadMonitor = documentLoadMonitor;
            _relationshipHub = relationshipHub;
            _inputTimer = new UITimer { Interval = 0.05 };
            _inputTimer.Elapsed += OnInputTimerElapsed;

            Rhino.Commands.Command.BeginCommand += OnBeginCommand;
            Rhino.Commands.Command.EndCommand += OnEndCommand;
            Rhino.Commands.Command.UndoRedo += OnUndoRedo;
            RhinoDoc.AddRhinoObject += OnRhinoObjectAdded;
            RhinoDoc.ReplaceRhinoObject += OnPresentationReplaced;
            RhinoDoc.DeleteRhinoObject += OnPresentationDeleted;
            RhinoDoc.UndeleteRhinoObject += OnPresentationDeleted;
            RhinoDoc.CloseDocument += OnCopyDocumentClosed;
            RhinoDoc.SelectObjects += OnObjectsSelected;
            RhinoDoc.GroupTableEvent += OnGroupTableChanged;
            RhinoDoc.ModifyObjectAttributes += OnCopyAttributesModified;
            RhinoDoc.BeforeTransformObjects += OnBeforeTransformObjects;
            RhinoDoc.AfterTransformObjects += OnAfterTransformObjects;
            if (_relationshipHub != null)
            {
                _relationshipHub.CommandInputObserved +=
                    OnOutlinerCommandInputObserved;
            }
            if (_documentLoadMonitor != null)
                _documentLoadMonitor.DocumentOpening += OnDocumentOpening;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _inputTimer.Stop();
            _inputTimer.Elapsed -= OnInputTimerElapsed;
            Rhino.Commands.Command.BeginCommand -= OnBeginCommand;
            Rhino.Commands.Command.EndCommand -= OnEndCommand;
            Rhino.Commands.Command.UndoRedo -= OnUndoRedo;
            RhinoDoc.AddRhinoObject -= OnRhinoObjectAdded;
            RhinoDoc.ReplaceRhinoObject -= OnPresentationReplaced;
            RhinoDoc.DeleteRhinoObject -= OnPresentationDeleted;
            RhinoDoc.UndeleteRhinoObject -= OnPresentationDeleted;
            RhinoDoc.CloseDocument -= OnCopyDocumentClosed;
            RhinoDoc.SelectObjects -= OnObjectsSelected;
            RhinoDoc.GroupTableEvent -= OnGroupTableChanged;
            RhinoDoc.ModifyObjectAttributes -= OnCopyAttributesModified;
            RhinoDoc.BeforeTransformObjects -= OnBeforeTransformObjects;
            RhinoDoc.AfterTransformObjects -= OnAfterTransformObjects;
            if (_relationshipHub != null)
            {
                _relationshipHub.CommandInputObserved -=
                    OnOutlinerCommandInputObserved;
            }
            if (_documentLoadMonitor != null)
                _documentLoadMonitor.DocumentOpening -= OnDocumentOpening;
            _sessions.Clear();
            _transformCopies.Clear();
            _copyNames.Clear();
            _presentationTransforms.Clear();
            ObjectPresentation.ClearAllCaches();
            _clipboardSnapshot = null;
        }

        private void OnBeginCommand(object sender, CommandEventArgs e)
        {
            _copyNames.Remove(e.DocumentRuntimeSerialNumber);
            if (_disposed || !IsDocumentReady(e.DocumentRuntimeSerialNumber))
                return;

            if (_sessions.TryGetValue(
                e.DocumentRuntimeSerialNumber,
                out var previousSession))
            {
                if (!previousSession.CommandEnded)
                    return;
                _sessions.Remove(e.DocumentRuntimeSerialNumber);
            }

            var isCopy = CopyCommands.Contains(e.CommandEnglishName);
            var capturesClipboard = ClipboardCaptureCommands.Contains(
                e.CommandEnglishName);
            var semanticName = string.Empty;
            if (!isCopy &&
                !capturesClipboard &&
                !SemanticCommands.TryGetValue(e.CommandEnglishName, out semanticName))
            {
                return;
            }

            var doc = e.Document ?? RhinoDoc.FromRuntimeSerialNumber(
                e.DocumentRuntimeSerialNumber);
            if (doc == null)
                return;

            var commandName = isCopy || capturesClipboard
                ? e.CommandEnglishName
                : semanticName;
            var tracksInputs = DerivedNamingPolicy.TracksInputs(commandName);
            var preselectedIds = doc.Objects
                .GetSelectedObjects(false, false)
                .Where(item => item != null)
                .Select(item => item.Id)
                .ToList();
            var session = new NamingCommandSession(
                e.DocumentRuntimeSerialNumber,
                commandName,
                isCopy,
                capturesClipboard,
                tracksInputs,
                preselectedIds);
            if (tracksInputs)
                CaptureInputObjects(session, doc, preselectedIds);
            if (capturesClipboard)
            {
                session.CopyOperation = CreateCopyOperation(
                    doc,
                    commandName,
                    preselectedIds);
            }
            else if (isCopy)
            {
                var isPaste = string.Equals(
                    commandName,
                    "Paste",
                    StringComparison.OrdinalIgnoreCase);
                session.CopyOperation = isPaste && _clipboardSnapshot != null
                    ? CloneCopyOperation(
                        _clipboardSnapshot,
                        doc.RuntimeSerialNumber,
                        commandName,
                        true)
                    : CreateCopyOperation(
                        doc,
                        commandName,
                        isPaste
                            ? Enumerable.Empty<Guid>()
                            : preselectedIds);
            }
            _sessions[e.DocumentRuntimeSerialNumber] = session;
            UpdateInputTimer();
        }

        private void OnEndCommand(object sender, CommandEventArgs e)
        {
            if (_sessions.TryGetValue(
                e.DocumentRuntimeSerialNumber,
                out var session) &&
                string.Equals(
                    session.CommandName,
                    e.CommandEnglishName,
                    StringComparison.OrdinalIgnoreCase))
            {
                session.CommandEnded = true;
                var succeeded = e.CommandResult == Result.Success;
                if (succeeded && session.IsCopy && session.CopyOperation != null)
                {
                    if (!session.CopyOperation.CompletionObserved)
                        session.CopyOperation.GroupRevision.Changed();
                    session.CopyOperation.CompletionObserved = true;
                    ApplyPendingCopyChanges(
                        session.CopyOperation,
                        e.Document,
                        e.Document?.CurrentUndoRecordSerialNumber ?? 0);
                }
                if (succeeded &&
                    session.CapturesClipboard &&
                    session.CopyOperation != null)
                {
                    _clipboardSnapshot = CloneCopyOperation(
                        session.CopyOperation,
                        session.DocumentSerialNumber,
                        session.CommandName);
                }

                if (!succeeded ||
                    !session.IsCopy ||
                    !HasPendingCopyWork(session.CopyOperation))
                {
                    _sessions.Remove(e.DocumentRuntimeSerialNumber);
                }
                UpdateInputTimer();
            }
        }

        private void OnPresentationReplaced(object sender, RhinoReplaceObjectEventArgs e)
        {
            ObjectPresentation.Invalidate(e.OldRhinoObject);
            ObjectPresentation.Invalidate(e.NewRhinoObject);
        }

        private void OnPresentationDeleted(object sender, RhinoObjectEventArgs e)
        {
            ObjectPresentation.Invalidate(e.TheObject);
        }

        private void OnCopyDocumentClosed(object sender, DocumentEventArgs e)
        {
            _copyNames.Remove(e.DocumentSerialNumber);
            _transformCopies.Remove(e.DocumentSerialNumber);
            ObjectPresentation.ClearCache(e.Document);
            _presentationTransforms.Clear();
        }

        private void OnUndoRedo(object sender, UndoRedoEventArgs e)
        {
            if (e.IsBeginUndo || e.IsEndUndo || e.IsBeginRedo || e.IsEndRedo)
            {
                _copyNames.Clear();
                ObjectPresentation.ClearAllCaches();
            }
            if (_disposed || (!e.IsBeforeEndRecording && !e.IsEndRecording))
                return;

            var removedSession = false;
            foreach (var pair in _sessions.ToList())
            {
                var session = pair.Value;
                if (!session.IsCopy || session.CopyOperation == null)
                    continue;

                var doc = RhinoDoc.FromRuntimeSerialNumber(pair.Key);
                if (e.IsBeforeEndRecording && doc != null)
                {
                    ApplyPendingCopyChanges(
                        session.CopyOperation,
                        doc,
                        e.UndoSerialNumber);
                }

                if (e.IsEndRecording && session.CommandEnded &&
                    (session.CopyOperation.UndoRecordSerialNumber == 0 ||
                     session.CopyOperation.UndoRecordSerialNumber ==
                        e.UndoSerialNumber))
                {
                    _sessions.Remove(pair.Key);
                    removedSession = true;
                }
            }

            foreach (var pair in _transformCopies.ToList())
            {
                var operation = pair.Value;
                var doc = RhinoDoc.FromRuntimeSerialNumber(pair.Key);
                if (e.IsBeforeEndRecording && doc != null)
                {
                    ApplyPendingCopyChanges(
                        operation,
                        doc,
                        e.UndoSerialNumber);
                }

                if (e.IsEndRecording && operation.CompletionObserved &&
                    (operation.UndoRecordSerialNumber == 0 ||
                     operation.UndoRecordSerialNumber == e.UndoSerialNumber))
                {
                    _transformCopies.Remove(pair.Key);
                }
            }

            if (removedSession)
                UpdateInputTimer();
        }

        private void OnRhinoObjectAdded(object sender, RhinoObjectEventArgs e)
        {
            if (_disposed || _mutating)
                return;

            var doc = sender as RhinoDoc ?? e.TheObject?.Document;
            if (doc == null ||
                !IsDocumentReady(doc.RuntimeSerialNumber) ||
                e.TheObject == null ||
                e.TheObject.IsDeleted ||
                e.TheObject.IsReference)
            {
                return;
            }

            if (!_sessions.TryGetValue(doc.RuntimeSerialNumber, out var session))
            {
                NameTransformCopyIfNeeded(doc, e.TheObject);
                return;
            }

            TryCaptureActiveGetObject(session, doc);
            if (!session.OutputIds.Contains(e.ObjectId))
                session.OutputIds.Add(e.ObjectId);

            if (session.CapturesClipboard)
                return;

            if (session.IsCopy)
            {
                QueueCopiedObject(session.CopyOperation, e.ObjectId);
                QueueCopiedGroupCandidates(
                    session.CopyOperation,
                    e.TheObject);
                ApplyPendingCopyChanges(
                    session.CopyOperation,
                    doc,
                    doc.CurrentUndoRecordSerialNumber);
                return;
            }

            if (doc.CurrentUndoRecordSerialNumber == 0)
                return;

            _mutating = true;
            try
            {
                // Copies keep attribute notes; derived outputs start without an input's note.
                if (session.TracksInputs &&
                    !string.IsNullOrEmpty(e.TheObject.Attributes.GetUserString(ObjectNote.Key)))
                {
                    var attributes = e.TheObject.Attributes.Duplicate();
                    attributes.SetUserString(ObjectNote.Key, null);
                    doc.Objects.ModifyAttributes(e.ObjectId, attributes, true);
                }
                NameSemanticOutputs(session, doc);
            }
            catch (Exception exception)
            {
                ReportErrorOnce("apply", exception);
            }
            finally
            {
                _mutating = false;
            }
        }

        private void OnObjectsSelected(
            object sender,
            RhinoObjectSelectionEventArgs e)
        {
            if (_disposed ||
                e.Document == null ||
                !IsDocumentReady(e.Document.RuntimeSerialNumber) ||
                !_sessions.TryGetValue(e.Document.RuntimeSerialNumber, out var session))
            {
                return;
            }

            if (session.IsCopy || session.CapturesClipboard)
            {
                TryCaptureInteractiveCopySources(session, e);
                return;
            }

            if (!session.TracksInputs)
                return;

            TryCaptureActiveGetObject(session, e.Document);
            try
            {
                var getObject = GetObject.ActiveGetObject(e.Document);
                if (getObject == null)
                    return;

                var ids = (e.RhinoObjects ?? Array.Empty<RhinoObject>())
                    .Where(item => item != null)
                    .Select(item => item.Id)
                    .ToList();
                AddInputObjects(
                    session,
                    e.Document,
                    getObject.SerialNumber,
                    ids);
            }
            catch (Exception exception)
            {
                ReportErrorOnce("selection-input", exception);
            }
        }

        private void OnOutlinerCommandInputObserved(
            object sender,
            OutlinerCommandInputObservedEventArgs e)
        {
            if (_disposed ||
                e == null ||
                !_sessions.TryGetValue(
                    e.DocumentSerialNumber,
                    out var session) ||
                !session.TracksInputs)
            {
                return;
            }

            var doc = RhinoDoc.FromRuntimeSerialNumber(e.DocumentSerialNumber);
            var rhinoObject = doc?.Objects.FindId(e.ObjectId);
            if (rhinoObject == null ||
                rhinoObject.IsDeleted ||
                rhinoObject.IsReference)
            {
                return;
            }

            if (e.GeometryFilter != ObjectType.AnyObject &&
                e.GeometryFilter != ObjectType.None &&
                (rhinoObject.ObjectType & e.GeometryFilter) == 0)
            {
                return;
            }

            AddInputObjects(
                session,
                doc,
                e.GetterSerialNumber,
                new[] { e.ObjectId });
        }

        private void OnCopyAttributesModified(object sender, RhinoModifyObjectAttributesEventArgs e)
        {
            if (_disposed || _mutating || e.Document == null || e.RhinoObject == null)
                return;
            var serial = e.Document.RuntimeSerialNumber;
            _sessions.TryGetValue(serial, out var session);
            _transformCopies.TryGetValue(serial, out var transform);
            var commandCopy = session?.CopyOperation;
            var inCommandCopy = commandCopy?.ObservedObjectIds.Contains(e.RhinoObject.Id) == true;
            var inTransformCopy = transform?.ObservedObjectIds.Contains(e.RhinoObject.Id) == true;
            if (!inCommandCopy && !inTransformCopy)
                return;
            var oldGroups = e.OldAttributes?.GetGroupList() ?? Array.Empty<int>();
            var newGroups = e.NewAttributes?.GetGroupList() ?? Array.Empty<int>();
            if (new HashSet<int>(oldGroups).SetEquals(newGroups))
                return;
            if (inCommandCopy)
                commandCopy.GroupRevision.Changed();
            if (inTransformCopy)
                transform.GroupRevision.Changed();
        }

        private void OnGroupTableChanged(object sender, GroupTableEventArgs e)
        {
            if (_disposed ||
                _mutating ||
                e.Document == null ||
                !IsDocumentReady(e.Document.RuntimeSerialNumber) ||
                (e.EventType != GroupTableEventType.Added &&
                 e.EventType != GroupTableEventType.Modified))
            {
                return;
            }

            try
            {
                if (_sessions.TryGetValue(
                    e.Document.RuntimeSerialNumber,
                    out var session) &&
                    session.IsCopy &&
                    session.CopyOperation != null)
                {
                    session.CopyOperation.PendingGroupIndices.Add(e.GroupIndex);
                    session.CopyOperation.GroupRevision.Changed();
                    ApplyPendingCopyChanges(
                        session.CopyOperation,
                        e.Document,
                        e.Document.CurrentUndoRecordSerialNumber);
                    return;
                }

                if (_transformCopies.TryGetValue(
                    e.Document.RuntimeSerialNumber,
                    out var transformSession))
                {
                    transformSession.PendingGroupIndices.Add(e.GroupIndex);
                    transformSession.GroupRevision.Changed();
                    ApplyPendingCopyChanges(
                        transformSession,
                        e.Document,
                        e.Document.CurrentUndoRecordSerialNumber);
                }
            }
            catch (Exception exception)
            {
                ReportErrorOnce("group-copy", exception);
            }
        }

        private void OnBeforeTransformObjects(
            object sender,
            RhinoTransformObjectsEventArgs e)
        {
            var sourceObjects = (e.Objects ?? Array.Empty<RhinoObject>())
                .Where(source => source != null)
                .ToList();
            var doc = sender as RhinoDoc ??
                sourceObjects.Select(source => source.Document)
                    .FirstOrDefault(candidate => candidate != null);
            if (_disposed ||
                doc == null ||
                !IsDocumentReady(doc.RuntimeSerialNumber))
                return;

            if (!e.ObjectsWillBeCopied)
            {
                _presentationTransforms[e.TransformEventId.ToString()] = sourceObjects;
                foreach (var source in sourceObjects)
                    ObjectPresentation.Invalidate(source);
                _transformCopies.Remove(doc.RuntimeSerialNumber);
                return;
            }

            try
            {
                var sourceIds = sourceObjects
                    .Where(source =>
                        !source.IsDeleted &&
                        !source.IsReference)
                    .Select(source => source.Id)
                    .ToList();
                var transformSession = CreateCopyOperation(
                    doc,
                    "Copy",
                    sourceIds,
                    e.TransformEventId.ToString());
                if (doc.CurrentUndoRecordSerialNumber != 0)
                {
                    transformSession.UndoRecordSerialNumber =
                        doc.CurrentUndoRecordSerialNumber;
                }

                if (transformSession.Sources.Count > 0)
                    _transformCopies[doc.RuntimeSerialNumber] = transformSession;
            }
            catch (Exception exception)
            {
                ReportErrorOnce("transform-copy-start", exception);
            }
        }

        private void OnAfterTransformObjects(
            object sender,
            RhinoAfterTransformObjectsEventArgs e)
        {
            var transformEventId = e.TransformEventId.ToString();
            if (_presentationTransforms.TryGetValue(transformEventId, out var transformed))
            {
                _presentationTransforms.Remove(transformEventId);
                foreach (var source in transformed)
                    ObjectPresentation.Invalidate(source);
            }
            var doc = sender as RhinoDoc;
            CopyOperationSession session = null;
            if (doc != null)
            {
                _transformCopies.TryGetValue(
                    doc.RuntimeSerialNumber,
                    out session);
            }
            if (session == null ||
                !string.Equals(
                    session.TransformEventId,
                    transformEventId,
                    StringComparison.Ordinal))
            {
                session = _transformCopies.Values.FirstOrDefault(candidate =>
                    string.Equals(
                        candidate.TransformEventId,
                        transformEventId,
                        StringComparison.Ordinal));
                doc = session == null
                    ? null
                    : RhinoDoc.FromRuntimeSerialNumber(
                        session.DocumentSerialNumber);
            }

            if (doc == null ||
                session == null ||
                !IsDocumentReady(doc.RuntimeSerialNumber))
            {
                return;
            }

            if (!session.CompletionObserved)
                session.GroupRevision.Changed();
            session.CompletionObserved = true;
            ApplyPendingCopyChanges(
                session,
                doc,
                doc.CurrentUndoRecordSerialNumber);
            ScheduleTransformCopyCompletion(
                doc.RuntimeSerialNumber,
                session.TransformEventId,
                2);
        }

        private void ScheduleTransformCopyCompletion(
            uint documentSerialNumber,
            string transformEventId,
            int remainingTurns)
        {
            Application.Instance.AsyncInvoke(() =>
            {
                if (_disposed ||
                    !_transformCopies.TryGetValue(
                        documentSerialNumber,
                        out var session) ||
                    !string.Equals(
                        session.TransformEventId,
                        transformEventId,
                        StringComparison.Ordinal))
                {
                    return;
                }

                var doc = RhinoDoc.FromRuntimeSerialNumber(documentSerialNumber);
                if (doc != null)
                {
                    try
                    {
                        ApplyPendingCopyChanges(
                            session,
                            doc,
                            doc.CurrentUndoRecordSerialNumber);
                    }
                    catch (Exception exception)
                    {
                        ReportErrorOnce("transform-group-copy", exception);
                    }
                }

                if (remainingTurns > 0)
                {
                    ScheduleTransformCopyCompletion(
                        documentSerialNumber,
                        transformEventId,
                        remainingTurns - 1);
                }
                else
                {
                    if (!HasPendingCopyWork(session))
                        _transformCopies.Remove(documentSerialNumber);
                }
            });
        }

        private void NameTransformCopyIfNeeded(
            RhinoDoc doc,
            RhinoObject copiedObject)
        {
            if (_mutating ||
                !_transformCopies.TryGetValue(
                    doc.RuntimeSerialNumber,
                    out var transformSession))
            {
                return;
            }

            try
            {
                QueueCopiedObject(transformSession, copiedObject.Id);
                QueueCopiedGroupCandidates(transformSession, copiedObject);
                ApplyPendingCopyChanges(
                    transformSession,
                    doc,
                    doc.CurrentUndoRecordSerialNumber);
            }
            catch (Exception exception)
            {
                ReportErrorOnce("transform-copy", exception);
            }
        }

        private void OnInputTimerElapsed(object sender, EventArgs e)
        {
            if (_disposed)
                return;

            foreach (var session in _sessions.Values
                .Where(candidate => candidate.TracksInputs)
                .ToList())
            {
                var doc = RhinoDoc.FromRuntimeSerialNumber(
                    session.DocumentSerialNumber);
                if (doc != null && IsDocumentReady(session.DocumentSerialNumber))
                    TryCaptureActiveGetObject(session, doc);
            }
        }

        private void OnDocumentOpening(
            object sender,
            DocumentLoadStateEventArgs e)
        {
            _sessions.Remove(e.DocumentSerialNumber);
            _transformCopies.Remove(e.DocumentSerialNumber);
            _copyNames.Remove(e.DocumentSerialNumber);
            ObjectPresentation.ClearCache(RhinoDoc.FromRuntimeSerialNumber(e.DocumentSerialNumber));
            UpdateInputTimer();
        }

        private bool IsDocumentReady(uint documentSerialNumber)
        {
            return _documentLoadMonitor == null ||
                _documentLoadMonitor.IsReady(documentSerialNumber);
        }

        private void TryCaptureActiveGetObject(
            NamingCommandSession session,
            RhinoDoc doc)
        {
            try
            {
                CaptureActiveGetObject(session, doc);
            }
            catch (Exception exception)
            {
                ReportErrorOnce("input", exception);
            }
        }

        private void CaptureActiveGetObject(
            NamingCommandSession session,
            RhinoDoc doc)
        {
            if (!session.TracksInputs)
                return;

            var getObject = GetObject.ActiveGetObject(doc);
            if (getObject == null)
                return;

            var existingStage = session.InputStages.FirstOrDefault(
                candidate => candidate.SerialNumber == getObject.SerialNumber);
            if (existingStage == null &&
                session.InputStages.Count == 0 &&
                session.PreselectedObjectIds.Count > 0 &&
                !getObject.ObjectsWerePreselected &&
                string.Equals(
                    session.CommandName,
                    "Sweep1",
                    StringComparison.OrdinalIgnoreCase))
            {
                AddInputObjects(
                    session,
                    doc,
                    0,
                    session.PreselectedObjectIds);
            }

            session.GetOrCreateStage(getObject.SerialNumber);
            if (getObject.ObjectsWerePreselected && session.InputStages.Count == 1)
            {
                AddInputObjects(
                    session,
                    doc,
                    getObject.SerialNumber,
                    session.PreselectedObjectIds);
            }

            var objectIds = new List<Guid>();
            for (var index = 0; index < getObject.ObjectCount; index++)
            {
                var objectReference = getObject.Object(index);
                if (objectReference != null)
                    objectIds.Add(objectReference.ObjectId);
            }
            AddInputObjects(
                session,
                doc,
                getObject.SerialNumber,
                objectIds);
        }

        private static void AddInputObjects(
            NamingCommandSession session,
            RhinoDoc doc,
            uint stageSerialNumber,
            IEnumerable<Guid> objectIds)
        {
            if (session == null || doc == null)
                return;

            var ids = (objectIds ?? Enumerable.Empty<Guid>())
                .Where(id => id != Guid.Empty)
                .Distinct()
                .ToList();
            if (ids.Count == 0)
                return;

            session.GetOrCreateStage(stageSerialNumber).Add(ids);
            CaptureInputObjects(session, doc, ids);
        }

        private static void CaptureInputObjects(
            NamingCommandSession session,
            RhinoDoc doc,
            IEnumerable<Guid> objectIds)
        {
            if (session == null || doc == null)
                return;

            foreach (var objectId in objectIds ?? Enumerable.Empty<Guid>())
            {
                if (objectId == Guid.Empty ||
                    session.InputObjects.ContainsKey(objectId))
                {
                    continue;
                }

                var rhinoObject = doc.Objects.FindId(objectId);
                if (rhinoObject == null ||
                    rhinoObject.IsDeleted ||
                    rhinoObject.IsReference)
                {
                    continue;
                }

                ObjectNamingService.TryGetInheritableRoot(
                    rhinoObject,
                    out var rootName);
                session.InputObjects[objectId] = new InputObjectSnapshot
                {
                    Id = objectId,
                    ObjectType = rhinoObject.ObjectType,
                    RootName = rootName ?? string.Empty,
                    DisplayName = rhinoObject.Attributes.Name ?? string.Empty
                };
            }
        }

        private CopyOperationSession CreateCopyOperation(
            RhinoDoc doc,
            string commandName,
            IEnumerable<Guid> sourceIds,
            string transformEventId = null)
        {
            var operation = new CopyOperationSession(
                doc.RuntimeSerialNumber,
                commandName,
                transformEventId);
            var sourceIdList = (sourceIds ?? Enumerable.Empty<Guid>())
                .Where(id => id != Guid.Empty)
                .Distinct()
                .ToList();
            CaptureCopySources(operation.Sources, doc, sourceIdList);
            CaptureCopySourceGroups(operation.SourceGroups, doc, sourceIdList);
            return operation;
        }

        private static CopyOperationSession CloneCopyOperation(
            CopyOperationSession source,
            uint documentSerialNumber,
            string commandName,
            bool requiresStoredNameMatch = false)
        {
            var clone = new CopyOperationSession(
                documentSerialNumber,
                commandName,
                source.TransformEventId);
            clone.SourceDocumentSerialNumber = source.SourceDocumentSerialNumber;
            clone.RequiresStoredNameMatch = requiresStoredNameMatch;
            foreach (var item in source.Sources.OrderBy(item => item.Ordinal))
            {
                clone.Sources.Add(new CopyObjectSource
                {
                    Ordinal = item.Ordinal,
                    Id = item.Id,
                    BaseName = item.BaseName,
                    StoredName = item.StoredName,
                    ObjectType = item.ObjectType,
                    UseGenericName = item.UseGenericName,
                    LineageRoot = item.LineageRoot
                });
            }

            foreach (var group in source.SourceGroups.OrderBy(item => item.Index))
            {
                clone.SourceGroups.Add(new CopyGroupSource
                {
                    Index = group.Index,
                    Id = group.Id,
                    DisplayName = group.DisplayName,
                    HasUserName = group.HasUserName,
                    MemberIds = group.MemberIds.ToList(),
                    CopiedMemberIds = group.CopiedMemberIds.ToList(),
                    PreserveOnCopy = group.PreserveOnCopy,
                    ExpansionByLayerPath = new Dictionary<string, bool>(
                        group.ExpansionByLayerPath,
                        StringComparer.OrdinalIgnoreCase)
                });
            }
            return clone;
        }

        private void TryCaptureInteractiveCopySources(
            NamingCommandSession session,
            RhinoObjectSelectionEventArgs e)
        {
            if (session.CopyOperation == null ||
                session.OutputIds.Count > 0 ||
                string.Equals(
                    session.CommandName,
                    "Paste",
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var sourceIds = (e.RhinoObjects ?? Array.Empty<RhinoObject>())
                .Where(item =>
                    item != null &&
                    !item.IsDeleted &&
                    !item.IsReference)
                .Select(item => item.Id)
                .Distinct()
                .ToList();
            if (sourceIds.Count == 0)
                return;

            sourceIds = session.CopyOperation.Sources
                .Select(item => item.Id)
                .Concat(sourceIds)
                .Distinct()
                .ToList();
            session.CopyOperation = CreateCopyOperation(
                e.Document,
                session.CommandName,
                sourceIds);
        }

        private static void QueueCopiedObject(
            CopyOperationSession operation,
            Guid objectId)
        {
            if (operation == null ||
                objectId == Guid.Empty ||
                !operation.ObservedObjectIds.Add(objectId))
            {
                return;
            }

            operation.PendingObjectIds.Add(objectId);
            operation.GroupRevision.Changed();
        }

        private static void QueueCopiedGroupCandidates(
            CopyOperationSession operation,
            RhinoObject copiedObject)
        {
            if (operation == null ||
                copiedObject == null ||
                operation.SourceGroups.Count == 0)
            {
                return;
            }

            foreach (var groupIndex in copiedObject.Attributes.GetGroupList() ??
                Array.Empty<int>())
            {
                if (groupIndex >= 0)
                    operation.PendingGroupIndices.Add(groupIndex);
            }
        }

        private void ApplyPendingCopyChanges(
            CopyOperationSession operation,
            RhinoDoc doc,
            uint undoRecordSerialNumber)
        {
            if (operation == null ||
                doc == null ||
                !EnsureActiveCopyUndo(
                    operation,
                    doc,
                    undoRecordSerialNumber))
            {
                return;
            }

            using (var diagnostic = OutlinerDiagnosticScope.Start("copy-processing", doc.RuntimeSerialNumber))
            {
                diagnostic?.Metrics.Detail("observedObjects", operation.ObservedObjectIds.Count);
                diagnostic?.Metrics.Detail("sourceGroups", operation.SourceGroups.Count);
                var timer = Stopwatch.StartNew();
                var wasMutating = _mutating;
                _mutating = true;
                try
                {
                    var reconcileGroups = operation.GroupRevision.NeedsReconciliation;
                    var groupRevision = operation.GroupRevision.Current;
                    if (reconcileGroups)
                        RefreshCopiedGroupCandidates(operation, doc);
                    foreach (var objectId in ReadyCopiedObjectIds(operation))
                    {
                        var copiedObject = doc.Objects.FindId(objectId);
                        if (copiedObject != null &&
                            !copiedObject.IsDeleted &&
                            !copiedObject.IsReference)
                        {
                            TrackAndNameCopiedObject(
                                operation,
                                doc,
                                copiedObject);
                        }
                        operation.PendingObjectIds.Remove(objectId);
                    }

                    if (reconcileGroups)
                    {
                        RefreshCopiedGroupCandidates(operation, doc);
                        ReconcileCopyBatches(operation, doc);
                        operation.GroupRevision.Reconciled(groupRevision);
                    }
                    diagnostic?.Metrics.Detail("groupReconciled", reconcileGroups ? 1 : 0);
                    TryFinalizeCopyGroups(
                        operation,
                        doc,
                        undoRecordSerialNumber);
                }
                catch (Exception exception)
                {
                    ReportErrorOnce("copy-finalize", exception);
                }
                finally
                {
                    _mutating = wasMutating;
                    if (OutlinerDiagnosticMode.IsEnabled && timer.ElapsedMilliseconds >= 50)
                        RhinoApp.WriteLine("Object Outliner perf [copy]: total={0} ms, observed={1}, groups={2}",
                            timer.ElapsedMilliseconds, operation.ObservedObjectIds.Count, operation.SourceGroups.Count);
                }
            }
        }

        private static bool HasPendingCopyWork(CopyOperationSession operation)
        {
            return operation != null &&
                (operation.PendingObjectIds.Count > 0 ||
                 operation.PendingGroupIndices.Count > 0);
        }

        private static IList<Guid> ReadyCopiedObjectIds(
            CopyOperationSession operation)
        {
            if (operation == null || operation.PendingObjectIds.Count == 0)
                return Array.Empty<Guid>();

            var sourceCount = operation.Sources.Count;
            if (sourceCount == 0)
                return operation.PendingObjectIds.ToList();

            var completeObjectCount =
                operation.ObservedObjectIds.Count / sourceCount * sourceCount;
            var readyCount = Math.Max(
                0,
                completeObjectCount - operation.AddedObjectIds.Count);
            return operation.PendingObjectIds.Take(readyCount).ToList();
        }

        private static void RefreshCopiedGroupCandidates(
            CopyOperationSession operation,
            RhinoDoc doc)
        {
            if (operation == null ||
                doc == null ||
                operation.SourceGroups.Count == 0)
            {
                return;
            }

            foreach (var objectId in operation.ObservedObjectIds)
            {
                var copiedObject = doc.Objects.FindId(objectId);
                if (copiedObject == null ||
                    copiedObject.IsDeleted ||
                    copiedObject.IsReference)
                {
                    continue;
                }

                QueueCopiedGroupCandidates(operation, copiedObject);
            }
        }

        private void TrackAndNameCopiedObject(
            CopyOperationSession operation,
            RhinoDoc doc,
            RhinoObject copiedObject)
        {
            if (operation == null ||
                doc == null ||
                copiedObject == null ||
                !operation.AddedObjectIds.Add(copiedObject.Id))
            {
                return;
            }

            var source = AssignCopySource(operation, doc, copiedObject);
            var useGenericName = source == null
                ? ObjectNamingService.UsesGenericCopyNaming(copiedObject)
                : source.UseGenericName;
            var sourceName = source == null
                ? useGenericName
                    ? ObjectNamingService.GetGenericNameBase(doc, copiedObject)
                    : ObjectNamingService.GetCopySourceBase(doc, copiedObject)
                : source.BaseName;
            var lineageRoot = source?.LineageRoot;
            if (string.IsNullOrWhiteSpace(lineageRoot) &&
                !useGenericName &&
                ObjectNamingService.TryGetInheritableRoot(
                    copiedObject,
                    out var copiedRoot))
            {
                lineageRoot = copiedRoot;
            }
            var copyName = GetOrCreateGenericNameAllocator(operation, doc)
                .Next(useGenericName ? sourceName : OutlinerStrings.AppendCopySuffix(sourceName));
            if (ObjectNamingService.TryApplyManagedName(
                doc,
                copiedObject.Id,
                copyName,
                "Copy",
                0,
                useGenericName
                    ? ObjectNamingService.GenericCopyRole
                    : string.Empty,
                false,
                true,
                lineageRoot))
            {
                operation.ReservedNames.Add(copyName);
            }
        }

        private GenericObjectNameAllocator GetOrCreateGenericNameAllocator(
            CopyOperationSession operation,
            RhinoDoc doc)
        {
            if (operation.GenericNameAllocator == null)
            {
                var undoSerial = doc.CurrentUndoRecordSerialNumber;
                if (undoSerial != 0 &&
                    _copyNames.TryGetValue(doc.RuntimeSerialNumber, out var cached) &&
                    cached.UndoSerial == undoSerial)
                {
                    operation.GenericNameAllocator = cached.Allocator;
                    return cached.Allocator;
                }

                using (var diagnostic = OutlinerDiagnosticScope.Start("copy-name-initialize", doc.RuntimeSerialNumber))
                {
                    var timer = Stopwatch.StartNew();
                    operation.GenericNameAllocator =
                        ObjectNamingService.CreateGenericNameAllocator(
                            doc,
                            operation.ObservedObjectIds,
                            operation.ReservedNames);
                    if (undoSerial != 0)
                        _copyNames[doc.RuntimeSerialNumber] = new CopyNameCache
                        {
                            UndoSerial = undoSerial,
                            Allocator = operation.GenericNameAllocator
                        };
                    if (OutlinerDiagnosticMode.IsEnabled)
                        RhinoApp.WriteLine("Object Outliner perf [copy-names]: initialize={0} ms", timer.ElapsedMilliseconds);
                }
            }

            return operation.GenericNameAllocator;
        }

        private static void CaptureCopySources(
            IList<CopyObjectSource> destination,
            RhinoDoc doc,
            IEnumerable<Guid> sourceIds)
        {
            foreach (var sourceId in sourceIds.Distinct())
            {
                var source = doc.Objects.FindId(sourceId);
                if (source == null || source.IsDeleted || source.IsReference)
                    continue;

                var useGenericName =
                    ObjectNamingService.UsesGenericCopyNaming(source);
                ObjectNamingService.TryGetInheritableRoot(
                    source,
                    out var lineageRoot);
                destination.Add(new CopyObjectSource
                {
                    Ordinal = destination.Count,
                    Id = source.Id,
                    BaseName = useGenericName
                        ? ObjectNamingService.GetGenericNameBase(doc, source)
                        : ObjectNamingService.GetCopySourceBase(doc, source),
                    StoredName = source.Attributes.Name ?? string.Empty,
                    ObjectType = source.ObjectType,
                    UseGenericName = useGenericName,
                    LineageRoot = lineageRoot ?? string.Empty
                });
            }
        }

        private static CopyObjectSource AssignCopySource(
            CopyOperationSession operation,
            RhinoDoc doc,
            RhinoObject copiedObject)
        {
            var candidates = operation.Sources
                .Where(candidate => candidate.ObjectType == copiedObject.ObjectType)
                .OrderBy(candidate => candidate.Ordinal)
                .ToList();
            if (candidates.Count == 0)
                return null;

            var copiedName = copiedObject.Attributes.Name ?? string.Empty;
            var matchingName = candidates.Where(candidate =>
                string.Equals(
                    candidate.StoredName,
                    copiedName,
                    StringComparison.Ordinal)).ToList();
            if (matchingName.Count > 0)
                candidates = matchingName;
            else if (operation.RequiresStoredNameMatch)
                return null;

            // Without group reconstruction, a unique source needs no batch history.
            if (operation.SourceGroups.Count == 0 && candidates.Count == 1)
                return candidates[0];

            var rankedIds = CopyMembershipMatcher.RankByMembership(
                candidates.Select(candidate =>
                    new CopyMembershipCandidate
                    {
                        Id = candidate.Id,
                        Ordinal = candidate.Ordinal,
                        GroupSizes = SourceMembershipSignature(
                            operation,
                            candidate.Id)
                    }),
                CopiedMembershipSignature(operation, doc, copiedObject));
            var candidatesById = candidates.ToDictionary(
                candidate => candidate.Id);
            candidates = rankedIds
                .Select(id => candidatesById[id])
                .ToList();

            foreach (var batch in operation.Batches.OrderBy(item => item.Index))
            {
                var available = candidates.Where(candidate =>
                    !batch.ObjectMap.ContainsKey(candidate.Id));
                var source = available.FirstOrDefault();
                if (source == null)
                    continue;

                batch.ObjectMap[source.Id] = copiedObject.Id;
                batch.CopiedObjectIds.Add(copiedObject.Id);
                batch.OriginalNames[copiedObject.Id] = copiedName;
                return source;
            }

            var newBatch = new CopyBatch { Index = operation.Batches.Count };
            var firstSource = candidates[0];
            newBatch.ObjectMap[firstSource.Id] = copiedObject.Id;
            newBatch.CopiedObjectIds.Add(copiedObject.Id);
            newBatch.OriginalNames[copiedObject.Id] = copiedName;
            operation.Batches.Add(newBatch);
            return firstSource;
        }

        private static IReadOnlyCollection<int> SourceMembershipSignature(
            CopyOperationSession operation,
            Guid sourceId)
        {
            return operation.SourceGroups
                .Where(group => group.CopiedMemberIds.Contains(sourceId))
                .Select(group => group.CopiedMemberIds.Count)
                .OrderBy(size => size)
                .ToList();
        }

        private static IReadOnlyCollection<int> CopiedMembershipSignature(
            CopyOperationSession operation,
            RhinoDoc doc,
            RhinoObject copiedObject)
        {
            var groupSizes = new List<int>();
            foreach (var groupIndex in copiedObject.Attributes.GetGroupList() ??
                Array.Empty<int>())
            {
                var members = doc.Groups.GroupMembers(groupIndex) ??
                    Array.Empty<RhinoObject>();
                var copiedMemberCount = members.Count(member =>
                    member != null &&
                    !member.IsDeleted &&
                    operation.ObservedObjectIds.Contains(member.Id));
                if (copiedMemberCount > 0)
                    groupSizes.Add(copiedMemberCount);
            }

            groupSizes.Sort();
            return groupSizes;
        }

        private static void ReconcileCopyBatches(
            CopyOperationSession operation,
            RhinoDoc doc)
        {
            if (operation == null ||
                doc == null ||
                operation.SourceGroups.Count == 0)
            {
                return;
            }

            foreach (var batch in operation.Batches
                .Where(item => item.IsComplete(operation.Sources.Count)))
            {
                var sources = operation.Sources.Select(source =>
                    new CopyMembershipCandidate
                    {
                        Id = source.Id,
                        Ordinal = source.Ordinal,
                        MatchKey = CopyMatchKey(
                            source.ObjectType,
                            source.StoredName),
                        GroupSizes = SourceMembershipSignature(
                            operation,
                            source.Id)
                    }).ToList();
                var copies = new List<CopyMembershipCandidate>();
                for (var index = 0; index < batch.CopiedObjectIds.Count; index++)
                {
                    var copiedId = batch.CopiedObjectIds[index];
                    var copiedObject = doc.Objects.FindId(copiedId);
                    if (copiedObject == null || copiedObject.IsDeleted)
                        continue;

                    batch.OriginalNames.TryGetValue(
                        copiedId,
                        out var originalName);
                    copies.Add(new CopyMembershipCandidate
                    {
                        Id = copiedId,
                        Ordinal = index,
                        MatchKey = CopyMatchKey(
                            copiedObject.ObjectType,
                            originalName),
                        GroupSizes = CopiedMembershipSignature(
                            operation,
                            doc,
                            copiedObject)
                    });
                }

                var reconciled = CopyMembershipMatcher.Reconcile(
                    sources,
                    copies,
                    batch.ObjectMap);
                if (reconciled.Count != batch.ObjectMap.Count)
                    continue;

                batch.ObjectMap.Clear();
                foreach (var pair in reconciled)
                    batch.ObjectMap[pair.Key] = pair.Value;
            }
        }

        private static string CopyMatchKey(
            ObjectType objectType,
            string storedName)
        {
            return string.Format(
                "{0}\u001f{1}",
                (int)objectType,
                storedName ?? string.Empty);
        }

        private void CaptureCopySourceGroups(
            IList<CopyGroupSource> destination,
            RhinoDoc doc,
            IEnumerable<Guid> selectedObjectIds)
        {
            var selected = new HashSet<Guid>(selectedObjectIds);
            if (selected.Count == 0)
                return;

            var expansionState = _relationshipHub?.GetGroupExpansionState(
                doc.RuntimeSerialNumber) ??
                new Dictionary<OutlinerGroupSliceKey, bool>();

            var timer = Stopwatch.StartNew();
            var groupIndices = selected
                .Select(id => doc.Objects.FindId(id))
                .Where(obj => obj != null)
                .SelectMany(obj => obj.Attributes.GetGroupList() ?? Array.Empty<int>())
                .Distinct()
                .OrderBy(index => index);
            foreach (var index in groupIndices)
            {
                var group = doc.Groups.FindIndex(index);
                if (group == null || group.IsDeleted)
                    continue;

                var members = doc.Groups.GroupMembers(index) ??
                    Array.Empty<RhinoObject>();
                var memberIds = members
                    .Where(item => item != null && !item.IsDeleted)
                    .Select(item => item.Id)
                    .Distinct()
                    .ToList();
                var copiedMemberIds = memberIds
                    .Where(selected.Contains)
                    .ToList();
                if (copiedMemberIds.Count == 0)
                    continue;

                var nativeName = doc.Groups.GroupName(index);
                var hasUserName = !string.IsNullOrWhiteSpace(nativeName);
                var name = ObjectNamingService.ResolveGroupDisplayName(
                    doc,
                    group,
                    OutlinerStrings.FormatDefaultGroupName(index + 1));
                var expansionByLayerPath = new Dictionary<string, bool>(
                    StringComparer.OrdinalIgnoreCase);
                foreach (var layerIndex in members
                    .Where(item => item != null && !item.IsDeleted)
                    .Select(item => item.Attributes.LayerIndex)
                    .Distinct())
                {
                    var layer = doc.Layers[layerIndex];
                    if (layer == null || layer.IsDeleted)
                        continue;

                    var key = new OutlinerGroupSliceKey(group.Id, layer.Id);
                    var expanded = !expansionState.TryGetValue(
                        key,
                        out var storedExpanded) || storedExpanded;
                    expansionByLayerPath[layer.FullPath ?? layer.Name] = expanded;
                }
                destination.Add(new CopyGroupSource
                {
                    Index = index,
                    Id = group.Id,
                    DisplayName = name,
                    HasUserName = hasUserName,
                    MemberIds = memberIds,
                    CopiedMemberIds = copiedMemberIds,
                    PreserveOnCopy = copiedMemberIds.Count == memberIds.Count,
                    ExpansionByLayerPath = expansionByLayerPath
                });
            }
            if (OutlinerDiagnosticMode.IsEnabled && timer.ElapsedMilliseconds >= 10)
                RhinoApp.WriteLine("Object Outliner perf [copy-groups]: capture={0} ms, groups={1}",
                    timer.ElapsedMilliseconds, destination.Count);
        }

        private void TryFinalizeCopyGroups(
            CopyOperationSession operation,
            RhinoDoc doc,
            uint undoRecordSerialNumber = 0)
        {
            if (operation == null ||
                doc == null ||
                operation.SourceGroups.Count == 0 ||
                operation.PendingGroupIndices.Count == 0 ||
                !EnsureActiveCopyUndo(
                    operation,
                    doc,
                    undoRecordSerialNumber))
            {
                return;
            }

            var expectations = new List<CopyGroupExpectation>();
            foreach (var batch in operation.Batches
                .Where(item => item.IsComplete(operation.Sources.Count))
                .OrderBy(item => item.Index))
            {
                foreach (var sourceGroup in operation.SourceGroups
                    .OrderBy(item => item.Index))
                {
                    var key = batch.Index + ":" + sourceGroup.Index;
                    if (operation.MatchedExpectationKeys.Contains(key))
                        continue;

                    var mappedIds = new List<Guid>();
                    foreach (var sourceMemberId in sourceGroup.CopiedMemberIds)
                    {
                        if (!batch.ObjectMap.TryGetValue(
                            sourceMemberId,
                            out var copiedMemberId))
                        {
                            mappedIds.Clear();
                            break;
                        }
                        mappedIds.Add(copiedMemberId);
                    }

                    if (mappedIds.Count == sourceGroup.CopiedMemberIds.Count)
                    {
                        expectations.Add(new CopyGroupExpectation
                        {
                            Key = key,
                            SourceGroupIndex = sourceGroup.Index,
                            BatchIndex = batch.Index,
                            Preserve = sourceGroup.PreserveOnCopy,
                            MemberIds = mappedIds
                        });
                    }
                }
            }

            if (expectations.Count == 0)
                return;

            var candidates = new List<CopyGroupCandidate>();
            foreach (var groupIndex in operation.PendingGroupIndices.ToList())
            {
                var group = doc.Groups.FindIndex(groupIndex);
                if (group == null || group.IsDeleted)
                {
                    operation.PendingGroupIndices.Remove(groupIndex);
                    continue;
                }

                var members = doc.Groups.GroupMembers(groupIndex) ??
                    Array.Empty<RhinoObject>();
                var copiedMemberIds = members
                    .Where(item => item != null && !item.IsDeleted)
                    .Select(item => item.Id)
                    .Distinct()
                    .ToList();
                if (copiedMemberIds.Count > 0)
                {
                    candidates.Add(new CopyGroupCandidate
                    {
                        GroupIndex = groupIndex,
                        MemberIds = copiedMemberIds
                    });
                }
            }

            var sourceGroupsByIndex = operation.SourceGroups.ToDictionary(
                item => item.Index);
            var matches = CopyGroupMatcher.Match(expectations, candidates);
            var copiedRelations = new List<OutlinerCopiedGroupRelation>();
            var wasMutating = _mutating;
            _mutating = true;
            try
            {
                foreach (var match in matches)
                {
                    if (!sourceGroupsByIndex.TryGetValue(
                        match.Expectation.SourceGroupIndex,
                        out var sourceGroup))
                    {
                        continue;
                    }

                    var applied = false;
                    if (sourceGroup.PreserveOnCopy)
                    {
                        var copyName = NextGroupCopyName(
                            doc,
                            sourceGroup.DisplayName,
                            match.Candidate.GroupIndex);
                        var targetGroup = doc.Groups.FindIndex(
                            match.Candidate.GroupIndex);
                        if (targetGroup != null && !targetGroup.IsDeleted)
                        {
                            if (sourceGroup.HasUserName)
                            {
                                var currentName = doc.Groups.GroupName(
                                    match.Candidate.GroupIndex);
                                var alreadyNamed = string.Equals(
                                    currentName,
                                    copyName,
                                    StringComparison.OrdinalIgnoreCase);
                                applied = alreadyNamed ||
                                    doc.Groups.ChangeGroupName(
                                        match.Candidate.GroupIndex,
                                        copyName);
                                if (applied)
                                {
                                    ObjectManagerPlugin.Instance?.NameStore
                                        ?.SetGroup(
                                            doc,
                                            targetGroup.Id,
                                            copyName,
                                            OutlinerNameOwnership
                                                .UserDerivedNative,
                                            "Copy",
                                            0,
                                            string.Empty,
                                            sourceGroup.DisplayName);
                                }
                            }
                            else
                            {
                                applied = ObjectManagerPlugin.Instance
                                    ?.NameStore?.SetGroup(
                                        doc,
                                        targetGroup.Id,
                                        copyName,
                                        OutlinerNameOwnership.ManagedAlias,
                                        "Copy",
                                        0,
                                        string.Empty,
                                        string.Empty) == true;
                            }
                        }
                    }
                    else
                        applied = doc.Groups.Delete(match.Candidate.GroupIndex);

                    if (!applied)
                        continue;

                    operation.MatchedExpectationKeys.Add(
                        match.Expectation.Key);
                    operation.PendingGroupIndices.Remove(
                        match.Candidate.GroupIndex);
                    if (sourceGroup.PreserveOnCopy)
                    {
                        var targetGroup = doc.Groups.FindIndex(
                            match.Candidate.GroupIndex);
                        if (targetGroup != null && !targetGroup.IsDeleted)
                        {
                            copiedRelations.Add(new OutlinerCopiedGroupRelation
                            {
                                SourceGroupId = sourceGroup.Id,
                                SourceGroupIndex = sourceGroup.Index,
                                TargetGroupId = targetGroup.Id,
                                TargetGroupIndex = targetGroup.Index,
                                BatchIndex = match.Expectation.BatchIndex,
                                ExpansionByTargetLayerId =
                                    ResolveCopiedGroupExpansion(
                                        doc,
                                        targetGroup.Index,
                                        sourceGroup.ExpansionByLayerPath)
                            });
                        }
                    }
                }
            }
            finally
            {
                _mutating = wasMutating;
            }
            _relationshipHub?.ReportGroupsCopied(
                operation.SourceDocumentSerialNumber,
                operation.DocumentSerialNumber,
                copiedRelations);
        }

        private static IDictionary<Guid, bool> ResolveCopiedGroupExpansion(
            RhinoDoc doc,
            int targetGroupIndex,
            IDictionary<string, bool> sourceExpansionByLayerPath)
        {
            var result = new Dictionary<Guid, bool>();
            var sourceState = sourceExpansionByLayerPath ??
                new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            var onlySourceState = sourceState.Count == 1
                ? sourceState.Values.First()
                : true;
            foreach (var layerIndex in (doc?.Groups.GroupMembers(
                    targetGroupIndex) ?? Array.Empty<RhinoObject>())
                .Where(item => item != null && !item.IsDeleted)
                .Select(item => item.Attributes.LayerIndex)
                .Distinct())
            {
                var layer = doc.Layers[layerIndex];
                if (layer == null || layer.IsDeleted)
                    continue;

                var path = layer.FullPath ?? layer.Name;
                result[layer.Id] = sourceState.TryGetValue(
                    path,
                    out var expanded)
                        ? expanded
                        : onlySourceState;
            }
            return result;
        }

        private static bool EnsureActiveCopyUndo(
            CopyOperationSession operation,
            RhinoDoc doc,
            uint undoRecordSerialNumber = 0)
        {
            var activeUndo = doc.CurrentUndoRecordSerialNumber;
            var candidateUndo = activeUndo != 0
                ? activeUndo
                : undoRecordSerialNumber;
            if (candidateUndo == 0)
                return false;

            if (operation.UndoRecordSerialNumber == 0)
                operation.UndoRecordSerialNumber = candidateUndo;
            return operation.UndoRecordSerialNumber == candidateUndo;
        }

        private static string NextGroupCopyName(
            RhinoDoc doc,
            string sourceName,
            int copiedGroupIndex)
        {
            var sourceBase = ObjectNamingService.GetCopySeriesBase(sourceName);
            var aliases = ObjectNamingService.GetEffectiveGroupNames(doc);
            for (var number = 1; ; number++)
            {
                var candidate = OutlinerStrings.FormatNumberedCopyName(
                    sourceBase,
                    number);
                var existing = doc.Groups.FindName(candidate);
                if ((existing == null || existing.Index == copiedGroupIndex) &&
                    !aliases.Contains(candidate))
                    return candidate;
            }
        }

        private static void NameSemanticOutputs(
            NamingCommandSession session,
            RhinoDoc doc)
        {
            var activeOutputs = session.OutputIds
                .Select(doc.Objects.FindId)
                .Where(item => item != null && !item.IsDeleted && !item.IsReference)
                .ToList();
            if (session.OperationNumber == 0)
            {
                session.OperationBaseName = GetOperationBaseName(
                    session,
                    doc,
                    activeOutputs);
                session.OperationNumber = ObjectNamingService.NextOperationNumber(
                    doc,
                    session.OperationBaseName,
                    session.ReservedNames);
            }

            var prefix = ObjectNamingService.FormatOperationPrefix(
                session.OperationBaseName ?? session.CommandName,
                session.OperationNumber);
            for (var index = 0; index < activeOutputs.Count; index++)
            {
                var name = activeOutputs.Count == 1
                    ? prefix
                    : string.Format("{0} Result.{1:000}", prefix, index + 1);
                ObjectNamingService.TryApplyManagedName(
                    doc,
                    activeOutputs[index].Id,
                    name,
                    session.CommandName,
                    session.OperationNumber,
                    string.Empty,
                    false,
                    true,
                    session.OperationRootName);
            }

            if (!session.InputsNamed)
                session.InputsNamed = TryNameInputs(session, doc, activeOutputs, prefix);
        }

        private static string GetOperationBaseName(
            NamingCommandSession session,
            RhinoDoc doc,
            IEnumerable<RhinoObject> outputs)
        {
            if (!DerivedNamingPolicy.TracksInputs(session.CommandName))
                return session.CommandName;

            if (session.InputStages.Count == 0 &&
                session.PreselectedObjectIds.Count > 0)
            {
                AddInputObjects(
                    session,
                    doc,
                    0,
                    session.PreselectedObjectIds);
            }

            if (!session.InputStages.Any(stage => stage.ObjectIds.Count > 0))
            {
                var historyParents = (outputs ?? Enumerable.Empty<RhinoObject>())
                    .Where(item => item != null)
                    .SelectMany(item =>
                        item.HistoryParents() ?? Array.Empty<Guid>())
                    .Distinct()
                    .ToList();
                AddInputObjects(session, doc, 0, historyParents);
            }

            var stages = session.InputStages
                .Select(stage => (IList<DerivedNamingCandidate>)stage.ObjectIds
                    .Where(session.InputObjects.ContainsKey)
                    .Select(objectId => session.InputObjects[objectId])
                    .Select(source => new DerivedNamingCandidate
                    {
                        Key = source.Id.ToString("D"),
                        RootName = source.RootName,
                        DisplayName = source.DisplayName
                    })
                    .ToList())
                .ToList();
            if (DerivedNamingPolicy.TryResolveRelationship(
                    session.CommandName,
                    stages,
                    out var relationship))
            {
                session.OperationRootName = relationship.RootName;
                return OutlinerStrings.FormatBooleanRelationship(relationship);
            }

            if (!DerivedNamingPolicy.TryResolveRoot(
                    session.CommandName,
                    stages,
                    out var rootName))
            {
                return session.CommandName;
            }

            session.OperationRootName = rootName;
            return string.Format("{0} {1}", rootName, session.CommandName);
        }

        private static bool TryNameInputs(
            NamingCommandSession session,
            RhinoDoc doc,
            IEnumerable<RhinoObject> outputs,
            string prefix)
        {
            if (!session.TracksInputs)
                return true;

            if (!string.Equals(
                    session.CommandName,
                    "Pipe",
                    StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(
                    session.CommandName,
                    "Sweep1",
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (session.InputStages.Count == 0 &&
                session.PreselectedObjectIds.Count > 0)
            {
                session.GetOrCreateStage(0).Add(session.PreselectedObjectIds);
            }

            var parentIds = new HashSet<Guid>(outputs
                .SelectMany(item => item.HistoryParents() ?? Array.Empty<Guid>())
                .Where(id => id != Guid.Empty));
            Func<IEnumerable<Guid>, List<Guid>> eligibleCurves = ids => ids
                .Distinct()
                .Where(id => parentIds.Count == 0 || parentIds.Contains(id))
                .Where(id =>
                {
                    var item = doc.Objects.FindId(id);
                    return item != null &&
                           !item.IsDeleted &&
                           !item.IsReference &&
                           item.ObjectType == ObjectType.Curve;
                })
                .ToList();

            if (string.Equals(
                session.CommandName,
                "Pipe",
                StringComparison.OrdinalIgnoreCase))
            {
                var rails = eligibleCurves(session.InputStages[0].ObjectIds);
                if (rails.Count == 0)
                    return false;

                NameRoleObjects(
                    doc,
                    rails,
                    prefix,
                    "Rail",
                    rails.Count > 1,
                    session);
                return true;
            }

            if (!string.Equals(
                    session.CommandName,
                    "Sweep1",
                    StringComparison.OrdinalIgnoreCase) ||
                session.InputStages.Count < 2)
            {
                return false;
            }

            var sweepRails = eligibleCurves(session.InputStages[0].ObjectIds);
            var profiles = eligibleCurves(session.InputStages[1].ObjectIds);
            if (sweepRails.Count == 0 || profiles.Count == 0)
                return false;

            NameRoleObjects(
                doc,
                sweepRails,
                prefix,
                "Rail",
                sweepRails.Count > 1,
                session);
            NameRoleObjects(
                doc,
                profiles,
                prefix,
                "Profile",
                true,
                session);
            return true;
        }

        private static void NameRoleObjects(
            RhinoDoc doc,
            IList<Guid> objectIds,
            string prefix,
            string role,
            bool alwaysNumber,
            NamingCommandSession session)
        {
            for (var index = 0; index < objectIds.Count; index++)
            {
                var name = alwaysNumber
                    ? string.Format("{0} {1}.{2:000}", prefix, role, index + 1)
                    : prefix + " " + role;
                ObjectNamingService.TryApplyManagedName(
                    doc,
                    objectIds[index],
                    name,
                    session.CommandName,
                    session.OperationNumber,
                    role,
                    true,
                    false);
            }
        }

        private void UpdateInputTimer()
        {
            if (_sessions.Values.Any(session => session.TracksInputs))
                _inputTimer.Start();
            else
                _inputTimer.Stop();
        }

        private void ReportErrorOnce(string context, Exception exception)
        {
            if (!_reportedErrors.Add(context))
                return;

            RhinoApp.WriteLine(
                "Object Outliner smart naming skipped {0}: {1}",
                context,
                exception.Message);
        }
    }
}
