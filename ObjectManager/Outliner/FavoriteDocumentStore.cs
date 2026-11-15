using System;
using System.Collections.Generic;
using System.Linq;
using Rhino;
using Rhino.Commands;
using Rhino.FileIO;

namespace ObjectManager.Outliner
{
    internal sealed class FavoriteGroupsChangedEventArgs : EventArgs
    {
        public FavoriteGroupsChangedEventArgs(
            uint documentSerialNumber,
            IEnumerable<Guid> groupIds)
        {
            DocumentSerialNumber = documentSerialNumber;
            GroupIds = new HashSet<Guid>((groupIds ?? Enumerable.Empty<Guid>())
                .Where(id => id != Guid.Empty));
        }

        public uint DocumentSerialNumber { get; }

        public ISet<Guid> GroupIds { get; }
    }

    internal sealed class FavoriteDocumentStore : IDisposable
    {
        private sealed class UndoTag
        {
            public UndoTag(
                uint documentSerialNumber,
                IDictionary<Guid, bool> states)
            {
                DocumentSerialNumber = documentSerialNumber;
                States = new Dictionary<Guid, bool>(states);
            }

            public uint DocumentSerialNumber { get; }

            public IDictionary<Guid, bool> States { get; }
        }

        private readonly object _sync = new object();
        private readonly Dictionary<uint, HashSet<Guid>> _favoritesByDocument =
            new Dictionary<uint, HashSet<Guid>>();
        private bool _disposed;

        public FavoriteDocumentStore()
        {
            RhinoDoc.CloseDocument += OnCloseDocument;
        }

        public event EventHandler<FavoriteGroupsChangedEventArgs> Changed;

        public bool IsFavorite(uint documentSerialNumber, Guid groupId)
        {
            lock (_sync)
            {
                return groupId != Guid.Empty &&
                    _favoritesByDocument.TryGetValue(
                        documentSerialNumber,
                        out var favorites) &&
                    favorites.Contains(groupId);
            }
        }

        public ISet<Guid> GetFavorites(uint documentSerialNumber)
        {
            lock (_sync)
            {
                return _favoritesByDocument.TryGetValue(
                    documentSerialNumber,
                    out var favorites)
                        ? new HashSet<Guid>(favorites)
                        : new HashSet<Guid>();
            }
        }

        public bool SetFavorites(
            RhinoDoc doc,
            IEnumerable<Guid> groupIds,
            bool favorite,
            bool registerUndo = true)
        {
            if (doc == null)
                return false;

            var ids = new HashSet<Guid>((groupIds ?? Enumerable.Empty<Guid>())
                .Where(id => id != Guid.Empty));
            if (ids.Count == 0)
                return false;

            var before = CaptureStates(doc.RuntimeSerialNumber, ids);
            if (before.Values.All(value => value == favorite))
                return false;

            if (registerUndo)
            {
                doc.AddCustomUndoEvent(
                    OutlinerStrings.UndoChangeFavorites,
                    OnUndoFavorites,
                    new UndoTag(doc.RuntimeSerialNumber, before));
            }

            ApplyStates(
                doc.RuntimeSerialNumber,
                ids.ToDictionary(id => id, id => favorite));
            doc.Modified = true;
            RaiseChanged(doc.RuntimeSerialNumber, ids);
            return true;
        }

        public void Write(RhinoDoc doc, BinaryArchiveWriter archive)
        {
            var favorites = doc == null
                ? new HashSet<Guid>()
                : GetFavorites(doc.RuntimeSerialNumber);
            archive.WriteInt(favorites.Count);
            foreach (var groupId in favorites.OrderBy(id => id))
                archive.WriteGuid(groupId);
        }

        public void Read(RhinoDoc doc, BinaryArchiveReader archive)
        {
            if (doc == null)
                return;

            var count = Math.Max(0, archive.ReadInt());
            var favorites = new HashSet<Guid>();
            for (var index = 0; index < count; index++)
            {
                var groupId = archive.ReadGuid();
                if (groupId != Guid.Empty)
                    favorites.Add(groupId);
            }

            lock (_sync)
                _favoritesByDocument[doc.RuntimeSerialNumber] = favorites;
            RaiseChanged(doc.RuntimeSerialNumber, favorites);
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            RhinoDoc.CloseDocument -= OnCloseDocument;
            lock (_sync)
                _favoritesByDocument.Clear();
        }

        private IDictionary<Guid, bool> CaptureStates(
            uint documentSerialNumber,
            IEnumerable<Guid> groupIds)
        {
            var favorites = GetFavorites(documentSerialNumber);
            return groupIds.ToDictionary(
                groupId => groupId,
                favorites.Contains);
        }

        private void ApplyStates(
            uint documentSerialNumber,
            IDictionary<Guid, bool> states)
        {
            lock (_sync)
            {
                if (!_favoritesByDocument.TryGetValue(
                    documentSerialNumber,
                    out var favorites))
                {
                    favorites = new HashSet<Guid>();
                    _favoritesByDocument[documentSerialNumber] = favorites;
                }

                foreach (var entry in states)
                {
                    if (entry.Value)
                        favorites.Add(entry.Key);
                    else
                        favorites.Remove(entry.Key);
                }
            }
        }

        private void OnUndoFavorites(object sender, CustomUndoEventArgs e)
        {
            var tag = e.Tag as UndoTag;
            if (tag == null ||
                e.Document == null ||
                e.Document.RuntimeSerialNumber != tag.DocumentSerialNumber)
            {
                return;
            }

            var current = CaptureStates(
                tag.DocumentSerialNumber,
                tag.States.Keys);
            e.Document.AddCustomUndoEvent(
                OutlinerStrings.UndoChangeFavorites,
                OnUndoFavorites,
                new UndoTag(tag.DocumentSerialNumber, current));
            ApplyStates(tag.DocumentSerialNumber, tag.States);
            RaiseChanged(tag.DocumentSerialNumber, tag.States.Keys);
        }

        private void OnCloseDocument(object sender, DocumentEventArgs e)
        {
            lock (_sync)
                _favoritesByDocument.Remove(e.DocumentSerialNumber);
        }

        private void RaiseChanged(
            uint documentSerialNumber,
            IEnumerable<Guid> groupIds)
        {
            Changed?.Invoke(
                this,
                new FavoriteGroupsChangedEventArgs(
                    documentSerialNumber,
                    groupIds));
        }
    }
}
