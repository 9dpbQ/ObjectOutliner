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
        public void PanelShown(uint documentSerialNumber, ShowPanelReason reason)
        {
            _treeGuides.Show(_tree);
            _isClosing = false;
            _isPanelVisible = true;
            AttachDocument(documentSerialNumber);
            if (IsDocumentReady(documentSerialNumber))
                QueueTreeRefresh();
            else
                ShowDeferredStatus(OutlinerStrings.OpeningDocument);
        }

        public void PanelHidden(uint documentSerialNumber, ShowPanelReason reason)
        {
            _treeGuides.Hide();
            EndStateGesture();
            CancelReferencePointer();
            _isPanelVisible = false;
            CloseContentFilterPopup();
            CloseChoicePopup();
            CloseNotePopup();
            _nameCell?.CancelActiveEdit();
            EndTreePan();
            ClearPotentialExpansionGesture();
            ClearCommandInputPointer();
            CancelExpansionSelectionProtection();
            _treeRefreshQueued = false;
            _treeRefreshRequestCount = 0;
            _pendingStructureChange.Clear();
            ClearPendingStateRefresh();
            _selectionRefreshQueued = false;
            _selectionRefreshGeneration++;
            _filteredSelectionProjectionQueued = false;
            CancelPendingFilterNavigation();
            CancelHistoryInspection(false);
            CancelProgrammaticTreeSelection();
            _treeRefreshTimer.Stop();
            _searchRefreshTimer.Stop();
            DetachDocumentEvents();
        }

        public void PanelClosing(uint documentSerialNumber, bool onCloseDocument)
        {
            _treeGuides.Hide();
            if (!onCloseDocument)
                CaptureWorkspaceState();
            if (onCloseDocument)
            {
                ResetIsolateTracking();
                _relationshipHub?.ForgetDocument(documentSerialNumber);
            }
            DisposeIsolationLifecycle();
            _isClosing = true;
            _isPanelVisible = false;
            CloseContentFilterPopup();
            CloseChoicePopup();
            CloseNotePopup();
            _nameEditGeneration++;
            _activeNameEditTarget = null;
            _primaryNameEditTarget = null;
            _nameCell?.CancelActiveEdit();
            EndStateGesture();
            CancelReferencePointer();
            EndTreePan();
            _treePanAdapter.Dispose();
            if (_relationshipHub != null)
            {
                _relationshipHub.GroupsCopied -= OnGroupsCopied;
                _relationshipHub.HistoryObserved -= OnHistoryObserved;
            }
            CancelProgrammaticTreeSelection();
            _treeRefreshTimer.Stop();
            _treeRefreshTimer.Elapsed -= OnTreeRefreshTimerElapsed;
            _stateRefreshTimer.Stop();
            _stateRefreshTimer.Elapsed -= OnStateRefreshTimerElapsed;
            _searchRefreshTimer.Stop();
            _searchRefreshTimer.Elapsed -= OnSearchRefreshTimerElapsed;
            _filteredSelectionProjectionQueued = false;
            CancelPendingFilterNavigation();
            CancelRevealNavigation();
            CancelHistoryInspection(true);
            ClearDragState();
            CancelExpansionSelectionProtection();
            DetachDocumentEvents();
            if (_documentLoadMonitor != null)
            {
                _documentLoadMonitor.DocumentOpening -= OnDocumentOpening;
                _documentLoadMonitor.DocumentReady -= OnDocumentReady;
            }
            if (_favoriteStore != null)
                _favoriteStore.Changed -= OnFavoriteGroupsChanged;
            if (_nameStore != null)
                _nameStore.Changed -= OnOutlinerNamesChanged;
            if (_groupOrderStore != null)
                _groupOrderStore.Changed -= OnGroupOrderChanged;
            RhinoApp.AppSettingsChanged -= OnAppSettingsChanged;
        }

        private void OnDocumentOpening(
            object sender,
            DocumentLoadStateEventArgs e)
        {
            if (e.DocumentSerialNumber != _documentSerialNumber)
                return;

            EndStateGesture();
            CancelReferencePointer();
            _treeRefreshQueued = false;
            _treeRefreshRequestCount = 0;
            _pendingStructureChange.Clear();
            ClearPendingStateRefresh();
            _selectionRefreshQueued = false;
            _filteredSelectionProjectionQueued = false;
            CancelPendingFilterNavigation();
            _viewState.ClearTemporaryFilterReveal();
            CancelHistoryInspection(true);
            _treeRefreshTimer.Stop();
            _searchRefreshTimer.Stop();
            _selectionRefreshGeneration++;
            CancelProgrammaticTreeSelection();
            ClearPotentialExpansionGesture();
            ClearCommandInputPointer();
            CancelExpansionSelectionProtection();
            _forceFullObjectLoad = false;
            _fullObjectLoadCompleted = false;
            _workspaceStateApplied = false;
            _largeDocumentDeferred = false;
            _largeDocumentObjectCount = 0;
            _loadedLargeLayerIndices.Clear();
            _missingSelectedObjectIds.Clear();
            DetachDocumentEvents();
            ShowDeferredStatus(OutlinerStrings.OpeningDocument);
        }

        private void OnDocumentReady(
            object sender,
            DocumentLoadStateEventArgs e)
        {
            if (e.DocumentSerialNumber != _documentSerialNumber ||
                !_isPanelVisible ||
                _isClosing)
            {
                return;
            }

            AttachDocument(e.DocumentSerialNumber);
            QueueTreeRefresh();
        }

        private void OnLoadAllObjectsClicked(object sender, EventArgs e)
        {
            if (!_largeDocumentDeferred)
                return;

            _forceFullObjectLoad = true;
            _fullObjectLoadCompleted = false;
            _largeDocumentDeferred = false;
            _loadedLargeLayerIndices.Clear();
            _loadAllObjectsButton.Visible = false;
            _status.Text = OutlinerStrings.LoadingObjects;
            QueueTreeRefresh();
        }

        private bool IsDocumentReady(uint documentSerialNumber)
        {
            return _documentLoadMonitor == null ||
                _documentLoadMonitor.IsReady(documentSerialNumber);
        }

        private void ShowDeferredStatus(string message)
        {
            RunProgrammaticTreeMutation(() =>
                _tree.DataStore = new TreeGridItemCollection());

            _documentModel = null;
            _snapshot = null;
            InvalidateVisibleSelectionRows();
            _selectedObjectIds.Clear();
            _status.Text = message;
            _loadAllObjectsButton.Visible = false;
            UpdateToolbarButtonState();
        }
    }
}
