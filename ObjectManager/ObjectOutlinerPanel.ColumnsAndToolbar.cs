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
        private GridColumn CreateStateColumn(
            int valueIndex,
            string toolTip,
            int width,
            string headerText = "")
        {
            return new GridColumn
            {
                DataCell = new ImageViewCell(valueIndex),
                Editable = false,
                HeaderText = headerText,
                HeaderToolTip = toolTip,
                MinWidth = width,
                MaxWidth = width,
                Width = width,
                Resizable = false
            };
        }

        private GridColumn CreateTextColumn(
            int valueIndex,
            string headerText,
            int width)
        {
            return new GridColumn
            {
                DataCell = new TextBoxCell(valueIndex),
                Editable = false,
                HeaderText = headerText,
                HeaderToolTip = headerText,
                MinWidth = 48,
                Width = width,
                Resizable = true
            };
        }

        private Control CreateLayerToolbar()
        {
            _addLayerButton = CreateToolbarButton(
                _icons.GetAddLayerIcon(),
                OutlinerStrings.NewLayer,
                OnAddLayerClicked,
                OnAddLayerAlternateMouseDown);
            _addSublayerButton = CreateToolbarButton(
                _icons.GetAddSublayerIcon(),
                OutlinerStrings.NewSublayer,
                OnAddSublayerClicked,
                OnAddSublayerAlternateMouseDown);
            _deleteButton = CreateToolbarButton(
                _icons.GetDeleteLayerIcon(),
                OutlinerStrings.DeleteSelectedItems,
                OnDeleteLayerClicked);
            _columnsButton = CreateToolbarButton(
                _icons.GetColumnChooserIcon(),
                OutlinerStrings.ChooseColumns,
                OnChooseColumnsClicked);
            _historyStackButton = new ToggleButton
            {
                Image = _icons.GetHistoryStackIcon(),
                MinimumSize = Size.Empty,
                Size = new Size(26, 24),
                ToolTip = OutlinerStrings.CompactHistoryToolTip,
                Checked = _viewState.CompactHistory
            };
            _historyStackButton.CheckedChanged +=
                OnHistoryStackCheckedChanged;
            _layerFilterButton = CreateToolbarButton(
                _icons.GetLayerFilterIcon(false),
                OutlinerStrings.FilterLayers,
                OnLayerFilterClicked);
            _displayModeButton = CreateToolbarButton(
                _icons.GetDisplayModeIcon(_viewState.DisplayMode),
                OutlinerStrings.FormatDisplayModeToolTip(
                    _viewState.DisplayMode),
                OnDisplayModeClicked);

            return new Panel
            {
                Padding = new Padding(4, 3),
                Content = new StackLayout
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 2,
                    Items =
                    {
                        _addLayerButton,
                        _addSublayerButton,
                        _deleteButton,
                        _layerFilterButton,
                        _historyStackButton,
                        _displayModeButton,
                        _columnsButton
                    }
                }
            };
        }

        private void OnHistoryStackCheckedChanged(object sender, EventArgs e)
        {
            if (_applyingWorkspaceState || _historyStackButton == null)
                return;

            var compact = _historyStackButton.Checked == true;
            if (_viewState.CompactHistory == compact)
                return;

            _viewState.CompactHistory = compact;
            RefreshProjection();
            CaptureWorkspaceState();
        }

        private void OnDisplayModeClicked(object sender, EventArgs e)
        {
            var anchor = sender as Control ?? _displayModeButton;
            if (!PrepareChoicePopup(anchor))
                return;

            var updating = false;
            var definitions = new[]
            {
                new KeyValuePair<OutlinerDisplayMode, string>(
                    OutlinerDisplayMode.All,
                    OutlinerStrings.DisplayAll),
                new KeyValuePair<OutlinerDisplayMode, string>(
                    OutlinerDisplayMode.Structure,
                    OutlinerStrings.DisplayStructure),
                new KeyValuePair<OutlinerDisplayMode, string>(
                    OutlinerDisplayMode.LayersOnly,
                    OutlinerStrings.DisplayLayersOnly)
            };
            var checkBoxes = definitions.ToDictionary(
                definition => definition.Key,
                definition => new CheckBox { Text = definition.Value });
            Action updateChecks = () =>
            {
                updating = true;
                try
                {
                    foreach (var entry in checkBoxes)
                    {
                        entry.Value.Checked =
                            _viewState.DisplayMode == entry.Key;
                    }
                }
                finally
                {
                    updating = false;
                }
            };
            foreach (var entry in checkBoxes)
            {
                var mode = entry.Key;
                var checkBox = entry.Value;
                checkBox.CheckedChanged += (checkSender, checkEvent) =>
                {
                    if (updating)
                        return;
                    if (checkBox.Checked != true)
                    {
                        updateChecks();
                        return;
                    }

                    ApplyDisplayMode(mode);
                    updateChecks();
                };
            }
            updateChecks();

            var content = new StackLayout
            {
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Orientation = Orientation.Vertical,
                Spacing = 4,
                Items =
                {
                    CreatePopupHeading(
                        OutlinerStrings.DisplayContentsHeading),
                    CreatePopupSeparator()
                }
            };
            foreach (var definition in definitions)
                content.Items.Add(checkBoxes[definition.Key]);

            ShowChoicePopup(
                anchor,
                245,
                content,
                checkBoxes.Values);
        }

        private void ApplyDisplayMode(OutlinerDisplayMode mode)
        {
            if (_viewState.DisplayMode == mode)
                return;

            _viewState.ClearTemporaryFilterReveal();
            _viewState.DisplayMode = mode;
            UpdateDisplayModePresentation();
            RefreshProjection();
            CaptureWorkspaceState();
        }

        private void UpdateDisplayModePresentation()
        {
            if (_displayModeButton != null)
            {
                _displayModeButton.Image = _icons.GetDisplayModeIcon(
                    _viewState.DisplayMode);
                _displayModeButton.ToolTip =
                    OutlinerStrings.FormatDisplayModeToolTip(
                        _viewState.DisplayMode);
            }
            if (_historyStackButton != null)
            {
                _historyStackButton.Enabled =
                    _viewState.DisplayMode == OutlinerDisplayMode.All;
            }
        }

        private static Button CreateToolbarButton(
            Image image,
            string toolTip,
            EventHandler<EventArgs> handler,
            EventHandler<MouseEventArgs> alternateHandler = null)
        {
            var button = new Button
            {
                Image = image,
                MinimumSize = Size.Empty,
                Size = new Size(26, 24),
                ToolTip = toolTip
            };
            button.Click += handler;
            if (alternateHandler != null)
            {
                button.MouseDown += (sender, e) =>
                {
                    if (e.Buttons != MouseButtons.Alternate)
                        return;

                    alternateHandler(sender, e);
                    e.Handled = true;
                };
            }
            return button;
        }

        private IList<OutlinerColumnLayoutEntry> CreateColumnLayout()
        {
            return new List<OutlinerColumnLayoutEntry>
            {
                new OutlinerColumnLayoutEntry("Name", OutlinerStrings.Name, _nameColumn, 0, 160, false),
                new OutlinerColumnLayoutEntry("Count", OutlinerStrings.Count, _countColumn, 1, 52, true),
                new OutlinerColumnLayoutEntry("Favorite", OutlinerStrings.Favorite, _favoriteColumn, 2, 20, true),
                new OutlinerColumnLayoutEntry("Note", OutlinerStrings.Note, _noteColumn, 3, 24, true),
                new OutlinerColumnLayoutEntry("Zoom", OutlinerStrings.Zoom, _zoomColumn, 14, 24, true),
                new OutlinerColumnLayoutEntry("Current", OutlinerStrings.Current, _currentColumn, 3, 20, true),
                new OutlinerColumnLayoutEntry("Visibility", OutlinerStrings.On, _visibilityColumn, 4, 20, true),
                new OutlinerColumnLayoutEntry("Lock", OutlinerStrings.Lock, _lockColumn, 5, 20, true),
                new OutlinerColumnLayoutEntry("Color", OutlinerStrings.Color, _colorColumn, 6, 22, true),
                new OutlinerColumnLayoutEntry("Material", OutlinerStrings.Material, _materialColumn, 7, 24, true),
                new OutlinerColumnLayoutEntry("Linetype", OutlinerStrings.Linetype, _linetypeColumn, 8, 72, true),
                new OutlinerColumnLayoutEntry("PlotColor", OutlinerStrings.PrintColor, _plotColorColumn, 9, 22, true),
                new OutlinerColumnLayoutEntry("PlotWeight", OutlinerStrings.PrintWidth, _plotWeightColumn, 10, 72, true),
                new OutlinerColumnLayoutEntry("SectionStyle", OutlinerStrings.SectionStyle, _sectionStyleColumn, 11, 82, true),
                new OutlinerColumnLayoutEntry("NewDetail", OutlinerStrings.NewDetailOn, _newDetailColumn, 12, 20, true),
                new OutlinerColumnLayoutEntry("ModelVisibility", OutlinerStrings.ModelOn, _modelVisibilityColumn, 13, 20, true)
            };
        }

        private void RestoreColumnLayout()
        {
            var settings = ObjectManagerPlugin.Instance?.Settings;
            _restoringColumnLayout = true;
            try
            {
                foreach (var entry in _columnLayout)
                {
                    entry.Column.Visible = !entry.CanHide ||
                        (settings?.GetBool("Columns." + entry.Key, entry.Column != _zoomColumn)
                            ?? entry.Column != _zoomColumn);

                    if (entry.Column.Resizable)
                        entry.Column.Width = Math.Max(
                            entry.Column.MinWidth,
                            entry.DefaultWidth);
                }

                var orderedEntries = _columnLayout
                    .OrderBy(entry => settings == null
                        ? entry.DefaultDisplayIndex
                        : settings.GetInteger(
                            "Columns.Order." + entry.Key,
                            entry.DefaultDisplayIndex))
                    .ThenBy(entry => entry.DefaultDisplayIndex)
                    .ToList();
                for (var index = 0; index < orderedEntries.Count; index++)
                    orderedEntries[index].Column.DisplayIndex = index;

                _nameColumn.Expand = true;
            }
            finally
            {
                _restoringColumnLayout = false;
            }
        }

        private void SaveColumnOrderAndVisibility()
        {
            var settings = ObjectManagerPlugin.Instance?.Settings;
            if (settings == null)
                return;

            foreach (var entry in _columnLayout)
            {
                settings.SetInteger(
                    "Columns.Order." + entry.Key,
                    entry.Column.DisplayIndex);
                if (entry.CanHide)
                    settings.SetBool(
                        "Columns." + entry.Key,
                        entry.Column.Visible);
            }
        }

        private static void FlushColumnSettings()
        {
            ObjectManagerPlugin.Instance?.SaveSettings();
        }

        private void OnColumnOrderChanged(object sender, GridColumnEventArgs e)
        {
            if (!_restoringColumnLayout)
            {
                SaveColumnOrderAndVisibility();
                FlushColumnSettings();
            }
        }


        private void OnChooseColumnsClicked(object sender, EventArgs e)
        {
            var anchor = sender as Control ?? _columnsButton;
            if (!PrepareChoicePopup(anchor))
                return;

            var updating = false;
            var entries = _columnLayout
                .Where(entry => entry.CanHide)
                .OrderBy(entry => entry.Column.DisplayIndex)
                .ToList();
            var checkBoxes = entries.ToDictionary(
                entry => entry,
                entry => new CheckBox
                {
                    Text = entry.Label,
                    Checked = entry.Column.Visible
                });
            Action updateChecks = () =>
            {
                updating = true;
                try
                {
                    foreach (var entry in entries)
                    {
                        checkBoxes[entry].Checked =
                            entry.Column.Visible;
                    }
                }
                finally
                {
                    updating = false;
                }
            };
            foreach (var entry in entries)
            {
                var currentEntry = entry;
                var checkBox = checkBoxes[currentEntry];
                checkBox.CheckedChanged += (checkSender, checkEvent) =>
                {
                    if (updating)
                        return;

                    currentEntry.Column.Visible = checkBox.Checked == true;
                    SaveColumnOrderAndVisibility();
                    FlushColumnSettings();
                };
            }

            var resetButton = new Button
            {
                Text = OutlinerStrings.ResetColumnLayout
            };
            resetButton.Click += (buttonSender, buttonEvent) =>
            {
                ResetColumnLayout();
                updateChecks();
            };

            var content = new StackLayout
            {
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Orientation = Orientation.Vertical,
                Spacing = 4,
                Items =
                {
                    CreatePopupHeading(OutlinerStrings.VisibleColumns),
                    CreatePopupSeparator()
                }
            };
            foreach (var entry in entries)
                content.Items.Add(checkBoxes[entry]);
            content.Items.Add(CreatePopupSeparator());
            var popupControls = checkBoxes.Values.Cast<Control>().ToList();
            content.Items.Add(resetButton);
            popupControls.Add(resetButton);

            ShowChoicePopup(
                anchor,
                245,
                content,
                popupControls);
        }

        private void ResetColumnLayout()
        {
            _restoringColumnLayout = true;
            try
            {
                foreach (var entry in _columnLayout)
                {
                    entry.Column.Visible = entry.Column != _zoomColumn;
                    if (entry.Column.Resizable)
                        entry.Column.Width = entry.DefaultWidth;
                }

                _nameColumn.Expand = true;

                foreach (var entry in _columnLayout.OrderBy(entry => entry.DefaultDisplayIndex))
                    entry.Column.DisplayIndex = entry.DefaultDisplayIndex;
            }
            finally
            {
                _restoringColumnLayout = false;
            }

            SaveColumnOrderAndVisibility();
            FlushColumnSettings();
            _tree.Invalidate();
        }
    }
}
