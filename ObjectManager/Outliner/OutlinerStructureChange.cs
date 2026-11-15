using System;
using System.Collections.Generic;
using System.Linq;

namespace ObjectManager.Outliner
{
    [Flags]
    internal enum OutlinerStructureChangeKind
    {
        None = 0,
        ObjectAdded = 1 << 0,
        ObjectDeleted = 1 << 1,
        ObjectUndeleted = 1 << 2,
        ObjectPurged = 1 << 3,
        ObjectReplaced = 1 << 4,
        ObjectAttributes = 1 << 5,
        LayerTable = 1 << 6,
        GroupTable = 1 << 7,
        InstanceDefinition = 1 << 8,
        History = 1 << 9,
        ExplicitFull = 1 << 10,
        Unknown = 1 << 11
    }

    internal sealed class OutlinerStructureChange : EventArgs
    {
        public OutlinerStructureChangeKind Kinds { get; private set; }

        public bool RequiresFullRefresh { get; private set; }

        public bool HasGroupImpact { get; private set; }

        public ISet<Guid> ObjectIds { get; } = new HashSet<Guid>();

        public ISet<int> LayerIndices { get; } = new HashSet<int>();

        public ISet<int> GroupIndices { get; } = new HashSet<int>();

        public ISet<int> AddedLayerIndices { get; } = new HashSet<int>();

        public bool IsEmpty =>
            Kinds == OutlinerStructureChangeKind.None &&
            ObjectIds.Count == 0 &&
            LayerIndices.Count == 0 &&
            GroupIndices.Count == 0;

        public string Cause => Kinds == OutlinerStructureChangeKind.None
            ? "unknown"
            : Kinds.ToString();

        public OutlinerStructureChange AddObject(
            OutlinerStructureChangeKind kind,
            Guid objectId,
            IEnumerable<int> layerIndices,
            IEnumerable<int> groupIndices,
            bool groupImpact = false)
        {
            Kinds |= kind;
            if (objectId != Guid.Empty)
                ObjectIds.Add(objectId);
            LayerIndices.UnionWith((layerIndices ?? Enumerable.Empty<int>())
                .Where(index => index >= 0));
            GroupIndices.UnionWith((groupIndices ?? Enumerable.Empty<int>())
                .Where(index => index >= 0));
            HasGroupImpact |= groupImpact;
            return this;
        }

        public OutlinerStructureChange ForceFull(
            OutlinerStructureChangeKind kind)
        {
            Kinds |= kind;
            RequiresFullRefresh = true;
            return this;
        }

        public OutlinerStructureChange AddLayers(
            OutlinerStructureChangeKind kind,
            IEnumerable<int> layerIndices)
        {
            Kinds |= kind;
            LayerIndices.UnionWith((layerIndices ?? Enumerable.Empty<int>())
                .Where(index => index >= 0));
            return this;
        }

        public OutlinerStructureChange AddLayerAddition(int layerIndex)
        {
            Kinds |= OutlinerStructureChangeKind.LayerTable;
            if (layerIndex >= 0)
            {
                LayerIndices.Add(layerIndex);
                AddedLayerIndices.Add(layerIndex);
            }
            return this;
        }

        public OutlinerStructureChange MarkGroupImpact(
            IEnumerable<int> groupIndices)
        {
            HasGroupImpact = true;
            GroupIndices.UnionWith((groupIndices ?? Enumerable.Empty<int>())
                .Where(index => index >= 0));
            return this;
        }

        public void Merge(OutlinerStructureChange other)
        {
            if (other == null)
                return;

            Kinds |= other.Kinds;
            RequiresFullRefresh |= other.RequiresFullRefresh;
            HasGroupImpact |= other.HasGroupImpact;
            ObjectIds.UnionWith(other.ObjectIds);
            LayerIndices.UnionWith(other.LayerIndices);
            GroupIndices.UnionWith(other.GroupIndices);
            AddedLayerIndices.UnionWith(other.AddedLayerIndices);
        }

        public OutlinerStructureChange Clone()
        {
            var clone = new OutlinerStructureChange();
            clone.Merge(this);
            return clone;
        }

        public void Clear()
        {
            Kinds = OutlinerStructureChangeKind.None;
            RequiresFullRefresh = false;
            HasGroupImpact = false;
            ObjectIds.Clear();
            LayerIndices.Clear();
            GroupIndices.Clear();
            AddedLayerIndices.Clear();
        }

        public static OutlinerStructureChange Full(
            OutlinerStructureChangeKind kind =
                OutlinerStructureChangeKind.ExplicitFull)
        {
            return new OutlinerStructureChange().ForceFull(kind);
        }
    }
}
