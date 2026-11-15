using System;
using System.Collections.Generic;
using System.Linq;
using Rhino;
using Rhino.Commands;
using Rhino.DocObjects;

namespace ObjectManager.Outliner
{
    internal sealed class OutlinerHistoryCaptureCoordinator : IDisposable
    {
        private sealed class CaptureSession
        {
            public CaptureSession(string commandName)
            {
                CommandName = commandName ?? string.Empty;
            }

            public string CommandName { get; }

            public ISet<Guid> CandidateIds { get; } = new HashSet<Guid>();
        }

        private readonly IDictionary<uint, List<CaptureSession>> _sessions =
            new Dictionary<uint, List<CaptureSession>>();
        private readonly DocumentLoadMonitor _documentLoadMonitor;
        private readonly OutlinerRelationshipHub _relationshipHub;
        private bool _reportedReadError;
        private bool _disposed;

        public OutlinerHistoryCaptureCoordinator(
            DocumentLoadMonitor documentLoadMonitor,
            OutlinerRelationshipHub relationshipHub)
        {
            _documentLoadMonitor = documentLoadMonitor;
            _relationshipHub = relationshipHub;

            Command.BeginCommand += OnBeginCommand;
            Command.EndCommand += OnEndCommand;
            RhinoDoc.AddRhinoObject += OnRhinoObjectAdded;
            RhinoDoc.ReplaceRhinoObject += OnRhinoObjectReplaced;
            RhinoDoc.CloseDocument += OnDocumentClosed;
            if (_documentLoadMonitor != null)
                _documentLoadMonitor.DocumentOpening += OnDocumentOpening;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            Command.BeginCommand -= OnBeginCommand;
            Command.EndCommand -= OnEndCommand;
            RhinoDoc.AddRhinoObject -= OnRhinoObjectAdded;
            RhinoDoc.ReplaceRhinoObject -= OnRhinoObjectReplaced;
            RhinoDoc.CloseDocument -= OnDocumentClosed;
            if (_documentLoadMonitor != null)
                _documentLoadMonitor.DocumentOpening -= OnDocumentOpening;
            _sessions.Clear();
        }

        private void OnBeginCommand(object sender, CommandEventArgs e)
        {
            if (_disposed || !IsDocumentReady(e.DocumentRuntimeSerialNumber))
                return;

            if (!_sessions.TryGetValue(
                    e.DocumentRuntimeSerialNumber,
                    out var sessions))
            {
                sessions = new List<CaptureSession>();
                _sessions[e.DocumentRuntimeSerialNumber] = sessions;
            }
            sessions.Add(new CaptureSession(e.CommandEnglishName));
        }

        private void OnEndCommand(object sender, CommandEventArgs e)
        {
            if (!_sessions.TryGetValue(
                    e.DocumentRuntimeSerialNumber,
                    out var sessions))
            {
                return;
            }

            var index = FindSessionIndex(sessions, e.CommandEnglishName);
            if (index < 0)
                return;

            var session = sessions[index];
            sessions.RemoveAt(index);
            if (sessions.Count == 0)
                _sessions.Remove(e.DocumentRuntimeSerialNumber);

            if (e.CommandResult != Result.Success || session.CandidateIds.Count == 0)
                return;

            var doc = e.Document ?? RhinoDoc.FromRuntimeSerialNumber(
                e.DocumentRuntimeSerialNumber);
            ReportHistory(doc, session.CandidateIds);
        }

        private void OnRhinoObjectAdded(object sender, RhinoObjectEventArgs e)
        {
            var doc = sender as RhinoDoc ?? e.TheObject?.Document;
            TrackCandidate(doc, e.ObjectId);
        }

        private void OnRhinoObjectReplaced(
            object sender,
            RhinoReplaceObjectEventArgs e)
        {
            TrackCandidate(
                e.Document ?? e.NewRhinoObject?.Document ?? sender as RhinoDoc,
                e.NewRhinoObject?.Id ?? e.ObjectId);
        }

        private void TrackCandidate(RhinoDoc doc, Guid objectId)
        {
            if (_disposed ||
                doc == null ||
                objectId == Guid.Empty ||
                !IsDocumentReady(doc.RuntimeSerialNumber) ||
                !_sessions.TryGetValue(doc.RuntimeSerialNumber, out var sessions) ||
                sessions.Count == 0)
            {
                return;
            }

            sessions[sessions.Count - 1].CandidateIds.Add(objectId);
        }

        private void ReportHistory(RhinoDoc doc, IEnumerable<Guid> candidateIds)
        {
            if (doc == null ||
                _relationshipHub == null ||
                doc.Objects.HistoryRecordCount <= 0)
            {
                return;
            }

            var relations = new List<OutlinerHistoryModel>();
            foreach (var objectId in candidateIds
                .Where(id => id != Guid.Empty)
                .Distinct())
            {
                try
                {
                    var rhinoObject = doc.Objects.FindId(objectId);
                    if (rhinoObject == null ||
                        rhinoObject.IsDeleted ||
                        rhinoObject.IsReference ||
                        !rhinoObject.HasHistoryRecord())
                    {
                        continue;
                    }

                    if (OutlinerHistoryReader.TryRead(
                            doc,
                            objectId,
                            out var history,
                            out _) &&
                        history != null &&
                        history.HasRelations)
                    {
                        relations.Add(history);
                    }
                }
                catch (Exception exception)
                {
                    if (_reportedReadError)
                        continue;
                    _reportedReadError = true;
                    RhinoApp.WriteLine(
                        "Object Outliner skipped History capture: {0}",
                        exception.Message);
                }
            }

            _relationshipHub.ReportHistory(doc.RuntimeSerialNumber, relations);
        }

        private static int FindSessionIndex(
            IList<CaptureSession> sessions,
            string commandName)
        {
            for (var index = sessions.Count - 1; index >= 0; index--)
            {
                if (string.Equals(
                        sessions[index].CommandName,
                        commandName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return index;
                }
            }
            return -1;
        }

        private bool IsDocumentReady(uint documentSerialNumber)
        {
            return _documentLoadMonitor == null ||
                _documentLoadMonitor.IsReady(documentSerialNumber);
        }

        private void OnDocumentOpening(
            object sender,
            DocumentLoadStateEventArgs e)
        {
            _sessions.Remove(e.DocumentSerialNumber);
        }

        private void OnDocumentClosed(object sender, DocumentEventArgs e)
        {
            _sessions.Remove(e.DocumentSerialNumber);
        }
    }
}
