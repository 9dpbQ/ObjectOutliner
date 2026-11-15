using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Eto.Drawing;
using Eto.Forms;

namespace ObjectManager.Outliner
{
    internal sealed class OutlinerNameEditEventArgs : EventArgs
    {
        public OutlinerNameEditEventArgs(TreeGridItem item, string text)
        {
            Item = item;
            Text = text;
        }

        public TreeGridItem Item { get; }

        public string Text { get; }
    }

    internal sealed class OutlinerNameCell : CustomCell
    {
        private readonly List<WeakReference<OutlinerNameCellView>> _views =
            new List<WeakReference<OutlinerNameCellView>>();
        private readonly Font _regularFont = SystemFonts.Default();
        private readonly Font _linkFont = SystemFonts.Default(
            null,
            FontDecoration.Underline);
        private long _configurationSequence;
        private TreeGridItem _activeEditItem;
        private string _activeEditText;
        private bool _handlingEditCommandKey;

        public event EventHandler<OutlinerNameEditEventArgs> EditCommitted;

        public event EventHandler EditEnded;

        public bool HasActiveEdit => _activeEditItem != null;

        public bool IsHandlingEditCommandKey => _handlingEditCommandKey;

        public bool IsHandlingCompositionCommandKey => GetLiveViews()
            .Any(candidate =>
                candidate.IsEditing &&
                candidate.IsHandlingCompositionCommandKey &&
                ReferenceEquals(candidate.Item, _activeEditItem));

        protected override string OnGetIdentifier(CellEventArgs args)
        {
            return "outliner-name";
        }

        protected override Control OnCreateCell(CellEventArgs args)
        {
            var view = new OutlinerNameCellView(
                args,
                _regularFont,
                _linkFont);
            view.EditCommitted += OnViewEditCommitted;
            view.EditCanceled += OnViewEditCanceled;
            view.EditDisplaced += OnViewEditDisplaced;
            view.EditCommandKeyPressed += OnViewEditCommandKeyPressed;
            view.Refreshed += OnViewRefreshed;
            _views.Add(new WeakReference<OutlinerNameCellView>(view));
            view.MarkConfigured(++_configurationSequence);
            return view;
        }

        protected override void OnConfigureCell(CellEventArgs args, Control control)
        {
            var view = control as OutlinerNameCellView;
            if (view != null)
            {
                view.RefreshFromCell();
                view.MarkConfigured(++_configurationSequence);
            }
        }

        public bool TryBeginEdit(TreeGridItem item, string text)
        {
            if (ReferenceEquals(_activeEditItem, item))
            {
                RestoreActiveEdit();
                return true;
            }

            CommitActiveEdit();

            var targetView = GetLiveViews()
                .Where(candidate => ReferenceEquals(candidate.Item, item))
                .OrderByDescending(candidate => candidate.ConfigurationSequence)
                .FirstOrDefault();
            if (targetView == null)
                return false;

            _activeEditItem = item;
            _activeEditText = text ?? string.Empty;
            if (targetView.BeginEdit(_activeEditText))
                return true;

            _activeEditItem = null;
            _activeEditText = null;
            EditEnded?.Invoke(this, EventArgs.Empty);
            return false;
        }

        public void CancelActiveEdit()
        {
            if (_activeEditItem == null)
                return;

            var activeView = FindActiveView();
            if (activeView != null)
            {
                activeView.CancelEdit();
                return;
            }

            _activeEditItem = null;
            _activeEditText = null;
            EditEnded?.Invoke(this, EventArgs.Empty);
        }

        public int RefreshItems(IEnumerable<TreeGridItem> items)
        {
            var targets = new HashSet<TreeGridItem>(
                items ?? Enumerable.Empty<TreeGridItem>());
            if (targets.Count == 0)
                return 0;

            var refreshed = 0;
            foreach (var view in GetLiveViews())
            {
                if (!targets.Contains(view.Item))
                    continue;

                view.RefreshFromCell();
                refreshed++;
            }
            return refreshed;
        }

        public void CommitActiveEdit()
        {
            if (_activeEditItem == null)
                return;

            var activeView = FindActiveView();
            if (activeView != null)
            {
                activeView.CommitEdit();
                return;
            }

            var item = _activeEditItem;
            var text = _activeEditText;
            _activeEditItem = null;
            _activeEditText = null;
            EditCommitted?.Invoke(
                this,
                new OutlinerNameEditEventArgs(item, text));
            EditEnded?.Invoke(this, EventArgs.Empty);
        }

        private OutlinerNameCellView FindActiveView()
        {
            return GetLiveViews().FirstOrDefault(candidate =>
                candidate.IsEditing &&
                ReferenceEquals(candidate.Item, _activeEditItem));
        }

        private IList<OutlinerNameCellView> GetLiveViews()
        {
            var liveViews = new List<OutlinerNameCellView>(_views.Count);
            for (var index = _views.Count - 1; index >= 0; index--)
            {
                if (_views[index].TryGetTarget(out var view) && view != null)
                {
                    liveViews.Add(view);
                }
                else
                {
                    _views.RemoveAt(index);
                }
            }
            return liveViews;
        }

        private void OnViewEditCommitted(
            object sender,
            OutlinerNameEditEventArgs e)
        {
            if (ReferenceEquals(_activeEditItem, e.Item))
            {
                _activeEditItem = null;
                _activeEditText = null;
            }
            EditCommitted?.Invoke(this, e);
            EditEnded?.Invoke(this, EventArgs.Empty);
        }

        private void OnViewEditCanceled(
            object sender,
            OutlinerNameEditEventArgs e)
        {
            if (!ReferenceEquals(_activeEditItem, e.Item))
                return;

            _activeEditItem = null;
            _activeEditText = null;
            EditEnded?.Invoke(this, EventArgs.Empty);
        }

        private void OnViewEditDisplaced(
            object sender,
            OutlinerNameEditEventArgs e)
        {
            if (!ReferenceEquals(_activeEditItem, e.Item))
                return;

            _activeEditText = e.Text ?? string.Empty;
            Application.Instance.AsyncInvoke(RestoreActiveEdit);
        }

        private void OnViewRefreshed(object sender, EventArgs e)
        {
            RestoreActiveEdit();
        }

        private void OnViewEditCommandKeyPressed(object sender, EventArgs e)
        {
            _handlingEditCommandKey = true;
            Application.Instance.AsyncInvoke(() =>
                _handlingEditCommandKey = false);
        }

        private void RestoreActiveEdit()
        {
            if (_activeEditItem == null || FindActiveView() != null)
                return;

            var targetView = GetLiveViews()
                .Where(candidate => ReferenceEquals(candidate.Item, _activeEditItem))
                .OrderByDescending(candidate => candidate.ConfigurationSequence)
                .FirstOrDefault();
            targetView?.BeginEdit(_activeEditText ?? string.Empty);
        }
    }

    internal sealed class OutlinerNameCellView : Panel
    {
        private readonly CellEventArgs _cell;
        private readonly Font _regularFont;
        private readonly Font _linkFont;
        private readonly Label _selectionMarker;
        private readonly ImageView _image;
        private readonly Label _label;
        private readonly TextBox _editor;
        private readonly Panel _textHost;
        private bool _editing;
        private bool _handlingCompositionCommandKey;
        private PendingEditAction _pendingKeyAction;

        private enum PendingEditAction
        {
            None,
            Commit,
            Cancel
        }

        public OutlinerNameCellView(
            CellEventArgs cell,
            Font regularFont,
            Font linkFont)
        {
            _cell = cell;
            _regularFont = regularFont;
            _linkFont = linkFont;

            _selectionMarker = new Label
            {
                Font = SystemFonts.Bold(8),
                Text = "\u25cf",
                TextColor = Colors.DarkOrange,
                VerticalAlignment = VerticalAlignment.Center,
                Visible = false
            };
            _image = new ImageView
            {
                Size = new Size(16, 16)
            };
            _label = new Label
            {
                Font = _regularFont,
                TextAlignment = TextAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Wrap = WrapMode.None
            };
            _editor = new TextBox
            {
                ShowBorder = false,
                TextAlignment = TextAlignment.Left
            };
            _editor.KeyDown += OnEditorKeyDown;
            _editor.KeyUp += OnEditorKeyUp;
            _editor.LostFocus += OnEditorLostFocus;

            _textHost = new Panel
            {
                Content = _label
            };
            Content = new StackLayout
            {
                Orientation = Orientation.Horizontal,
                Padding = new Padding(0),
                Spacing = 3,
                VerticalContentAlignment = VerticalAlignment.Center,
                Items =
                {
                    new StackLayoutItem(_selectionMarker, false),
                    new StackLayoutItem(_image, false),
                    new StackLayoutItem(_textHost, true)
                }
            };

            _cell.PropertyChanged += OnCellPropertyChanged;
            RefreshFromCell();
        }

        public event EventHandler<OutlinerNameEditEventArgs> EditCommitted;

        public event EventHandler<OutlinerNameEditEventArgs> EditCanceled;

        public event EventHandler<OutlinerNameEditEventArgs> EditDisplaced;

        public event EventHandler EditCommandKeyPressed;

        public event EventHandler Refreshed;

        public TreeGridItem Item { get; private set; }

        public bool IsEditing => _editing;

        public bool IsHandlingCompositionCommandKey =>
            _handlingCompositionCommandKey;

        public long ConfigurationSequence { get; private set; }

        public void MarkConfigured(long sequence)
        {
            ConfigurationSequence = sequence;
        }

        public void RefreshFromCell()
        {
            var nextItem = _cell.Item as TreeGridItem;
            OutlinerNameEditEventArgs displacedEdit = null;
            if (!ReferenceEquals(Item, nextItem) && _editing)
                displacedEdit = DetachEdit();

            Item = nextItem;
            if (Item == null)
            {
                _selectionMarker.Visible = false;
                _image.Image = null;
                _image.Visible = false;
                _label.Text = string.Empty;
                if (displacedEdit != null)
                    EditDisplaced?.Invoke(this, displacedEdit);
                Refreshed?.Invoke(this, EventArgs.Empty);
                return;
            }

            var values = Item.Values;
            var icon = values != null && values.Length > OutlinerValueIndex.Icon
                ? values[OutlinerValueIndex.Icon] as Image
                : null;
            _image.Image = icon;
            _image.Visible = icon != null;

            var text = values != null && values.Length > OutlinerValueIndex.Name
                ? Convert.ToString(values[OutlinerValueIndex.Name])
                : string.Empty;
            _label.Text = text ?? string.Empty;

            var unavailable = values != null &&
                values.Length > OutlinerValueIndex.IsUnavailable &&
                values[OutlinerValueIndex.IsUnavailable] is bool &&
                (bool)values[OutlinerValueIndex.IsUnavailable];
            var containsSelectedDescendant = values != null &&
                values.Length > OutlinerValueIndex.ContainsSelectedDescendant &&
                values[OutlinerValueIndex.ContainsSelectedDescendant] is bool &&
                (bool)values[OutlinerValueIndex.ContainsSelectedDescendant];
            _selectionMarker.Visible = containsSelectedDescendant;
            var node = Item.Tag as OutlinerNodeReference;
            var isLink = !unavailable && node != null && node.IsObjectReference;
            _label.Font = isLink
                ? _linkFont
                : _regularFont;
            _label.TextColor = _cell.IsSelected
                ? _cell.CellTextColor
                : isLink
                    ? OutlinerTextColors.Link
                    : OutlinerTextColors.GetText(unavailable || (values != null &&
                        values.Length > OutlinerValueIndex.DimName && Equals(values[OutlinerValueIndex.DimName], true)));
            if (displacedEdit != null)
                EditDisplaced?.Invoke(this, displacedEdit);
            Refreshed?.Invoke(this, EventArgs.Empty);
        }


        public bool BeginEdit(string text)
        {
            if (Item == null || _editing)
                return false;

            _editing = true;
            _handlingCompositionCommandKey = false;
            _pendingKeyAction = PendingEditAction.None;
            _editor.Text = text ?? string.Empty;
            _textHost.Content = _editor;
            Application.Instance.AsyncInvoke(() =>
            {
                if (!_editing)
                    return;
                _editor.Focus();
                _editor.SelectAll();
            });
            return true;
        }

        public void CommitEdit()
        {
            CompleteEdit(true);
        }

        public void CancelEdit()
        {
            CompleteEdit(false);
        }

        private void CompleteEdit(bool commit)
        {
            if (!_editing)
                return;

            var edit = DetachEdit();

            if (commit)
                EditCommitted?.Invoke(this, edit);
            else
                EditCanceled?.Invoke(this, edit);
            RefreshFromCell();
        }

        private OutlinerNameEditEventArgs DetachEdit()
        {
            var edit = new OutlinerNameEditEventArgs(Item, _editor.Text);
            _editing = false;
            _handlingCompositionCommandKey = false;
            _pendingKeyAction = PendingEditAction.None;
            _textHost.Content = _label;
            return edit;
        }

        private void OnCellPropertyChanged(
            object sender,
            PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "Item" ||
                e.PropertyName == "IsSelected" ||
                e.PropertyName == "CellTextColor" ||
                e.PropertyName == "Value")
            {
                RefreshFromCell();
            }
        }

        private void OnEditorKeyDown(object sender, KeyEventArgs e)
        {
            var isCommandKey = e.Key == Keys.Enter || e.Key == Keys.Escape;
            if (isCommandKey &&
                OutlinerTextCompositionAdapter.IsComposing(_editor))
            {
                _handlingCompositionCommandKey = true;
                _pendingKeyAction = PendingEditAction.None;
                return;
            }

            _handlingCompositionCommandKey = false;
            if (e.Key == Keys.Enter)
            {
                e.Handled = true;
                _pendingKeyAction = PendingEditAction.Commit;
                EditCommandKeyPressed?.Invoke(this, EventArgs.Empty);
            }
            else if (e.Key == Keys.Escape)
            {
                e.Handled = true;
                _pendingKeyAction = PendingEditAction.Cancel;
                EditCommandKeyPressed?.Invoke(this, EventArgs.Empty);
            }
        }

        private void OnEditorKeyUp(object sender, KeyEventArgs e)
        {
            if (_handlingCompositionCommandKey &&
                (e.Key == Keys.Enter || e.Key == Keys.Escape))
            {
                _handlingCompositionCommandKey = false;
                _pendingKeyAction = PendingEditAction.None;
                return;
            }

            if ((e.Key != Keys.Enter && e.Key != Keys.Escape) ||
                _pendingKeyAction == PendingEditAction.None)
            {
                return;
            }

            e.Handled = true;
            var action = _pendingKeyAction;
            _pendingKeyAction = PendingEditAction.None;
            Application.Instance.AsyncInvoke(() =>
            {
                if (!_editing)
                    return;

                if (action == PendingEditAction.Commit)
                    CommitEdit();
                else
                    CancelEdit();
            });
        }

        private void OnEditorLostFocus(object sender, EventArgs e)
        {
            if (!_editing)
                return;

            if (OutlinerTextCompositionAdapter.IsComposing(_editor))
            {
                _handlingCompositionCommandKey = false;
                _pendingKeyAction = PendingEditAction.None;
                Application.Instance.AsyncInvoke(() =>
                {
                    if (!_editing || _editor.HasFocus)
                        return;
                    if (!OutlinerTextCompositionAdapter.IsComposing(_editor))
                        CommitEdit();
                });
                return;
            }

            var action = _pendingKeyAction;
            _pendingKeyAction = PendingEditAction.None;
            if (action == PendingEditAction.Cancel)
                CancelEdit();
            else
                CommitEdit();
        }
    }
}
