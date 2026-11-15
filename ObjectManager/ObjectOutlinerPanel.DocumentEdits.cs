using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using Eto.Drawing;
using Eto.Forms;
using Rhino;
using Rhino.DocObjects;
using Rhino.UI;
using ObjectManager.Outliner;

namespace ObjectManager
{
    public sealed partial class ObjectOutlinerPanel
    {
        private void RenameLayer(
            RhinoDoc doc,
            TreeGridItem item,
            OutlinerNodeReference node,
            string newName)
        {
            var layer = doc.Layers.FindId(node.Id);
            if (layer == null)
                return;

            if (string.IsNullOrWhiteSpace(newName))
            {
                item.SetValue(1, layer.Name);
                return;
            }

            if (string.Equals(layer.Name, newName, StringComparison.Ordinal))
                return;

            var renamed = ModifyLayer(
                doc,
                node.Id,
                OutlinerStrings.UndoRenameLayer,
                target => target.Name = newName);
            if (!renamed)
            {
                item.SetValue(1, layer.Name);
                MessageBox.Show(
                    this,
                    OutlinerStrings.InvalidLayerName,
                    MessageBoxType.Information);
            }
        }

        private void RenameObject(
            RhinoDoc doc,
            TreeGridItem item,
            OutlinerNodeReference node,
            string newName)
        {
            var rhinoObject = doc.Objects.FindId(node.Id);
            if (rhinoObject == null)
                return;

            if (string.IsNullOrWhiteSpace(newName))
            {
                item.SetValue(1, GetObjectDisplayName(rhinoObject));
                return;
            }

            var attributes = rhinoObject.Attributes.Duplicate();
            attributes.Name = newName;
            ObjectNamingService.ClearManagedName(attributes);
            var renamed = ModifyDocument(
                doc,
                OutlinerStrings.UndoRenameObject,
                () => doc.Objects.ModifyAttributes(node.Id, attributes, true),
                false);
            if (!renamed)
                item.SetValue(1, GetObjectDisplayName(rhinoObject));
        }

        private void RenameGroup(
            RhinoDoc doc,
            TreeGridItem item,
            OutlinerNodeReference node,
            string newName)
        {
            var group = doc.Groups.FindIndex(node.NativeIndex);
            var model = _documentModel?.Groups.FirstOrDefault(
                candidate => candidate.Id == node.Id);
            if (group == null || group.IsDeleted || group.Id != node.Id)
            {
                QueueTreeRefresh();
                return;
            }

            if (string.IsNullOrWhiteSpace(newName))
            {
                item.SetValue(1, model?.Name ?? string.Empty);
                QueueTreeRefresh();
                return;
            }

            var currentName = doc.Groups.GroupName(node.NativeIndex);
            if (string.Equals(currentName, newName, StringComparison.Ordinal))
            {
                QueueTreeRefresh();
                return;
            }

            var renamed = ModifyDocument(
                doc,
                OutlinerStrings.UndoRenameGroup,
                () => doc.Groups.ChangeGroupName(node.NativeIndex, newName),
                false);
            if (renamed)
                return;

            item.SetValue(1, model?.Name ?? currentName ?? string.Empty);
            MessageBox.Show(
                this,
                OutlinerStrings.InvalidGroupName,
                MessageBoxType.Information);
            QueueTreeRefresh();
        }

        private string GetObjectDisplayName(RhinoObject rhinoObject)
        {
            if (ObjectNamingService.TryResolveName(
                    rhinoObject,
                    out var resolvedName))
            {
                return resolvedName.Name;
            }

            return _provisionalNames.TryGetValue(rhinoObject.Id, out var name)
                ? name
                : string.Empty;
        }

        private bool ModifyLayer(
            RhinoDoc doc,
            Guid layerId,
            string undoDescription,
            Action<Layer> update)
        {
            var layer = doc.Layers.FindId(layerId);
            if (layer == null || layer.IsReference)
                return false;

            return ModifyStateDocument(doc, undoDescription,
                new OutlinerStateChange().AddLayerNames(new[] { layerId }), () =>
            {
                update(layer);
                return doc.Layers.Modify(layer, layerId, true);
            });
        }

        private void ToggleFavorite(
            RhinoDoc doc,
            OutlinerNodeReference node)
        {
            if (doc == null || node == null || _documentModel == null)
                return;

            if (node.Kind == OutlinerNodeKind.Layer &&
                _documentModel.LayersById.TryGetValue(
                    node.Id,
                    out var layerModel) &&
                layerModel.CanEditFavorite)
            {
                var layer = doc.Layers.FindId(node.Id);
                if (layer == null || layer.IsReference)
                    return;

                var favorite = !layerModel.IsFavorite;
                var change = new OutlinerStateChange()
                    .AddFavoriteLayers(new[] { node.Id });
                ModifyStateDocument(
                    doc,
                    OutlinerStrings.UndoChangeFavorite,
                    change,
                    () =>
                    {
                        var updatedLayer = CreateDetachedLayerSettings(layer);
                        updatedLayer.SetUserString(
                            FavoriteCodec.UserStringKey,
                            FavoriteCodec.Write(favorite));
                        return doc.Layers.Modify(
                            updatedLayer,
                            node.Id,
                            true);
                    });
                return;
            }

            if (node.Kind == OutlinerNodeKind.Object &&
                _documentModel.ObjectsById.TryGetValue(
                    node.Id,
                    out var objectModel) &&
                objectModel.CanEditFavorite)
            {
                var rhinoObject = doc.Objects.FindId(node.Id);
                if (rhinoObject == null ||
                    rhinoObject.IsDeleted ||
                    rhinoObject.IsReference)
                {
                    return;
                }

                var attributes = rhinoObject.Attributes.Duplicate();
                attributes.SetUserString(
                    FavoriteCodec.UserStringKey,
                    FavoriteCodec.Write(!objectModel.IsFavorite));
                ModifyStateDocument(
                    doc,
                    OutlinerStrings.UndoChangeFavorite,
                    new OutlinerStateChange()
                        .AddFavoriteObjects(new[] { node.Id }),
                    () => doc.Objects.ModifyAttributes(
                        node.Id,
                        attributes,
                        true));
                return;
            }

            if (node.Kind == OutlinerNodeKind.GroupSlice &&
                _documentModel.GroupsById.TryGetValue(
                    node.Id,
                    out var groupModel) &&
                groupModel.CanEditFavorite &&
                _favoriteStore != null)
            {
                ModifyStateDocument(
                    doc,
                    OutlinerStrings.UndoChangeFavorite,
                    new OutlinerStateChange()
                        .AddFavoriteGroups(new[] { node.Id }),
                    () => _favoriteStore.SetFavorites(
                        doc,
                        new[] { node.Id },
                        !groupModel.IsFavorite));
            }
        }

        private static Layer CreateDetachedLayerSettings(Layer source)
        {
            var copy = new Layer();
            copy.CopyAttributesFrom(source);
            copy.Name = source.Name;
            copy.ParentLayerId = source.ParentLayerId;

            var userStrings = source.GetUserStrings();
            if (userStrings == null)
                return copy;

            foreach (var key in userStrings.AllKeys)
            {
                if (!string.IsNullOrEmpty(key))
                    copy.SetUserString(key, userStrings[key]);
            }
            return copy;
        }

        private bool ModifyStateDocument(
            RhinoDoc doc,
            string undoDescription,
            OutlinerStateChange change,
            Func<bool> update)
        {
            var previousChange = _activeStateChange;
            _activeStateChange = change;
            bool changed;
            try
            {
                changed = ModifyDocument(
                    doc,
                    undoDescription,
                    update,
                    false);
            }
            finally
            {
                _activeStateChange = previousChange;
            }

            if (changed)
                QueueStateRefresh(change);
            return changed;
        }

        private bool ApplyStateDocumentChange(
            OutlinerStateChange change,
            Func<bool> update)
        {
            var previousChange = _activeStateChange;
            _activeStateChange = change;
            bool changed;
            try
            {
                changed = update();
            }
            finally
            {
                _activeStateChange = previousChange;
            }

            if (changed)
                QueueStateRefresh(change);
            return changed;
        }

        private bool ModifyDocument(
            RhinoDoc doc,
            string undoDescription,
            Func<bool> update,
            bool queueFullRefresh = true)
        {
            var undoRecord = doc.BeginUndoRecord(undoDescription);
            bool changed;
            try
            {
                changed = update();
            }
            finally
            {
                if (undoRecord != 0)
                    doc.EndUndoRecord(undoRecord);
            }

            if (changed)
            {
                doc.Views.Redraw();
                if (queueFullRefresh)
                    QueueTreeRefresh();
            }
            return changed;
        }

        private RhinoDoc GetEditableDocument()
        {
            var doc = RhinoDoc.FromRuntimeSerialNumber(_documentSerialNumber);
            return doc != null && !doc.IsCommandRunning ? doc : null;
        }

        private void OnCellFormatting(object sender, GridCellFormatEventArgs e)
        {
            var item = e.Item as TreeGridItem;
            if (item == null ||
                item.Values == null ||
                item.Values.Length <= OutlinerValueIndex.IsUnavailable)
                return;

            if (item.Values[OutlinerValueIndex.IsUnavailable] is bool &&
                (bool)item.Values[OutlinerValueIndex.IsUnavailable])
            {
                e.ForegroundColor = OutlinerTextColors.GetText(true);
                return;
            }

            var node = item.Tag as OutlinerNodeReference;
            if (e.Column == _nameColumn && item.Values.Length > OutlinerValueIndex.DimName &&
                Equals(item.Values[OutlinerValueIndex.DimName], true))
                e.ForegroundColor = OutlinerTextColors.GetText(true);
            else if (node != null && node.IsObjectReference && e.Column == _nameColumn)
                e.ForegroundColor = OutlinerTextColors.Link;
        }

        private static bool IsUnavailable(GridItem item)
        {
            return item != null &&
                item.Values != null &&
                item.Values.Length > OutlinerValueIndex.IsUnavailable &&
                item.Values[OutlinerValueIndex.IsUnavailable] is bool &&
                (bool)item.Values[OutlinerValueIndex.IsUnavailable];
        }
    }
}
