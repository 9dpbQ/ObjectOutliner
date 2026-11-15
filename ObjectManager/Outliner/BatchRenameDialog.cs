using System;
using System.Collections.Generic;
using System.Linq;
using Eto.Drawing;
using Eto.Forms;

namespace ObjectManager.Outliner
{
    internal sealed class BatchRenameDialog : Dialog<bool>
    {
        private readonly IList<string> _names;
        private readonly DropDown _mode;
        private readonly Panel _settingsHost;
        private readonly TreeGridView _preview;
        private readonly Button _applyButton;
        private readonly TextBox _formatText;
        private readonly NumericStepper _startNumber;
        private readonly NumericStepper _digitCount;
        private readonly DropDown _separator;
        private readonly DropDown _numberPlacement;
        private readonly TextBox _findText;
        private readonly TextBox _replacementText;
        private readonly CheckBox _matchCase;
        private readonly TextBox _addedText;
        private readonly DropDown _textPlacement;
        private string _selectedSourceName;
        private bool _refreshingPreview;

        public BatchRenameDialog(IEnumerable<string> names)
        {
            _names = (names ?? Enumerable.Empty<string>())
                .Select(name => name ?? string.Empty)
                .ToList();

            Title = OutlinerStrings.BatchRenameTitle;
            MinimumSize = new Size(620, 470);
            Resizable = true;
            Padding = new Padding(12);

            _mode = CreateDropDown(new[]
            {
                OutlinerStrings.BatchRenameFormat,
                OutlinerStrings.BatchRenameReplace,
                OutlinerStrings.BatchRenameAdd
            });
            _selectedSourceName = _names.FirstOrDefault() ?? string.Empty;
            _formatText = new TextBox
            {
                Text = _selectedSourceName
            };
            _startNumber = new NumericStepper
            {
                DecimalPlaces = 0,
                MinValue = 0,
                MaxValue = 999999,
                Value = 1
            };
            _digitCount = new NumericStepper
            {
                DecimalPlaces = 0,
                MinValue = 1,
                MaxValue = 9,
                Value = 3
            };
            _separator = CreateDropDown(new[]
            {
                ".",
                "_",
                "-",
                OutlinerStrings.NoSeparator
            });
            _numberPlacement = CreatePlacementDropDown();
            _findText = new TextBox();
            _replacementText = new TextBox();
            _matchCase = new CheckBox
            {
                Text = OutlinerStrings.BatchRenameMatchCase
            };
            _addedText = new TextBox();
            _textPlacement = CreatePlacementDropDown();

            _settingsHost = new Panel();
            _preview = new TreeGridView
            {
                AllowMultipleSelection = false,
                Border = BorderType.Line,
                GridLines = GridLines.Horizontal,
                RowHeight = 23,
                ShowHeader = true
            };
            _preview.Columns.Add(new GridColumn
            {
                HeaderText = OutlinerStrings.BatchRenameOriginal,
                DataCell = new TextBoxCell(0),
                Editable = false,
                Expand = true,
                MinWidth = 180
            });
            _preview.Columns.Add(new GridColumn
            {
                HeaderText = OutlinerStrings.BatchRenameResult,
                DataCell = new TextBoxCell(1),
                Editable = false,
                Expand = true,
                MinWidth = 180
            });

            _applyButton = new Button
            {
                Text = OutlinerStrings.BatchRenameApply,
                Enabled = false
            };
            var cancelButton = new Button { Text = OutlinerStrings.Cancel };
            _applyButton.Click += (sender, e) => Close(true);
            cancelButton.Click += (sender, e) => Close(false);
            DefaultButton = _applyButton;
            AbortButton = cancelButton;

            Content = new StackLayout
            {
                Orientation = Orientation.Vertical,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Spacing = 8,
                Items =
                {
                    new StackLayout
                    {
                        Orientation = Orientation.Horizontal,
                        VerticalContentAlignment = VerticalAlignment.Center,
                        Spacing = 8,
                        Items =
                        {
                            new Label { Text = OutlinerStrings.BatchRenameMode },
                            new StackLayoutItem(_mode, true)
                        }
                    },
                    new StackLayoutItem(_settingsHost, false),
                    new StackLayoutItem(_preview, true),
                    new StackLayout
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalContentAlignment = HorizontalAlignment.Right,
                        Spacing = 8,
                        Items = { cancelButton, _applyButton }
                    }
                }
            };

            _mode.SelectedIndexChanged += (sender, e) => RefreshMode();
            _preview.SelectionChanged += (sender, e) =>
                UseSelectedPreviewNameAsBase();
            _formatText.TextChanged += (sender, e) => RefreshPreview();
            _startNumber.ValueChanged += (sender, e) => RefreshPreview();
            _digitCount.ValueChanged += (sender, e) => RefreshPreview();
            _separator.SelectedIndexChanged += (sender, e) => RefreshPreview();
            _numberPlacement.SelectedIndexChanged += (sender, e) => RefreshPreview();
            _findText.TextChanged += (sender, e) => RefreshPreview();
            _replacementText.TextChanged += (sender, e) => RefreshPreview();
            _matchCase.CheckedChanged += (sender, e) => RefreshPreview();
            _addedText.TextChanged += (sender, e) => RefreshPreview();
            _textPlacement.SelectedIndexChanged += (sender, e) => RefreshPreview();
            RefreshMode();
        }

        public BatchRenameOptions Options => CreateOptions();

        private static DropDown CreateDropDown(IEnumerable<string> values)
        {
            return new DropDown
            {
                DataStore = (values ?? Enumerable.Empty<string>()).ToList(),
                SelectedIndex = 0
            };
        }

        private static DropDown CreatePlacementDropDown()
        {
            return CreateDropDown(new[]
            {
                OutlinerStrings.BatchRenameAfter,
                OutlinerStrings.BatchRenameBefore
            });
        }

        private void RefreshMode()
        {
            var layout = new DynamicLayout
            {
                DefaultSpacing = new Size(8, 6)
            };
            switch ((BatchRenameMode)Math.Max(0, _mode.SelectedIndex))
            {
                case BatchRenameMode.Replace:
                    layout.AddRow(OutlinerStrings.BatchRenameFind, _findText);
                    layout.AddRow(
                        OutlinerStrings.BatchRenameReplacement,
                        _replacementText);
                    layout.AddRow(null, _matchCase);
                    break;
                case BatchRenameMode.Add:
                    layout.AddRow(OutlinerStrings.BatchRenameText, _addedText);
                    layout.AddRow(
                        OutlinerStrings.BatchRenamePlacement,
                        _textPlacement);
                    break;
                default:
                    layout.AddRow(
                        OutlinerStrings.BatchRenameBaseName,
                        _formatText);
                    layout.AddRow(
                        OutlinerStrings.BatchRenameStartNumber,
                        _startNumber);
                    layout.AddRow(
                        OutlinerStrings.BatchRenameDigits,
                        _digitCount);
                    layout.AddRow(
                        OutlinerStrings.BatchRenameSeparator,
                        _separator);
                    layout.AddRow(
                        OutlinerStrings.BatchRenamePlacement,
                        _numberPlacement);
                    break;
            }
            _settingsHost.Content = layout;
            RefreshPreview();
        }

        private void RefreshPreview()
        {
            var options = CreateOptions();
            var rows = BatchRenamePlanner.CreatePreview(_names, options);
            var roots = new TreeGridItemCollection();
            foreach (var row in rows)
            {
                roots.Add(new TreeGridItem
                {
                    Values = new object[]
                    {
                        row.OriginalName,
                        row.ProposedName
                    }
                });
            }
            _refreshingPreview = true;
            try
            {
                _preview.DataStore = roots;
                var selected = roots
                    .OfType<TreeGridItem>()
                    .FirstOrDefault(item => string.Equals(
                        Convert.ToString(item.Values[0]),
                        _selectedSourceName,
                        StringComparison.Ordinal));
                if (selected == null)
                    selected = roots.OfType<TreeGridItem>().FirstOrDefault();
                if (selected != null)
                {
                    _selectedSourceName = Convert.ToString(
                        selected.Values[0]) ?? string.Empty;
                    _preview.SelectedItem = selected;
                }
            }
            finally
            {
                _refreshingPreview = false;
            }
            _applyButton.Enabled = BatchRenamePlanner.IsValid(rows, options);
        }

        private void UseSelectedPreviewNameAsBase()
        {
            if (_refreshingPreview)
                return;

            var item = _preview.SelectedItem as TreeGridItem;
            var name = item?.Values != null && item.Values.Length > 0
                ? Convert.ToString(item.Values[0]) ?? string.Empty
                : string.Empty;
            _selectedSourceName = name;
            if (string.IsNullOrEmpty(name) ||
                (BatchRenameMode)Math.Max(0, _mode.SelectedIndex) !=
                    BatchRenameMode.Format)
            {
                return;
            }

            _formatText.Text = name;
            _formatText.Focus();
            _formatText.SelectAll();
        }

        private BatchRenameOptions CreateOptions()
        {
            var separators = new[] { ".", "_", "-", string.Empty };
            var separatorIndex = Math.Max(0, Math.Min(
                separators.Length - 1,
                _separator.SelectedIndex));
            return new BatchRenameOptions
            {
                Mode = (BatchRenameMode)Math.Max(0, _mode.SelectedIndex),
                FormatText = _formatText.Text,
                StartNumber = (int)Math.Round(_startNumber.Value),
                DigitCount = (int)Math.Round(_digitCount.Value),
                Separator = separators[separatorIndex],
                NumberPlacement = GetPlacement(_numberPlacement),
                FindText = _findText.Text,
                ReplacementText = _replacementText.Text,
                MatchCase = _matchCase.Checked == true,
                AddedText = _addedText.Text,
                TextPlacement = GetPlacement(_textPlacement)
            };
        }

        private static BatchRenamePlacement GetPlacement(DropDown dropDown)
        {
            return dropDown.SelectedIndex == 1
                ? BatchRenamePlacement.Before
                : BatchRenamePlacement.After;
        }
    }
}
