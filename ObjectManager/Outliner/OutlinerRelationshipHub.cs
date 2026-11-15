using System;
using System.Collections.Generic;
using System.Linq;
using Rhino.DocObjects;

namespace ObjectManager.Outliner
{
    internal sealed class OutlinerCommandInputObservedEventArgs : EventArgs
    {
        public uint DocumentSerialNumber { get; set; }

        public Guid ObjectId { get; set; }

        public uint GetterSerialNumber { get; set; }

        public ObjectType GeometryFilter { get; set; }
    }

    internal sealed class OutlinerCopiedGroupRelation
    {
        public Guid SourceGroupId { get; set; }

        public int SourceGroupIndex { get; set; }

        public Guid TargetGroupId { get; set; }

        public int TargetGroupIndex { get; set; }

        public int BatchIndex { get; set; }

        public IDictionary<Guid, bool> ExpansionByTargetLayerId { get; set; } =
            new Dictionary<Guid, bool>();
    }

    internal sealed class OutlinerGroupsCopiedEventArgs : EventArgs
    {
        public uint SourceDocumentSerialNumber { get; set; }

        public uint TargetDocumentSerialNumber { get; set; }

        public IList<OutlinerCopiedGroupRelation> Relations { get; set; } =
            new List<OutlinerCopiedGroupRelation>();
    }

    internal sealed class OutlinerHistoryObservedEventArgs : EventArgs
    {
        public uint DocumentSerialNumber { get; set; }

        public IList<OutlinerHistoryModel> Relations { get; set; } =
            new List<OutlinerHistoryModel>();
    }

    internal sealed class OutlinerRelationshipHub
    {
        private readonly object _sync = new object();
        private readonly Dictionary<uint, Dictionary<OutlinerGroupSliceKey, bool>>
            _groupExpansionByDocument =
                new Dictionary<uint, Dictionary<OutlinerGroupSliceKey, bool>>();
        private readonly Dictionary<uint, List<OutlinerCopiedGroupRelation>>
            _pendingCopiedGroups =
                new Dictionary<uint, List<OutlinerCopiedGroupRelation>>();
        private readonly Dictionary<uint, Dictionary<Guid, OutlinerHistoryModel>>
            _pendingHistory =
                new Dictionary<uint, Dictionary<Guid, OutlinerHistoryModel>>();

        public event EventHandler<OutlinerCommandInputObservedEventArgs>
            CommandInputObserved;

        public event EventHandler<OutlinerGroupsCopiedEventArgs> GroupsCopied;

        public event EventHandler<OutlinerHistoryObservedEventArgs>
            HistoryObserved;

        public void ReportCommandInput(
            uint documentSerialNumber,
            Guid objectId,
            uint getterSerialNumber,
            ObjectType geometryFilter)
        {
            if (documentSerialNumber == 0 || objectId == Guid.Empty)
                return;

            CommandInputObserved?.Invoke(
                this,
                new OutlinerCommandInputObservedEventArgs
                {
                    DocumentSerialNumber = documentSerialNumber,
                    ObjectId = objectId,
                    GetterSerialNumber = getterSerialNumber,
                    GeometryFilter = geometryFilter
                });
        }

        public void ReportGroupsCopied(
            uint sourceDocumentSerialNumber,
            uint targetDocumentSerialNumber,
            IEnumerable<OutlinerCopiedGroupRelation> relations)
        {
            var copied = (relations ??
                    Enumerable.Empty<OutlinerCopiedGroupRelation>())
                .Where(item => item != null && item.TargetGroupId != Guid.Empty)
                .ToList();
            if (targetDocumentSerialNumber == 0 || copied.Count == 0)
                return;

            lock (_sync)
            {
                if (!_pendingCopiedGroups.TryGetValue(
                        targetDocumentSerialNumber,
                        out var pending))
                {
                    pending = new List<OutlinerCopiedGroupRelation>();
                    _pendingCopiedGroups[targetDocumentSerialNumber] = pending;
                }
                pending.AddRange(copied);
            }

            GroupsCopied?.Invoke(
                this,
                new OutlinerGroupsCopiedEventArgs
                {
                    SourceDocumentSerialNumber = sourceDocumentSerialNumber,
                    TargetDocumentSerialNumber = targetDocumentSerialNumber,
                    Relations = copied
                });
        }

        public void ReportHistory(
            uint documentSerialNumber,
            IEnumerable<OutlinerHistoryModel> relations)
        {
            var observed = (relations ?? Enumerable.Empty<OutlinerHistoryModel>())
                .Where(item => item != null &&
                    item.ObjectId != Guid.Empty &&
                    item.HasRelations)
                .Select(CloneHistory)
                .ToList();
            if (documentSerialNumber == 0 || observed.Count == 0)
                return;

            lock (_sync)
            {
                if (!_pendingHistory.TryGetValue(
                        documentSerialNumber,
                        out var pending))
                {
                    pending = new Dictionary<Guid, OutlinerHistoryModel>();
                    _pendingHistory[documentSerialNumber] = pending;
                }

                foreach (var relation in observed)
                    pending[relation.ObjectId] = CloneHistory(relation);
            }

            HistoryObserved?.Invoke(
                this,
                new OutlinerHistoryObservedEventArgs
                {
                    DocumentSerialNumber = documentSerialNumber,
                    Relations = observed
                });
        }

        public void ReplaceGroupExpansionState(
            uint documentSerialNumber,
            IDictionary<OutlinerGroupSliceKey, bool> state)
        {
            if (documentSerialNumber == 0)
                return;

            lock (_sync)
            {
                _groupExpansionByDocument[documentSerialNumber] =
                    new Dictionary<OutlinerGroupSliceKey, bool>(
                        state ??
                        new Dictionary<OutlinerGroupSliceKey, bool>());
            }
        }

        public void SetGroupExpansionState(
            uint documentSerialNumber,
            OutlinerGroupSliceKey key,
            bool expanded)
        {
            if (documentSerialNumber == 0)
                return;

            lock (_sync)
            {
                if (!_groupExpansionByDocument.TryGetValue(
                        documentSerialNumber,
                        out var state))
                {
                    state = new Dictionary<OutlinerGroupSliceKey, bool>();
                    _groupExpansionByDocument[documentSerialNumber] = state;
                }
                state[key] = expanded;
            }
        }

        public IDictionary<OutlinerGroupSliceKey, bool> GetGroupExpansionState(
            uint documentSerialNumber)
        {
            lock (_sync)
            {
                return _groupExpansionByDocument.TryGetValue(
                    documentSerialNumber,
                    out var state)
                        ? new Dictionary<OutlinerGroupSliceKey, bool>(state)
                        : new Dictionary<OutlinerGroupSliceKey, bool>();
            }
        }

        public IList<OutlinerCopiedGroupRelation> TakePendingCopiedGroups(
            uint documentSerialNumber)
        {
            lock (_sync)
            {
                if (!_pendingCopiedGroups.TryGetValue(
                        documentSerialNumber,
                        out var pending))
                {
                    return new List<OutlinerCopiedGroupRelation>();
                }

                _pendingCopiedGroups.Remove(documentSerialNumber);
                return pending.ToList();
            }
        }

        public IList<OutlinerHistoryModel> TakePendingHistory(
            uint documentSerialNumber)
        {
            lock (_sync)
            {
                if (!_pendingHistory.TryGetValue(
                        documentSerialNumber,
                        out var pending))
                {
                    return new List<OutlinerHistoryModel>();
                }

                _pendingHistory.Remove(documentSerialNumber);
                return pending.Values.Select(CloneHistory).ToList();
            }
        }

        public void ForgetDocument(uint documentSerialNumber)
        {
            lock (_sync)
            {
                _groupExpansionByDocument.Remove(documentSerialNumber);
                _pendingCopiedGroups.Remove(documentSerialNumber);
                _pendingHistory.Remove(documentSerialNumber);
            }
        }

        private static OutlinerHistoryModel CloneHistory(
            OutlinerHistoryModel source)
        {
            return new OutlinerHistoryModel
            {
                ObjectId = source.ObjectId,
                ParentIds = source.ParentIds
                    .Where(id => id != Guid.Empty)
                    .Distinct()
                    .ToList(),
                ChildIds = source.ChildIds
                    .Where(id => id != Guid.Empty)
                    .Distinct()
                    .ToList()
            };
        }
    }
}
