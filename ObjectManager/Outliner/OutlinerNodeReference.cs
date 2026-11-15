using System;

namespace ObjectManager.Outliner
{
    internal struct OutlinerGroupSliceKey : IEquatable<OutlinerGroupSliceKey>
    {
        public OutlinerGroupSliceKey(Guid groupId, Guid layerId)
        {
            GroupId = groupId;
            LayerId = layerId;
        }

        public Guid GroupId { get; }

        public Guid LayerId { get; }

        public bool Equals(OutlinerGroupSliceKey other)
        {
            return GroupId == other.GroupId && LayerId == other.LayerId;
        }

        public override bool Equals(object obj)
        {
            return obj is OutlinerGroupSliceKey &&
                Equals((OutlinerGroupSliceKey)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (GroupId.GetHashCode() * 397) ^ LayerId.GetHashCode();
            }
        }
    }

    internal enum OutlinerNodeKind
    {
        Layer,
        Object,
        GroupSlice,
        GroupMemberReference,
        HistorySection,
        HistoryReference,
        BlockDefinitionComponent,
        DeferredLayerObjects
    }

    internal enum OutlinerHistorySectionKind
    {
        None,
        Parents,
        Children
    }

    internal sealed class OutlinerNodeReference
    {
        private OutlinerNodeReference(
            OutlinerNodeKind kind,
            Guid id,
            int layerIndex,
            Guid ownerId,
            int nativeIndex,
            OutlinerHistorySectionKind historySection)
        {
            Kind = kind;
            Id = id;
            LayerIndex = layerIndex;
            OwnerId = ownerId;
            NativeIndex = nativeIndex;
            HistorySection = historySection;
        }

        public static OutlinerNodeReference ForLayer(Guid id, int layerIndex)
        {
            return new OutlinerNodeReference(
                OutlinerNodeKind.Layer,
                id,
                layerIndex,
                Guid.Empty,
                -1,
                OutlinerHistorySectionKind.None);
        }

        public static OutlinerNodeReference ForObject(
            Guid id,
            int layerIndex,
            Guid containerId)
        {
            return new OutlinerNodeReference(
                OutlinerNodeKind.Object,
                id,
                layerIndex,
                containerId,
                -1,
                OutlinerHistorySectionKind.None);
        }

        public static OutlinerNodeReference ForGroupSlice(
            Guid groupId,
            Guid layerId,
            int layerIndex,
            int groupIndex)
        {
            return new OutlinerNodeReference(
                OutlinerNodeKind.GroupSlice,
                groupId,
                layerIndex,
                layerId,
                groupIndex,
                OutlinerHistorySectionKind.None);
        }

        public static OutlinerNodeReference ForGroupMemberReference(
            Guid objectId,
            Guid groupId,
            int layerIndex,
            int groupIndex)
        {
            return new OutlinerNodeReference(
                OutlinerNodeKind.GroupMemberReference,
                objectId,
                layerIndex,
                groupId,
                groupIndex,
                OutlinerHistorySectionKind.None);
        }

        public static OutlinerNodeReference ForHistorySection(
            Guid objectId,
            int layerIndex,
            OutlinerHistorySectionKind section)
        {
            return new OutlinerNodeReference(
                OutlinerNodeKind.HistorySection,
                objectId,
                layerIndex,
                objectId,
                -1,
                section);
        }

        public static OutlinerNodeReference ForHistoryReference(
            Guid targetObjectId,
            Guid sourceObjectId,
            int layerIndex,
            OutlinerHistorySectionKind section)
        {
            return new OutlinerNodeReference(
                OutlinerNodeKind.HistoryReference,
                targetObjectId,
                layerIndex,
                sourceObjectId,
                -1,
                section);
        }

        public static OutlinerNodeReference ForBlockDefinitionComponent(
            Guid componentObjectId,
            Guid instanceObjectId,
            int layerIndex,
            int componentIndex)
        {
            return new OutlinerNodeReference(
                OutlinerNodeKind.BlockDefinitionComponent,
                componentObjectId,
                layerIndex,
                instanceObjectId,
                componentIndex,
                OutlinerHistorySectionKind.None);
        }

        public static OutlinerNodeReference ForDeferredLayerObjects(
            Guid layerId,
            int layerIndex)
        {
            return new OutlinerNodeReference(
                OutlinerNodeKind.DeferredLayerObjects,
                layerId,
                layerIndex,
                layerId,
                -1,
                OutlinerHistorySectionKind.None);
        }

        public OutlinerNodeKind Kind { get; }

        public Guid Id { get; }

        public int LayerIndex { get; }

        public Guid OwnerId { get; }

        public int NativeIndex { get; }

        public OutlinerHistorySectionKind HistorySection { get; }

        public bool IsCanonical =>
            Kind == OutlinerNodeKind.Layer || Kind == OutlinerNodeKind.Object;

        public bool IsObjectReference =>
            Kind == OutlinerNodeKind.GroupMemberReference ||
            Kind == OutlinerNodeKind.HistoryReference;

        public bool IsReadOnlyRelationship =>
            IsObjectReference ||
            Kind == OutlinerNodeKind.HistorySection ||
            Kind == OutlinerNodeKind.BlockDefinitionComponent;
    }
}
