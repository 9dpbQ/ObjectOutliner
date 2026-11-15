using System;
using Eto.Drawing;
using Eto.Forms;
using ObjectManager.Outliner;
using Rhino;

namespace ObjectManager
{
    public sealed partial class ObjectOutlinerPanel
    {
        private Form _notePopup;

        private void CloseNotePopup()
        {
            _notePopup?.Close();
        }

        private void UpdateNoteToolTip(PointF location)
        {
            var cell = _tree.GetCellAt(location);
            var node = (cell?.Item as TreeGridItem)?.Tag as OutlinerNodeReference;
            var text = string.Empty;
            if (cell?.Column == _noteColumn && node?.Kind == OutlinerNodeKind.GroupSlice &&
                _documentModel.GroupsById.TryGetValue(node.Id, out var group))
                text = string.IsNullOrEmpty(group.Note) ? OutlinerStrings.EditNote : ObjectNote.Preview(group.Note);
            if (cell?.Column == _noteColumn && node != null &&
                _documentModel != null &&
                (node.Kind == OutlinerNodeKind.Object || node.IsObjectReference) &&
                _documentModel.ObjectsById.TryGetValue(node.Id, out var model))
                text = string.IsNullOrEmpty(model.Note) ? OutlinerStrings.EditNote : ObjectNote.Preview(model.Note);
            if (_tree.ToolTip != text)
                _tree.ToolTip = text;
        }

        private void OpenNote(OutlinerNodeReference node, PointF location)
        {
            var isGroup = node.Kind == OutlinerNodeKind.GroupSlice;
            if (!isGroup && node.Kind != OutlinerNodeKind.Object && !node.IsObjectReference)
                return;
            CloseNotePopup();
            var serial = _documentSerialNumber;
            var doc = RhinoDoc.FromRuntimeSerialNumber(serial);
            if (doc == null || doc.IsCommandRunning)
                return;
            var obj = isGroup ? null : doc.Objects.FindId(node.Id);
            _documentModel.GroupsById.TryGetValue(node.Id, out var groupModel);
            var group = isGroup && groupModel != null ? doc.Groups.FindIndex(groupModel.Index) : null;
            if (isGroup ? group == null || group.Id != node.Id || group.IsDeleted || group.IsReference :
                obj == null || obj.IsDeleted || obj.IsReference)
                return;
            var original = ObjectNote.Normalize(isGroup ? group.GetUserString(ObjectNote.Key) :
                obj.Attributes.GetUserString(ObjectNote.Key));
            var editor = new TextArea { Text = original, Wrap = true, Size = new Size(300, 160) };
            var delete = new Button
            {
                Image = _icons.GetDeleteNoteIcon(),
                ToolTip = OutlinerStrings.DeleteNote,
                Size = new Size(28, 26)
            };
            var save = new Button { Image = _icons.GetCurrentLayerIcon(), ToolTip = OutlinerStrings.SaveNote, Size = delete.Size };
            var title = isGroup ? groupModel.Name : _documentModel?.ObjectsById.TryGetValue(node.Id, out var model) == true
                ? model.DisplayName : obj.Attributes.Name;
            title = OutlinerStrings.NoteTitle(title);
            var mac = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX);
            var popup = new Form
            {
                Owner = _tree.ParentWindow,
                WindowStyle = WindowStyle.None,
                Resizable = false,
                ClientSize = new Size(330, 222),
                Content = CreatePopupFrame(new StackLayout
                {
                    Padding = 8, Spacing = 6,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Items =
                    {
                        new StackLayout { Orientation = Orientation.Horizontal, Spacing = 4,
                            Items = { mac ? delete : save, new StackLayoutItem(new Label { Text = title, ToolTip = title,
                                Wrap = WrapMode.None, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center }, true), mac ? save : delete } },
                        new StackLayoutItem(editor, true)
                    }
                })
            };
            var cancelled = false;
            _notePopup = popup;
            save.Click += (s, e) => popup.Close();
            delete.Click += (s, e) => { editor.Text = string.Empty; popup.Close(); };
            popup.KeyDown += (s, e) =>
            {
                if (e.Key != Keys.Escape || OutlinerTextCompositionAdapter.IsComposing(editor))
                    return;
                cancelled = true;
                e.Handled = true;
                popup.Close();
            };
            popup.LostFocus += (s, e) => Application.Instance.AsyncInvoke(() =>
            {
                if (ReferenceEquals(_notePopup, popup) && !popup.HasFocus &&
                    !editor.HasFocus && !delete.HasFocus && !save.HasFocus)
                    popup.Close();
            });
            popup.Closing += (s, e) =>
            {
                if (cancelled)
                    return;
                var currentDoc = RhinoDoc.FromRuntimeSerialNumber(serial);
                var current = isGroup ? null : currentDoc?.Objects.FindId(node.Id);
                var currentGroup = isGroup ? currentDoc?.Groups.FindIndex(groupModel.Index) : null;
                var value = ObjectNote.Normalize(editor.Text);
                if (currentDoc == null || currentDoc.IsClosing ||
                    (isGroup ? currentGroup == null || currentGroup.Id != node.Id ||
                        currentGroup.IsDeleted || currentGroup.IsReference :
                        current == null || current.IsDeleted || current.IsReference) || value == original)
                    return;
                var undo = currentDoc.BeginUndoRecord(OutlinerStrings.EditNote);
                try
                {
                    var stored = value.Length == 0 ? null : value;
                    bool saved;
                    if (isGroup)
                    {
                        saved = currentGroup.SetUserString(ObjectNote.Key, stored);
                        if (saved && _documentModel.GroupsById.TryGetValue(node.Id, out var displayed))
                        {
                            displayed.Note = value;
                            if (_snapshot.GroupSliceItemsByGroupId.TryGetValue(node.Id, out var slices))
                            {
                                foreach (var slice in slices)
                                    slice.SetValue(OutlinerValueIndex.Note, _icons.GetNoteIcon(value.Length > 0));
                                ReloadStateItems(slices);
                            }
                        }
                    }
                    else
                    {
                        var attributes = current.Attributes.Duplicate();
                        attributes.SetUserString(ObjectNote.Key, stored);
                        saved = currentDoc.Objects.ModifyAttributes(current.Id, attributes, true);
                    }
                    if (saved)
                        currentDoc.Modified = true;
                    else
                    {
                        e.Cancel = true;
                        _status.Text = OutlinerStrings.NoteSaveFailed;
                        editor.Focus();
                    }
                }
                finally
                {
                    if (undo != 0)
                        currentDoc.EndUndoRecord(undo);
                }
            };
            popup.Closed += (s, e) =>
            {
                if (ReferenceEquals(_notePopup, popup))
                    _notePopup = null;
            };
            var point = _tree.PointToScreen(location);
            var area = (Screen.FromPoint(point) ?? Screen.PrimaryScreen).WorkingArea;
            popup.Location = new Point((int)Math.Max(area.Left, Math.Min(point.X, area.Right - 330)),
                (int)Math.Max(area.Top, Math.Min(point.Y + 16, area.Bottom - 222)));
            popup.Show();
            editor.Focus();
        }
    }
}
