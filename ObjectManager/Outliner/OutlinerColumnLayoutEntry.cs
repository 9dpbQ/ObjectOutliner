using Eto.Forms;

namespace ObjectManager.Outliner
{
    internal sealed class OutlinerColumnLayoutEntry
    {
        public OutlinerColumnLayoutEntry(
            string key,
            string label,
            GridColumn column,
            int defaultDisplayIndex,
            int defaultWidth,
            bool canHide)
        {
            Key = key;
            Label = label;
            Column = column;
            DefaultDisplayIndex = defaultDisplayIndex;
            DefaultWidth = defaultWidth;
            CanHide = canHide;
        }

        public string Key { get; }

        public string Label { get; }

        public GridColumn Column { get; }

        public int DefaultDisplayIndex { get; }

        public int DefaultWidth { get; }

        public bool CanHide { get; }
    }
}
