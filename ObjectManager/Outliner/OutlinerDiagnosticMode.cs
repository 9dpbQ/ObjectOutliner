using System.Threading;

namespace ObjectManager.Outliner
{
    internal static class OutlinerDiagnosticMode
    {
        private static int _enabled;

        public static bool IsEnabled => Volatile.Read(ref _enabled) != 0;

        public static void SetEnabled(bool enabled)
        {
            Volatile.Write(ref _enabled, enabled ? 1 : 0);
        }

        public static bool Toggle()
        {
            while (true)
            {
                var current = Volatile.Read(ref _enabled);
                var next = current == 0 ? 1 : 0;
                if (Interlocked.CompareExchange(
                        ref _enabled,
                        next,
                        current) == current)
                    return next != 0;
            }
        }
    }
}
