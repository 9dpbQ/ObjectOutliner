using System.Reflection;
using ObjectManager.Outliner;
using Rhino;
using Rhino.FileIO;
using Rhino.PlugIns;
using Rhino.UI;

namespace ObjectManager
{
    ///<summary>
    /// <para>Every RhinoCommon .rhp assembly must have one and only one PlugIn-derived
    /// class. DO NOT create instances of this class yourself. It is the
    /// responsibility of Rhino to create an instance of this class.</para>
    /// <para>To complete plug-in information, please also see all PlugInDescription
    /// attributes in AssemblyInfo.cs (you might need to click "Project" ->
    /// "Show All Files" to see it in the "Solution Explorer" window).</para>
    ///</summary>
    public class ObjectManagerPlugin : PlugIn
    {
        private DocumentLoadMonitor _documentLoadMonitor;
        private FavoriteDocumentStore _favoriteStore;
        private OutlinerWorkspaceDocumentStore _workspaceStore;
        private ObjectAliasDocumentStore _nameStore;
        private GroupOrderDocumentStore _groupOrderStore;
        private OutlinerRelationshipHub _relationshipHub;
        private OutlinerHistoryCaptureCoordinator _historyCaptureCoordinator;
        private ObjectNamingCoordinator _namingCoordinator;

        public ObjectManagerPlugin()
        {
            Instance = this;
        }

        ///<summary>Gets the only instance of the ObjectManagerPlugin plug-in.</summary>
        public static ObjectManagerPlugin Instance { get; private set; }

        internal DocumentLoadMonitor DocumentLoadMonitor => _documentLoadMonitor;

        internal FavoriteDocumentStore FavoriteStore => _favoriteStore;

        internal OutlinerWorkspaceDocumentStore WorkspaceStore =>
            _workspaceStore;

        internal ObjectAliasDocumentStore NameStore => _nameStore;

        internal GroupOrderDocumentStore GroupOrderStore => _groupOrderStore;

        internal OutlinerRelationshipHub RelationshipHub => _relationshipHub;

        protected override LoadReturnCode OnLoad(ref string errorMessage)
        {
            _documentLoadMonitor = new DocumentLoadMonitor();
            _favoriteStore = new FavoriteDocumentStore();
            _workspaceStore = new OutlinerWorkspaceDocumentStore();
            _nameStore = new ObjectAliasDocumentStore();
            _groupOrderStore = new GroupOrderDocumentStore();
            _relationshipHub = new OutlinerRelationshipHub();
            _historyCaptureCoordinator = new OutlinerHistoryCaptureCoordinator(
                _documentLoadMonitor,
                _relationshipHub);
            _namingCoordinator = new ObjectNamingCoordinator(
                _documentLoadMonitor,
                _relationshipHub);
            Panels.RegisterPanel(
                this,
                typeof(ObjectOutlinerPanel),
                OutlinerStrings.PanelTitle,
                Assembly.GetExecutingAssembly(),
                "ObjectManager.EmbeddedResources.object-outliner.ico",
                PanelType.PerDoc);

            return LoadReturnCode.Success;
        }

        protected override bool ShouldCallWriteDocument(
            FileWriteOptions options)
        {
            return true;
        }

        protected override void WriteDocument(
            RhinoDoc doc,
            BinaryArchiveWriter archive,
            FileWriteOptions options)
        {
            archive.Write3dmChunkVersion(4, 0);
            _favoriteStore?.Write(doc, archive);
            _workspaceStore?.Write(doc, archive);
            _nameStore?.Write(doc, archive);
            _groupOrderStore?.Write(doc, archive);
        }

        protected override void ReadDocument(
            RhinoDoc doc,
            BinaryArchiveReader archive,
            FileReadOptions options)
        {
            archive.Read3dmChunkVersion(out var major, out var minor);
            if (major == 1)
            {
                _favoriteStore?.Read(doc, archive);
                return;
            }
            if (major != 2 && major != 3 && major != 4)
                return;

            _favoriteStore?.Read(doc, archive);
            try
            {
                _workspaceStore?.Read(doc, archive);
            }
            catch (System.Exception exception)
            {
                RhinoApp.WriteLine(
                    "Object Outliner ignored invalid workspace data: {0}",
                    exception.Message);
            }
            if (major < 3)
                return;

            try
            {
                _nameStore?.Read(doc, archive);
            }
            catch (System.Exception exception)
            {
                RhinoApp.WriteLine(
                    "Object Outliner ignored invalid naming data: {0}",
                    exception.Message);
            }
            if (major < 4)
                return;

            try
            {
                _groupOrderStore?.Read(doc, archive);
            }
            catch (System.Exception exception)
            {
                RhinoApp.WriteLine(
                    "Object Outliner ignored invalid group order data: {0}",
                    exception.Message);
            }
        }

        protected override void OnShutdown()
        {
            _namingCoordinator?.Dispose();
            _namingCoordinator = null;
            _historyCaptureCoordinator?.Dispose();
            _historyCaptureCoordinator = null;
            _relationshipHub = null;
            _groupOrderStore?.Dispose();
            _groupOrderStore = null;
            _nameStore?.Dispose();
            _nameStore = null;
            _workspaceStore?.Dispose();
            _workspaceStore = null;
            _favoriteStore?.Dispose();
            _favoriteStore = null;
            _documentLoadMonitor?.Dispose();
            _documentLoadMonitor = null;
            base.OnShutdown();
        }
    }
}
