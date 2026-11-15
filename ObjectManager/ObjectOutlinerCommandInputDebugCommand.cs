using System.Runtime.InteropServices;
using ObjectManager.Outliner;
using Rhino;
using Rhino.Commands;

namespace ObjectManager
{
    [Guid("c3ec7d97-1d4f-4628-b149-0735fcc93b85")]
    [CommandStyle(Style.Hidden)]
    public sealed class ObjectOutlinerCommandInputDebugCommand : Command
    {
        public override string EnglishName =>
            "ObjectOutlinerCommandInputDebug";

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            OutlinerCommandInputDiagnostics.Enabled =
                !OutlinerCommandInputDiagnostics.Enabled;
            RhinoApp.WriteLine(
                "Object Outliner command input diagnostics: {0}",
                OutlinerCommandInputDiagnostics.Enabled
                    ? "enabled"
                    : "disabled");
            return Result.Success;
        }
    }
}
