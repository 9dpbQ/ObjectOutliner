using Rhino;
using Rhino.Commands;
using Rhino.UI;

namespace ObjectManager
{
    public class ShowObjectManagerCommand : Command
    {
        public ShowObjectManagerCommand()
        {
            // Rhino only creates one instance of each command class defined in a
            // plug-in, so it is safe to store a refence in a static property.
            Instance = this;
        }

        ///<summary>The only instance of this command.</summary>
        public static ShowObjectManagerCommand Instance { get; private set; }

        ///<returns>The command name as it appears on the Rhino command line.</returns>
        public override string EnglishName => "ObjectOutliner";

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            Panels.OpenPanel(typeof(ObjectOutlinerPanel), true);
            return Result.Success;
        }
    }
}
