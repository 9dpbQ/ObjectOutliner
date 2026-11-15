using System;

namespace ObjectManager.Outliner
{
    internal static class OutlinerRefreshPolicy
    {
        public static bool ReplaceSelection(int added, int removed)
        {
            // Repeated native UnselectRow calls are costly even for a few dozen rows.
            return removed > 1 || (long)added + removed >= 128;
        }

        public static bool UseIncrementalLayers(int layers, int objects, int totalObjects)
        {
            return layers <= 8 ||
                (layers <= 64 && objects <= Math.Max(4096, totalObjects / 2));
        }
    }
}
