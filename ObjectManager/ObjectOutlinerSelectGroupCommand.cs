using System.Runtime.InteropServices;
using ObjectManager.Outliner;
using Rhino.Commands;
using Rhino.DocObjects;

namespace ObjectManager
{
    [Guid("47e4d051-3910-4e09-89de-3f90111512ea")]
    [CommandStyle(Style.Hidden | Style.DoNotRepeat)]
    public sealed class ObjectOutlinerSelectGroupCommand : SelCommand
    {
        public ObjectOutlinerSelectGroupCommand()
        {
            BeQuiet = true;
            TestLights = true;
        }

        public override string EnglishName =>
            "ObjectOutlinerSelectGroup";

        protected override bool SelFilter(RhinoObject rhinoObject)
        {
            return OutlinerGroupCommandInputRequest.Matches(rhinoObject);
        }
    }
}
