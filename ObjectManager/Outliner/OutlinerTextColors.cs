using Eto.Drawing;
using Rhino.ApplicationSettings;

namespace ObjectManager.Outliner
{
    internal static class OutlinerTextColors
    {
        public static Color Link => Color.FromArgb(0x32, 0x84, 0xD6, 255);

        public static Color GetText(bool disabled = false)
        {
            try
            {
                var color = AppearanceSettings.GetPaintColor(
                    disabled ? PaintColor.TextDisabled : PaintColor.TextEnabled,
                    true);
                return Color.FromArgb(color.R, color.G, color.B, color.A);
            }
            catch
            {
                return disabled ? SystemColors.DisabledText : SystemColors.ControlText;
            }
        }
    }
}
