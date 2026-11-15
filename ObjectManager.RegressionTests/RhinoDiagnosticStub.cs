namespace Rhino
{
    // Diagnostics tests do not load the native Rhino runtime or write to its console.
    internal static class RhinoApp
    {
        public static void WriteLine(string text) { }
    }
}
