using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Eto.Forms;

namespace ObjectManager.Outliner
{
    internal sealed class MacTreeGuides
    {
        private IDisposable _windows;
        private void ShowWindows(TreeGridView tree)
        {
            if (_windows != null || tree.Handler.GetType().Assembly.GetName().Name != "Eto.Wpf") return;
            var path = Path.Combine(Path.GetDirectoryName(typeof(MacTreeGuides).Assembly.Location),
                "ObjectOutliner.WindowsGuides.dll");
            if (!File.Exists(path)) return;
            Func<object, object> parent = obj =>
            {
                var p = (obj as TreeGridItem)?.Parent as TreeGridItem;
                return p?.Tag is OutlinerNodeReference ? p : null;
            };
            Func<object, bool> last = obj =>
            {
                var p = parent(obj) as TreeGridItem;
                return p != null && p.Children.Count > 0 && ReferenceEquals(p.Children[p.Children.Count - 1], obj);
            };
            try
            {
                var type = Assembly.LoadFrom(path).GetType("ObjectOutliner.WindowsGuides.Guides", true);
                _windows = type.GetMethod("Attach").Invoke(null, new object[] { tree.ControlObject, parent, last }) as IDisposable;
            }
            catch (Exception e)
            {
                System.Diagnostics.Debug.WriteLine(e);
            }
        }
#if NET7_0
        private IntPtr _view;
        private delegate IntPtr Attach(IntPtr table);
        private delegate void Update(IntPtr view);
        private static Attach _attach;
        private static Update _refresh, _detach;

        public void Show(TreeGridView tree)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) { ShowWindows(tree); return; }
            if (_view != IntPtr.Zero || !RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return;
            var handler = tree.Handler;
            var table = handler?.GetType().GetProperty("Table")?.GetValue(handler);
            var handle = table?.GetType().GetProperty("Handle")?.GetValue(table);
            var pointer = handle is IntPtr p ? p :
                handle?.GetType().GetProperty("Handle")?.GetValue(handle) as IntPtr? ?? IntPtr.Zero;
            if (pointer == IntPtr.Zero) return;
            if (_attach == null)
            {
                var path = Path.Combine(Path.GetDirectoryName(typeof(MacTreeGuides).Assembly.Location),
                    "libOOTreeGuides.dylib");
                if (!NativeLibrary.TryLoad(path, out var lib)) return;
                _refresh = Marshal.GetDelegateForFunctionPointer<Update>(NativeLibrary.GetExport(lib, "OORefreshGuides"));
                _detach = Marshal.GetDelegateForFunctionPointer<Update>(NativeLibrary.GetExport(lib, "OODetachGuides"));
                _attach = Marshal.GetDelegateForFunctionPointer<Attach>(NativeLibrary.GetExport(lib, "OOAttachGuides"));
            }
            _view = _attach(pointer);
        }
        public void Refresh() { if (_view != IntPtr.Zero) _refresh(_view); }
        public void Hide()
        {
            _windows?.Dispose();
            _windows = null;
            if (_view == IntPtr.Zero) return;
            _detach(_view);
            _view = IntPtr.Zero;
        }
#else
        public void Show(TreeGridView tree) { ShowWindows(tree); }
        public void Refresh() { }
        public void Hide() { _windows?.Dispose(); _windows = null; }
#endif
    }
}
