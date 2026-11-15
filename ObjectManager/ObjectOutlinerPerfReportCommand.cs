using ObjectManager.Outliner;
using Rhino;
using Rhino.Commands;

namespace ObjectManager
{
    [CommandStyle(Style.Hidden)]
    public sealed class ObjectOutlinerPerfReportCommand : Command
    {
        public override string EnglishName => "ObjectOutlinerPerfReport";

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            foreach (var line in OutlinerPerformanceRecorder.CreateSummaryLines())
                RhinoApp.WriteLine(line);
            return Result.Success;
        }
    }
}
