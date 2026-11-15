using System;
using System.Collections.Generic;
using System.Linq;
using Rhino;
using Rhino.Commands;
using Rhino.FileIO;

namespace ObjectManager.Outliner
{
    internal sealed class GroupOrderChangedEventArgs : EventArgs
    {
        public GroupOrderChangedEventArgs(
            uint documentSerialNumber,
            IEnumerable<OutlinerGroupSliceKey> keys)
        {
            DocumentSerialNumber = documentSerialNumber;
            Keys = new HashSet<OutlinerGroupSliceKey>(
                keys ?? Enumerable.Empty<OutlinerGroupSliceKey>());
        }

        public uint DocumentSerialNumber { get; }

        public ISet<OutlinerGroupSliceKey> Keys { get; }
    }

    internal sealed class GroupOrderDocumentStore : IDisposable
    {
        private const int MaxEntryCount = 1000000;

        private sealed class UndoTag
        {
            public UndoTag(
                uint documentSerialNumber,
                IDictionary<OutlinerGroupSliceKey, int?> values)
            {
                DocumentSerialNumber = documentSerialNumber;
                Values = new Dictionary<OutlinerGroupSliceKey, int?>(values);
            }

            public uint DocumentSerialNumber { get; }

            public IDictionary<OutlinerGroupSliceKey, int?> Values { get; }
        }

        private readonly object _sync = new object();
        private readonly Dictionary<uint, Dictionary<OutlinerGroupSliceKey, int>>
            _ordersByDocument =
                new Dictionary<uint, Dictionary<OutlinerGroupSliceKey, int>>();
        private bool _disposed;

        public GroupOrderDocumentStore()
        {
            RhinoDoc.CloseDocument += OnDocumentClosed;
        }

        public event EventHandler<GroupOrderChangedEventArgs> Changed;

        public IDictionary<OutlinerGroupSliceKey, int> GetOrders(
            uint documentSerialNumber)
        {
            lock (_sync)
            {
                return _ordersByDocument.TryGetValue(
                    documentSerialNumber,
                    out var orders)
                        ? new Dictionary<OutlinerGroupSliceKey, int>(orders)
                        : new Dictionary<OutlinerGroupSliceKey, int>();
            }
        }

        public bool SetSequence(
            RhinoDoc doc,
            Guid layerId,
            IEnumerable<Guid> groupIds,
            bool registerUndo = true)
        {
            if (doc == null || layerId == Guid.Empty)
                return false;

            var sequence = (groupIds ?? Enumerable.Empty<Guid>())
                .Where(id => id != Guid.Empty)
                .Distinct()
                .ToList();
            if (sequence.Count == 0)
                return false;

            var desired = sequence
                .Select((groupId, order) => new
                {
                    Key = new OutlinerGroupSliceKey(groupId, layerId),
                    Order = order
                })
                .ToDictionary(item => item.Key, item => (int?)item.Order);
            return Apply(doc, desired, registerUndo);
        }

        public void Write(RhinoDoc doc, BinaryArchiveWriter archive)
        {
            var orders = doc == null
                ? new Dictionary<OutlinerGroupSliceKey, int>()
                : GetOrders(doc.RuntimeSerialNumber);
            archive.WriteInt(orders.Count);
            foreach (var entry in orders
                .OrderBy(item => item.Key.LayerId)
                .ThenBy(item => item.Value)
                .ThenBy(item => item.Key.GroupId))
            {
                archive.WriteGuid(entry.Key.GroupId);
                archive.WriteGuid(entry.Key.LayerId);
                archive.WriteInt(entry.Value);
            }
        }

        public void Read(RhinoDoc doc, BinaryArchiveReader archive)
        {
            if (doc == null)
                return;

            var count = archive.ReadInt();
            if (count < 0 || count > MaxEntryCount)
                throw new InvalidOperationException(
                    "Invalid Object Outliner group order data.");

            var orders = new Dictionary<OutlinerGroupSliceKey, int>();
            for (var index = 0; index < count; index++)
            {
                var groupId = archive.ReadGuid();
                var layerId = archive.ReadGuid();
                var order = archive.ReadInt();
                if (groupId != Guid.Empty && layerId != Guid.Empty)
                {
                    orders[new OutlinerGroupSliceKey(groupId, layerId)] = order;
                }
            }

            lock (_sync)
                _ordersByDocument[doc.RuntimeSerialNumber] = orders;
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            RhinoDoc.CloseDocument -= OnDocumentClosed;
            lock (_sync)
                _ordersByDocument.Clear();
        }

        private bool Apply(
            RhinoDoc doc,
            IDictionary<OutlinerGroupSliceKey, int?> desired,
            bool registerUndo)
        {
            var before = Capture(doc.RuntimeSerialNumber, desired.Keys);
            if (desired.All(entry => before[entry.Key] == entry.Value))
                return false;

            if (registerUndo)
            {
                doc.AddCustomUndoEvent(
                    OutlinerStrings.UndoReorderGroups,
                    OnUndo,
                    new UndoTag(doc.RuntimeSerialNumber, before));
            }

            ApplyValues(doc.RuntimeSerialNumber, desired);
            doc.Modified = true;
            RaiseChanged(doc.RuntimeSerialNumber, desired.Keys);
            return true;
        }

        private IDictionary<OutlinerGroupSliceKey, int?> Capture(
            uint documentSerialNumber,
            IEnumerable<OutlinerGroupSliceKey> keys)
        {
            var current = GetOrders(documentSerialNumber);
            return keys.Distinct().ToDictionary(
                key => key,
                key => current.TryGetValue(key, out var order)
                    ? (int?)order
                    : null);
        }

        private void ApplyValues(
            uint documentSerialNumber,
            IDictionary<OutlinerGroupSliceKey, int?> values)
        {
            lock (_sync)
            {
                if (!_ordersByDocument.TryGetValue(
                    documentSerialNumber,
                    out var orders))
                {
                    orders = new Dictionary<OutlinerGroupSliceKey, int>();
                    _ordersByDocument[documentSerialNumber] = orders;
                }

                foreach (var entry in values)
                {
                    if (entry.Value.HasValue)
                        orders[entry.Key] = entry.Value.Value;
                    else
                        orders.Remove(entry.Key);
                }
            }
        }

        private void OnUndo(object sender, CustomUndoEventArgs e)
        {
            var tag = e.Tag as UndoTag;
            if (tag == null ||
                e.Document == null ||
                e.Document.RuntimeSerialNumber != tag.DocumentSerialNumber)
            {
                return;
            }

            var current = Capture(tag.DocumentSerialNumber, tag.Values.Keys);
            e.Document.AddCustomUndoEvent(
                OutlinerStrings.UndoReorderGroups,
                OnUndo,
                new UndoTag(tag.DocumentSerialNumber, current));
            ApplyValues(tag.DocumentSerialNumber, tag.Values);
            RaiseChanged(tag.DocumentSerialNumber, tag.Values.Keys);
        }

        private void OnDocumentClosed(object sender, DocumentEventArgs e)
        {
            lock (_sync)
                _ordersByDocument.Remove(e.DocumentSerialNumber);
        }

        private void RaiseChanged(
            uint documentSerialNumber,
            IEnumerable<OutlinerGroupSliceKey> keys)
        {
            Changed?.Invoke(
                this,
                new GroupOrderChangedEventArgs(documentSerialNumber, keys));
        }
    }
}
