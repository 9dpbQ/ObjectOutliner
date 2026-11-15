using System.Runtime.InteropServices;
using ObjectManager.Outliner;
using Rhino;
using Rhino.Commands;

namespace ObjectManager
{
    [Guid("7c239e75-b14f-4f37-849c-7e849275d066")]
    public sealed class ObjectOutlinerDebugModeCommand : Command
    {
        public override string EnglishName => "ObjectOutlinerDebugMode";

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            var enabled = OutlinerDiagnosticMode.Toggle();
            RhinoApp.WriteLine(
                enabled
                    ? OutlinerStrings.DebugModeEnabled
                    : OutlinerStrings.DebugModeDisabled);
            if (enabled)
                RhinoApp.WriteLine("Object Outliner diagnostic log: " + OutlinerPerformanceRecorder.LogPath);
            return Result.Success;
        }
    }
}
