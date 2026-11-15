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
        private void AttachDocument(uint documentSerialNumber)
        {
            _diagnosticSelectionQueuedAt = 0;
            _diagnosticStructureQueuedAt = 0;
            _diagnosticSelectionRequests = 0;
            _diagnosticSuppressedDocumentEvents = 0;
            CloseNotePopup();
            if (_documentSerialNumber == documentSerialNumber && _documentEvents != null)
                return;

            var documentChanged = _documentSerialNumber != documentSerialNumber;
            if (documentChanged)
            {
                EndStateGesture();
                CancelReferencePointer();
            }
            DetachDocumentEvents();
            _documentSerialNumber = documentSerialNumber;
            if (documentChanged)
            {
                _documentModel = null;
                _snapshot = null;
                InvalidateVisibleSelectionRows();
                _provisionalNames.Clear();
                _layerExpansionState.Clear();
                _groupExpansionState.Clear();
                _historyExpansionState.Clear();
                ResetWorkspaceViewStateForDocument();
                CancelPendingFilterNavigation();
                _selectedObjectIds.Clear();
                _selectedLayerIds.Clear();
                _primaryLayerId = Guid.Empty;
                _pendingEditLayerId = Guid.Empty;
                _nameEditGeneration++;
                _activeNameEditTarget = null;
                _primaryNameEditTarget = null;
                _forceFullObjectLoad = false;
                _fullObjectLoadCompleted = false;
                _workspaceStateApplied = false;
                _largeDocumentDeferred = false;
                _largeDocumentObjectCount = 0;
                _loadedLargeLayerIndices.Clear();
                _missingSelectedObjectIds.Clear();
                _pendingStructureChange.Clear();
                _treeRefreshRequestCount = 0;
                CancelHistoryInspection(true);
                ResetIsolateTracking();
                CancelRevealNavigation();
            }

            if (!_isPanelVisible ||
                !IsDocumentReady(documentSerialNumber) ||
                RhinoDoc.FromRuntimeSerialNumber(documentSerialNumber) == null)
                return;

            ApplyWorkspaceState();

            _documentEvents = new DocumentEventBridge(documentSerialNumber);
            _documentEvents.StructureChanged += OnDocumentStructureChanged;
            _documentEvents.StateChanged += OnDocumentStateChanged;
            _documentEvents.SelectionChanged += OnDocumentSelectionChanged;
            _documentEvents.CurrentLayerChanged += OnDocumentCurrentLayerChanged;
        }

        private void ResetWorkspaceViewStateForDocument()
        {
            _viewState.SearchText = string.Empty;
            _viewState.LayerFilter.Reset();
            _viewState.ContentFilter.Reset();
            _viewState.DisplayMode = OutlinerDisplayMode.All;
            _viewState.CompactHistory = true;
            _viewState.ClearReveal();
            _viewState.ClearTemporaryFilterReveal();

            _applyingWorkspaceState = true;
            try
            {
                if (_searchBox != null)
                    _searchBox.Text = string.Empty;
                if (_historyStackButton != null)
                    _historyStackButton.Checked = true;
                UpdateFilterControlValues();
                UpdateDisplayModePresentation();
            }
            finally
            {
                _applyingWorkspaceState = false;
            }
        }

        private void DetachDocumentEvents()
        {
            if (_documentEvents == null)
                return;

            _documentEvents.StructureChanged -= OnDocumentStructureChanged;
            _documentEvents.StateChanged -= OnDocumentStateChanged;
            _documentEvents.SelectionChanged -= OnDocumentSelectionChanged;
            _documentEvents.CurrentLayerChanged -= OnDocumentCurrentLayerChanged;
            _documentEvents.Dispose();
            _documentEvents = null;
        }

        private void OnDocumentStructureChanged(
            object sender,
            OutlinerStructureChange change)
        {
            QueueStructureRefresh(change);
        }

        private void OnDocumentStateChanged(
            object sender,
            OutlinerStateChange change)
        {
            if (change == null || change.IsEmpty)
                return;

            var queuedChange = new OutlinerStateChange();
            queuedChange.Merge(change);
            if (_activeStateChange != null)
                queuedChange.Merge(_activeStateChange);
            QueueStateRefresh(queuedChange);
        }

        private void OnFavoriteGroupsChanged(
            object sender,
            FavoriteGroupsChangedEventArgs e)
        {
            if (e == null || e.DocumentSerialNumber != _documentSerialNumber)
                return;

            QueueStateRefresh(
                new OutlinerStateChange().AddFavoriteGroups(e.GroupIds));
        }

        private void OnGroupOrderChanged(
            object sender,
            GroupOrderChangedEventArgs e)
        {
            if (e == null ||
                e.DocumentSerialNumber != _documentSerialNumber ||
                _isClosing ||
                !_isPanelVisible)
            {
                return;
            }

            Application.Instance.AsyncInvoke(() =>
            {
                if (!_isClosing &&
                    _isPanelVisible &&
                    e.DocumentSerialNumber == _documentSerialNumber &&
                    _documentModel != null)
                {
                    RefreshProjection();
                }
            });
        }

        private void OnOutlinerNamesChanged(
            object sender,
            OutlinerNamesChangedEventArgs e)
        {
            if (e == null || e.DocumentSerialNumber != _documentSerialNumber)
                return;

            if (e.ObjectIds.Count > 256)
            {
                QueueTreeRefresh();
                return;
            }

            var doc = RhinoDoc.FromRuntimeSerialNumber(_documentSerialNumber);
            if (doc == null)
                return;

            var change = new OutlinerStructureChange();
            if (e.GroupIds.Count > 0)
            {
                var remaining = new HashSet<Guid>(e.GroupIds);
                var groupIndices = new List<int>();
                for (var index = 0; index < doc.Groups.Count && remaining.Count > 0; index++)
                {
                    var group = doc.Groups.FindIndex(index);
                    if (group != null && !group.IsDeleted && remaining.Remove(group.Id))
                        groupIndices.Add(index);
                }
                if (remaining.Count > 0)
                {
                    QueueTreeRefresh();
                    return;
                }
                change.AddLayers(OutlinerStructureChangeKind.GroupTable, Array.Empty<int>())
                    .MarkGroupImpact(groupIndices);
            }
            foreach (var objectId in e.ObjectIds)
            {
                var rhinoObject = doc.Objects.FindId(objectId);
                if (rhinoObject == null || rhinoObject.IsDeleted)
                    continue;

                change.AddObject(
                    OutlinerStructureChangeKind.ObjectAttributes,
                    objectId,
                    new[] { rhinoObject.Attributes.LayerIndex },
                    rhinoObject.Attributes.GetGroupList(),
                    false);
            }

            if (!change.IsEmpty)
                QueueStructureRefresh(change);
        }

        private void OnAppSettingsChanged(object sender, EventArgs e)
        {
            if (!_icons.RefreshTheme() || _isClosing || !_isPanelVisible)
                return;

            QueueTreeRefresh();
        }

        private void OnDocumentCurrentLayerChanged(object sender, EventArgs e)
        {
            QueueStateRefresh(new OutlinerStateChange
            {
                CurrentLayerChanged = true
            });
        }

        private void OnDocumentSelectionChanged(object sender, EventArgs e)
        {
            if (OutlinerDiagnosticMode.IsEnabled && _syncingDocumentSelection)
                _diagnosticSuppressedDocumentEvents++;
            if (!_syncingDocumentSelection)
                QueueSelectionRefresh();
        }
    }
}
