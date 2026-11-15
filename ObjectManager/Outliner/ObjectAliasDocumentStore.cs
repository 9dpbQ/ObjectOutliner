using System;
using System.Collections.Generic;
using System.Linq;
using Rhino;
using Rhino.FileIO;

namespace ObjectManager.Outliner
{
    internal sealed class OutlinerNameRecord
    {
        public Guid Id { get; set; }

        public string Name { get; set; }

        public OutlinerNameOwnership Ownership { get; set; }

        public string Command { get; set; }

        public int OperationNumber { get; set; }

        public string Role { get; set; }

        public string LineageRoot { get; set; }

        public OutlinerNameRecord Clone()
        {
            return new OutlinerNameRecord
            {
                Id = Id,
                Name = Name,
                Ownership = Ownership,
                Command = Command,
                OperationNumber = OperationNumber,
                Role = Role,
                LineageRoot = LineageRoot
            };
        }
    }

    internal sealed class OutlinerNamesChangedEventArgs : EventArgs
    {
        public OutlinerNamesChangedEventArgs(
            uint documentSerialNumber,
            IEnumerable<Guid> objectIds,
            IEnumerable<Guid> groupIds)
        {
            DocumentSerialNumber = documentSerialNumber;
            ObjectIds = new HashSet<Guid>(objectIds ?? Enumerable.Empty<Guid>());
            GroupIds = new HashSet<Guid>(groupIds ?? Enumerable.Empty<Guid>());
            ObjectIds.Remove(Guid.Empty);
            GroupIds.Remove(Guid.Empty);
        }

        public uint DocumentSerialNumber { get; }

        public ISet<Guid> ObjectIds { get; }

        public ISet<Guid> GroupIds { get; }
    }

    internal sealed class ObjectAliasDocumentStore : IDisposable
    {
        private const int FormatVersion = 1;
        private const int MaxEntryCount = 1000000;

        private sealed class DocumentNames
        {
            public IDictionary<Guid, OutlinerNameRecord> Objects { get; } =
                new Dictionary<Guid, OutlinerNameRecord>();

            public IDictionary<Guid, OutlinerNameRecord> Groups { get; } =
                new Dictionary<Guid, OutlinerNameRecord>();
        }

        private readonly object _sync = new object();
        private readonly IDictionary<uint, DocumentNames> _namesByDocument =
            new Dictionary<uint, DocumentNames>();
        private bool _disposed;

        public ObjectAliasDocumentStore()
        {
            RhinoDoc.CloseDocument += OnDocumentClosed;
        }

        public event EventHandler<OutlinerNamesChangedEventArgs> Changed;

        public bool TryGetObject(
            uint documentSerialNumber,
            Guid objectId,
            out OutlinerNameRecord record)
        {
            return TryGet(
                documentSerialNumber,
                objectId,
                false,
                out record);
        }

        public bool TryGetGroup(
            uint documentSerialNumber,
            Guid groupId,
            out OutlinerNameRecord record)
        {
            return TryGet(
                documentSerialNumber,
                groupId,
                true,
                out record);
        }

        public bool SetObject(
            RhinoDoc doc,
            Guid objectId,
            string name,
            OutlinerNameOwnership ownership,
            string command,
            int operationNumber,
            string role,
            string lineageRoot)
        {
            return Set(
                doc,
                objectId,
                false,
                name,
                ownership,
                command,
                operationNumber,
                role,
                lineageRoot);
        }

        public bool SetGroup(
            RhinoDoc doc,
            Guid groupId,
            string name,
            OutlinerNameOwnership ownership,
            string command,
            int operationNumber,
            string role,
            string lineageRoot)
        {
            return Set(
                doc,
                groupId,
                true,
                name,
                ownership,
                command,
                operationNumber,
                role,
                lineageRoot);
        }

        public IReadOnlyDictionary<Guid, OutlinerNameRecord> GetObjectSnapshot(
            uint documentSerialNumber)
        {
            lock (_sync)
            {
                if (!_namesByDocument.TryGetValue(
                        documentSerialNumber,
                        out var names))
                {
                    return new Dictionary<Guid, OutlinerNameRecord>();
                }

                return names.Objects.ToDictionary(
                    item => item.Key,
                    item => item.Value.Clone());
            }
        }

        public void Write(RhinoDoc doc, BinaryArchiveWriter archive)
        {
            archive.WriteInt(FormatVersion);
            if (doc == null)
            {
                archive.WriteInt(0);
                archive.WriteInt(0);
                return;
            }

            List<OutlinerNameRecord> objects;
            List<OutlinerNameRecord> groups;
            lock (_sync)
            {
                if (!_namesByDocument.TryGetValue(
                        doc.RuntimeSerialNumber,
                        out var names))
                {
                    objects = new List<OutlinerNameRecord>();
                    groups = new List<OutlinerNameRecord>();
                }
                else
                {
                    objects = names.Objects.Values
                        .Where(item => ShouldPersistObject(doc, item))
                        .Select(item => item.Clone())
                        .ToList();
                    groups = names.Groups.Values
                        .Where(item => ShouldPersistGroup(doc, item))
                        .Select(item => item.Clone())
                        .ToList();
                }
            }

            WriteRecords(archive, objects);
            WriteRecords(archive, groups);
        }

        public void Read(RhinoDoc doc, BinaryArchiveReader archive)
        {
            if (doc == null)
                return;

            var version = archive.ReadInt();
            if (version != FormatVersion)
                throw new InvalidOperationException(
                    "Unsupported Object Outliner naming format.");

            var names = new DocumentNames();
            ReadRecords(archive, names.Objects);
            ReadRecords(archive, names.Groups);
            lock (_sync)
                _namesByDocument[doc.RuntimeSerialNumber] = names;

            RaiseChanged(
                doc.RuntimeSerialNumber,
                names.Objects.Keys,
                names.Groups.Keys);
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            RhinoDoc.CloseDocument -= OnDocumentClosed;
            lock (_sync)
                _namesByDocument.Clear();
        }

        private bool TryGet(
            uint documentSerialNumber,
            Guid id,
            bool group,
            out OutlinerNameRecord record)
        {
            record = null;
            if (documentSerialNumber == 0 || id == Guid.Empty)
                return false;

            lock (_sync)
            {
                if (!_namesByDocument.TryGetValue(
                        documentSerialNumber,
                        out var names))
                {
                    return false;
                }

                var records = group ? names.Groups : names.Objects;
                if (!records.TryGetValue(id, out var stored))
                    return false;

                record = stored.Clone();
                return true;
            }
        }

        private bool Set(
            RhinoDoc doc,
            Guid id,
            bool group,
            string name,
            OutlinerNameOwnership ownership,
            string command,
            int operationNumber,
            string role,
            string lineageRoot)
        {
            if (doc == null || id == Guid.Empty || string.IsNullOrWhiteSpace(name))
                return false;

            var record = new OutlinerNameRecord
            {
                Id = id,
                Name = name.Trim(),
                Ownership = ownership,
                Command = command ?? string.Empty,
                OperationNumber = Math.Max(0, operationNumber),
                Role = role ?? string.Empty,
                LineageRoot = (lineageRoot ?? string.Empty).Trim()
            };
            var changed = false;
            lock (_sync)
            {
                if (!_namesByDocument.TryGetValue(
                        doc.RuntimeSerialNumber,
                        out var names))
                {
                    names = new DocumentNames();
                    _namesByDocument[doc.RuntimeSerialNumber] = names;
                }

                var records = group ? names.Groups : names.Objects;
                if (!records.TryGetValue(id, out var existing) ||
                    !SameRecord(existing, record))
                {
                    records[id] = record;
                    changed = true;
                }
            }

            if (!changed)
                return false;

            doc.Modified = true;
            RaiseChanged(
                doc.RuntimeSerialNumber,
                group ? Array.Empty<Guid>() : new[] { id },
                group ? new[] { id } : Array.Empty<Guid>());
            return true;
        }

        private static bool SameRecord(
            OutlinerNameRecord left,
            OutlinerNameRecord right)
        {
            return left != null &&
                right != null &&
                left.Id == right.Id &&
                left.Ownership == right.Ownership &&
                left.OperationNumber == right.OperationNumber &&
                string.Equals(left.Name, right.Name, StringComparison.Ordinal) &&
                string.Equals(left.Command, right.Command, StringComparison.Ordinal) &&
                string.Equals(left.Role, right.Role, StringComparison.Ordinal) &&
                string.Equals(
                    left.LineageRoot,
                    right.LineageRoot,
                    StringComparison.Ordinal);
        }

        private static bool ShouldPersistObject(
            RhinoDoc doc,
            OutlinerNameRecord record)
        {
            var rhinoObject = record == null
                ? null
                : doc?.Objects.FindId(record.Id);
            if (rhinoObject == null || rhinoObject.IsDeleted)
                return false;

            var nativeName = rhinoObject.Attributes.Name ?? string.Empty;
            if (record.Ownership == OutlinerNameOwnership.ManagedAlias)
            {
                return string.IsNullOrWhiteSpace(nativeName) ||
                    ObjectNamingService.TryGetManagedName(
                        rhinoObject,
                        out _);
            }

            return string.Equals(
                nativeName,
                record.Name,
                StringComparison.Ordinal);
        }

        private static bool ShouldPersistGroup(
            RhinoDoc doc,
            OutlinerNameRecord record)
        {
            var group = record == null
                ? null
                : doc?.Groups.FindId(record.Id);
            if (group == null || group.IsDeleted)
                return false;

            var nativeName = doc.Groups.GroupName(group.Index) ?? string.Empty;
            return record.Ownership == OutlinerNameOwnership.ManagedAlias
                ? string.IsNullOrWhiteSpace(nativeName)
                : string.Equals(
                    nativeName,
                    record.Name,
                    StringComparison.Ordinal);
        }

        private static void WriteRecords(
            BinaryArchiveWriter archive,
            IEnumerable<OutlinerNameRecord> records)
        {
            var items = (records ?? Enumerable.Empty<OutlinerNameRecord>())
                .Where(item => item != null && item.Id != Guid.Empty)
                .OrderBy(item => item.Id)
                .ToList();
            archive.WriteInt(items.Count);
            foreach (var item in items)
            {
                archive.WriteGuid(item.Id);
                archive.WriteString(item.Name ?? string.Empty);
                archive.WriteInt((int)item.Ownership);
                archive.WriteString(item.Command ?? string.Empty);
                archive.WriteInt(Math.Max(0, item.OperationNumber));
                archive.WriteString(item.Role ?? string.Empty);
                archive.WriteString(item.LineageRoot ?? string.Empty);
            }
        }

        private static void ReadRecords(
            BinaryArchiveReader archive,
            IDictionary<Guid, OutlinerNameRecord> destination)
        {
            var count = archive.ReadInt();
            if (count < 0 || count > MaxEntryCount)
                throw new InvalidOperationException(
                    "Invalid Object Outliner naming entry count.");

            for (var index = 0; index < count; index++)
            {
                var id = archive.ReadGuid();
                var name = archive.ReadString() ?? string.Empty;
                var ownershipValue = archive.ReadInt();
                var command = archive.ReadString() ?? string.Empty;
                var operationNumber = Math.Max(0, archive.ReadInt());
                var role = archive.ReadString() ?? string.Empty;
                var lineageRoot = archive.ReadString() ?? string.Empty;
                if (id == Guid.Empty ||
                    string.IsNullOrWhiteSpace(name) ||
                    !Enum.IsDefined(
                        typeof(OutlinerNameOwnership),
                        ownershipValue))
                {
                    continue;
                }

                destination[id] = new OutlinerNameRecord
                {
                    Id = id,
                    Name = name,
                    Ownership = (OutlinerNameOwnership)ownershipValue,
                    Command = command,
                    OperationNumber = operationNumber,
                    Role = role,
                    LineageRoot = lineageRoot
                };
            }
        }

        private void OnDocumentClosed(object sender, DocumentEventArgs e)
        {
            if (e?.Document == null)
                return;

            lock (_sync)
                _namesByDocument.Remove(e.Document.RuntimeSerialNumber);
        }

        private void RaiseChanged(
            uint documentSerialNumber,
            IEnumerable<Guid> objectIds,
            IEnumerable<Guid> groupIds)
        {
            Changed?.Invoke(
                this,
                new OutlinerNamesChangedEventArgs(
                    documentSerialNumber,
                    objectIds,
                    groupIds));
        }
    }
}
