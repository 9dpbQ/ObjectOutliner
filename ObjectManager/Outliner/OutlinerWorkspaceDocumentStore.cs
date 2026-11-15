using System;
using System.Collections.Generic;
using System.Linq;
using Rhino;
using Rhino.Commands;
using Rhino.DocObjects;
using Rhino.FileIO;

namespace ObjectManager.Outliner
{
    internal sealed class OutlinerWorkspaceDocumentStore : IDisposable
    {
        private sealed class StagedIsolation
        {
            public StagedIsolation(
                OutlinerIsolationMode mode,
                OutlinerIsolateRequest request)
            {
                Mode = mode;
                Request = CloneRequest(request);
            }

            public OutlinerIsolationMode Mode { get; }

            public OutlinerIsolateRequest Request { get; }
        }

        private sealed class SaveSuspension
        {
            public SaveSuspension(OutlinerIsolateSnapshot isolation)
            {
                Isolation = isolation?.Clone();
            }

            public OutlinerIsolateSnapshot Isolation { get; }

            public HashSet<Guid> ChangedObjects { get; } = new HashSet<Guid>();
        }

        private readonly object _sync = new object();
        private readonly Dictionary<uint, OutlinerWorkspaceState>
            _statesByDocument =
                new Dictionary<uint, OutlinerWorkspaceState>();
        private readonly Dictionary<uint, OutlinerIsolateState>
            _isolateTrackers =
                new Dictionary<uint, OutlinerIsolateState>();
        private readonly Dictionary<uint, StagedIsolation> _stagedIsolation =
            new Dictionary<uint, StagedIsolation>();
        private readonly Dictionary<uint, SaveSuspension> _saveSuspensions =
            new Dictionary<uint, SaveSuspension>();
        private readonly HashSet<uint> _pendingRestoreDocuments =
            new HashSet<uint>();
        private bool _disposed;
        private readonly Dictionary<uint, OutlinerIsolateSnapshot> _legacyIsolation =
            new Dictionary<uint, OutlinerIsolateSnapshot>();

        public OutlinerWorkspaceDocumentStore()
        {
            Command.BeginCommand += OnCommandStarted;
            Command.EndCommand += OnCommandEnded;
            RhinoDoc.BeginSaveDocument += OnBeginSaveDocument;
            RhinoDoc.EndSaveDocument += OnEndSaveDocument;
            RhinoDoc.EndOpenDocumentInitialViewUpdate += OnDocumentOpened;
            RhinoDoc.CloseDocument += OnDocumentClosed;
            RhinoApp.Idle += OnRhinoIdle;
            RhinoDoc.ModifyObjectAttributes += OnAttributesChanged;
        }

        public OutlinerWorkspaceState Get(uint documentSerialNumber)
        {
            lock (_sync)
            {
                return _statesByDocument.TryGetValue(
                    documentSerialNumber,
                    out var state)
                        ? state.Clone()
                        : null;
            }
        }

        public void Set(
            uint documentSerialNumber,
            OutlinerWorkspaceState state)
        {
            if (documentSerialNumber == 0 || state == null)
                return;

            lock (_sync)
                _statesByDocument[documentSerialNumber] = state.Clone();
        }

        public void UpdateIsolation(
            RhinoDoc doc,
            OutlinerIsolateSnapshot isolation,
            IEnumerable<Guid> selectedObjectIds = null)
        {
            if (doc == null)
                return;

            lock (_sync)
            {
                var state = GetOrCreateState(doc.RuntimeSerialNumber);
                state.Initialized = true;
                state.Isolation = isolation?.Clone();
                if (selectedObjectIds != null)
                {
                    state.SelectedObjectIds.Clear();
                    state.SelectedObjectIds.UnionWith(selectedObjectIds.Where(
                        id => id != Guid.Empty));
                }
            }
        }

        public void StageIsolationRequest(
            uint documentSerialNumber,
            OutlinerIsolationMode mode,
            OutlinerIsolateRequest request)
        {
            if (documentSerialNumber == 0 ||
                mode == OutlinerIsolationMode.None ||
                request == null)
                return;

            lock (_sync)
            {
                _stagedIsolation[documentSerialNumber] =
                    new StagedIsolation(mode, request);
            }
        }

        public OutlinerIsolateRequest PeekIsolationRequest(
            uint documentSerialNumber,
            OutlinerIsolationMode mode)
        {
            lock (_sync)
            {
                if (!_stagedIsolation.TryGetValue(
                    documentSerialNumber,
                    out var staged) ||
                    staged.Mode != mode)
                {
                    return null;
                }

                return CloneRequest(staged.Request);
            }
        }

        public void Write(RhinoDoc doc, BinaryArchiveWriter archive)
        {
            var state = doc == null
                ? null
                : Get(doc.RuntimeSerialNumber);
            OutlinerWorkspaceCodec.Write(archive, state);
        }

        public void Read(RhinoDoc doc, BinaryArchiveReader archive)
        {
            if (doc == null)
                return;

            var state = OutlinerWorkspaceCodec.Read(archive);
            lock (_sync)
            {
                _statesByDocument[doc.RuntimeSerialNumber] = state;
                if (state.Isolation != null)
                {
                    _legacyIsolation[doc.RuntimeSerialNumber] = state.Isolation;
                    state.Isolation = null;
                    _isolateTrackers.Remove(doc.RuntimeSerialNumber);
                }
                else
                {
                    _isolateTrackers.Remove(doc.RuntimeSerialNumber);
                }
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            Command.BeginCommand -= OnCommandStarted;
            Command.EndCommand -= OnCommandEnded;
            RhinoDoc.BeginSaveDocument -= OnBeginSaveDocument;
            RhinoDoc.EndSaveDocument -= OnEndSaveDocument;
            RhinoDoc.EndOpenDocumentInitialViewUpdate -= OnDocumentOpened;
            RhinoDoc.CloseDocument -= OnDocumentClosed;
            RhinoApp.Idle -= OnRhinoIdle;
            RhinoDoc.ModifyObjectAttributes -= OnAttributesChanged;
            lock (_sync)
            {
                _statesByDocument.Clear();
                _isolateTrackers.Clear();
                _stagedIsolation.Clear();
                _saveSuspensions.Clear();
                _pendingRestoreDocuments.Clear();
                _legacyIsolation.Clear();
            }
        }

        private void OnCommandStarted(object sender, CommandEventArgs e)
        {
            if (e == null)
                return;

            if (!TryGetStartIsolationMode(e, out var mode))
                return;

            var doc = RhinoDoc.FromRuntimeSerialNumber(
                e.DocumentRuntimeSerialNumber);
            if (doc == null)
                return;

            var request = PeekIsolationRequest(
                e.DocumentRuntimeSerialNumber,
                mode);
            lock (_sync)
            {
                var tracker = new OutlinerIsolateState();
                tracker.BeginNativeIsolation(doc, request, mode);
                _isolateTrackers[e.DocumentRuntimeSerialNumber] = tracker;
            }
        }

        private void OnCommandEnded(object sender, CommandEventArgs e)
        {
            if (e == null)
                return;

            var serialNumber = e.DocumentRuntimeSerialNumber;
            var doc = RhinoDoc.FromRuntimeSerialNumber(serialNumber);
            if (doc != null)
                RestoreSaveSuspension(doc);
            if (TryGetStartIsolationMode(e, out var startMode))
            {
                OutlinerIsolateSnapshot snapshot = null;
                lock (_sync)
                {
                    if (_isolateTrackers.TryGetValue(
                        serialNumber,
                        out var tracker))
                    {
                        tracker.CompleteNativeIsolation(
                            doc,
                            e.CommandResult == Result.Success);
                        snapshot = tracker.CreateSnapshot();
                    }
                    _stagedIsolation.Remove(serialNumber);
                }
                UpdateIsolation(
                    doc,
                    snapshot,
                    ReadSelectedObjectIds(doc));
                return;
            }

            if (!TryGetEndIsolationMode(e, out var endMode) ||
                e.CommandResult != Result.Success)
                return;

            lock (_sync)
            {
                if (_isolateTrackers.TryGetValue(
                    serialNumber,
                    out var tracker) &&
                    tracker.IsActive &&
                    tracker.Mode != endMode)
                {
                    return;
                }

                _isolateTrackers.Remove(serialNumber);
                _stagedIsolation.Remove(serialNumber);
            }
            UpdateIsolation(doc, null, ReadSelectedObjectIds(doc));
        }

        private void OnBeginSaveDocument(
            object sender,
            DocumentSaveEventArgs e)
        {
            if (e == null || e.ExportSelected)
                return;

            var doc = e.Document;
            var state = doc == null ? null : Get(doc.RuntimeSerialNumber);
            var isolation = doc != null && _isolateTrackers.TryGetValue(
                doc.RuntimeSerialNumber, out var activeTracker)
                    ? activeTracker.CreateSnapshot() : state?.Isolation;
            if (doc == null || isolation == null || !isolation.IsActive)
                return;

            state = state ?? new OutlinerWorkspaceState { Initialized = true };
            state.SelectedObjectIds.Clear();
            state.SelectedObjectIds.UnionWith(ReadSelectedObjectIds(doc));
            Set(doc.RuntimeSerialNumber, state);
            var suspension = new SaveSuspension(isolation);
            lock (_sync)
            {
                _saveSuspensions[doc.RuntimeSerialNumber] =
                    suspension;
            }

            var modified = doc.Modified;
            var undoRecording = doc.UndoRecordingEnabled;
            try
            {
                doc.UndoRecordingEnabled = false;
                foreach (var objectId in isolation.SuppressedObjectIds)
                {
                    var item = doc.Objects.FindId(objectId);
                    if (item == null || item.IsDeleted)
                        continue;
                    if (isolation.Mode == OutlinerIsolationMode.Visibility &&
                        item.IsHidden)
                    {
                        if (doc.Objects.Show(objectId, true))
                            suspension.ChangedObjects.Add(objectId);
                    }
                    else if (isolation.Mode == OutlinerIsolationMode.Lock &&
                        item.IsLocked)
                    {
                        if (doc.Objects.Unlock(objectId, true))
                            suspension.ChangedObjects.Add(objectId);
                    }
                }
            }
            catch
            {
                RestoreSaveSuspension(doc);
                throw;
            }
            finally
            {
                doc.UndoRecordingEnabled = undoRecording;
                doc.Modified = modified;
            }
        }

        private void OnEndSaveDocument(
            object sender,
            DocumentSaveEventArgs e)
        {
            if (e == null || e.ExportSelected || e.Document == null)
                return;

            RestoreSaveSuspension(e.Document);
        }

        private void OnDocumentOpened(object sender, DocumentOpenEventArgs e)
        {
            if (e == null || e.Merge)
                return;

            lock (_sync)
            {
                if (_statesByDocument.TryGetValue(
                    e.DocumentSerialNumber,
                    out var state) &&
                    state.Initialized)
                {
                    _pendingRestoreDocuments.Add(e.DocumentSerialNumber);
                }
            }
        }

        private void OnRhinoIdle(object sender, EventArgs e)
        {
            uint[] serialNumbers;
            lock (_sync)
                serialNumbers = _pendingRestoreDocuments.ToArray();

            foreach (var serialNumber in serialNumbers)
            {
                var doc = RhinoDoc.FromRuntimeSerialNumber(serialNumber);
                if (doc == null || doc.IsClosing)
                {
                    RemovePendingRestore(serialNumber);
                    continue;
                }
                if (doc.IsCommandRunning)
                    continue;

                try
                {
                    RestoreWorkspace(doc);
                }
                catch (Exception exception)
                {
                    RhinoApp.WriteLine(
                        "Object Outliner could not restore the saved workspace: {0}",
                        exception.Message);
                }
                finally
                {
                    RemovePendingRestore(serialNumber);
                }
            }
        }

        private void RestoreWorkspace(RhinoDoc doc)
        {
            var state = Get(doc.RuntimeSerialNumber);
            if (state == null || !state.Initialized)
                return;

            var desiredModified = doc.Modified;

            try
            {
                if (_legacyIsolation.TryGetValue(doc.RuntimeSerialNumber, out var isolation))
                {
                    foreach (var id in isolation.SuppressedObjectIds)
                    {
                        var item = doc.Objects.FindId(id);
                        if (item == null || item.IsDeleted)
                            continue;
                        if (isolation.Mode == OutlinerIsolationMode.Visibility && item.IsHidden)
                            doc.Objects.Show(id, true);
                        else if (isolation.Mode == OutlinerIsolationMode.Lock && item.IsLocked)
                            doc.Objects.Unlock(id, true);
                    }
                    _legacyIsolation.Remove(doc.RuntimeSerialNumber);
                }
                RestoreSelection(doc, state.SelectedObjectIds);
                doc.Views.Redraw();
            }
            finally
            {
                doc.Modified = desiredModified;
            }
        }


        private void OnDocumentClosed(object sender, DocumentEventArgs e)
        {
            if (e == null)
                return;

            lock (_sync)
            {
                var serialNumber = e.DocumentSerialNumber;
                _statesByDocument.Remove(serialNumber);
                _isolateTrackers.Remove(serialNumber);
                _stagedIsolation.Remove(serialNumber);
                _saveSuspensions.Remove(serialNumber);
                _pendingRestoreDocuments.Remove(serialNumber);
                _legacyIsolation.Remove(serialNumber);
            }
        }

        private OutlinerWorkspaceState GetOrCreateState(uint serialNumber)
        {
            if (!_statesByDocument.TryGetValue(serialNumber, out var state))
            {
                state = new OutlinerWorkspaceState();
                _statesByDocument[serialNumber] = state;
            }
            return state;
        }

        private void RemovePendingRestore(uint serialNumber)
        {
            lock (_sync)
            {
                _pendingRestoreDocuments.Remove(serialNumber);
            }
        }

        private void RestoreSaveSuspension(RhinoDoc doc)
        {
            var serialNumber = doc.RuntimeSerialNumber;
            SaveSuspension suspension;
            lock (_sync)
            {
                if (!_saveSuspensions.TryGetValue(serialNumber, out suspension))
                    return;
            }
            var modified = doc.Modified;
            var undoRecording = doc.UndoRecordingEnabled;
            try
            {
                doc.UndoRecordingEnabled = false;
                foreach (var id in suspension.ChangedObjects)
                {
                    if (suspension.Isolation.Mode == OutlinerIsolationMode.Visibility)
                        doc.Objects.Hide(id, true);
                    else
                        doc.Objects.Lock(id, true);
                }
            }
            finally
            {
                _saveSuspensions.Remove(serialNumber);
                doc.UndoRecordingEnabled = undoRecording;
                doc.Modified = modified;
            }
        }

        private void OnAttributesChanged(object sender, RhinoModifyObjectAttributesEventArgs e)
        {
            var doc = e.Document;
            if (doc == null || _saveSuspensions.ContainsKey(doc.RuntimeSerialNumber) ||
                !_isolateTrackers.TryGetValue(doc.RuntimeSerialNumber, out var tracker) ||
                tracker.IsCapturing || !tracker.IsActive || e.RhinoObject == null)
                return;
            if (e.OldAttributes == null || e.NewAttributes == null ||
                e.OldAttributes.Mode == e.NewAttributes.Mode)
                return;
            tracker.ReleaseManuallyChangedObject(e.RhinoObject.Id);
            UpdateIsolation(doc, tracker.CreateSnapshot());
        }

        private static void RestoreSelection(
            RhinoDoc doc,
            IEnumerable<Guid> objectIds)
        {
            if (doc == null)
                return;

            doc.Objects.UnselectAll();
            foreach (var objectId in objectIds ?? Enumerable.Empty<Guid>())
            {
                var item = doc.Objects.FindId(objectId);
                if (item != null && !item.IsDeleted && item.IsSelectable())
                    doc.Objects.Select(objectId);
            }
        }

        private static IEnumerable<Guid> ReadSelectedObjectIds(RhinoDoc doc)
        {
            return doc == null
                ? Enumerable.Empty<Guid>()
                : doc.Objects.GetSelectedObjects(false, false)
                    .Where(item => item != null)
                    .Select(item => item.Id)
                    .ToList();
        }

        private static OutlinerIsolateRequest CloneRequest(
            OutlinerIsolateRequest request)
        {
            return request == null
                ? null
                : new OutlinerIsolateRequest(
                    request.Kind,
                    request.NodeIds,
                    request.ObjectIds);
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
    }
}
