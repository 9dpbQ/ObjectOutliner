using System;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Eto.Forms;

namespace ObjectManager.Outliner
{
    internal static class OutlinerTextCompositionAdapter
    {
        private const int CompositionString = 0x0008;

        public static bool IsComposing(Control textBox)
        {
            var handler = textBox?.Handler;
            if (handler == null)
                return false;

            var assemblyName = handler.GetType().Assembly.GetName().Name;
            if (string.Equals(
                assemblyName,
                "Eto.macOS",
                StringComparison.Ordinal))
            {
                return IsMacComposing(handler);
            }
            if (string.Equals(
                assemblyName,
                "Eto.Wpf",
                StringComparison.Ordinal))
            {
                return IsWindowsComposing(handler);
            }
            return false;
        }

        private static bool IsMacComposing(object handler)
        {
            try
            {
                var control = GetPublicProperty(handler, "Control") ??
                    GetPublicProperty(handler, "NativeControl");
                var editor = GetPublicProperty(control, "CurrentEditor") ?? control;
                var value = GetPublicProperty(editor, "HasMarkedText");
                return value is bool && (bool)value;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsWindowsComposing(object handler)
        {
            IntPtr inputContext = IntPtr.Zero;
            IntPtr windowHandle = IntPtr.Zero;
            try
            {
                var control = GetPublicProperty(handler, "Control") ??
                    GetPublicProperty(handler, "NativeControl");
                windowHandle = GetWpfWindowHandle(control);
                if (windowHandle == IntPtr.Zero)
                    return false;

                inputContext = ImmGetContext(windowHandle);
                return inputContext != IntPtr.Zero &&
                    ImmGetCompositionString(
                        inputContext,
                        CompositionString,
                        IntPtr.Zero,
                        0) > 0;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (inputContext != IntPtr.Zero && windowHandle != IntPtr.Zero)
                    ImmReleaseContext(windowHandle, inputContext);
            }
        }

        private static IntPtr GetWpfWindowHandle(object control)
        {
            if (control == null)
                return IntPtr.Zero;

            var presentationCore = AppDomain.CurrentDomain
                .GetAssemblies()
                .FirstOrDefault(assembly => string.Equals(
                    assembly.GetName().Name,
                    "PresentationCore",
                    StringComparison.Ordinal));
            var presentationSource = presentationCore?
                .GetType("System.Windows.PresentationSource", false);
            var fromVisual = presentationSource?
                .GetMethods(BindingFlags.Static | BindingFlags.Public)
                .FirstOrDefault(method =>
                    method.Name == "FromVisual" &&
                    method.GetParameters().Length == 1 &&
                    method.GetParameters()[0].ParameterType
                        .IsInstanceOfType(control));
            var source = fromVisual?.Invoke(null, new[] { control });
            var handle = GetPublicProperty(source, "Handle");
            return handle is IntPtr ? (IntPtr)handle : IntPtr.Zero;
        }

        private static object GetPublicProperty(object target, string name)
        {
            return target?
                .GetType()
                .GetProperty(
                    name,
                    BindingFlags.Instance | BindingFlags.Public)
                ?.GetValue(target);
        }

        [DllImport("imm32.dll")]
        private static extern IntPtr ImmGetContext(IntPtr windowHandle);

        [DllImport("imm32.dll")]
        private static extern bool ImmReleaseContext(
            IntPtr windowHandle,
            IntPtr inputContext);

        [DllImport(
            "imm32.dll",
            CharSet = CharSet.Unicode,
            EntryPoint = "ImmGetCompositionStringW")]
        private static extern int ImmGetCompositionString(
            IntPtr inputContext,
            int index,
            IntPtr buffer,
            int bufferLength);
    }
}
