using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Rhino;
using Rhino.DocObjects;
using Rhino.Input;
using Rhino.Input.Custom;

namespace ObjectManager.Outliner
{
    internal static class OutlinerGroupCommandInputRequest
    {
        private sealed class ActiveRequest
        {
            public uint DocumentSerialNumber { get; set; }

            public ISet<Guid> MemberIds { get; set; }
        }

        private sealed class RequestScope : IDisposable
        {
            private ActiveRequest _request;

            public RequestScope(ActiveRequest request)
            {
                _request = request;
            }

            public void Dispose()
            {
                var request = Interlocked.Exchange(ref _request, null);
                if (request != null)
                {
                    Interlocked.CompareExchange(
                        ref _active,
                        null,
                        request);
                }
            }
        }

        private static ActiveRequest _active;

        public static IDisposable Begin(
            uint documentSerialNumber,
            IEnumerable<Guid> memberIds)
        {
            var request = new ActiveRequest
            {
                DocumentSerialNumber = documentSerialNumber,
                MemberIds = new HashSet<Guid>(
                    (memberIds ?? Enumerable.Empty<Guid>())
                        .Where(id => id != Guid.Empty))
            };
            Interlocked.Exchange(ref _active, request);
            return new RequestScope(request);
        }

        public static bool Matches(RhinoObject rhinoObject)
        {
            var request = Volatile.Read(ref _active);
            var document = rhinoObject?.Document;
            return request != null &&
                document != null &&
                document.RuntimeSerialNumber == request.DocumentSerialNumber &&
                request.MemberIds.Contains(rhinoObject.Id);
        }
    }

    internal static class OutlinerCommandInputDiagnostics
    {
        public static bool Enabled { get; set; }

        public static void WriteLine(string format, params object[] arguments)
        {
            if (Enabled)
                RhinoApp.WriteLine(string.Format(format, arguments));
        }
    }

    internal sealed class OutlinerCommandInputProbe
    {
        public bool HasDocument { get; set; }

        public bool IsCommandRunning { get; set; }

        public bool InGet { get; set; }

        public bool InGetObject { get; set; }

        public bool HasActiveGetter { get; set; }

        public uint GetterSerialNumber { get; set; }

        public int ObjectCount { get; set; }

        public ObjectType GeometryFilter { get; set; }

        public bool ReferenceObjectSelect { get; set; }

        public bool ShouldTrace
        {
            get
            {
                return IsCommandRunning ||
                    InGet ||
                    InGetObject ||
                    HasActiveGetter;
            }
        }
    }

    internal enum OutlinerCommandInputStatus
    {
        Inactive,
        Rejected,
        Dispatched
    }

    internal sealed class OutlinerCommandInputResult
    {
        public OutlinerCommandInputStatus Status { get; set; }

        public string Reason { get; set; }

        public uint GetterSerialNumber { get; set; }

        public int BeforeCount { get; set; }

        public int AfterCount { get; set; }
    }

    internal sealed class OutlinerCommandInputBridge
    {
        private readonly OutlinerRelationshipHub _relationshipHub;
        private int _traceSequence;

        public OutlinerCommandInputBridge(
            OutlinerRelationshipHub relationshipHub = null)
        {
            _relationshipHub = relationshipHub;
        }

        public OutlinerCommandInputProbe Inspect(RhinoDoc doc)
        {
            var probe = new OutlinerCommandInputProbe
            {
                HasDocument = doc != null
            };
            if (doc == null)
                return probe;

            try
            {
                probe.IsCommandRunning = doc.IsCommandRunning;
                probe.InGet = RhinoGet.InGet(doc);
                probe.InGetObject = RhinoGet.InGetObject(doc);
            }
            catch
            {
            }

            if (!TryGetActiveGetter(doc, out var getter))
                return probe;

            probe.HasActiveGetter = true;
            try
            {
                probe.GetterSerialNumber = getter.SerialNumber;
                probe.ObjectCount = getter.ObjectCount;
                probe.GeometryFilter = getter.GeometryFilter;
                probe.ReferenceObjectSelect = getter.ReferenceObjectSelect;
            }
            catch
            {
            }

            return probe;
        }

        public int TracePointer(
            RhinoDoc doc,
            Guid objectId,
            string objectName,
            OutlinerCommandInputProbe probe,
            string targetKind = "object",
            int targetCount = 1)
        {
            var traceId = ++_traceSequence;
            probe = probe ?? Inspect(doc);
            OutlinerCommandInputDiagnostics.WriteLine(
                "Object Outliner command input #{0} [pointer]: target={1}:{2}, targets={3}, name=\"{4}\", doc={5}, command={6}, inGet={7}, inGetObject={8}, activeGetter={9}, getter={10}, count={11}, filter={12}",
                traceId,
                targetKind ?? "object",
                objectId,
                Math.Max(0, targetCount),
                Sanitize(objectName),
                doc == null ? 0 : doc.RuntimeSerialNumber,
                probe.IsCommandRunning,
                probe.InGet,
                probe.InGetObject,
                probe.HasActiveGetter,
                probe.GetterSerialNumber,
                probe.ObjectCount,
                probe.GeometryFilter);
            return traceId;
        }

        public OutlinerCommandInputResult TrySubmit(
            RhinoDoc doc,
            Guid objectId,
            int traceId)
        {
            var before = Inspect(doc);
            if (!before.HasActiveGetter)
            {
                return Result(
                    traceId,
                    OutlinerCommandInputStatus.Inactive,
                    "no-active-getter",
                    0,
                    0,
                    0,
                    string.Format(
                        "command={0}, inGet={1}, inGetObject={2}",
                        before.IsCommandRunning,
                        before.InGet,
                        before.InGetObject));
            }

            if (!TryValidateObject(
                    doc,
                    objectId,
                    before.ReferenceObjectSelect,
                    out var rejectionReason,
                    out var objectDetail))
            {
                return Result(
                    traceId,
                    OutlinerCommandInputStatus.Rejected,
                    rejectionReason,
                    before.GetterSerialNumber,
                    before.ObjectCount,
                    before.ObjectCount,
                    objectDetail);
            }

            var documentSerialNumber = doc.RuntimeSerialNumber;
            var commandScript = string.Format(
                "_SelID {0}",
                objectId.ToString("D"));
            bool commandStarted;
            try
            {
                commandStarted = RhinoApp.RunScript(commandScript, false);
            }
            catch (Exception exception)
            {
                return Result(
                    traceId,
                    OutlinerCommandInputStatus.Rejected,
                    "selid-exception",
                    before.GetterSerialNumber,
                    before.ObjectCount,
                    before.ObjectCount,
                    string.Format(
                        "{0}, exception={1}: {2}",
                        objectDetail,
                        exception.GetType().FullName,
                        Sanitize(exception.Message)));
            }

            var currentDocument = RhinoDoc.FromRuntimeSerialNumber(
                documentSerialNumber);
            var after = Inspect(currentDocument);
            var dispatchDetail = string.Format(
                "method=SelID, runScript={0}, afterActiveGetter={1}, afterGetter={2}, afterCount={3}, {4}",
                commandStarted,
                after.HasActiveGetter,
                after.GetterSerialNumber,
                after.ObjectCount,
                objectDetail);
            OutlinerCommandInputDiagnostics.WriteLine(
                "Object Outliner command input #{0} [dispatch]: {1}",
                traceId,
                dispatchDetail);

            if (!commandStarted)
            {
                return Result(
                    traceId,
                    OutlinerCommandInputStatus.Rejected,
                    "selid-did-not-run",
                    before.GetterSerialNumber,
                    before.ObjectCount,
                    after.ObjectCount,
                    dispatchDetail);
            }

            _relationshipHub?.ReportCommandInput(
                documentSerialNumber,
                objectId,
                before.GetterSerialNumber,
                before.GeometryFilter);

            return Result(
                traceId,
                OutlinerCommandInputStatus.Dispatched,
                string.Empty,
                before.GetterSerialNumber,
                before.ObjectCount,
                after.ObjectCount,
                dispatchDetail);
        }

        public OutlinerCommandInputResult TrySubmitGroup(
            RhinoDoc doc,
            Guid groupId,
            int groupIndex,
            IEnumerable<Guid> memberIds,
            int traceId)
        {
            var before = Inspect(doc);
            if (!before.HasActiveGetter)
            {
                return Result(
                    traceId,
                    OutlinerCommandInputStatus.Inactive,
                    "no-active-getter",
                    0,
                    0,
                    0,
                    string.Format(
                        "command={0}, inGet={1}, inGetObject={2}",
                        before.IsCommandRunning,
                        before.InGet,
                        before.InGetObject));
            }

            var members = (memberIds ?? Enumerable.Empty<Guid>())
                .Where(id => id != Guid.Empty)
                .Distinct()
                .ToList();
            var validGroup = false;
            try
            {
                var group = groupIndex < 0
                    ? null
                    : doc.Groups.FindIndex(groupIndex);
                validGroup = group != null &&
                    !group.IsDeleted &&
                    group.Id == groupId;
            }
            catch
            {
                validGroup = false;
            }

            var groupDetail = string.Format(
                "groupId={0}, groupIndex={1}, members={2}",
                groupId,
                groupIndex,
                members.Count);
            if (!validGroup || members.Count == 0)
            {
                return Result(
                    traceId,
                    OutlinerCommandInputStatus.Rejected,
                    !validGroup
                        ? "missing-group"
                        : "empty-group",
                    before.GetterSerialNumber,
                    before.ObjectCount,
                    before.ObjectCount,
                    groupDetail);
            }

            var documentSerialNumber = doc.RuntimeSerialNumber;
            bool commandStarted;
            try
            {
                using (OutlinerGroupCommandInputRequest.Begin(
                    documentSerialNumber,
                    members))
                {
                    commandStarted = RhinoApp.RunScript(
                        "_ObjectOutlinerSelectGroup",
                        false);
                }
            }
            catch (Exception exception)
            {
                return Result(
                    traceId,
                    OutlinerCommandInputStatus.Rejected,
                    "group-selection-exception",
                    before.GetterSerialNumber,
                    before.ObjectCount,
                    before.ObjectCount,
                    string.Format(
                        "{0}, exception={1}: {2}",
                        groupDetail,
                        exception.GetType().FullName,
                        Sanitize(exception.Message)));
            }

            var currentDocument = RhinoDoc.FromRuntimeSerialNumber(
                documentSerialNumber);
            var after = Inspect(currentDocument);
            var dispatchDetail = string.Format(
                "method=ObjectOutlinerSelectGroup, runScript={0}, afterActiveGetter={1}, afterGetter={2}, afterCount={3}, {4}",
                commandStarted,
                after.HasActiveGetter,
                after.GetterSerialNumber,
                after.ObjectCount,
                groupDetail);
            OutlinerCommandInputDiagnostics.WriteLine(
                "Object Outliner command input #{0} [dispatch]: {1}",
                traceId,
                dispatchDetail);

            if (!commandStarted)
            {
                return Result(
                    traceId,
                    OutlinerCommandInputStatus.Rejected,
                    "group-selection-did-not-run",
                    before.GetterSerialNumber,
                    before.ObjectCount,
                    after.ObjectCount,
                    dispatchDetail);
            }

            foreach (var memberId in members)
            {
                _relationshipHub?.ReportCommandInput(
                    documentSerialNumber,
                    memberId,
                    before.GetterSerialNumber,
                    before.GeometryFilter);
            }

            return Result(
                traceId,
                OutlinerCommandInputStatus.Dispatched,
                string.Empty,
                before.GetterSerialNumber,
                before.ObjectCount,
                after.ObjectCount,
                dispatchDetail);
        }

        private static bool TryGetActiveGetter(
            RhinoDoc doc,
            out GetObject getter)
        {
            getter = null;
            if (doc == null)
                return false;

            try
            {
                getter = GetObject.ActiveGetObject(doc);
            }
            catch
            {
                getter = null;
            }
            return getter != null;
        }

        private static bool TryValidateObject(
            RhinoDoc doc,
            Guid objectId,
            bool referenceObjectSelect,
            out string rejectionReason,
            out string detail)
        {
            rejectionReason = string.Empty;
            detail = string.Empty;
            var rhinoObject = objectId == Guid.Empty || doc == null
                ? null
                : doc.Objects.FindId(objectId);
            if (rhinoObject == null || rhinoObject.IsDeleted)
            {
                rejectionReason = "missing-object";
                return false;
            }

            detail = DescribeObject(rhinoObject);
            if (!rhinoObject.IsSelectable(false, false, false, false))
            {
                rejectionReason = "unavailable-object";
                return false;
            }
            if (rhinoObject.IsReference && !referenceObjectSelect)
            {
                rejectionReason = "reference-object";
                return false;
            }

            return true;
        }

        private static OutlinerCommandInputResult Result(
            int traceId,
            OutlinerCommandInputStatus status,
            string reason,
            uint getterSerialNumber,
            int beforeCount,
            int afterCount,
            string detail)
        {
            var result = new OutlinerCommandInputResult
            {
                Status = status,
                Reason = reason ?? string.Empty,
                GetterSerialNumber = getterSerialNumber,
                BeforeCount = beforeCount,
                AfterCount = afterCount
            };
            OutlinerCommandInputDiagnostics.WriteLine(
                "Object Outliner command input #{0} [result]: status={1}, reason={2}, getter={3}, count={4}->{5}{6}",
                traceId,
                status,
                string.IsNullOrEmpty(reason) ? "dispatched" : reason,
                getterSerialNumber,
                beforeCount,
                afterCount,
                string.IsNullOrEmpty(detail) ? string.Empty : ", " + detail);
            return result;
        }

        private static string DescribeObject(RhinoObject rhinoObject)
        {
            if (rhinoObject == null)
                return "object=null";

            return string.Format(
                "objectType={0}, selected={1}, hidden={2}, locked={3}, reference={4}",
                rhinoObject.ObjectType,
                rhinoObject.IsSelected(false),
                rhinoObject.IsHidden,
                rhinoObject.IsLocked,
                rhinoObject.IsReference);
        }

        private static string Sanitize(string value)
        {
            return (value ?? string.Empty)
                .Replace("\r", " ")
                .Replace("\n", " ")
                .Replace("\"", "'");
        }
    }
}
