using System;
using System.Collections.Generic;
using System.Linq;
using Rhino.FileIO;

namespace ObjectManager.Outliner
{
    internal sealed class OutlinerWorkspaceState
    {
        public bool Initialized { get; set; }

        public bool WasFullyLoaded { get; set; }

        public int LastObjectCount { get; set; }

        public int LastProjectedRowCount { get; set; }

        public string SearchText { get; set; } = string.Empty;

        public Dictionary<Guid, bool> LayerExpansion { get; } =
            new Dictionary<Guid, bool>();

        public Dictionary<OutlinerGroupSliceKey, bool> GroupExpansion { get; } =
            new Dictionary<OutlinerGroupSliceKey, bool>();

        public Dictionary<Guid, bool> HistoryExpansion { get; } =
            new Dictionary<Guid, bool>();

        public OutlinerLayerScopeMode LayerScope { get; set; }

        public OutlinerLayerVisibilityFilter LayerVisibility { get; set; }

        public OutlinerLockFilter LayerLock { get; set; }

        public OutlinerLayerOccupancyFilter LayerOccupancy { get; set; }

        public HashSet<Guid> FilterLayerIds { get; } = new HashSet<Guid>();

        public HashSet<OutlinerIconKind> ObjectTypes { get; } =
            new HashSet<OutlinerIconKind>();

        public bool FavoritesOnly { get; set; }

        public bool SelectedObjectsOnly { get; set; }

        public bool UserNamedOnly { get; set; }
        public bool NotesOnly { get; set; }

        public OutlinerDisplayMode DisplayMode { get; set; }

        public bool CompactHistory { get; set; } = true;

        public HashSet<Guid> SelectedObjectIds { get; } = new HashSet<Guid>();

        public HashSet<Guid> SelectedLayerIds { get; } = new HashSet<Guid>();

        public OutlinerIsolateSnapshot Isolation { get; set; }

        public OutlinerWorkspaceState Clone()
        {
            var clone = new OutlinerWorkspaceState
            {
                Initialized = Initialized,
                WasFullyLoaded = WasFullyLoaded,
                LastObjectCount = LastObjectCount,
                LastProjectedRowCount = LastProjectedRowCount,
                SearchText = SearchText ?? string.Empty,
                LayerScope = LayerScope,
                LayerVisibility = LayerVisibility,
                LayerLock = LayerLock,
                LayerOccupancy = LayerOccupancy,
                FavoritesOnly = FavoritesOnly,
                SelectedObjectsOnly = SelectedObjectsOnly,
                UserNamedOnly = UserNamedOnly,
                NotesOnly = NotesOnly,
                DisplayMode = DisplayMode,
                CompactHistory = CompactHistory,
                Isolation = Isolation?.Clone()
            };
            CopyDictionary(LayerExpansion, clone.LayerExpansion);
            CopyDictionary(GroupExpansion, clone.GroupExpansion);
            CopyDictionary(HistoryExpansion, clone.HistoryExpansion);
            clone.FilterLayerIds.UnionWith(FilterLayerIds);
            clone.ObjectTypes.UnionWith(ObjectTypes);
            clone.SelectedObjectIds.UnionWith(SelectedObjectIds);
            clone.SelectedLayerIds.UnionWith(SelectedLayerIds);
            return clone;
        }

        private static void CopyDictionary<TKey>(
            IDictionary<TKey, bool> source,
            IDictionary<TKey, bool> destination)
        {
            foreach (var entry in source)
                destination[entry.Key] = entry.Value;
        }
    }

    internal static class OutlinerWorkspaceCodec
    {
        private const int FormatVersion = 4;
        private const int MaxEntryCount = 1000000;

        public static void Write(
            BinaryArchiveWriter archive,
            OutlinerWorkspaceState state)
        {
            state = state ?? new OutlinerWorkspaceState();
            archive.WriteInt(FormatVersion);
            archive.WriteBool(state.Initialized);
            if (!state.Initialized)
                return;

            archive.WriteBool(state.WasFullyLoaded);
            archive.WriteInt(Math.Max(0, state.LastObjectCount));
            archive.WriteInt(Math.Max(0, state.LastProjectedRowCount));
            archive.WriteString(state.SearchText ?? string.Empty);
            WriteGuidBoolDictionary(archive, state.LayerExpansion);
            WriteGroupBoolDictionary(archive, state.GroupExpansion);
            WriteGuidBoolDictionary(archive, state.HistoryExpansion);
            archive.WriteInt((int)state.LayerScope);
            archive.WriteInt((int)state.LayerVisibility);
            archive.WriteInt((int)state.LayerLock);
            archive.WriteInt((int)state.LayerOccupancy);
            WriteGuids(archive, state.FilterLayerIds);
            WriteEnums(archive, state.ObjectTypes);
            archive.WriteBool(state.FavoritesOnly);
            WriteGuids(archive, state.SelectedObjectIds);
            WriteGuids(archive, state.SelectedLayerIds);
            // Retain the archive slot for compatibility, but isolation is session-only.
            WriteIsolation(archive, null);
            archive.WriteBool(state.CompactHistory);
            archive.WriteBool(state.SelectedObjectsOnly);
            archive.WriteBool(state.UserNamedOnly);
            archive.WriteInt((int)state.DisplayMode);
            archive.WriteBool(state.NotesOnly);
        }

        public static OutlinerWorkspaceState Read(BinaryArchiveReader archive)
        {
            var version = archive.ReadInt();
            if (version < 1 || version > FormatVersion)
                throw new InvalidOperationException(
                    "Unsupported Object Outliner workspace format.");

            var state = new OutlinerWorkspaceState
            {
                Initialized = archive.ReadBool()
            };
            if (!state.Initialized)
                return state;

            state.WasFullyLoaded = archive.ReadBool();
            state.LastObjectCount = Math.Max(0, archive.ReadInt());
            state.LastProjectedRowCount = Math.Max(0, archive.ReadInt());
            state.SearchText = archive.ReadString() ?? string.Empty;
            ReadGuidBoolDictionary(archive, state.LayerExpansion);
            ReadGroupBoolDictionary(archive, state.GroupExpansion);
            ReadGuidBoolDictionary(archive, state.HistoryExpansion);
            state.LayerScope = ReadEnum<OutlinerLayerScopeMode>(archive);
            state.LayerVisibility =
                ReadEnum<OutlinerLayerVisibilityFilter>(archive);
            state.LayerLock = ReadEnum<OutlinerLockFilter>(archive);
            state.LayerOccupancy =
                ReadEnum<OutlinerLayerOccupancyFilter>(archive);
            ReadGuids(archive, state.FilterLayerIds);
            ReadEnums(archive, state.ObjectTypes);
            state.FavoritesOnly = archive.ReadBool();
            ReadGuids(archive, state.SelectedObjectIds);
            ReadGuids(archive, state.SelectedLayerIds);
            state.Isolation = ReadIsolation(archive);
            state.CompactHistory = version >= 2
                ? archive.ReadBool()
                : true;
            if (version >= 3)
            {
                state.SelectedObjectsOnly = archive.ReadBool();
                state.UserNamedOnly = archive.ReadBool();
                state.DisplayMode = ReadEnum<OutlinerDisplayMode>(archive);
            }
            if (version >= 4)
                state.NotesOnly = archive.ReadBool();
            return state;
        }

        private static void WriteGuidBoolDictionary(
            BinaryArchiveWriter archive,
            IDictionary<Guid, bool> values)
        {
            archive.WriteInt(values.Count);
            foreach (var entry in values.OrderBy(item => item.Key))
            {
                archive.WriteGuid(entry.Key);
                archive.WriteBool(entry.Value);
            }
        }

        private static void ReadGuidBoolDictionary(
            BinaryArchiveReader archive,
            IDictionary<Guid, bool> destination)
        {
            var count = ReadCount(archive);
            for (var index = 0; index < count; index++)
            {
                var id = archive.ReadGuid();
                var value = archive.ReadBool();
                if (id != Guid.Empty)
                    destination[id] = value;
            }
        }

        private static void WriteGroupBoolDictionary(
            BinaryArchiveWriter archive,
            IDictionary<OutlinerGroupSliceKey, bool> values)
        {
            archive.WriteInt(values.Count);
            foreach (var entry in values
                .OrderBy(item => item.Key.GroupId)
                .ThenBy(item => item.Key.LayerId))
            {
                archive.WriteGuid(entry.Key.GroupId);
                archive.WriteGuid(entry.Key.LayerId);
                archive.WriteBool(entry.Value);
            }
        }

        private static void ReadGroupBoolDictionary(
            BinaryArchiveReader archive,
            IDictionary<OutlinerGroupSliceKey, bool> destination)
        {
            var count = ReadCount(archive);
            for (var index = 0; index < count; index++)
            {
                var groupId = archive.ReadGuid();
                var layerId = archive.ReadGuid();
                var value = archive.ReadBool();
                if (groupId != Guid.Empty && layerId != Guid.Empty)
                {
                    destination[new OutlinerGroupSliceKey(groupId, layerId)] =
                        value;
                }
            }
        }

        private static void WriteGuids(
            BinaryArchiveWriter archive,
            IEnumerable<Guid> values)
        {
            var ids = new HashSet<Guid>(values ?? Enumerable.Empty<Guid>());
            ids.Remove(Guid.Empty);
            archive.WriteInt(ids.Count);
            foreach (var id in ids.OrderBy(value => value))
                archive.WriteGuid(id);
        }

        private static void ReadGuids(
            BinaryArchiveReader archive,
            ISet<Guid> destination)
        {
            var count = ReadCount(archive);
            for (var index = 0; index < count; index++)
            {
                var id = archive.ReadGuid();
                if (id != Guid.Empty)
                    destination.Add(id);
            }
        }

        private static void WriteEnums(
            BinaryArchiveWriter archive,
            IEnumerable<OutlinerIconKind> values)
        {
            var items = new HashSet<OutlinerIconKind>(
                values ?? Enumerable.Empty<OutlinerIconKind>());
            archive.WriteInt(items.Count);
            foreach (var value in items.OrderBy(item => (int)item))
                archive.WriteInt((int)value);
        }

        private static void ReadEnums(
            BinaryArchiveReader archive,
            ISet<OutlinerIconKind> destination)
        {
            var count = ReadCount(archive);
            for (var index = 0; index < count; index++)
            {
                var value = archive.ReadInt();
                if (Enum.IsDefined(typeof(OutlinerIconKind), value))
                    destination.Add((OutlinerIconKind)value);
            }
        }

        private static void WriteIsolation(
            BinaryArchiveWriter archive,
            OutlinerIsolateSnapshot isolation)
        {
            var active = isolation != null && isolation.IsActive;
            archive.WriteBool(active);
            if (!active)
                return;

            archive.WriteInt((int)isolation.Mode);
            archive.WriteInt((int)isolation.TargetKind);
            WriteGuids(archive, isolation.TargetNodeIds);
            WriteGuids(archive, isolation.TargetObjectIds);
            WriteGuids(archive, isolation.SuppressedObjectIds);
        }

        private static OutlinerIsolateSnapshot ReadIsolation(
            BinaryArchiveReader archive)
        {
            if (!archive.ReadBool())
                return null;

            var mode = ReadEnum<OutlinerIsolationMode>(archive);
            var kind = ReadEnum<OutlinerIsolateTargetKind>(archive);
            var targetNodeIds = new HashSet<Guid>();
            var targetObjectIds = new HashSet<Guid>();
            var suppressedObjectIds = new HashSet<Guid>();
            ReadGuids(archive, targetNodeIds);
            ReadGuids(archive, targetObjectIds);
            ReadGuids(archive, suppressedObjectIds);
            var snapshot = new OutlinerIsolateSnapshot(
                mode,
                kind,
                targetNodeIds,
                targetObjectIds,
                suppressedObjectIds);
            return snapshot.IsActive ? snapshot : null;
        }

        private static T ReadEnum<T>(BinaryArchiveReader archive)
            where T : struct
        {
            var value = archive.ReadInt();
            return Enum.IsDefined(typeof(T), value)
                ? (T)Enum.ToObject(typeof(T), value)
                : default(T);
        }

        private static int ReadCount(BinaryArchiveReader archive)
        {
            var count = archive.ReadInt();
            if (count < 0 || count > MaxEntryCount)
                throw new InvalidOperationException(
                    "Invalid Object Outliner workspace entry count.");
            return count;
        }
    }
}
