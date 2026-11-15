using System;
using System.Collections.Generic;
using System.Linq;
using Eto.Drawing;
using Eto.Forms;

namespace ObjectManager
{
    public sealed partial class ObjectOutlinerPanel
    {
        private Form _choicePopup;
        private Control _choicePopupAnchor;
        private IList<Control> _choicePopupFocusControls;

        private bool PrepareChoicePopup(Control anchor)
        {
            CloseContentFilterPopup();
            if (_choicePopup == null)
                return !_isClosing;

            var togglingCurrent = ReferenceEquals(
                _choicePopupAnchor,
                anchor);
            CloseChoicePopup();
            return !togglingCurrent && !_isClosing;
        }

        private void ShowChoicePopup(
            Control anchor,
            int width,
            Control content,
            IEnumerable<Control> focusControls)
        {
            if (anchor == null || content == null || _isClosing)
                return;

            var popupContent = CreatePopupFrame(content);
            var preferredSize = popupContent.GetPreferredSize(
                new SizeF(width, 1000));
            var popup = new Form
            {
                ClientSize = new Size(
                    width,
                    Math.Max(1, (int)Math.Ceiling(preferredSize.Height))),
                Owner = anchor.ParentWindow,
                Resizable = false,
                Topmost = false,
                WindowStyle = WindowStyle.None,
                Content = popupContent
            };

            _choicePopup = popup;
            _choicePopupAnchor = anchor;
            _choicePopupFocusControls = (focusControls ??
                    Enumerable.Empty<Control>())
                .Where(control => control != null)
                .Distinct()
                .ToList();

            popup.KeyDown += (sender, e) =>
            {
                if (e.Key != Keys.Escape)
                    return;
                e.Handled = true;
                CloseChoicePopup();
            };
            popup.LostFocus += (sender, e) =>
            {
                Application.Instance.AsyncInvoke(() =>
                {
                    if (ReferenceEquals(_choicePopup, popup) &&
                        !ChoicePopupHasFocus())
                    {
                        CloseChoicePopup();
                    }
                });
            };
            popup.Closed += (sender, e) => ClearChoicePopup(popup);

            PositionPopup(popup, anchor);
            popup.Show();
            popup.Focus();
        }

        private static Control CreatePopupFrame(Control content)
        {
            return new Panel
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
        }

        private static Label CreatePopupHeading(string text)
        {
            return new Label
            {
                Text = text,
                Font = SystemFonts.Bold(9),
                TextAlignment = TextAlignment.Right
            };
        }

        private static Panel CreatePopupSeparator()
        {
            return new Panel
            {
                BackgroundColor = SystemColors.Control,
                Height = 1
            };
        }

        private static void PositionPopup(Form popup, Control anchor)
        {
            if (popup == null || anchor == null)
                return;

            var topLeft = anchor.PointToScreen(new PointF(0, 0));
            var bottomRight = anchor.PointToScreen(new PointF(
                anchor.Width,
                anchor.Height));
            var screen = Screen.FromPoint(bottomRight) ?? Screen.PrimaryScreen;
            var workingArea = screen.WorkingArea;
            var width = popup.ClientSize.Width;
            var height = popup.ClientSize.Height;

            var x = Math.Max(
                workingArea.Left,
                Math.Min(bottomRight.X - width, workingArea.Right - width));
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

        private bool ChoicePopupHasFocus()
        {
            return _choicePopup?.HasFocus == true ||
                (_choicePopupFocusControls?.Any(
                    control => control.HasFocus) == true);
        }

        private void CloseChoicePopup()
        {
            var popup = _choicePopup;
            if (popup == null)
                return;

            popup.Close();
            ClearChoicePopup(popup);
        }

        private void ClearChoicePopup(Form popup)
        {
            if (!ReferenceEquals(_choicePopup, popup))
                return;

            _choicePopup = null;
            _choicePopupAnchor = null;
            _choicePopupFocusControls = null;
        }
    }
}
