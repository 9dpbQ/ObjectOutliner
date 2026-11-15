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
        private void QueueSelectionRefresh()
        {
            if (!OutlinerDiagnosticMode.IsEnabled)
            {
                _diagnosticSelectionQueuedAt = 0;
                _diagnosticSelectionRequests = 0;
            }
            if (OutlinerDiagnosticMode.IsEnabled && !_isClosing && !_revealInProgress)
            {
                _diagnosticSelectionRequests++;
                if (_diagnosticSelectionQueuedAt == 0)
                    _diagnosticSelectionQueuedAt = Stopwatch.GetTimestamp();
            }
            if (_selectionRefreshQueued || _isClosing || _revealInProgress)
                return;

            var generation = _selectionRefreshGeneration;
            _selectionRefreshQueued = true;
            Application.Instance.AsyncInvoke(() =>
            {
                using (var diagnostic = OutlinerDiagnosticScope.Start(
                    "dispatch-selection", _documentSerialNumber,
                    _diagnosticSelectionQueuedAt, _diagnosticSelectionRequests))
                {
                    _diagnosticSelectionQueuedAt = 0;
                    _diagnosticSelectionRequests = 0;
                    diagnostic?.Metrics.Detail("supersededByStructure", _treeRefreshQueued ? 1 : 0);
                    _selectionRefreshQueued = false;
                    if (!_isClosing &&
                        !_revealInProgress &&
                        generation == _selectionRefreshGeneration &&
                        !_treeRefreshQueued)
                        SynchronizeSelectionFromDocument();
                }
            });
        }

        private void RunProgrammaticTreeMutation(
            Action update,
            bool reconcileAfterCompletion = true)
        {
            RunProgrammaticTreeSelection(update, reconcileAfterCompletion);
        }

        private void SetProgrammaticTreeSelection(Action update)
        {
            RunProgrammaticTreeSelection(update, false);
        }

        private void RunProgrammaticTreeSelection(
            Action update,
            bool reconcileAfterCompletion)
        {
            if (update == null)
                return;

            var generation = _selectionEventGate.BeginTreeWrite();
            try
            {
                update();
            }
            catch
            {
                _selectionEventGate.CancelTreeWrite();
                throw;
            }

            Application.Instance.AsyncInvoke(() =>
                Application.Instance.AsyncInvoke(() =>
                    CompleteProgrammaticTreeSelection(
                        generation,
                        reconcileAfterCompletion)));
        }

        private void CompleteProgrammaticTreeSelection(
            int generation,
            bool reconcileAfterCompletion)
        {
            if (_isClosing ||
                !_selectionEventGate.CompleteTreeWrite(generation))
                return;

            UpdateToolbarButtonState();
            if (reconcileAfterCompletion)
                QueueSelectionRefresh();
        }

        private void CancelProgrammaticTreeSelection()
        {
            _selectionEventGate.CancelTreeWrite();
        }

        private void OnTreeSelectionChanged(object sender, EventArgs e)
        {
            if (_selectionEventGate.SuppressTreeSelection)
            {
                _programmaticSelectionEventCount++;
                return;
            }

            UpdateToolbarButtonState();
            if (_commandInputPointerNode != null ||
                _revealInProgress ||
                _snapshot == null)
                return;

            if (_potentialExpansionItem != null)
            {
                _deferredPointerSelectionChange = true;
                return;
            }

            ApplyTreeSelectionToDocument();
        }

        private void ApplyTreeSelectionToDocument()
        {
            using (var diagnostic = OutlinerDiagnosticScope.Start("tree-to-document", _documentSerialNumber))
            {
                if (_selectionEventGate.SuppressTreeSelection ||
                    _revealInProgress ||
                    _snapshot == null)
                    return;

                var doc = RhinoDoc.FromRuntimeSerialNumber(_documentSerialNumber);
                if (doc == null)
                    return;

                if (doc.IsCommandRunning)
                {
                    QueueSelectionRefresh();
                    return;
                }

                var selectedItems = _tree.SelectedItems
                    .OfType<TreeGridItem>()
                    .ToList();
                var primarySelectedItem = _tree.SelectedItem as TreeGridItem;
                if (selectedItems.Any(item => !_snapshot.Contains(item)) ||
                    (primarySelectedItem != null &&
                     !_snapshot.Contains(primarySelectedItem)))
                {
                    QueueSelectionRefresh();
                    return;
                }
                var primaryTarget = NameEditTarget.FromItem(primarySelectedItem);
                if (primaryTarget != null)
                    _primaryNameEditTarget = primaryTarget;
                if (selectedItems.Any(IsUnavailable))
                {
                    SynchronizeSelectionFromDocument();
                    return;
                }

                var selectedNodes = selectedItems
                    .Select(item => item.Tag as OutlinerNodeReference)
                    .Where(item => item != null)
                    .ToList();
                if (selectedNodes.Any(item =>
                    item.IsReadOnlyRelationship ||
                    item.Kind == OutlinerNodeKind.DeferredLayerObjects))
                {
                    QueueSelectionRefresh();
                    return;
                }

                var selectedObjectIds = selectedNodes
                    .Where(item => item.Kind == OutlinerNodeKind.Object)
                    .Select(item => item.Id)
                    .Distinct()
                    .ToList();
                var selectedGroupIds = selectedNodes
                    .Where(item => item.Kind == OutlinerNodeKind.GroupSlice)
                    .Select(item => item.Id)
                    .Distinct()
                    .ToList();
                foreach (var groupId in selectedGroupIds)
                {
                    foreach (var memberId in _selectionController.GetSelectableIds(
                        doc,
                        GetGroupMemberIds(groupId)))
                    {
                        if (!selectedObjectIds.Contains(memberId))
                            selectedObjectIds.Add(memberId);
                    }
                }

                _selectedLayerIds.Clear();
                foreach (var layerNode in selectedNodes.Where(item => item.Kind == OutlinerNodeKind.Layer))
                    _selectedLayerIds.Add(layerNode.Id);

                var primarySelectedNode = primarySelectedItem?.Tag as
                    OutlinerNodeReference;
                var primaryLayer = primarySelectedNode != null &&
                    primarySelectedNode.Kind == OutlinerNodeKind.Layer
                        ? primarySelectedNode
                        : selectedNodes.LastOrDefault(
                            item => item.Kind == OutlinerNodeKind.Layer);
                if (primaryLayer != null)
                    _primaryLayerId = primaryLayer.Id;

                if (selectedObjectIds.Count == 0 &&
                    selectedNodes.Any(item => item.Kind == OutlinerNodeKind.Layer))
                {
                    QueueSelectionRefresh();
                    return;
                }

                ApplyDocumentSelection(doc, selectedObjectIds);

                SynchronizeSelectionFromDocument();
            }
        }

        private bool ApplyDocumentSelection(
            RhinoDoc doc,
            IEnumerable<Guid> objectIds)
        {
            if (doc == null)
                return false;

            using (var diagnostic = OutlinerDiagnosticScope.Start("document-selection-write", doc.RuntimeSerialNumber))
            {
                OutlinerSelectionResult result;
                _syncingDocumentSelection = true;
                try
                {
                    result = diagnostic == null
                        ? _selectionController.Apply(doc, objectIds)
                        : diagnostic.Metrics.MeasureDetail("nativeSelection", () => _selectionController.Apply(doc, objectIds));
                    if (result.Changed)
                    {
                        if (diagnostic == null)
                            doc.Views.Redraw();
                        else
                            diagnostic.Metrics.MeasureDetail("nativeRedraw", () => doc.Views.Redraw());
                    }
                }
                finally
                {
                    _syncingDocumentSelection = false;
                }

                return result.FullyApplied;
            }
        }

        private void RestoreSelectableDocumentSelection(
            RhinoDoc doc,
            IEnumerable<Guid> objectIds)
        {
            ApplyDocumentSelection(
                doc,
                _selectionController.GetSelectableIds(doc, objectIds));
        }
    }
}
