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
    [Guid("bf807f70-e7fd-4f67-9717-38ff35d3b406")]
    public sealed partial class ObjectOutlinerPanel : Panel, IPanel
    {
        private sealed class NameEditTarget
        {
            private NameEditTarget(
                OutlinerNodeKind kind,
                Guid id,
                Guid ownerId)
            {
                Kind = kind;
                Id = id;
                OwnerId = ownerId;
            }

            public OutlinerNodeKind Kind { get; }

            public Guid Id { get; }

            public Guid OwnerId { get; }

            public static NameEditTarget FromItem(TreeGridItem item)
            {
                return FromNode(item?.Tag as OutlinerNodeReference);
            }

            public static NameEditTarget FromNode(OutlinerNodeReference node)
            {
                if (node == null ||
                    (node.Kind != OutlinerNodeKind.Layer &&
                     node.Kind != OutlinerNodeKind.Object &&
                     node.Kind != OutlinerNodeKind.GroupSlice))
                {
                    return null;
                }

                return new NameEditTarget(node.Kind, node.Id, node.OwnerId);
            }
        }

        private const string ObjectDragDataType = "com.objectmanager.rhino-object-ids";
        private const string LayerDragDataType = "com.objectmanager.rhino-layer-ids";
        private const string GroupDragDataType = "com.objectmanager.rhino-group-id";
        private const float DragThreshold = 4f;
        private const int AutomaticObjectLoadLimit = 15000;

        private uint _documentSerialNumber;
        private readonly TreeGridView _tree;
        private readonly ITreePanAdapter _treePanAdapter;
        private readonly MacTreeGuides _treeGuides = new MacTreeGuides();
        private Button _addLayerButton;
        private Button _addSublayerButton;
        private Button _deleteButton;
        private Button _displayModeButton;
        private Button _columnsButton;
        private ToggleButton _historyStackButton;
        private readonly SearchBox _searchBox;
        private readonly UITimer _treeRefreshTimer;
        private readonly UITimer _stateRefreshTimer;
        private readonly UITimer _searchRefreshTimer;
        private readonly Label _status;
        private readonly Button _loadAllObjectsButton;
        private readonly OutlinerIconFactory _icons;
        private readonly DocumentLoadMonitor _documentLoadMonitor;
        private readonly FavoriteDocumentStore _favoriteStore;
        private readonly OutlinerWorkspaceDocumentStore _workspaceStore;
        private readonly ObjectAliasDocumentStore _nameStore;
        private readonly GroupOrderDocumentStore _groupOrderStore;
        private readonly OutlinerRelationshipHub _relationshipHub;
        private readonly OutlinerSelectionController _selectionController;
        private readonly OutlinerCommandInputBridge _commandInputBridge;
        private readonly OutlinerSelectionEventGate _selectionEventGate;
        private readonly OutlinerNameCell _nameCell;
        private readonly GridColumn _nameColumn;
        private readonly GridColumn _countColumn;
        private readonly GridColumn _favoriteColumn;
        private readonly GridColumn _noteColumn;
        private readonly GridColumn _zoomColumn;
        private readonly GridColumn _currentColumn;
        private readonly GridColumn _visibilityColumn;
        private readonly GridColumn _lockColumn;
        private readonly GridColumn _colorColumn;
        private readonly GridColumn _materialColumn;
        private readonly GridColumn _linetypeColumn;
        private readonly GridColumn _plotColorColumn;
        private readonly GridColumn _plotWeightColumn;
        private readonly GridColumn _sectionStyleColumn;
        private readonly GridColumn _newDetailColumn;
        private readonly GridColumn _modelVisibilityColumn;
        private readonly IList<OutlinerColumnLayoutEntry> _columnLayout;
        private readonly Dictionary<Guid, string> _provisionalNames;
        private readonly Dictionary<Guid, bool> _layerExpansionState;
        private readonly Dictionary<OutlinerGroupSliceKey, bool> _groupExpansionState;
        private readonly Dictionary<Guid, bool> _historyExpansionState;
        private readonly Dictionary<Guid, TreeGridItem> _pendingBlockExpansionItems =
            new Dictionary<Guid, TreeGridItem>();
        private bool _blockExpansionRefreshQueued;
        private readonly OutlinerViewState _viewState;
        private readonly HashSet<Guid> _selectedObjectIds;
        private readonly HashSet<Guid> _selectedLayerIds;
        private readonly Dictionary<Guid, int> _visibleObjectRows;
        private readonly Dictionary<Guid, int> _visibleLayerRows;
        private readonly Dictionary<Guid, List<int>> _visibleGroupRows;
        private readonly HashSet<TreeGridItem> _visibleTreeItems;
        private readonly List<TreeGridItem> _visibleRows;
        private readonly HashSet<int> _renderedSelectedRows;
        private readonly HashSet<TreeGridItem> _collapsedSelectionIndicatorItems;
        private readonly HashSet<int> _loadedLargeLayerIndices;
        private readonly HashSet<Guid> _missingSelectedObjectIds;
        private readonly Dictionary<Guid, OutlinerHistoryModel> _historyCache;
        private readonly HashSet<Guid> _resolvedHistoryObjectIds;
        private readonly HashSet<Guid> _protectedHistoryObjectIds;
        private readonly OutlinerStateChange _pendingStateChange;
        private readonly OutlinerStructureChange _pendingStructureChange;
        private Guid _primaryLayerId;
        private Guid _pendingEditLayerId;
        private NameEditTarget _activeNameEditTarget;
        private NameEditTarget _primaryNameEditTarget;
        private TreeGridItem _potentialExpansionItem;
        private bool _potentialExpansionOccurred;
        private bool _deferredPointerSelectionChange;
        private int _nameEditGeneration;
        private OutlinerNodeReference _dragSourceNode;
        private OutlinerNodeReference _commandInputPointerNode;
        private int _commandInputPointerTraceId;
        private OutlinerNodeReference _blockedPointerNode;
        private GridColumn _blockedPointerColumn;
        private PointF _dragStartPoint;
        private OutlinerNodeKind? _draggedNodeKind;
        private List<Guid> _draggedNodeIds;
        private List<OutlinerNodeReference> _draggedGroupNodes;
        private Guid _dragHoverLayerId;
        private DateTime _dragHoverStartedAt;
        private DocumentEventBridge _documentEvents;
        private OutlinerDocumentModel _documentModel;
        private OutlinerSnapshot _snapshot;
        private bool _treeRefreshQueued;
        private bool _selectionRefreshQueued;
        private bool _syncingDocumentSelection;
        private bool _projectingTree;
        private bool _revealInProgress;
        private bool _restoringColumnLayout;
        private bool _isPanelVisible;
        private bool _isClosing;
        private bool _linkCursorActive;
        private bool _treePanActive;
        private PointF _treePanStartScreenPoint;
        private bool _forceFullObjectLoad;
        private bool _fullObjectLoadCompleted;
        private bool _workspaceStateApplied;
        private bool _applyingWorkspaceState;
        private bool _largeDocumentDeferred;
        private int _largeDocumentObjectCount;
        private int _treeRefreshRequestCount;
        private int _stateRefreshRequestCount;
        private OutlinerStateChange _activeStateChange;
        private int _selectionRefreshGeneration;
        private int _revealGeneration;
        private bool _visibleSelectionRowsDirty;
        private bool _renderedSelectedRowsValid;
        private int _programmaticSelectionEventCount;
        private bool _historyInspectionQueued;
        private Guid _pendingHistoryObjectId;
        private int _historyInspectionGeneration;

        public ObjectOutlinerPanel(uint documentSerialNumber)
        {
            _documentSerialNumber = documentSerialNumber;
            _documentLoadMonitor = ObjectManagerPlugin.Instance?.DocumentLoadMonitor;
            _favoriteStore = ObjectManagerPlugin.Instance?.FavoriteStore;
            _workspaceStore = ObjectManagerPlugin.Instance?.WorkspaceStore;
            _nameStore = ObjectManagerPlugin.Instance?.NameStore;
            _groupOrderStore = ObjectManagerPlugin.Instance?.GroupOrderStore;
            _relationshipHub = ObjectManagerPlugin.Instance?.RelationshipHub;
            _icons = new OutlinerIconFactory();
            _selectionController = new OutlinerSelectionController();
            _commandInputBridge = new OutlinerCommandInputBridge(
                _relationshipHub);
            _selectionEventGate = new OutlinerSelectionEventGate();
            _provisionalNames = new Dictionary<Guid, string>();
            _layerExpansionState = new Dictionary<Guid, bool>();
            _groupExpansionState = new Dictionary<OutlinerGroupSliceKey, bool>();
            _historyExpansionState = new Dictionary<Guid, bool>();
            _viewState = new OutlinerViewState();
            _selectedObjectIds = new HashSet<Guid>();
            _selectedLayerIds = new HashSet<Guid>();
            _visibleObjectRows = new Dictionary<Guid, int>();
            _visibleLayerRows = new Dictionary<Guid, int>();
            _visibleGroupRows = new Dictionary<Guid, List<int>>();
            _visibleTreeItems = new HashSet<TreeGridItem>();
            _visibleRows = new List<TreeGridItem>();
            _renderedSelectedRows = new HashSet<int>();
            _collapsedSelectionIndicatorItems = new HashSet<TreeGridItem>();
            _visibleSelectionRowsDirty = true;
            _loadedLargeLayerIndices = new HashSet<int>();
            _missingSelectedObjectIds = new HashSet<Guid>();
            _historyCache = new Dictionary<Guid, OutlinerHistoryModel>();
            _resolvedHistoryObjectIds = new HashSet<Guid>();
            _protectedHistoryObjectIds = new HashSet<Guid>();
            _pendingStateChange = new OutlinerStateChange();
            _pendingStructureChange = new OutlinerStructureChange();
            if (_relationshipHub != null)
            {
                _relationshipHub.GroupsCopied += OnGroupsCopied;
                _relationshipHub.HistoryObserved += OnHistoryObserved;
            }

            Style = Panels.EtoPanelStyleName;
            Padding = new Padding(0);
            MinimumSize = new Size(240, 260);

            _tree = new TreeGridView
            {
                AllowColumnReordering = true,
                AllowDrop = true,
                AllowEmptySelection = true,
                AllowMultipleSelection = true,
                Border = BorderType.Line,
                GridLines = GridLines.Horizontal,
                RowHeight = 24,
                ShowHeader = true
            };
            _nameCell = CustomCell.SupportsControlView
                ? new OutlinerNameCell()
                : null;
            if (_nameCell != null)
            {
                _nameCell.EditCommitted += OnNameCellEditCommitted;
                _nameCell.EditEnded += OnNameCellEditEnded;
            }

            _nameColumn = new GridColumn
            {
                DataCell = _nameCell != null
                    ? (Cell)_nameCell
                    : new ImageTextCell(0, 1),
                Editable = _nameCell == null,
                Expand = true,
                HeaderText = OutlinerStrings.Name,
                MinWidth = 100,
                Resizable = true
            };
            _tree.Columns.Add(_nameColumn);

            _countColumn = CreateTextColumn(
                OutlinerValueIndex.Count,
                OutlinerStrings.Count,
                52);

            _favoriteColumn = CreateStateColumn(
                OutlinerValueIndex.Favorite, OutlinerStrings.Favorite, 20);
            _noteColumn = CreateStateColumn(OutlinerValueIndex.Note, OutlinerStrings.Note, 24);
            _zoomColumn = CreateStateColumn(OutlinerValueIndex.Zoom, OutlinerStrings.Zoom, 24);

            _currentColumn = CreateStateColumn(
                OutlinerValueIndex.Current, OutlinerStrings.CurrentLayer, 20);
            _visibilityColumn = CreateStateColumn(
                OutlinerValueIndex.Visibility, OutlinerStrings.Visibility, 20);
            _lockColumn = CreateStateColumn(
                OutlinerValueIndex.Lock, OutlinerStrings.Lock, 20);
            _colorColumn = CreateStateColumn(
                OutlinerValueIndex.Color, OutlinerStrings.LayerColor, 22);
            _materialColumn = CreateStateColumn(
                OutlinerValueIndex.Material,
                OutlinerStrings.Material,
                24,
                OutlinerStrings.Material);
            _linetypeColumn = CreateTextColumn(
                OutlinerValueIndex.Linetype, OutlinerStrings.Linetype, 72);
            _plotColorColumn = CreateStateColumn(
                OutlinerValueIndex.PlotColor, OutlinerStrings.PrintColor, 22);
            _plotWeightColumn = CreateTextColumn(
                OutlinerValueIndex.PlotWeight, OutlinerStrings.PrintWidth, 72);
            _sectionStyleColumn = CreateTextColumn(
                OutlinerValueIndex.SectionStyle, OutlinerStrings.SectionStyle, 82);
            _newDetailColumn = CreateStateColumn(
                OutlinerValueIndex.NewDetailVisibility,
                OutlinerStrings.NewDetailOn,
                20);
            _modelVisibilityColumn = CreateStateColumn(
                OutlinerValueIndex.ModelVisibility,
                OutlinerStrings.ModelOn,
                20);
            _tree.Columns.Add(_countColumn);
            _tree.Columns.Add(_favoriteColumn);
            _tree.Columns.Add(_noteColumn);
            _tree.Columns.Add(_zoomColumn);
            _tree.Columns.Add(_currentColumn);
            _tree.Columns.Add(_visibilityColumn);
            _tree.Columns.Add(_lockColumn);
            _tree.Columns.Add(_colorColumn);
            _tree.Columns.Add(_materialColumn);
            _tree.Columns.Add(_linetypeColumn);
            _tree.Columns.Add(_plotColorColumn);
            _tree.Columns.Add(_plotWeightColumn);
            _tree.Columns.Add(_sectionStyleColumn);
            _tree.Columns.Add(_newDetailColumn);
            _tree.Columns.Add(_modelVisibilityColumn);
            _columnLayout = CreateColumnLayout();
            RestoreColumnLayout();
            _treePanAdapter = TreePanAdapterFactory.Create(
                _tree,
                OnNativeTreePanStarted,
                OnNativeTreePanEnded);
            _tree.ColumnOrderChanged += OnColumnOrderChanged;
            _tree.CellFormatting += OnCellFormatting;
            _tree.CellClick += OnTreeCellClick;
            _tree.CellDoubleClick += OnTreeCellDoubleClick;
            _tree.CellEditing += OnTreeCellEditing;
            _tree.CellEdited += OnTreeCellEdited;
            _tree.SelectionChanged += OnTreeSelectionChanged;
            _tree.KeyDown += OnTreeKeyDown;
            _tree.Expanding += OnTreeExpansionStarting;
            _tree.Collapsing += OnTreeExpansionStarting;
            _tree.Expanded += OnTreeExpansionChanged;
            _tree.Collapsed += OnTreeExpansionChanged;
            _tree.MouseDown += OnTreeMouseDown;
            _tree.MouseMove += OnTreeMouseMove;
            _tree.MouseLeave += OnTreeMouseLeave;
            _tree.MouseUp += OnTreeMouseUp;
            _tree.DragOver += OnTreeDragOver;
            _tree.DragDrop += OnTreeDragDrop;
            _tree.DragEnd += OnTreeDragEnd;

            _searchBox = new SearchBox
            {
                PlaceholderText = OutlinerStrings.Search
            };
            _searchBox.TextChanged += OnSearchTextChanged;
            _treeRefreshTimer = new UITimer { Interval = 0.2 };
            _treeRefreshTimer.Elapsed += OnTreeRefreshTimerElapsed;
            _stateRefreshTimer = new UITimer { Interval = 0.03 };
            _stateRefreshTimer.Elapsed += OnStateRefreshTimerElapsed;
            _searchRefreshTimer = new UITimer { Interval = 0.15 };
            _searchRefreshTimer.Elapsed += OnSearchRefreshTimerElapsed;

            _status = new Label
            {
                Text = OutlinerStrings.WaitingForDocument,
                TextColor = SystemColors.DisabledText,
                VerticalAlignment = VerticalAlignment.Center
            };
            _loadAllObjectsButton = new Button
            {
                Text = OutlinerStrings.LoadObjects,
                ToolTip = OutlinerStrings.LoadObjectsToolTip,
                Visible = false
            };
            _loadAllObjectsButton.Click += OnLoadAllObjectsClicked;
            _revealFilteredSelectionButton = new Button
            {
                Text = OutlinerStrings.ShowSelection,
                ToolTip = OutlinerStrings.ShowSelectionToolTip,
                Visible = false
            };
            _revealFilteredSelectionButton.Click +=
                OnRevealFilteredSelectionClicked;

            var statusPanel = new Panel
            {
                Padding = new Padding(10, 5),
                Content = new StackLayout
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Items =
                    {
                        new StackLayoutItem(_status, true),
                        new StackLayoutItem(
                            _revealFilteredSelectionButton,
                            false),
                        new StackLayoutItem(_loadAllObjectsButton, false)
                    }
                }
            };

            var toolbar = CreateLayerToolbar();
            var searchPanel = CreateSearchAndFilterArea();
            UpdateFilterControlValues();

            Content = new StackLayout
            {
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Orientation = Orientation.Vertical,
                Spacing = 0,
                Items =
                {
                    new StackLayoutItem(toolbar, false),
                    new StackLayoutItem(searchPanel, false),
                    new StackLayoutItem(_tree, true),
                    new StackLayoutItem(statusPanel, false)
                }
            };

            if (_documentLoadMonitor != null)
            {
                _documentLoadMonitor.DocumentOpening += OnDocumentOpening;
                _documentLoadMonitor.DocumentReady += OnDocumentReady;
            }
            if (_favoriteStore != null)
                _favoriteStore.Changed += OnFavoriteGroupsChanged;
            if (_nameStore != null)
                _nameStore.Changed += OnOutlinerNamesChanged;
            if (_groupOrderStore != null)
                _groupOrderStore.Changed += OnGroupOrderChanged;
            RhinoApp.AppSettingsChanged += OnAppSettingsChanged;
            InitializeIsolationLifecycle();
            UpdateToolbarButtonState();
        }
    }
}
