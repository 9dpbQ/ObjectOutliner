using System;
using System.Collections.Generic;
using System.Linq;
using Eto.Drawing;
using Eto.Forms;
using ObjectManager.Outliner;
using Rhino;

namespace ObjectManager
{
    public sealed partial class ObjectOutlinerPanel
    {
        private sealed class FilterNavigationAnchor
        {
            public FilterNavigationAnchor(
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
        }

        private const int ContentFilterPopupWidth = 250;
        private Button _layerFilterButton;
        private Button _contentFilterButton;
        private Form _contentFilterPopup;
        private CheckBox _allObjectTypesFilter;
        private CheckBox _favoritesOnlyFilter;
        private CheckBox _selectedObjectsOnlyFilter;
        private CheckBox _userNamedOnlyFilter;
        private CheckBox _notesOnlyFilter;
        private Dictionary<OutlinerIconKind, CheckBox> _objectTypeFilters;
        private Button _revealFilteredSelectionButton;
        private bool _updatingFilterControls;
        private bool _filteredSelectionProjectionQueued;
        private FilterNavigationAnchor _pendingFilterNavigationAnchor;
        private int _filterNavigationGeneration;
        private int _hiddenSelectedObjectCount;
        private int _hiddenSelectedLayerCount;
        private int _hiddenByLayerFilterCount;
        private int _hiddenByContentFilterCount;

        private Control CreateSearchAndFilterArea()
        {
            _contentFilterButton = CreateToolbarButton(
                _icons.GetObjectFilterIcon(false),
                OutlinerStrings.FilterObjects,
                OnContentFilterButtonClicked);

            var searchRow = new StackLayout
            {
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Orientation = Orientation.Horizontal,
                Spacing = 3,
                VerticalContentAlignment = VerticalAlignment.Center,
                Items =
                {
                    new StackLayoutItem(_searchBox, true),
                    new StackLayoutItem(_contentFilterButton, false)
                }
            };

            return new Panel
            {
                Padding = new Padding(4, 3),
                Content = searchRow
            };
        }

        private static KeyValuePair<OutlinerIconKind, string>[]
            GetObjectTypeFilterDefinitions()
        {
            return new[]
            {
                new KeyValuePair<OutlinerIconKind, string>(
                    OutlinerIconKind.Point, OutlinerStrings.Point),
                new KeyValuePair<OutlinerIconKind, string>(
                    OutlinerIconKind.PointCloud, OutlinerStrings.PointCloud),
                new KeyValuePair<OutlinerIconKind, string>(
                    OutlinerIconKind.Curve, OutlinerStrings.Curve),
                new KeyValuePair<OutlinerIconKind, string>(
                    OutlinerIconKind.Surface, OutlinerStrings.Surface),
                new KeyValuePair<OutlinerIconKind, string>(
                    OutlinerIconKind.Polysurface, OutlinerStrings.Polysurface),
                new KeyValuePair<OutlinerIconKind, string>(
                    OutlinerIconKind.Extrusion, OutlinerStrings.Extrusion),
                new KeyValuePair<OutlinerIconKind, string>(
                    OutlinerIconKind.Mesh, OutlinerStrings.Mesh),
                new KeyValuePair<OutlinerIconKind, string>(
                    OutlinerIconKind.SubD, OutlinerStrings.SubD),
                new KeyValuePair<OutlinerIconKind, string>(
                    OutlinerIconKind.Block, OutlinerStrings.BlockInstance),
                new KeyValuePair<OutlinerIconKind, string>(
                    OutlinerIconKind.Annotation, OutlinerStrings.Annotation),
                new KeyValuePair<OutlinerIconKind, string>(
                    OutlinerIconKind.Light, OutlinerStrings.Light),
                new KeyValuePair<OutlinerIconKind, string>(
                    OutlinerIconKind.Hatch, OutlinerStrings.Hatch),
                new KeyValuePair<OutlinerIconKind, string>(
                    OutlinerIconKind.Other, OutlinerStrings.Other)
            };
        }

        private Form CreateContentFilterPopup()
        {
            _allObjectTypesFilter = new CheckBox
            {
                Text = OutlinerStrings.AllObjectTypes,
                Checked = true
            };
            _allObjectTypesFilter.CheckedChanged += OnAllObjectTypesChanged;

            _objectTypeFilters = new Dictionary<OutlinerIconKind, CheckBox>();
            var typeDefinitions = GetObjectTypeFilterDefinitions();
            foreach (var definition in typeDefinitions)
            {
                var checkBox = new CheckBox { Text = definition.Value };
                var kind = definition.Key;
                checkBox.CheckedChanged += (sender, e) =>
                    OnObjectTypeChanged(kind, checkBox.Checked == true);
                _objectTypeFilters[kind] = checkBox;
            }

            var typeRows = new StackLayout
            {
                Orientation = Orientation.Vertical,
                Spacing = 1
            };
            typeRows.Items.Add(_allObjectTypesFilter);
            for (var index = 0; index < typeDefinitions.Length; index += 2)
            {
                var row = new StackLayout
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Items =
                    {
                        new StackLayoutItem(
                            _objectTypeFilters[typeDefinitions[index].Key],
                            true)
                    }
                };
                if (index + 1 < typeDefinitions.Length)
                {
                    row.Items.Add(new StackLayoutItem(
                        _objectTypeFilters[typeDefinitions[index + 1].Key],
                        true));
                }
                typeRows.Items.Add(row);
            }

            _favoritesOnlyFilter = new CheckBox
            {
                Text = OutlinerStrings.FavoritesOnly
            };
            _favoritesOnlyFilter.CheckedChanged += OnFavoritesOnlyChanged;

            _selectedObjectsOnlyFilter = new CheckBox
            {
                Text = OutlinerStrings.SelectedObjectsOnly
            };
            _selectedObjectsOnlyFilter.CheckedChanged +=
                OnSelectedObjectsOnlyChanged;

            _userNamedOnlyFilter = new CheckBox
            {
                Text = OutlinerStrings.UserNamedOnly
            };
            _userNamedOnlyFilter.CheckedChanged += OnUserNamedOnlyChanged;
            _notesOnlyFilter = new CheckBox { Text = OutlinerStrings.NotesOnly };
            _notesOnlyFilter.CheckedChanged += (sender, e) =>
            {
                if (_updatingFilterControls || _notesOnlyFilter == null)
                    return;
                var enabled = _notesOnlyFilter.Checked == true;
                ApplyDiscoveryFilterChange(
                    () => _viewState.ContentFilter.NotesOnly = enabled, !enabled);
            };

            var separator = new Panel
            {
                BackgroundColor = SystemColors.Control,
                Height = 1
            };

            var content = new StackLayout
            {
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Orientation = Orientation.Vertical,
                Spacing = 4,
                Items =
                {
                    new Label
                    {
                        Text = OutlinerStrings.ObjectType,
                        Font = SystemFonts.Bold(9)
                    },
                    separator,
                    typeRows,
                    new Panel
                    {
                        BackgroundColor = SystemColors.Control,
                        Height = 1
                    },
                    new Label
                    {
                        Text = OutlinerStrings.ShowOnly,
                        Font = SystemFonts.Bold(9)
                    },
                    _selectedObjectsOnlyFilter,
                    _userNamedOnlyFilter,
                    _notesOnlyFilter,
                    _favoritesOnlyFilter
                }
            };

            var popupContent = new Panel
            {
                BackgroundColor = SystemColors.Control,
                Padding = new Padding(1),
                Content = new Panel
                {
                    BackgroundColor = SystemColors.ControlBackground,
                    Padding = new Padding(10, 8),
                    Content = content
                }
            };
            var preferredSize = popupContent.GetPreferredSize(
                new SizeF(ContentFilterPopupWidth, 1000));

            var popup = new Form
            {
                ClientSize = new Size(
                    ContentFilterPopupWidth,
                    Math.Max(1, (int)Math.Ceiling(preferredSize.Height))),
                Owner = _contentFilterButton.ParentWindow,
                Resizable = false,
                Topmost = false,
                WindowStyle = WindowStyle.None,
                Content = popupContent
            };
            popup.KeyDown += (sender, e) =>
            {
                if (e.Key != Keys.Escape)
                    return;
                e.Handled = true;
                CloseContentFilterPopup();
            };
            popup.LostFocus += (sender, e) =>
            {
                Application.Instance.AsyncInvoke(() =>
                {
                    if (ReferenceEquals(_contentFilterPopup, popup) &&
                        !ContentFilterPopupHasFocus())
                    {
                        CloseContentFilterPopup();
                    }
                });
            };
            popup.Closed += (sender, e) =>
            {
                if (!ReferenceEquals(_contentFilterPopup, popup))
                    return;
                _contentFilterPopup = null;
                _allObjectTypesFilter = null;
                _favoritesOnlyFilter = null;
                _selectedObjectsOnlyFilter = null;
                _userNamedOnlyFilter = null;
                _notesOnlyFilter = null;
                _objectTypeFilters = null;
            };
            return popup;
        }

        private void OnLayerFilterClicked(object sender, EventArgs e)
        {
            var anchor = sender as Control ?? _layerFilterButton;
            if (!PrepareChoicePopup(anchor))
                return;

            var filter = _viewState.LayerFilter;
            var selectedLayers = new HashSet<Guid>(_selectedLayerIds);
            var updating = false;
            var allLayers = new CheckBox { Text = OutlinerStrings.AllLayers };
            var currentTree = new CheckBox
            {
                Text = OutlinerStrings.CurrentLayerTree
            };
            var selectedTrees = new CheckBox
            {
                Text = OutlinerStrings.SelectedLayerTrees
            };
            var onLayers = new CheckBox { Text = OutlinerStrings.OnLayers };
            var offLayers = new CheckBox { Text = OutlinerStrings.OffLayers };
            var lockedLayers = new CheckBox
            {
                Text = OutlinerStrings.LockedLayers
            };
            var unlockedLayers = new CheckBox
            {
                Text = OutlinerStrings.UnlockedLayers
            };
            var layersWithObjects = new CheckBox
            {
                Text = OutlinerStrings.LayersWithObjects
            };
            var emptyLayers = new CheckBox
            {
                Text = OutlinerStrings.EmptyLayers
            };
            var checkBoxes = new[]
            {
                allLayers,
                currentTree,
                selectedTrees,
                onLayers,
                offLayers,
                lockedLayers,
                unlockedLayers,
                layersWithObjects,
                emptyLayers
            };
            Action updateChecks = () =>
            {
                updating = true;
                try
                {
                    allLayers.Checked = !filter.IsActive;
                    currentTree.Checked = filter.Scope ==
                        OutlinerLayerScopeMode.CurrentLayerTree;
                    selectedTrees.Checked = filter.Scope ==
                        OutlinerLayerScopeMode.SelectedLayerTrees;
                    selectedTrees.Enabled = selectedTrees.Checked == true ||
                        selectedLayers.Count > 0;
                    onLayers.Checked = filter.Visibility ==
                        OutlinerLayerVisibilityFilter.On;
                    offLayers.Checked = filter.Visibility ==
                        OutlinerLayerVisibilityFilter.Off;
                    lockedLayers.Checked = filter.Lock ==
                        OutlinerLockFilter.Locked;
                    unlockedLayers.Checked = filter.Lock ==
                        OutlinerLockFilter.Unlocked;
                    layersWithObjects.Checked = filter.Occupancy ==
                        OutlinerLayerOccupancyFilter.WithObjects;
                    emptyLayers.Checked = filter.Occupancy ==
                        OutlinerLayerOccupancyFilter.Empty;
                }
                finally
                {
                    updating = false;
                }
            };

            allLayers.CheckedChanged += (checkSender, checkEvent) =>
            {
                if (updating)
                    return;
                if (allLayers.Checked != true)
                {
                    updateChecks();
                    return;
                }

                var wasActive = filter.IsActive;
                ApplyDiscoveryFilterChange(filter.Reset, wasActive);
                updateChecks();
            };
            currentTree.CheckedChanged += (checkSender, checkEvent) =>
            {
                if (updating)
                    return;
                var wasActive = filter.Scope ==
                    OutlinerLayerScopeMode.CurrentLayerTree;
                var enabled = currentTree.Checked == true;
                ApplyDiscoveryFilterChange(() =>
                {
                    filter.Scope = enabled
                        ? OutlinerLayerScopeMode.CurrentLayerTree
                        : OutlinerLayerScopeMode.All;
                    filter.SelectedLayerIds.Clear();
                }, wasActive && !enabled);
                updateChecks();
            };
            selectedTrees.CheckedChanged += (checkSender, checkEvent) =>
            {
                if (updating)
                    return;
                if (!selectedTrees.Enabled)
                {
                    updateChecks();
                    return;
                }

                var wasActive = filter.Scope ==
                    OutlinerLayerScopeMode.SelectedLayerTrees;
                var enabled = selectedTrees.Checked == true;
                ApplyDiscoveryFilterChange(() =>
                {
                    filter.Scope = enabled
                        ? OutlinerLayerScopeMode.SelectedLayerTrees
                        : OutlinerLayerScopeMode.All;
                    filter.SelectedLayerIds.Clear();
                    if (enabled)
                        filter.SelectedLayerIds.UnionWith(selectedLayers);
                }, wasActive && !enabled);
                updateChecks();
            };

            BindLayerVisibilityFilterCheckBox(
                onLayers,
                OutlinerLayerVisibilityFilter.On,
                () => updating,
                updateChecks);
            BindLayerVisibilityFilterCheckBox(
                offLayers,
                OutlinerLayerVisibilityFilter.Off,
                () => updating,
                updateChecks);
            BindLayerLockFilterCheckBox(
                lockedLayers,
                OutlinerLockFilter.Locked,
                () => updating,
                updateChecks);
            BindLayerLockFilterCheckBox(
                unlockedLayers,
                OutlinerLockFilter.Unlocked,
                () => updating,
                updateChecks);
            BindLayerOccupancyFilterCheckBox(
                layersWithObjects,
                OutlinerLayerOccupancyFilter.WithObjects,
                () => updating,
                updateChecks);
            BindLayerOccupancyFilterCheckBox(
                emptyLayers,
                OutlinerLayerOccupancyFilter.Empty,
                () => updating,
                updateChecks);
            updateChecks();

            var content = new StackLayout
            {
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Orientation = Orientation.Vertical,
                Spacing = 4,
                Items =
                {
                    CreatePopupHeading(OutlinerStrings.LayerFilterHeading),
                    CreatePopupSeparator(),
                    allLayers,
                    CreatePopupSeparator(),
                    currentTree,
                    selectedTrees,
                    CreatePopupSeparator(),
                    onLayers,
                    offLayers,
                    lockedLayers,
                    unlockedLayers,
                    layersWithObjects,
                    emptyLayers
                }
            };
            ShowChoicePopup(anchor, 270, content, checkBoxes);
        }

        private void BindLayerVisibilityFilterCheckBox(
            CheckBox checkBox,
            OutlinerLayerVisibilityFilter value,
            Func<bool> isUpdating,
            Action updateChecks)
        {
            checkBox.CheckedChanged += (sender, e) =>
            {
                if (isUpdating())
                    return;

                var filter = _viewState.LayerFilter;
                var wasActive = filter.Visibility == value;
                var enabled = checkBox.Checked == true;
                if (!enabled && !wasActive)
                {
                    updateChecks();
                    return;
                }

                ApplyDiscoveryFilterChange(
                    () => filter.Visibility = enabled
                        ? value
                        : OutlinerLayerVisibilityFilter.All,
                    wasActive && !enabled);
                updateChecks();
            };
        }

        private void BindLayerLockFilterCheckBox(
            CheckBox checkBox,
            OutlinerLockFilter value,
            Func<bool> isUpdating,
            Action updateChecks)
        {
            checkBox.CheckedChanged += (sender, e) =>
            {
                if (isUpdating())
                    return;

                var filter = _viewState.LayerFilter;
                var wasActive = filter.Lock == value;
                var enabled = checkBox.Checked == true;
                if (!enabled && !wasActive)
                {
                    updateChecks();
                    return;
                }

                ApplyDiscoveryFilterChange(
                    () => filter.Lock = enabled
                        ? value
                        : OutlinerLockFilter.All,
                    wasActive && !enabled);
                updateChecks();
            };
        }

        private void BindLayerOccupancyFilterCheckBox(
            CheckBox checkBox,
            OutlinerLayerOccupancyFilter value,
            Func<bool> isUpdating,
            Action updateChecks)
        {
            checkBox.CheckedChanged += (sender, e) =>
            {
                if (isUpdating())
                    return;

                var filter = _viewState.LayerFilter;
                var wasActive = filter.Occupancy == value;
                var enabled = checkBox.Checked == true;
                if (!enabled && !wasActive)
                {
                    updateChecks();
                    return;
                }

                ApplyDiscoveryFilterChange(
                    () => filter.Occupancy = enabled
                        ? value
                        : OutlinerLayerOccupancyFilter.All,
                    wasActive && !enabled);
                updateChecks();
            };
        }

        private void OnContentFilterButtonClicked(object sender, EventArgs e)
        {
            CloseChoicePopup();
            if (_contentFilterPopup != null)
            {
                CloseContentFilterPopup();
                return;
            }
            if (_isClosing)
                return;

            var popup = CreateContentFilterPopup();
            _contentFilterPopup = popup;
            UpdateFilterControlValues();
            PositionContentFilterPopup(popup);
            popup.Show();
            popup.Focus();
        }

        private void PositionContentFilterPopup(Form popup)
        {
            if (popup == null || _contentFilterButton == null)
                return;

            var topLeft = _contentFilterButton.PointToScreen(
                new PointF(0, 0));
            var bottomRight = _contentFilterButton.PointToScreen(
                new PointF(
                    _contentFilterButton.Width,
                    _contentFilterButton.Height));
            var screen = Screen.FromPoint(bottomRight) ?? Screen.PrimaryScreen;
            var workingArea = screen.WorkingArea;
            var width = popup.ClientSize.Width;
            var height = popup.ClientSize.Height;

            var x = bottomRight.X - width;
            x = Math.Max(
                workingArea.Left,
                Math.Min(x, workingArea.Right - width));

            var y = bottomRight.Y + 2;
            if (y + height > workingArea.Bottom)
                y = topLeft.Y - height - 2;
            y = Math.Max(
                workingArea.Top,
                Math.Min(y, workingArea.Bottom - height));

            popup.Location = new Point(
                (int)Math.Round(x),
                (int)Math.Round(y));
        }

        private bool ContentFilterPopupHasFocus()
        {
            if (_contentFilterPopup?.HasFocus == true ||
                _allObjectTypesFilter?.HasFocus == true ||
                _favoritesOnlyFilter?.HasFocus == true ||
                _selectedObjectsOnlyFilter?.HasFocus == true ||
                _userNamedOnlyFilter?.HasFocus == true ||
                _notesOnlyFilter?.HasFocus == true)
            {
                return true;
            }

            return _objectTypeFilters != null &&
                _objectTypeFilters.Values.Any(checkBox => checkBox.HasFocus);
        }

        private void CloseContentFilterPopup()
        {
            var popup = _contentFilterPopup;
            if (popup == null)
                return;

            popup.Close();
            if (!ReferenceEquals(_contentFilterPopup, popup))
                return;

            _contentFilterPopup = null;
            _allObjectTypesFilter = null;
            _favoritesOnlyFilter = null;
            _selectedObjectsOnlyFilter = null;
            _userNamedOnlyFilter = null;
            _notesOnlyFilter = null;
            _objectTypeFilters = null;
        }

        private void OnAllObjectTypesChanged(object sender, EventArgs e)
        {
            if (_updatingFilterControls)
                return;

            if (_allObjectTypesFilter.Checked != true)
            {
                UpdateFilterControlValues();
                return;
            }

            var wasActive = _viewState.ContentFilter.ObjectTypes.Count > 0;
            ApplyDiscoveryFilterChange(
                () => _viewState.ContentFilter.ObjectTypes.Clear(),
                wasActive);
        }

        private void OnObjectTypeChanged(
            OutlinerIconKind kind,
            bool isChecked)
        {
            if (_updatingFilterControls)
                return;

            var selectedTypes = _viewState.ContentFilter.ObjectTypes;
            var previousCount = selectedTypes.Count;
            ApplyDiscoveryFilterChange(() =>
            {
                if (isChecked)
                    selectedTypes.Add(kind);
                else
                    selectedTypes.Remove(kind);
            }, previousCount > 0 &&
                !isChecked &&
                previousCount == 1);
        }

        private void OnFavoritesOnlyChanged(object sender, EventArgs e)
        {
            if (_updatingFilterControls || _favoritesOnlyFilter == null)
                return;

            var wasEnabled = _viewState.ContentFilter.FavoritesOnly;
            var enabled = _favoritesOnlyFilter.Checked == true;
            ApplyDiscoveryFilterChange(
                () => _viewState.ContentFilter.FavoritesOnly = enabled,
                wasEnabled && !enabled);
        }

        private void OnSelectedObjectsOnlyChanged(object sender, EventArgs e)
        {
            if (_updatingFilterControls || _selectedObjectsOnlyFilter == null)
                return;

            var wasEnabled = _viewState.ContentFilter.SelectedObjectsOnly;
            var enabled = _selectedObjectsOnlyFilter.Checked == true;
            ApplyDiscoveryFilterChange(
                () => _viewState.ContentFilter.SelectedObjectsOnly = enabled,
                wasEnabled && !enabled);
        }

        private void OnUserNamedOnlyChanged(object sender, EventArgs e)
        {
            if (_updatingFilterControls || _userNamedOnlyFilter == null)
                return;

            var wasEnabled = _viewState.ContentFilter.UserNamedOnly;
            var enabled = _userNamedOnlyFilter.Checked == true;
            ApplyDiscoveryFilterChange(
                () => _viewState.ContentFilter.UserNamedOnly = enabled,
                wasEnabled && !enabled);
        }

        private void ApplyDiscoveryFilterChange(
            Action update,
            bool restoreSelectionAfterChange)
        {
            if (update == null || _documentModel == null)
                return;

            if (restoreSelectionAfterChange)
                PrepareFilterReturnNavigation();
            else
                CancelPendingFilterNavigation();

            _viewState.ClearTemporaryFilterReveal();
            update();
            UpdateFilterControlValues();
            try
            {
                RefreshProjection();
            }
            catch (Exception exception)
            {
                HandleTreeRefreshError(exception);
            }
        }

        private void UpdateFilterControlValues()
        {
            _updatingFilterControls = true;
            try
            {
                if (_allObjectTypesFilter != null)
                {
                    _allObjectTypesFilter.Checked =
                        _viewState.ContentFilter.ObjectTypes.Count == 0;
                }
                if (_objectTypeFilters != null)
                {
                    foreach (var entry in _objectTypeFilters)
                    {
                        entry.Value.Checked =
                            _viewState.ContentFilter.ObjectTypes.Contains(
                                entry.Key);
                    }
                }
                if (_favoritesOnlyFilter != null)
                {
                    _favoritesOnlyFilter.Checked =
                        _viewState.ContentFilter.FavoritesOnly;
                }
                if (_selectedObjectsOnlyFilter != null)
                {
                    _selectedObjectsOnlyFilter.Checked =
                        _viewState.ContentFilter.SelectedObjectsOnly;
                }
                if (_userNamedOnlyFilter != null)
                {
                    _userNamedOnlyFilter.Checked =
                        _viewState.ContentFilter.UserNamedOnly;
                }
                if (_notesOnlyFilter != null)
                    _notesOnlyFilter.Checked = _viewState.ContentFilter.NotesOnly;
            }
            finally
            {
                _updatingFilterControls = false;
            }

            UpdateFilterButtonPresentation();
        }

        private void UpdateFilterButtonPresentation()
        {
            if (_layerFilterButton != null)
            {
                _layerFilterButton.Image = _icons.GetLayerFilterIcon(
                    _viewState.HasLayerFilter,
                    _hiddenByLayerFilterCount > 0);
                _layerFilterButton.ToolTip =
                    OutlinerStrings.FormatFilterLayersToolTip(
                        _viewState.LayerFilter.ActiveConditionCount);
            }
            if (_contentFilterButton != null)
            {
                _contentFilterButton.Image = _icons.GetObjectFilterIcon(
                    _viewState.HasContentFilter,
                    _hiddenByContentFilterCount > 0);
                _contentFilterButton.ToolTip =
                    OutlinerStrings.FormatFilterObjectsToolTip(
                        _viewState.ContentFilter.ActiveConditionCount);
            }
        }

        private void PrepareFilterReturnNavigation()
        {
            CancelPendingFilterNavigation();
            var anchor = CaptureFilterNavigationAnchor();
            if (anchor == null)
                return;

            ExpandFilterNavigationPath(anchor);
            _pendingFilterNavigationAnchor = anchor;
        }

        private FilterNavigationAnchor CaptureFilterNavigationAnchor()
        {
            var selectedItem = _tree.SelectedItem as TreeGridItem;
            var node = selectedItem?.Tag as OutlinerNodeReference;
            if (node != null)
            {
                if (node.Kind == OutlinerNodeKind.Layer ||
                    node.Kind == OutlinerNodeKind.Object ||
                    node.Kind == OutlinerNodeKind.GroupSlice)
                {
                    return new FilterNavigationAnchor(
                        node.Kind,
                        node.Id,
                        node.OwnerId);
                }
                if (node.IsObjectReference)
                {
                    return new FilterNavigationAnchor(
                        OutlinerNodeKind.Object,
                        node.Id,
                        Guid.Empty);
                }
            }

            var selectedLayerId = _selectedLayerIds.FirstOrDefault();
            if (selectedLayerId != Guid.Empty)
            {
                return new FilterNavigationAnchor(
                    OutlinerNodeKind.Layer,
                    selectedLayerId,
                    Guid.Empty);
            }

            var selectedObjectId = _selectedObjectIds.FirstOrDefault();
            return selectedObjectId == Guid.Empty
                ? null
                : new FilterNavigationAnchor(
                    OutlinerNodeKind.Object,
                    selectedObjectId,
                    Guid.Empty);
        }

        private void ExpandFilterNavigationPath(FilterNavigationAnchor anchor)
        {
            if (anchor == null || _documentModel == null)
                return;

            if (anchor.Kind == OutlinerNodeKind.Layer)
            {
                if (!_documentModel.LayersById.TryGetValue(
                    anchor.Id,
                    out var layer))
                {
                    return;
                }
                ExpandLayerPath(layer.ParentId);
                return;
            }

            if (anchor.Kind == OutlinerNodeKind.GroupSlice)
            {
                ExpandLayerPath(anchor.OwnerId);
                var currentGroupId = _documentModel.GroupHierarchy
                    .GetParentGroupId(anchor.Id);
                var visitedGroups = new HashSet<Guid>();
                while (currentGroupId != Guid.Empty &&
                    visitedGroups.Add(currentGroupId))
                {
                    SetGroupExpansionState(
                        new OutlinerGroupSliceKey(
                            currentGroupId,
                            anchor.OwnerId),
                        true);
                    currentGroupId = _documentModel.GroupHierarchy
                        .GetParentGroupId(currentGroupId);
                }
                return;
            }

            if (!_documentModel.ObjectsById.TryGetValue(
                anchor.Id,
                out var objectModel))
            {
                return;
            }

            ExpandLayerPath(objectModel.LayerId);
            foreach (var groupId in _documentModel.GroupHierarchy
                .GetCanonicalGroupPath(objectModel.Id))
            {
                SetGroupExpansionState(
                    new OutlinerGroupSliceKey(
                        groupId,
                        objectModel.LayerId),
                    true);
            }
        }

        private void ExpandLayerPath(Guid layerId)
        {
            var currentId = layerId;
            var visited = new HashSet<Guid>();
            while (currentId != Guid.Empty && visited.Add(currentId))
            {
                _layerExpansionState[currentId] = true;
                if (!_documentModel.LayersById.TryGetValue(
                    currentId,
                    out var layer))
                {
                    break;
                }
                currentId = layer.ParentId;
            }
        }

        private void SchedulePendingFilterNavigation()
        {
            var anchor = _pendingFilterNavigationAnchor;
            if (anchor == null || _isClosing)
                return;

            var generation = _filterNavigationGeneration;
            Application.Instance.AsyncInvoke(() =>
            {
                if (_isClosing ||
                    generation != _filterNavigationGeneration ||
                    !ReferenceEquals(anchor, _pendingFilterNavigationAnchor))
                {
                    return;
                }

                ExpandProjectedFilterPath(anchor);
                _tree.Invalidate();
                Application.Instance.AsyncInvoke(() =>
                    CompleteFilterNavigation(anchor, generation));
            });
        }

        private void ExpandProjectedFilterPath(FilterNavigationAnchor anchor)
        {
            if (_snapshot == null || anchor == null)
                return;

            if (anchor.Kind == OutlinerNodeKind.Object &&
                _documentModel.ObjectsById.TryGetValue(
                    anchor.Id,
                    out var objectModel))
            {
                ExpandRevealPath(objectModel);
                return;
            }

            if (anchor.Kind == OutlinerNodeKind.GroupSlice)
            {
                ExpandLayerItems(anchor.OwnerId);
                var currentGroupId = _documentModel.GroupHierarchy
                    .GetParentGroupId(anchor.Id);
                while (currentGroupId != Guid.Empty)
                {
                    if (_snapshot.GroupSliceItems.TryGetValue(
                        new OutlinerGroupSliceKey(
                            currentGroupId,
                            anchor.OwnerId),
                        out var groupItem))
                    {
                        groupItem.Expanded = true;
                    }
                    currentGroupId = _documentModel.GroupHierarchy
                        .GetParentGroupId(currentGroupId);
                }
                InvalidateVisibleSelectionRows();
                return;
            }

            if (anchor.Kind == OutlinerNodeKind.Layer &&
                _documentModel.LayersById.TryGetValue(
                    anchor.Id,
                    out var layer))
            {
                ExpandLayerItems(layer.ParentId);
                InvalidateVisibleSelectionRows();
            }
        }

        private void ExpandLayerItems(Guid layerId)
        {
            var currentId = layerId;
            var visited = new HashSet<Guid>();
            while (currentId != Guid.Empty && visited.Add(currentId))
            {
                if (_snapshot.LayerItems.TryGetValue(
                    currentId,
                    out var layerItem))
                {
                    layerItem.Expanded = true;
                }
                if (!_documentModel.LayersById.TryGetValue(
                    currentId,
                    out var layer))
                {
                    break;
                }
                currentId = layer.ParentId;
            }
        }

        private void CompleteFilterNavigation(
            FilterNavigationAnchor anchor,
            int generation)
        {
            if (_isClosing ||
                generation != _filterNavigationGeneration ||
                !ReferenceEquals(anchor, _pendingFilterNavigationAnchor) ||
                _snapshot == null)
            {
                return;
            }

            TreeGridItem target = null;
            if (anchor.Kind == OutlinerNodeKind.Layer)
                _snapshot.LayerItems.TryGetValue(anchor.Id, out target);
            else if (anchor.Kind == OutlinerNodeKind.Object)
                _snapshot.ObjectItems.TryGetValue(anchor.Id, out target);
            else if (anchor.Kind == OutlinerNodeKind.GroupSlice)
            {
                _snapshot.GroupSliceItems.TryGetValue(
                    new OutlinerGroupSliceKey(anchor.Id, anchor.OwnerId),
                    out target);
            }

            if (target != null)
            {
                SynchronizeSelectionFromDocument();
                var visibleItems = new List<TreeGridItem>();
                AddVisibleItems(_snapshot.Roots, visibleItems);
                var row = visibleItems.IndexOf(target);
                if (row >= 0)
                    _tree.ScrollToRow(row);
            }
            else if (_treeRefreshQueued &&
                anchor.Kind == OutlinerNodeKind.Object &&
                !_documentModel.ObjectsById.ContainsKey(anchor.Id))
            {
                return;
            }

            _pendingFilterNavigationAnchor = null;
        }

        private void CancelPendingFilterNavigation()
        {
            _filterNavigationGeneration++;
            _pendingFilterNavigationAnchor = null;
        }

        private void OnRevealFilteredSelectionClicked(object sender, EventArgs e)
        {
            if (_documentModel == null)
                return;

            var doc = RhinoDoc.FromRuntimeSerialNumber(_documentSerialNumber);
            if (doc == null)
                return;

            _viewState.TemporarilyRevealedLayerIds.Clear();
            _viewState.TemporarilyRevealedLayerIds.UnionWith(
                _selectedLayerIds.Where(_documentModel.LayersById.ContainsKey));
            _viewState.TemporarilyRevealedObjectIds.Clear();
            _viewState.TemporarilyRevealedObjectIds.UnionWith(
                _selectedObjectIds);

            PrepareFilterReturnNavigation();
            var loadedAnyLayer = false;
            if (_largeDocumentDeferred)
            {
                foreach (var objectId in _selectedObjectIds)
                {
                    var rhinoObject = doc.Objects.FindId(objectId);
                    if (rhinoObject == null || rhinoObject.IsDeleted)
                        continue;
                    loadedAnyLayer |= _loadedLargeLayerIndices.Add(
                        rhinoObject.Attributes.LayerIndex);
                }
            }

            if (loadedAnyLayer)
            {
                QueueTreeRefresh();
                return;
            }

            RefreshProjection();
        }

        private void UpdateHiddenSelectionState(RhinoDoc doc)
        {
            _hiddenSelectedObjectCount = 0;
            _hiddenSelectedLayerCount = 0;
            _hiddenByLayerFilterCount = 0;
            _hiddenByContentFilterCount = 0;
            if (_documentModel == null || _snapshot == null)
                return;

            foreach (var layerId in _selectedLayerIds)
            {
                if (_snapshot.LayerItems.ContainsKey(layerId) ||
                    !_documentModel.LayersById.TryGetValue(
                        layerId,
                        out var layer))
                {
                    continue;
                }

                _hiddenSelectedLayerCount++;
                if (!_viewState.LayerFilter.Matches(layer, _documentModel))
                    _hiddenByLayerFilterCount++;
            }

            foreach (var objectId in _selectedObjectIds)
            {
                if (_snapshot.ObjectItems.ContainsKey(objectId))
                    continue;
                if (_documentModel.ObjectsById.TryGetValue(
                    objectId,
                    out var objectModel))
                {
                    var layerMatches =
                        _documentModel.LayersById.TryGetValue(
                            objectModel.LayerId,
                            out var layer) &&
                        _viewState.LayerFilter.Matches(
                            layer,
                            _documentModel);
                    var contentMatches =
                        _viewState.ContentFilter.MatchesObject(
                            objectModel,
                            _selectedObjectIds) &&
                        (!_viewState.ContentFilter.FavoritesOnly ||
                         IsFavoriteFilterMatch(objectModel));
                    if (_viewState.DisplayMode != OutlinerDisplayMode.All &&
                        _snapshot.LayerItems.ContainsKey(objectModel.LayerId))
                    {
                        continue;
                    }

                    _hiddenSelectedObjectCount++;
                    if (!layerMatches)
                    {
                        _hiddenByLayerFilterCount++;
                    }
                    else if (!contentMatches)
                    {
                        _hiddenByContentFilterCount++;
                    }
                    continue;
                }

                _hiddenSelectedObjectCount++;

                var rhinoObject = doc?.Objects.FindId(objectId);
                if (rhinoObject == null || rhinoObject.IsDeleted)
                    continue;
                if (_documentModel.LayersByIndex.TryGetValue(
                    rhinoObject.Attributes.LayerIndex,
                    out var deferredLayer) &&
                    !_viewState.LayerFilter.Matches(
                        deferredLayer,
                        _documentModel))
                {
                    _hiddenByLayerFilterCount++;
                }
            }

            UpdateFilterButtonPresentation();
        }

        private void QueueFilteredSelectionProjection()
        {
            if (_filteredSelectionProjectionQueued ||
                _isClosing ||
                _treeRefreshQueued)
            {
                return;
            }

            _filteredSelectionProjectionQueued = true;
            Application.Instance.AsyncInvoke(() =>
            {
                _filteredSelectionProjectionQueued = false;
                if (_isClosing || _treeRefreshQueued || _documentModel == null)
                    return;

                try
                {
                    RefreshProjection();
                }
                catch (Exception exception)
                {
                    HandleTreeRefreshError(exception);
                }
            });
        }

        private bool RequiresFilterProjection(OutlinerStateChange change)
        {
            if (change == null || !(_viewState.HasFilters || _viewState.HasSearch))
                return false;

            if (change.LayerNamesChanged)
                return true;

            if (change.CurrentLayerChanged &&
                _viewState.LayerFilter.Scope ==
                    OutlinerLayerScopeMode.CurrentLayerTree)
            {
                return true;
            }

            if (change.FavoritesChanged &&
                _viewState.ContentFilter.FavoritesOnly)
            {
                return true;
            }

            var layerStateAffectsFilter =
                _viewState.LayerFilter.Visibility !=
                    OutlinerLayerVisibilityFilter.All ||
                _viewState.LayerFilter.Lock != OutlinerLockFilter.All;
            if (layerStateAffectsFilter &&
                (change.LayerStateChanged ||
                 change.ObjectIds.Count > 0 ||
                 change.IsolateChanged))
            {
                return true;
            }

            return false;
        }

        private bool IsFavoriteFilterMatch(OutlinerObjectModel objectModel)
        {
            if (objectModel == null || _documentModel == null)
                return false;
            if (objectModel.IsFavorite)
                return true;
            if (_documentModel.LayersById.TryGetValue(
                objectModel.LayerId,
                out var layer))
            {
                var visited = new HashSet<Guid>();
                while (layer != null && visited.Add(layer.Id))
                {
                    if (layer.IsFavorite)
                        return true;
                    layer = layer.ParentId != Guid.Empty &&
                        _documentModel.LayersById.TryGetValue(
                            layer.ParentId,
                            out var parent)
                                ? parent
                                : null;
                }
            }

            return objectModel.GroupIds.Any(groupId =>
                _documentModel.GroupsById.TryGetValue(groupId, out var group) &&
                group.IsFavorite);
        }
    }
}
