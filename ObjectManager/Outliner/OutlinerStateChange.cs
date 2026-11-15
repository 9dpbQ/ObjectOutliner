using System;
using System.Collections.Generic;
using System.Linq;

namespace ObjectManager.Outliner
{
    internal sealed class OutlinerStateChange : EventArgs
    {
        public bool CurrentLayerChanged { get; set; }

        public bool LayerStateChanged { get; private set; }

        public bool LayerPropertiesChanged { get; private set; }
        public bool LayerNamesChanged { get; private set; }

        public bool IsolateChanged { get; set; }

        public bool FavoritesChanged { get; private set; }

        public ISet<Guid> LayerIds { get; } = new HashSet<Guid>();

        public ISet<Guid> ObjectIds { get; } = new HashSet<Guid>();

        public ISet<Guid> GroupIds { get; } = new HashSet<Guid>();

        public bool IsEmpty =>
            !CurrentLayerChanged &&
            !LayerStateChanged &&
            !LayerPropertiesChanged &&
            !IsolateChanged &&
            !FavoritesChanged &&
            ObjectIds.Count == 0 &&
            GroupIds.Count == 0;

        public OutlinerStateChange AddLayers(IEnumerable<Guid> layerIds)
        {
            LayerStateChanged = true;
            LayerIds.UnionWith((layerIds ?? Enumerable.Empty<Guid>())
                .Where(id => id != Guid.Empty));
            return this;
        }

        public OutlinerStateChange AddLayerProperties(
            IEnumerable<Guid> layerIds)
        {
            LayerPropertiesChanged = true;
            LayerIds.UnionWith((layerIds ?? Enumerable.Empty<Guid>())
                .Where(id => id != Guid.Empty));
            return this;
        }

        public OutlinerStateChange AddObjects(IEnumerable<Guid> objectIds)
        {
            ObjectIds.UnionWith((objectIds ?? Enumerable.Empty<Guid>())
                .Where(id => id != Guid.Empty));
            return this;
        }

        public OutlinerStateChange AddLayerNames(IEnumerable<Guid> layerIds)
        {
            LayerNamesChanged = true;
            return AddLayerProperties(layerIds);
        }

        public OutlinerStateChange AddFavoriteLayers(
            IEnumerable<Guid> layerIds)
        {
            FavoritesChanged = true;
            return AddLayerProperties(layerIds);
        }

        public OutlinerStateChange AddFavoriteObjects(
            IEnumerable<Guid> objectIds)
        {
            FavoritesChanged = true;
            return AddObjects(objectIds);
        }

        public OutlinerStateChange AddFavoriteGroups(
            IEnumerable<Guid> groupIds)
        {
            FavoritesChanged = true;
            GroupIds.UnionWith((groupIds ?? Enumerable.Empty<Guid>())
                .Where(id => id != Guid.Empty));
            return this;
        }

        public void Merge(OutlinerStateChange other)
        {
            if (other == null)
                return;

            CurrentLayerChanged |= other.CurrentLayerChanged;
            LayerStateChanged |= other.LayerStateChanged;
            LayerPropertiesChanged |= other.LayerPropertiesChanged;
            LayerNamesChanged |= other.LayerNamesChanged;
            IsolateChanged |= other.IsolateChanged;
            FavoritesChanged |= other.FavoritesChanged;
            LayerIds.UnionWith(other.LayerIds);
            ObjectIds.UnionWith(other.ObjectIds);
            GroupIds.UnionWith(other.GroupIds);
        }

        public void Clear()
        {
            CurrentLayerChanged = false;
            LayerStateChanged = false;
            LayerPropertiesChanged = false;
            LayerNamesChanged = false;
            IsolateChanged = false;
            FavoritesChanged = false;
            LayerIds.Clear();
            ObjectIds.Clear();
            GroupIds.Clear();
        }
    }
}
