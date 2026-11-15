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
        private void RevealObject(Guid objectId, bool toggleSelection)
        {
            if (_documentModel == null)
                return;

            var objectModel = _documentModel.Objects.FirstOrDefault(
                item => item.Id == objectId);
            if (objectModel == null)
                return;

            var doc = RhinoDoc.FromRuntimeSerialNumber(_documentSerialNumber);
            if (doc == null)
                return;

            var generation = BeginRevealNavigation();
            ApplyRevealSelection(doc, objectId, toggleSelection);
            _viewState.RevealedObjectId = objectId;
            var layersById = _documentModel.Layers.ToDictionary(item => item.Id);
            var currentLayerId = objectModel.LayerId;
            var visited = new HashSet<Guid>();
            while (currentLayerId != Guid.Empty && visited.Add(currentLayerId))
            {
                _layerExpansionState[currentLayerId] = true;
                OutlinerLayerModel layer;
                if (!layersById.TryGetValue(currentLayerId, out layer))
                    break;
                currentLayerId = layer.ParentId;
            }

            if (_snapshot != null)
            {
                foreach (var groupId in _snapshot.GroupHierarchy
                    .GetCanonicalGroupPath(objectId))
                {
                    SetGroupExpansionState(
                        new OutlinerGroupSliceKey(
                            groupId,
                            objectModel.LayerId),
                        true);
                }
            }

            RefreshProjection();
            Application.Instance.AsyncInvoke(() =>
            {
                if (!IsCurrentReveal(generation))
                    return;

                if (_snapshot == null || _documentModel == null)
                {
                    FinishRevealNavigation(generation);
                    return;
                }

                var currentObjectModel = _documentModel.Objects.FirstOrDefault(
                    item => item.Id == objectId);
                if (currentObjectModel == null)
                {
                    FinishRevealNavigation(generation);
                    return;
                }

                ExpandRevealPath(currentObjectModel);
                _tree.Invalidate();

                Application.Instance.AsyncInvoke(() =>
                    CompleteRevealNavigation(generation, objectId));
            });
        }

        private int BeginRevealNavigation()
        {
            _revealInProgress = true;
            _selectionRefreshGeneration++;
            return ++_revealGeneration;
        }

        private void CancelRevealNavigation()
        {
            _revealGeneration++;
            _selectionRefreshGeneration++;
            _revealInProgress = false;
        }

        private bool IsCurrentReveal(int generation)
        {
            return !_isClosing &&
                _revealInProgress &&
                generation == _revealGeneration;
        }

        private void ApplyRevealSelection(
            RhinoDoc doc,
            Guid objectId,
            bool toggleSelection)
        {
            var rhinoObject = doc.Objects.FindId(objectId);
            if (!OutlinerSelectionController.IsSelectable(rhinoObject))
                return;

            var desiredIds = _selectionController.Read(doc);
            if (toggleSelection)
            {
                if (!desiredIds.Add(objectId))
                    desiredIds.Remove(objectId);
            }
            else
                desiredIds = new HashSet<Guid> { objectId };

            ApplyDocumentSelection(doc, desiredIds);
        }

        private void ExpandRevealPath(OutlinerObjectModel objectModel)
        {
            if (_snapshot == null)
                return;

            var layersById = _documentModel.Layers.ToDictionary(item => item.Id);
            var currentLayerId = objectModel.LayerId;
            var visited = new HashSet<Guid>();
            while (currentLayerId != Guid.Empty && visited.Add(currentLayerId))
            {
                TreeGridItem layerItem;
                if (_snapshot.LayerItems.TryGetValue(currentLayerId, out layerItem))
                    layerItem.Expanded = true;

                OutlinerLayerModel layer;
                if (!layersById.TryGetValue(currentLayerId, out layer))
                    break;
                currentLayerId = layer.ParentId;
            }

            foreach (var groupId in _snapshot.GroupHierarchy
                .GetCanonicalGroupPath(objectModel.Id))
            {
                var key = new OutlinerGroupSliceKey(groupId, objectModel.LayerId);
                SetGroupExpansionState(key, true);
                TreeGridItem groupItem;
                if (_snapshot.GroupSliceItems.TryGetValue(key, out groupItem))
                    groupItem.Expanded = true;
            }


            if (_snapshot.ObjectItems.TryGetValue(
                    objectModel.Id,
                    out var objectItem))
            {
                var parent = objectItem.Parent as TreeGridItem;
                while (parent != null)
                {
                    var parentNode = parent.Tag as OutlinerNodeReference;
                    if (parentNode?.Kind == OutlinerNodeKind.Object)
                    {
                        parent.Expanded = true;
                        _historyExpansionState[parentNode.Id] = true;
                    }
                    parent = parent.Parent as TreeGridItem;
                }
            }
            InvalidateVisibleSelectionRows();
        }

        private void CompleteRevealNavigation(int generation, Guid objectId)
        {
            if (!IsCurrentReveal(generation))
                return;

            TreeGridItem targetItem;
            if (_snapshot == null ||
                !_snapshot.ObjectItems.TryGetValue(objectId, out targetItem))
            {
                FinishRevealNavigation(generation);
                return;
            }

            SynchronizeSelectionFromDocument();
            var visibleItems = new List<TreeGridItem>();
            AddVisibleItems(_snapshot.Roots, visibleItems);
            var row = visibleItems.IndexOf(targetItem);
            if (row >= 0)
                _tree.ScrollToRow(row);

            FinishRevealNavigation(generation);
        }

        private void FinishRevealNavigation(int generation)
        {
            if (!IsCurrentReveal(generation))
                return;

            _revealInProgress = false;
            _selectionRefreshGeneration++;
            if (!_viewState.HasSearch)
                _viewState.ClearReveal();
            SynchronizeSelectionFromDocument();
        }
    }
}
