using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;
using Libraries;
using ProgrammingParadigms;
using DomainAbstractions;
using RequirementsAbstractions;
using WPFCanvas = System.Windows.Controls.Canvas;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Threading;
using Application;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Path = System.IO.Path;

namespace Application
{
    /// <summary>
    /// This version of GALADE is standalone, i.e. it is a single executable.
    /// </summary>
    public class Application
    {
        // Public fields and properties

        // Private fields
        private MainWindow _mainWindow = null;
        private Dictionary<string, string> _startUpSingletonSettings = new Dictionary<string, string>()
        {
            {"DefaultFilePath", "" },
            {"LatestDiagramFilePath", "" },
            {"LatestCodeFilePath", "" },
            {"ProjectFolderPath", "" },
            {"ApplicationCodeFilePath", "" }
        };

        private bool LOG_ALL_WIRING = false;

        // Methods
        private Application Initialize()
        {
            Wiring.PostWiringInitialize();
            return this;
        }

        [STAThread]
        public static void Main(string[] args)
        {
            InitTest();

            Logging.Log(args.ToString());

            Application app = new Application();
            var mainWindow = app.Initialize()._mainWindow;
            mainWindow.CreateUI();
            var windowApp = mainWindow.CreateApp();
            mainWindow.Run(windowApp);
        }

        public static void InitTest()
        {
            var dict = new Dictionary<string, string>()
            {
                {"\"key1\"", "val1"},
                {"\"Key2\"", "val2"},
                {"\"key3\"", "val3"},

            };

            Utilities.EditKeys(dict, s => s.Trim('\"'), key => key.StartsWith("\"k"));

        }

        /// <summary>
        /// Initialises a JObject property and returns whether the property was missing.
        /// </summary>
        /// <param name="obj"></param>
        /// <param name="propertyName"></param>
        /// <param name="initialValue"></param>
        /// <returns></returns>
        private bool InitialiseMissingJObjectProperty(JObject obj, string propertyName, JToken initialValue)
        {
            if (!obj.ContainsKey(propertyName))
            {
                obj[propertyName] = initialValue;
                return true;
            }

            return false;
        }

        private void CreateWiring()
        {
            var fullVersion = Assembly.GetExecutingAssembly().GetName().Version;
            var VERSION_NUMBER = $"{fullVersion.Major}.{fullVersion.Minor}.{fullVersion.Build}";

            #region Set up directory and file paths
            string APP_DIRECTORY = Utilities.GetApplicationDirectory();

            var userGuidePaths = new Dictionary<string, string>()
            {
                { "Functionality", System.IO.Path.Combine(APP_DIRECTORY, "Documentation/newUserGuide_Functionality.txt") },
                { "Controls", System.IO.Path.Combine(APP_DIRECTORY, "Documentation/newUserGuide_Controls.txt") },
                { "Menu", System.IO.Path.Combine(APP_DIRECTORY, "Documentation/newUserGuide_Menu.txt") }
            };

            string SETTINGS_FILEPATH = System.IO.Path.Combine(APP_DIRECTORY, "settings.json");
            string WIRING_LOG_FILEPATH = System.IO.Path.Combine(APP_DIRECTORY, "wiringLog.log");
            string RUNTIME_LOG_FILEPATH = System.IO.Path.Combine(APP_DIRECTORY, "runtimeLog.log");
            string LOG_ARCHIVE_DIRECTORY = System.IO.Path.Combine(APP_DIRECTORY, "Logs");
            string BACKUPS_DIRECTORY = System.IO.Path.Combine(APP_DIRECTORY, "Backups");

            // Initialise and clear logs
            if (!System.IO.Directory.Exists(APP_DIRECTORY)) System.IO.Directory.CreateDirectory(APP_DIRECTORY);
            if (!System.IO.Directory.Exists(LOG_ARCHIVE_DIRECTORY)) System.IO.Directory.CreateDirectory(LOG_ARCHIVE_DIRECTORY);
            if (!System.IO.Directory.Exists(BACKUPS_DIRECTORY)) System.IO.Directory.CreateDirectory(BACKUPS_DIRECTORY);
            Logging.WriteText(path: WIRING_LOG_FILEPATH, content: "", createNewFile: true); // Create a blank log for wiring
            Logging.WriteText(path: RUNTIME_LOG_FILEPATH, content: "", createNewFile: true); // Create a blank log for all exceptions and general runtime output

            JObject settingsObj = new JObject();
            if (File.Exists(SETTINGS_FILEPATH))
            {
                try
                {
                    // Parse and update existing settings
                    var settings = File.ReadAllText(SETTINGS_FILEPATH);
                    settingsObj = JObject.Parse(settings);
                    
                }
                catch (Exception e)
                {
                    Logging.Log($"Error: Your settings file at {SETTINGS_FILEPATH} is formatted incorrectly. Please delete the file and re-run GALADE to recreate it.");
                }
            }

            // Initialise and overwrite the current settings file with any missing settings
            bool settingsIncomplete = false;

            settingsIncomplete |= InitialiseMissingJObjectProperty(settingsObj, "DefaultFilePath", "");
            settingsIncomplete |= InitialiseMissingJObjectProperty(settingsObj, "LatestDiagramFilePath", "");
            settingsIncomplete |= InitialiseMissingJObjectProperty(settingsObj, "LatestCodeFilePath", "");
            settingsIncomplete |= InitialiseMissingJObjectProperty(settingsObj, "ProjectFolderPath", "");
            settingsIncomplete |= InitialiseMissingJObjectProperty(settingsObj, "ApplicationCodeFilePath", "");
            settingsIncomplete |= InitialiseMissingJObjectProperty(settingsObj, "DefaultFilePath", "");
            settingsIncomplete |= InitialiseMissingJObjectProperty(settingsObj, "RecentProjectPaths", new JArray());

            if (settingsIncomplete) File.WriteAllText(SETTINGS_FILEPATH, settingsObj.ToString());
            #endregion

            #region Diagram constants and singletons

            StateTransition<Enums.DiagramMode> stateTransition = new StateTransition<Enums.DiagramMode>(Enums.DiagramMode.Idle)
            {
                InstanceName = "stateTransition",
                Matches = (flag, currentState) => (flag & currentState) != 0
            };

            #endregion

            #region Set up logging
            if (LOG_ALL_WIRING) Wiring.Output += output => Logging.Log(output, WIRING_LOG_FILEPATH); // Print all WireTos to a log file
            Logging.LogOutput += output =>
            {
                if (output is Exception)
                {
                    Logging.Log(output as Exception, RUNTIME_LOG_FILEPATH);
                }
                else if (output is string)
                {
                    Logging.Log(output as string, RUNTIME_LOG_FILEPATH);
                }
                else
                {
                    Logging.Log(output, RUNTIME_LOG_FILEPATH);
                }
            };

            AppDomain.CurrentDomain.FirstChanceException += (sender, e) => Logging.Log(e.Exception, RUNTIME_LOG_FILEPATH);

            AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
            {
                // Save a timestamped copy of the current runtime log
                Logging.Log(e.ExceptionObject as Exception ?? e.ExceptionObject, RUNTIME_LOG_FILEPATH);
                File.Copy(RUNTIME_LOG_FILEPATH, System.IO.Path.Combine(LOG_ARCHIVE_DIRECTORY, $"{Utilities.GetCurrentTime()}.log")); // Archive current log when app shuts down unexpectedly

                // Save a timestamped backup of the current diagram
                // var diagramContents = mainGraph.Serialise();
                // File.WriteAllText(System.IO.Path.Combine(BACKUPS_DIRECTORY, $"{Utilities.GetCurrentTime()}.ala"), diagramContents);
            };

            var globalMessages = new List<string>();

            Logging.MessageOutput += message =>
            {
                globalMessages.Add(message);
                Logging.Log(message);
            };

            #endregion

            Graph mainGraph = new Graph();

            WPFCanvas mainCanvas = new WPFCanvas();
            AbstractionModelManager abstractionModelManager = new AbstractionModelManager();

            List<string> availableAbstractions = new List<string>();
            var nodeSearchResults = new List<ALANode>();
            var nodeSearchTextResults = new System.Collections.ObjectModel.ObservableCollection<string>();

#if DEBUG
            var versionCheckSendInitialPulse = false;
            var showDebugMenu = true;
#else
            var versionCheckSendInitialPulse = true;
            var showDebugMenu = false;
#endif

            // var layoutDiagram = new RightTreeLayout<ALANode>() {InstanceName="layoutDiagram",GetID=n => n.Id,GetWidth=n => n.Width,GetHeight=n => n.Height,SetX=(n, x) => n.PositionX = x,SetY=(n, y) => n.PositionY = y,GetChildren=n => {    var GetParent = new Func<ALAWire, ALANode>(wire => (wire.SourcePortBox.Payload as Port).IsReversePort ? wire.Destination : wire.Source);    var GetChild = new Func<ALAWire, ALANode>(wire => (wire.SourcePortBox.Payload as Port).IsReversePort ? wire.Source : wire.Destination);    var children = mainGraph.Edges.OfType<ALAWire>().Where(wire => GetParent(wire)?.Equals(n) ?? false).Select(GetChild);        return children;},HorizontalGap=100,VerticalGap=20,InitialX=50,InitialY=50,GetRoots=() => mainGraph.Roots.OfType<ALANode>().Select(n => n.Id).ToHashSet()};

            // BEGIN AUTO-GENERATED INSTANTIATIONS FOR GALADE_Standalone
            MainWindow mainWindow = new MainWindow(title:"GALADE") {InstanceName="mainWindow"}; /* {"IsRoot":true} */
            DataFlowConnector<string> latestVersion = new DataFlowConnector<string>() {InstanceName="latestVersion"}; /* {"IsRoot":false} */
            Vertical mainWindowVertical = new Vertical() {InstanceName="mainWindowVertical",Layouts=new[]{0, 2, 0}}; /* {"IsRoot":false} */
            UIConfig UIConfig_canvasDisplayHoriz = new UIConfig() {InstanceName="UIConfig_canvasDisplayHoriz"}; /* {"IsRoot":false} */
            CanvasDisplay mainCanvasDisplay = new CanvasDisplay() {StateTransition=stateTransition,Height=720,Width=1280,Background=Brushes.White,Canvas=mainCanvas,InstanceName="mainCanvasDisplay"}; /* {"IsRoot":false} */
            DataFlowConnector<string> currentDiagramName = new DataFlowConnector<string>() {InstanceName="currentDiagramName"}; /* {"IsRoot":false} */
            DataFlowConnector<bool> searchFilterNameChecked = new DataFlowConnector<bool>() {InstanceName="searchFilterNameChecked",Data=true}; /* {"IsRoot":false} */
            DataFlowConnector<bool> searchFilterTypeChecked = new DataFlowConnector<bool>() {InstanceName="searchFilterTypeChecked",Data=true}; /* {"IsRoot":false} */
            DataFlowConnector<bool> searchFilterInstanceNameChecked = new DataFlowConnector<bool>() {InstanceName="searchFilterInstanceNameChecked",Data=true}; /* {"IsRoot":false} */
            DataFlowConnector<bool> searchFilterFieldsAndPropertiesChecked = new DataFlowConnector<bool>() {InstanceName="searchFilterFieldsAndPropertiesChecked",Data=true}; /* {"IsRoot":false} */
            KeyEvent id_855f86954b3e4776909cde23cd96d071 = new KeyEvent(eventName:"KeyUp") {InstanceName="Pressed the A key",Condition=args => mainGraph.Get("SelectedNode") != null && stateTransition.CurrentStateMatches(Enums.DiagramMode.IdleSelected),Key=Key.A}; /* {"IsRoot":false} */
            ContextMenu id_581015f073614919a33126efd44bf477 = new ContextMenu() {InstanceName="id_581015f073614919a33126efd44bf477"}; /* {"IsRoot":false} */
            MenuItem id_57e6a33441c54bc89dc30a28898cb1c0 = new MenuItem(header:"Add root") {InstanceName="id_57e6a33441c54bc89dc30a28898cb1c0"}; /* {"IsRoot":false} */
            EventConnector id_ad29db53c0d64d4b8be9e31474882158 = new EventConnector() {InstanceName="id_ad29db53c0d64d4b8be9e31474882158"}; /* {"IsRoot":false} */
            RightTreeLayout<ALANode> layoutDiagram = new RightTreeLayout<ALANode>() {InstanceName="layoutDiagram",GetID=n => n.Id,GetWidth=n => n.Width,GetHeight=n => n.Height,SetX=(n, x) => n.PositionX = x,SetY=(n, y) => n.PositionY = y,GetChildren=n => mainGraph.Edges.Where(e => e is ALAWire wire && wire.Source != null && wire.Destination != null && wire.Source == n).Select(e => ((e as ALAWire).Destination) as ALANode),HorizontalGap=100,VerticalGap=20,InitialX=50,InitialY=50}; /* {"IsRoot":false} */
            EventConnector startRightTreeLayoutProcess = new EventConnector() {InstanceName="startRightTreeLayoutProcess"}; /* {"IsRoot":false} */
            KeyEvent id_ed16dd83790542f4bce1db7c9f2b928f = new KeyEvent(eventName:"KeyDown") {InstanceName="R key pressed",Condition=args => stateTransition.CurrentStateMatches(Enums.DiagramMode.Idle | Enums.DiagramMode.IdleSelected),Key=Key.R}; /* {"IsRoot":false} */
            Apply<AbstractionModel, object> createNewALANode = new Apply<AbstractionModel, object>() {InstanceName="createNewALANode",Lambda=input =>{    var node = new ALANode();    node.Model = input;    node.Graph = mainGraph;    node.Canvas = mainCanvas;    node.StateTransition = stateTransition;    if (!availableAbstractions.Any())        availableAbstractions = abstractionModelManager.GetAbstractionTypes().OrderBy(s => s).ToList();    node.AvailableAbstractions.AddRange(availableAbstractions);    node.TypeChanged += newType =>    {        if (node.Model.Type == newType)            return;        node.LoadDefaultModel(abstractionModelManager.GetAbstractionModel(newType));        node.UpdateUI();        Dispatcher.CurrentDispatcher.Invoke(() =>        {            var edges = mainGraph.Edges;            foreach (var edge in edges)            {                (edge as ALAWire).Refresh();            }        }        , DispatcherPriority.ContextIdle);    }    ;    mainGraph.AddNode(node);    node.CreateInternals();    mainCanvas.Children.Add(node.Render);    node.FocusOnTypeDropDown();    return node;}}; /* {"IsRoot":false} */
            MenuBar id_42967d39c2334aab9c23697d04177f8a = new MenuBar() {InstanceName="id_42967d39c2334aab9c23697d04177f8a"}; /* {"IsRoot":false} */
            MenuItem id_f19494c1e76f460a9189c172ac98de60 = new MenuItem(header:"File") {InstanceName="File"}; /* {"IsRoot":false} */
            MenuItem id_d59c0c09aeaf46c186317b9aeaf95e2e = new MenuItem(header:"Open Project") {InstanceName="Open Project"}; /* {"IsRoot":false} */
            FolderBrowser id_463b31fe2ac04972b5055a3ff2f74fe3 = new FolderBrowser() {InstanceName="id_463b31fe2ac04972b5055a3ff2f74fe3",Description=""}; /* {"IsRoot":false} */
            DirectorySearch id_63088b53f85b4e6bb564712c525e063c = new DirectorySearch(directoriesToFind:new string[] { "DomainAbstractions", "ProgrammingParadigms", "RequirementsAbstractions", "Modules" }) {InstanceName="id_63088b53f85b4e6bb564712c525e063c",FilenameFilter="*.cs"}; /* {"IsRoot":false} */
            Apply<Dictionary<string, List<string>>, IEnumerable<string>> id_a98457fc05fc4e84bfb827f480db93d3 = new Apply<Dictionary<string, List<string>>, IEnumerable<string>>() {InstanceName="id_a98457fc05fc4e84bfb827f480db93d3",Lambda=input =>{    var list = new List<string>();    if (input.ContainsKey("DomainAbstractions"))    {        list = input["DomainAbstractions"];    }    return list;}}; /* {"IsRoot":false} */
            ForEach<string> id_f5d3730393ab40d78baebcb9198808da = new ForEach<string>() {InstanceName="id_f5d3730393ab40d78baebcb9198808da"}; /* {"IsRoot":false} */
            ApplyAction<string> id_6bc94d5f257847ff8a9a9c45e02333b4 = new ApplyAction<string>() {InstanceName="id_6bc94d5f257847ff8a9a9c45e02333b4",Lambda=input =>{    abstractionModelManager.CreateAbstractionModelFromPath(input);}}; /* {"IsRoot":false} */
            GetSetting getProjectFolderPath = new GetSetting(name:"ProjectFolderPath") {InstanceName="getProjectFolderPath"}; /* {"IsRoot":false} */
            KeyEvent id_bbd9df1f15ea4926b97567d08b6835dd = new KeyEvent(eventName:"KeyDown") {InstanceName="Enter key pressed",Key=Key.Enter}; /* {"IsRoot":false} */
            ApplyAction<object> id_6e249d6520104ca5a1a4d847a6c862a8 = new ApplyAction<object>() {InstanceName="Focus on backgroundCanvas",Lambda=input =>{    (input as WPFCanvas).Focus();}}; /* {"IsRoot":false} */
            MenuItem id_08d455bfa9744704b21570d06c3c5389 = new MenuItem(header:"Debug") {InstanceName="Debug"}; /* {"IsRoot":false} */
            MenuItem id_843593fbc341437bb7ade21d0c7f6729 = new MenuItem(header:"TextEditor test") {InstanceName="TextEditor test"}; /* {"IsRoot":false} */
            PopupWindow id_91726b8a13804a0994e27315b0213fe8 = new PopupWindow(title:"") {Height=720,Width=1280,Resize=SizeToContent.WidthAndHeight,InstanceName="id_91726b8a13804a0994e27315b0213fe8"}; /* {"IsRoot":false} */
            Box id_a2e6aa4f4d8e41b59616d63362768dde = new Box() {InstanceName="id_a2e6aa4f4d8e41b59616d63362768dde",Width=100,Height=100}; /* {"IsRoot":false} */
            TextEditor id_826249b1b9d245709de6f3b24503be2d = new TextEditor() {InstanceName="id_826249b1b9d245709de6f3b24503be2d",Width=1280,Height=720}; /* {"IsRoot":false} */
            DataFlowConnector<string> id_a1f87102954345b69de6841053fce813 = new DataFlowConnector<string>() {InstanceName="id_a1f87102954345b69de6841053fce813"}; /* {"IsRoot":false} */
            MouseButtonEvent id_6d1f4415e8d849e19f5d432ea96d9abb = new MouseButtonEvent(eventName:"MouseRightButtonDown") {InstanceName="Right button down on canvas",Condition=args => stateTransition.CurrentStateMatches(Enums.DiagramMode.Idle | Enums.DiagramMode.IdleSelected),ExtractSender=null}; /* {"IsRoot":false} */
            ApplyAction<object> id_e7e60dd036af4a869e10a64b2c216104 = new ApplyAction<object>() {InstanceName="Update to Idle",Lambda=input =>{    Mouse.Capture(input as WPFCanvas);    stateTransition.Update(Enums.DiagramMode.Idle);}}; /* {"IsRoot":false} */
            MouseButtonEvent id_44b41ddf67864f29ae9b59ed0bec2927 = new MouseButtonEvent(eventName:"MouseRightButtonUp") {InstanceName="Right button up on canvas",Condition=args => stateTransition.CurrentStateMatches(Enums.DiagramMode.Idle | Enums.DiagramMode.IdleSelected),ExtractSender=null}; /* {"IsRoot":false} */
            ApplyAction<object> id_da4f1dedd74549e283777b5f7259ad7f = new ApplyAction<object>() {InstanceName="Release capture and update to Idle",Lambda=input =>{    if (Mouse.Captured?.Equals(input) ?? false)        Mouse.Capture(null);    stateTransition.Update(Enums.DiagramMode.Idle);}}; /* {"IsRoot":false} */
            StateChangeListener id_368a7dc77fe24060b5d4017152492c1e = new StateChangeListener() {StateTransition=stateTransition,PreviousStateShouldMatch=Enums.DiagramMode.Any,CurrentStateShouldMatch=Enums.DiagramMode.Any,InstanceName="id_368a7dc77fe24060b5d4017152492c1e"}; /* {"IsRoot":false} */
            Apply<Tuple<Enums.DiagramMode, Enums.DiagramMode>, bool> id_2f4df1d9817246e5a9184857ec5a2bf8 = new Apply<Tuple<Enums.DiagramMode, Enums.DiagramMode>, bool>() {InstanceName="id_2f4df1d9817246e5a9184857ec5a2bf8",Lambda=input =>{    return input.Item1 == Enums.DiagramMode.AwaitingPortSelection && input.Item2 == Enums.DiagramMode.Idle;}}; /* {"IsRoot":false} */
            IfElse id_c80f46b08d894d4faa674408bf846b3f = new IfElse() {InstanceName="id_c80f46b08d894d4faa674408bf846b3f"}; /* {"IsRoot":false} */
            EventConnector id_642ae4874d1e4fd2a777715cc1996b49 = new EventConnector() {InstanceName="id_642ae4874d1e4fd2a777715cc1996b49"}; /* {"IsRoot":false} */
            Apply<object, object> createAndPaintALAWire = new Apply<object, object>() {InstanceName="createAndPaintALAWire",Lambda=input =>{    var source = mainGraph.Get("SelectedNode") as ALANode;    var destination = input as ALANode;    var sourcePort = source.GetSelectedPort(inputPort: false);    var destinationPort = destination.GetSelectedPort(inputPort: true);    var wire = new ALAWire()    {Graph = mainGraph, Canvas = mainCanvas, Source = source, Destination = destination, SourcePortBox = sourcePort, DestinationPortBox = destinationPort, StateTransition = stateTransition};    mainGraph.AddEdge(wire);    wire.Paint();    return wire;}}; /* {"IsRoot":false} */
            KeyEvent id_1de443ed1108447199237a8c0c584fcf = new KeyEvent(eventName:"KeyDown") {InstanceName="Delete pressed",Key=Key.Delete}; /* {"IsRoot":false} */
            EventLambda id_46a4d6e6cfb940278eb27561c43cbf37 = new EventLambda() {InstanceName="id_46a4d6e6cfb940278eb27561c43cbf37",Lambda=() =>{    var selectedNode = mainGraph.Get("SelectedNode") as ALANode;    if (selectedNode == null)        return;    selectedNode.Delete(deleteChildren: false);}}; /* {"IsRoot":false} */
            MenuItem id_83c3db6e4dfa46518991f706f8425177 = new MenuItem(header:"Refresh") {InstanceName="id_83c3db6e4dfa46518991f706f8425177"}; /* {"IsRoot":false} */
            Data<AbstractionModel> createDummyAbstractionModel = new Data<AbstractionModel>() {InstanceName="createDummyAbstractionModel",Lambda=() =>{    var model = new AbstractionModel()    {Type = "NewNode", Name = ""};    model.AddImplementedPort("Port", "input");    model.AddAcceptedPort("Port", "output");    return model;},storedData=default}; /* {"IsRoot":false} */
            Data<AbstractionModel> id_5297a497d2de44e5bc0ea2c431cdcee6 = new Data<AbstractionModel>() {InstanceName="id_5297a497d2de44e5bc0ea2c431cdcee6",Lambda=createDummyAbstractionModel.Lambda}; /* {"IsRoot":false} */
            Apply<AbstractionModel, object> id_9bd4555e80434a7b91b65e0b386593b0 = new Apply<AbstractionModel, object>() {InstanceName="id_9bd4555e80434a7b91b65e0b386593b0",Lambda=createNewALANode.Lambda}; /* {"IsRoot":false} */
            ApplyAction<object> id_7fabbaae488340a59d940100d38e9447 = new ApplyAction<object>() {InstanceName="id_7fabbaae488340a59d940100d38e9447",Lambda=input =>{    var alaNode = input as ALANode;    var mousePos = Mouse.GetPosition(mainCanvas);    alaNode.PositionX = mousePos.X;    alaNode.PositionY = mousePos.Y;    mainGraph.Set("LatestNode", input);    if (mainGraph.Get("SelectedNode") == null)    {        mainGraph.Set("SelectedNode", input);    }    mainGraph.Roots.Add(input);    alaNode.IsRoot = true;}}; /* {"IsRoot":false} */
            MenuItem id_bb687ee0b7dd4b86a38a3f81ddbab75f = new MenuItem(header:"Open Code File") {InstanceName="Open Code File"}; /* {"IsRoot":false} */
            FileBrowser id_14170585873a4fb6a7550bfb3ce8ecd4 = new FileBrowser() {InstanceName="id_14170585873a4fb6a7550bfb3ce8ecd4",Mode="Open"}; /* {"IsRoot":false} */
            FileReader id_2810e4e86da348b98b39c987e6ecd7b6 = new FileReader() {InstanceName="id_2810e4e86da348b98b39c987e6ecd7b6"}; /* {"IsRoot":false} */
            CreateDiagramFromCode createDiagramFromCode = new CreateDiagramFromCode() {InstanceName="createDiagramFromCode",Graph=mainGraph,Canvas=mainCanvas,ModelManager=abstractionModelManager,StateTransition=stateTransition,Update=false}; /* {"IsRoot":false} */
            EventConnector id_f9b8e7f524a14884be753d19a351a285 = new EventConnector() {InstanceName="id_f9b8e7f524a14884be753d19a351a285"}; /* {"IsRoot":false} */
            Apply<Dictionary<string, List<string>>, IEnumerable<string>> id_8fc35564768b4a64a57dc321cc1f621f = new Apply<Dictionary<string, List<string>>, IEnumerable<string>>() {InstanceName="id_8fc35564768b4a64a57dc321cc1f621f",Lambda=input =>{    var list = new List<string>();    if (input.ContainsKey("ProgrammingParadigms"))    {        list = input["ProgrammingParadigms"];    }    return list;}}; /* {"IsRoot":false} */
            Apply<Dictionary<string, List<string>>, IEnumerable<string>> id_0fd49143884d4a6e86e6ed0ea2f1b5b4 = new Apply<Dictionary<string, List<string>>, IEnumerable<string>>() {InstanceName="id_0fd49143884d4a6e86e6ed0ea2f1b5b4",Lambda=input =>{    var list = new List<string>();    if (input.ContainsKey("RequirementsAbstractions"))    {        list = input["RequirementsAbstractions"];    }    return list;}}; /* {"IsRoot":false} */
            DataFlowConnector<Dictionary<string, List<string>>> id_35fceab68423425195096666f27475e9 = new DataFlowConnector<Dictionary<string, List<string>>>() {InstanceName="id_35fceab68423425195096666f27475e9"}; /* {"IsRoot":false} */
            Data<UIElement> id_643997d9890f41d7a3fcab722aa48f89 = new Data<UIElement>() {InstanceName="id_643997d9890f41d7a3fcab722aa48f89",Lambda=() => mainCanvas}; /* {"IsRoot":false} */
            DataFlowConnector<MouseWheelEventArgs> mouseWheelArgs = new DataFlowConnector<MouseWheelEventArgs>() {InstanceName="mouseWheelArgs"}; /* {"IsRoot":false} */
            Scale id_39850a5c8e0941b3bfe846cbc45ebc90 = new Scale() {InstanceName="Zoom in by 10%",WidthMultiplier=1.1,HeightMultiplier=1.1,GetAbsoluteCentre=() => mouseWheelArgs.Data.GetPosition(mainCanvas),GetScaleSensitiveCentre=() => Mouse.GetPosition(mainCanvas)}; /* {"IsRoot":false} */
            Data<UIElement> id_261d188e3ce64cc8a06f390ba51e092f = new Data<UIElement>() {InstanceName="id_261d188e3ce64cc8a06f390ba51e092f",Lambda=() => mainCanvas}; /* {"IsRoot":false} */
            Scale id_607ebc3589a34e86a6eee0c0639f57cc = new Scale() {InstanceName="Zoom out by 10%",WidthMultiplier=0.9,HeightMultiplier=0.9,GetAbsoluteCentre=() => mouseWheelArgs.Data.GetPosition(mainCanvas),GetScaleSensitiveCentre=() => Mouse.GetPosition(mainCanvas)}; /* {"IsRoot":false} */
            DataFlowConnector<UIElement> id_843620b3a9ed45bea231b841b52e5621 = new DataFlowConnector<UIElement>() {InstanceName="id_843620b3a9ed45bea231b841b52e5621"}; /* {"IsRoot":false} */
            DataFlowConnector<UIElement> id_04c07393f532472792412d2a555510b9 = new DataFlowConnector<UIElement>() {InstanceName="id_04c07393f532472792412d2a555510b9"}; /* {"IsRoot":false} */
            ApplyAction<UIElement> id_841e8fee0e8a4f45819508b2086496cc = new ApplyAction<UIElement>() {InstanceName="id_841e8fee0e8a4f45819508b2086496cc",Lambda=input =>{    var transform = (input.RenderTransform as TransformGroup)?.Children.OfType<ScaleTransform>().FirstOrDefault();    if (transform == null)        return;    var minScale = 0.6; /*Logging.Log($"Scale: {transform.ScaleX}, {transform.ScaleX}");*/    bool nodeIsTooSmall = transform.ScaleX < minScale && transform.ScaleY < minScale;    var nodes = mainGraph.Nodes;    foreach (var node in nodes)    {        if (node is ALANode alaNode)            alaNode.ShowTypeTextMask(nodeIsTooSmall);    }}}; /* {"IsRoot":false} */
            MouseWheelEvent id_2a7c8f3b6b5e4879ad5a35ff6d8538fd = new MouseWheelEvent(eventName:"MouseWheel") {InstanceName="id_2a7c8f3b6b5e4879ad5a35ff6d8538fd"}; /* {"IsRoot":false} */
            Apply<MouseWheelEventArgs, bool> id_33990435606f4bbc9ba1786ed05672ab = new Apply<MouseWheelEventArgs, bool>() {InstanceName="Is scroll up?",Lambda=args =>{    return args.Delta > 0;}}; /* {"IsRoot":false} */
            IfElse id_6909a5f3b0e446d3bb0c1382dac1faa9 = new IfElse() {InstanceName="id_6909a5f3b0e446d3bb0c1382dac1faa9"}; /* {"IsRoot":false} */
            DataFlowConnector<string> id_cf7df48ac3304a8894a7536261a3b474 = new DataFlowConnector<string>() {InstanceName="id_cf7df48ac3304a8894a7536261a3b474"}; /* {"IsRoot":false} */
            DispatcherEvent id_4a268943755348b68ee2cb6b71f73c40 = new DispatcherEvent() {InstanceName="id_4a268943755348b68ee2cb6b71f73c40",Priority=DispatcherPriority.ApplicationIdle}; /* {"IsRoot":false} */
            MenuItem id_a34c047df9ae4235a08b037fd9e48ab8 = new MenuItem(header:"Generate Code") {InstanceName="Generate Code"}; /* {"IsRoot":false} */
            GenerateALACode id_b5364bf1c9cd46a28e62bb2eb0e11692 = new GenerateALACode() {InstanceName="id_b5364bf1c9cd46a28e62bb2eb0e11692",Graph=mainGraph}; /* {"IsRoot":false} */
            GetSetting id_a3efe072d6b44816a631d90ccef5b71e = new GetSetting(name:"ApplicationCodeFilePath") {InstanceName="id_a3efe072d6b44816a631d90ccef5b71e"}; /* {"IsRoot":false} */
            Data<string> id_fcfcb5f0ae544c968dcbc734ac1db51b = new Data<string>() {InstanceName="id_fcfcb5f0ae544c968dcbc734ac1db51b",storedData=SETTINGS_FILEPATH}; /* {"IsRoot":true} */
            EditSetting id_f928bf426b204bc89ba97219c97df162 = new EditSetting() {InstanceName="id_f928bf426b204bc89ba97219c97df162",JSONPath="$..ApplicationCodeFilePath"}; /* {"IsRoot":false} */
            Data<string> id_c01710b47a2a4deb824311c4dc46222d = new Data<string>() {InstanceName="id_c01710b47a2a4deb824311c4dc46222d",storedData=SETTINGS_FILEPATH}; /* {"IsRoot":true} */
            Cast<string, object> id_f07ddae8b4ee431d8ede6c21e1fe01c5 = new Cast<string, object>() {InstanceName="id_f07ddae8b4ee431d8ede6c21e1fe01c5"}; /* {"IsRoot":false} */
            DataFlowConnector<string> setting_currentDiagramCodeFilePath = new DataFlowConnector<string>() {InstanceName="setting_currentDiagramCodeFilePath"}; /* {"IsRoot":false} */
            Cast<string, object> id_460891130e9e499184b84a23c2e43c9f = new Cast<string, object>() {InstanceName="id_460891130e9e499184b84a23c2e43c9f"}; /* {"IsRoot":false} */
            Data<string> id_ecfbf0b7599e4340b8b2f79b7d1e29cb = new Data<string>() {InstanceName="id_ecfbf0b7599e4340b8b2f79b7d1e29cb",storedData=SETTINGS_FILEPATH}; /* {"IsRoot":true} */
            Apply<Dictionary<string, List<string>>, IEnumerable<string>> id_92effea7b90745299826cd566a0f2b88 = new Apply<Dictionary<string, List<string>>, IEnumerable<string>>() {InstanceName="id_92effea7b90745299826cd566a0f2b88",Lambda=input =>{    var list = new List<string>();    if (input.ContainsKey("Modules"))    {        list = input["Modules"];    }    return list;}}; /* {"IsRoot":false} */
            Data<string> id_c5fdc10d2ceb4577bef01977ee8e9dd1 = new Data<string>() {InstanceName="Get setting_currentDiagramCodeFilePath",Lambda=() => setting_currentDiagramCodeFilePath.Data}; /* {"IsRoot":false} */
            FileReader id_72140c92ac4f4255abe9d149068fa16f = new FileReader() {InstanceName="id_72140c92ac4f4255abe9d149068fa16f"}; /* {"IsRoot":false} */
            DataFlowConnector<string> id_1d55a1faa3dd4f78ad22ac73051f5d2d = new DataFlowConnector<string>() {InstanceName="id_1d55a1faa3dd4f78ad22ac73051f5d2d"}; /* {"IsRoot":false} */
            EventConnector generateCode = new EventConnector() {InstanceName="generateCode"}; /* {"IsRoot":false} */
            EditSetting id_60229af56d92436996d2ee8d919083a3 = new EditSetting() {InstanceName="id_60229af56d92436996d2ee8d919083a3",JSONPath="$..ProjectFolderPath"}; /* {"IsRoot":false} */
            Data<string> id_58c03e4b18bb43de8106a4423ca54318 = new Data<string>() {InstanceName="id_58c03e4b18bb43de8106a4423ca54318",storedData=SETTINGS_FILEPATH}; /* {"IsRoot":true} */
            FileWriter id_2b42bd6059334bfabc3df1d047751d7a = new FileWriter() {InstanceName="id_2b42bd6059334bfabc3df1d047751d7a"}; /* {"IsRoot":false} */
            DataFlowConnector<string> id_b9865ebcd2864642a96573ced52bbb7f = new DataFlowConnector<string>() {InstanceName="id_b9865ebcd2864642a96573ced52bbb7f"}; /* {"IsRoot":false} */
            InsertFileCodeLines insertInstantiations = new InsertFileCodeLines() {StartLandmark="// BEGIN AUTO-GENERATED INSTANTIATIONS",EndLandmark="// END AUTO-GENERATED INSTANTIATIONS",Indent="            ",InstanceName="insertInstantiations"}; /* {"IsRoot":false} */
            InsertFileCodeLines insertWireTos = new InsertFileCodeLines() {StartLandmark="// BEGIN AUTO-GENERATED WIRING",EndLandmark="// END AUTO-GENERATED WIRING",Indent="            ",InstanceName="insertWireTos"}; /* {"IsRoot":false} */
            EventConnector id_0e563f77c5754bdb8a75b7f55607e9b0 = new EventConnector() {InstanceName="id_0e563f77c5754bdb8a75b7f55607e9b0"}; /* {"IsRoot":false} */
            MenuItem id_96ab5fcf787a4e6d88af011f6e3daeae = new MenuItem(header:"Generics test") {InstanceName="Generics test"}; /* {"IsRoot":false} */
            EventLambda id_026d2d87a422495aa46c8fc4bda7cdd7 = new EventLambda() {InstanceName="id_026d2d87a422495aa46c8fc4bda7cdd7",Lambda=() =>{    var node = mainGraph.Nodes.First() as ALANode;    node.Model.UpdateGeneric(0, "testType");}}; /* {"IsRoot":false} */
            Horizontal statusBarHorizontal = new Horizontal() {Margin=new Thickness(5),InstanceName="statusBarHorizontal"}; /* {"IsRoot":false} */
            Text globalMessageTextDisplay = new Text(text:"") {Height=20,InstanceName="globalMessageTextDisplay"}; /* {"IsRoot":false} */
            EventLambda id_c4f838d19a6b4af9ac320799ebe9791f = new EventLambda() {InstanceName="id_c4f838d19a6b4af9ac320799ebe9791f",Lambda=() =>{    Logging.MessageOutput += message => (globalMessageTextDisplay as IDataFlow<string>).Data = message;}}; /* {"IsRoot":false} */
            EventLambda id_5e77c28f15294641bb881592d2cd7ac9 = new EventLambda() {InstanceName="id_5e77c28f15294641bb881592d2cd7ac9",Lambda=() =>{    Logging.Message("Beginning code generation...");}}; /* {"IsRoot":false} */
            EventLambda id_3f30a573358d4fd08c4c556281737360 = new EventLambda() {InstanceName="Print code generation success message",Lambda=() =>{    var sb = new StringBuilder();    sb.Append($"[{DateTime.Now:h:mm:ss tt}] Completed code generation successfully");    if (!string.IsNullOrEmpty(currentDiagramName.Data))        sb.Append($" for diagram {currentDiagramName.Data}");    sb.Append("!");    Logging.Message(sb.ToString());}}; /* {"IsRoot":false} */
            ExtractALACode extractALACode = new ExtractALACode() {InstanceName="extractALACode"}; /* {"IsRoot":false} */
            Data<string> id_a2d71044048840b0a69356270e6520ac = new Data<string>() {InstanceName="id_a2d71044048840b0a69356270e6520ac",Lambda=() =>{ /* Put the code inside a CreateWiring() method in a dummy class so that CreateDiagramFromCode uses it correctly. TODO: Update CreateDiagramFromCode to use landmarks by default. */    var sb = new StringBuilder();    sb.AppendLine("class DummyClass {");    sb.AppendLine("void CreateWiring() {");    sb.AppendLine(extractALACode.Instantiations);    sb.AppendLine(extractALACode.Wiring);    sb.AppendLine("}");    sb.AppendLine("}");    return sb.ToString();}}; /* {"IsRoot":false} */
            KeyEvent id_a26b08b25184469db6f0c4987d4c68dd = new KeyEvent(eventName:"KeyDown") {InstanceName="CTRL + S pressed",Key=Key.S,Modifiers=new Key[]{Key.LeftCtrl}}; /* {"IsRoot":false} */
            MenuItem id_6f93680658e04f8a9ab15337cee1eca3 = new MenuItem(header:"Pull from code") {InstanceName="Pull from code"}; /* {"IsRoot":false} */
            FileReader id_9f411cfea16b45ed9066dd8f2006e1f1 = new FileReader() {InstanceName="id_9f411cfea16b45ed9066dd8f2006e1f1"}; /* {"IsRoot":false} */
            EventConnector id_db598ad59e5542a0adc5df67ced27f73 = new EventConnector() {InstanceName="id_db598ad59e5542a0adc5df67ced27f73"}; /* {"IsRoot":false} */
            DataFlowConnector<string> id_9b866e4112fd4347a2a3e81441401dea = new DataFlowConnector<string>() {InstanceName="id_9b866e4112fd4347a2a3e81441401dea"}; /* {"IsRoot":false} */
            GetSetting id_5ddd02478c734777b9e6f1079b4b3d45 = new GetSetting(name:"ApplicationCodeFilePath") {InstanceName="id_5ddd02478c734777b9e6f1079b4b3d45"}; /* {"IsRoot":false} */
            Apply<string, bool> id_d5d3af7a3c9a47bf9af3b1a1e1246267 = new Apply<string, bool>() {InstanceName="id_d5d3af7a3c9a47bf9af3b1a1e1246267",Lambda=s => !string.IsNullOrEmpty(s)}; /* {"IsRoot":false} */
            IfElse id_2ce385b32256413ab2489563287afaac = new IfElse() {InstanceName="id_2ce385b32256413ab2489563287afaac"}; /* {"IsRoot":false} */
            DataFlowConnector<string> latestCodeFilePath = new DataFlowConnector<string>() {InstanceName="latestCodeFilePath"}; /* {"IsRoot":false} */
            DispatcherEvent id_dcd4c90552dc4d3fb579833da87cd829 = new DispatcherEvent() {InstanceName="id_dcd4c90552dc4d3fb579833da87cd829",Priority=DispatcherPriority.Loaded}; /* {"IsRoot":false} */
            EventLambda id_1e62a1e411c9464c94ee234dd9dd3fdc = new EventLambda() {InstanceName="id_1e62a1e411c9464c94ee234dd9dd3fdc",Lambda=() =>{    createDiagramFromCode.Update = false;    layoutDiagram.InitialY = 50;}}; /* {"IsRoot":false} */
            MouseButtonEvent id_0b4478e56d614ca091979014db65d076 = new MouseButtonEvent(eventName:"MouseDown") {InstanceName="id_0b4478e56d614ca091979014db65d076",Condition=args => args.ChangedButton == MouseButton.Middle && args.ButtonState == MouseButtonState.Pressed}; /* {"IsRoot":false} */
            ApplyAction<object> id_d90fbf714f5f4fdc9b43cbe4d5cebf1c = new ApplyAction<object>() {InstanceName="id_d90fbf714f5f4fdc9b43cbe4d5cebf1c",Lambda=input =>{    (input as UIElement)?.Focus();    stateTransition.Update(Enums.DiagramMode.Idle);}}; /* {"IsRoot":false} */
            Horizontal mainHorizontal = new Horizontal() {Ratios=new[]{1, 3},InstanceName="mainHorizontal"}; /* {"IsRoot":false} */
            Horizontal sidePanelHoriz = new Horizontal(visible:true) {InstanceName="sidePanelHoriz"}; /* {"IsRoot":false} */
            Vertical id_987196dd20ab4721b0c193bb7a2064f4 = new Vertical() {InstanceName="id_987196dd20ab4721b0c193bb7a2064f4",Layouts=new int[]{2}}; /* {"IsRoot":false} */
            TabContainer id_7b250b222ca44ba2922547f03a4aef49 = new TabContainer() {InstanceName="id_7b250b222ca44ba2922547f03a4aef49"}; /* {"IsRoot":false} */
            Tab directoryExplorerTab = new Tab(title:"Directory Explorer") {InstanceName="directoryExplorerTab"}; /* {"IsRoot":false} */
            MenuItem id_4a42bbf671cd4dba8987bd656e5a2ced = new MenuItem(header:"View") {InstanceName="View"}; /* {"IsRoot":false} */
            Horizontal canvasDisplayHoriz = new Horizontal() {InstanceName="canvasDisplayHoriz"}; /* {"IsRoot":false} */
            DirectoryTree directoryTreeExplorer = new DirectoryTree() {InstanceName="directoryTreeExplorer",FilenameFilter="*.cs",Height=700}; /* {"IsRoot":false} */
            Vertical id_e8a68acda2aa4d54add689bd669589d3 = new Vertical() {InstanceName="id_e8a68acda2aa4d54add689bd669589d3",Layouts=new int[]{2, 0}}; /* {"IsRoot":false} */
            Horizontal projectDirectoryTreeHoriz = new Horizontal() {InstanceName="projectDirectoryTreeHoriz"}; /* {"IsRoot":false} */
            Horizontal projectDirectoryOptionsHoriz = new Horizontal() {VertAlignment=VerticalAlignment.Bottom,InstanceName="projectDirectoryOptionsHoriz"}; /* {"IsRoot":false} */
            Button id_0d4d34a2cd6749759ac0c2708ddf0cbc = new Button(title:"Open diagram from file") {InstanceName="id_0d4d34a2cd6749759ac0c2708ddf0cbc"}; /* {"IsRoot":false} */
            StateChangeListener id_08a51a5702e34a38af808db65a3a6eb3 = new StateChangeListener() {StateTransition=stateTransition,PreviousStateShouldMatch=Enums.DiagramMode.Any,CurrentStateShouldMatch=Enums.DiagramMode.Idle,InstanceName="id_08a51a5702e34a38af808db65a3a6eb3"}; /* {"IsRoot":false} */
            EventConnector id_9d14914fdf0647bb8b4b20ea799e26c8 = new EventConnector() {InstanceName="id_9d14914fdf0647bb8b4b20ea799e26c8"}; /* {"IsRoot":false} */
            EventLambda unhighlightAllWires = new EventLambda() {InstanceName="unhighlightAllWires",Lambda=() =>{    var wires = mainGraph.Edges.OfType<ALAWire>();    foreach (var wire in wires)    {        wire.Deselect();    }}}; /* {"IsRoot":false} */
            DataFlowConnector<MouseWheelEventArgs> id_6d789ff1a0bc4a2d8e88733adc266be8 = new DataFlowConnector<MouseWheelEventArgs>() {InstanceName="id_6d789ff1a0bc4a2d8e88733adc266be8"}; /* {"IsRoot":false} */
            EventConnector id_a236bd13c516401eb5a83a451a875dd0 = new EventConnector() {InstanceName="id_a236bd13c516401eb5a83a451a875dd0"}; /* {"IsRoot":false} */
            EventLambda id_6fdaaf997d974e30bbb7c106c40e997c = new EventLambda() {InstanceName="Change createDiagramFromCode.Update to true",Lambda=() => createDiagramFromCode.Update = true}; /* {"IsRoot":false} */
            DataFlowConnector<object> latestAddedNode = new DataFlowConnector<object>() {InstanceName="latestAddedNode"}; /* {"IsRoot":false} */
            MenuItem id_86a7f0259b204907a092da0503eb9873 = new MenuItem(header:"Test DirectoryTree") {InstanceName="Test DirectoryTree"}; /* {"IsRoot":false} */
            FolderBrowser id_3710469340354a1bbb4b9d3371c9c012 = new FolderBrowser() {InstanceName="Choose test folder"}; /* {"IsRoot":false} */
            DirectoryTree testDirectoryTree = new DirectoryTree() {InstanceName="testDirectoryTree"}; /* {"IsRoot":false} */
            MenuItem testSimulateKeyboard = new MenuItem(header:"Test SimulateKeyboard") {InstanceName="testSimulateKeyboard"}; /* {"IsRoot":false} */
            SimulateKeyboard id_5c31090d2c954aa7b4a10e753bdfc03a = new SimulateKeyboard() {InstanceName="Type 'HELLO'",Keys="HELLO".Select(c => c.ToString()).ToList(),Modifiers=new List<string>(){"SHIFT"}}; /* {"IsRoot":false} */
            EventConnector id_52b8f2c28c2e40cabedbd531171c779a = new EventConnector() {InstanceName="id_52b8f2c28c2e40cabedbd531171c779a"}; /* {"IsRoot":false} */
            SimulateKeyboard id_86ecd8f953324e34adc6238338f75db5 = new SimulateKeyboard() {InstanceName="Type comma and space",Keys=new List<string>(){"COMMA", "SPACE"}}; /* {"IsRoot":false} */
            SimulateKeyboard id_63e463749abe41d28d05b877479070f8 = new SimulateKeyboard() {InstanceName="Type 'WORLD'",Keys="WORLD".Select(c => c.ToString()).ToList(),Modifiers=new List<string>(){"SHIFT"}}; /* {"IsRoot":false} */
            SimulateKeyboard id_66e516b6027649e1995a531d03c0c518 = new SimulateKeyboard() {InstanceName="Type '!'",Keys=new List<string>(){"1"},Modifiers=new List<string>(){"SHIFT"}}; /* {"IsRoot":false} */
            KeyEvent id_8863f404bed34d47922654bd0190259c = new KeyEvent(eventName:"KeyDown") {InstanceName="CTRL + C pressed",Condition=args => stateTransition.CurrentStateMatches(Enums.DiagramMode.IdleSelected),Key=Key.C,Modifiers=new Key[]{Key.LeftCtrl}}; /* {"IsRoot":false} */
            Data<AbstractionModel> cloneSelectedNodeModel = new Data<AbstractionModel>() {InstanceName="cloneSelectedNodeModel",Lambda=() =>{    var selectedNode = mainGraph.Get("SelectedNode") as ALANode;    if (selectedNode == null)        return null;    var baseModel = selectedNode.Model;    var clone = new AbstractionModel(baseModel);    return clone;}}; /* {"IsRoot":false} */
            ApplyAction<AbstractionModel> id_0f802a208aad42209777c13b2e61fe56 = new ApplyAction<AbstractionModel>() {InstanceName="id_0f802a208aad42209777c13b2e61fe56",Lambda=input =>{    if (input == null)    {        Logging.Message("Nothing was copied.", timestamp: true);    }    else    {        mainGraph.Set("ClonedModel", input);        Logging.Message($"Copied {input} successfully.", timestamp: true);    }}}; /* {"IsRoot":false} */
            KeyEvent id_7363c80d952e4246aba050e007287444 = new KeyEvent(eventName:"KeyUp") {InstanceName="CTRL + V pressed",Condition=args => stateTransition.CurrentStateMatches(Enums.DiagramMode.IdleSelected),Key=Key.V,Modifiers=new Key[]{Key.LeftCtrl}}; /* {"IsRoot":false} */
            ConvertToEvent<object> id_8647cbf4ac4049a99204b0e3aa70c326 = new ConvertToEvent<object>() {InstanceName="id_8647cbf4ac4049a99204b0e3aa70c326"}; /* {"IsRoot":false} */
            EventConnector id_5a22e32e96e641d49c6fb4bdf6fcd94b = new EventConnector() {InstanceName="id_5a22e32e96e641d49c6fb4bdf6fcd94b"}; /* {"IsRoot":false} */
            EventLambda id_36c5f05380b04b378de94534411f3f88 = new EventLambda() {InstanceName="Overwrite with cloned model",Lambda=() =>{    var clonedModel = mainGraph.Get("ClonedModel") as AbstractionModel;    var latestNode = latestAddedNode.Data as ALANode;    if (latestNode == null) return;        var model = latestNode?.Model;    if (model == null)        return;        model.CloneFrom(clonedModel);    latestNode.UpdateUI();    latestNode.RefreshParameterRows(removeEmptyRows: true);    latestNode.ChangeTypeInUI(clonedModel.Type);    latestNode.FocusOnTypeDropDown();}}; /* {"IsRoot":false} */
            DispatcherEvent id_0945b34f58a146ff983962f595f57fb2 = new DispatcherEvent() {InstanceName="id_0945b34f58a146ff983962f595f57fb2"}; /* {"IsRoot":false} */
            ApplyAction<KeyEventArgs> id_4341066281bc4015a668a3bbbcb7256b = new ApplyAction<KeyEventArgs>() {InstanceName="id_4341066281bc4015a668a3bbbcb7256b",Lambda=args => args.Handled = true}; /* {"IsRoot":false} */
            DataFlowConnector<AbstractionModel> id_024b1810c2d24db3b9fac1ccce2fad9e = new DataFlowConnector<AbstractionModel>() {InstanceName="id_024b1810c2d24db3b9fac1ccce2fad9e"}; /* {"IsRoot":false} */
            MenuItem id_2c933997055b4122bdb77945f1abb560 = new MenuItem(header:"Test reset canvas on root") {InstanceName="Test reset canvas on root"}; /* {"IsRoot":false} */
            Data<ALANode> id_0eea701e0bc84c42a9f17ccc200ef2ef = new Data<ALANode>() {InstanceName="id_0eea701e0bc84c42a9f17ccc200ef2ef",Lambda=() => mainGraph?.Roots.FirstOrDefault() as ALANode}; /* {"IsRoot":false} */
            ApplyAction<ALANode> resetViewOnNode = new ApplyAction<ALANode>() {InstanceName="resetViewOnNode",Lambda=node =>{    if (node == null)        return;    var render = node.Render;    var renderPosition = new Point(WPFCanvas.GetLeft(render), WPFCanvas.GetTop(render));    var windowWidth = UIConfig_canvasDisplayHoriz.ActualWidth;    var windowHeight = UIConfig_canvasDisplayHoriz.ActualHeight;    var centre = new Point(windowWidth / 2 - 20, windowHeight / 2 - 20);    WPFCanvas.SetLeft(mainCanvas, -renderPosition.X + centre.X);    WPFCanvas.SetTop(mainCanvas, -renderPosition.Y + centre.Y);}}; /* {"IsRoot":false} */
            MenuItem id_29ed401eb9c240d98bf5c6d1f00c5c76 = new MenuItem(header:"Test reset canvas on selected node") {InstanceName="Test reset canvas on selected node"}; /* {"IsRoot":false} */
            Data<ALANode> id_fa857dd7432e406c8c6c642152b37730 = new Data<ALANode>() {InstanceName="id_fa857dd7432e406c8c6c642152b37730",Lambda=() => mainGraph.Get("SelectedNode") as ALANode}; /* {"IsRoot":false} */
            DataFlowConnector<string> id_42c7f12c13804ec7b111291739be78f5 = new DataFlowConnector<string>() {InstanceName="id_42c7f12c13804ec7b111291739be78f5"}; /* {"IsRoot":false} */
            ConvertToEvent<string> id_409be365df274cc6a7a124e8a80316a5 = new ConvertToEvent<string>() {InstanceName="id_409be365df274cc6a7a124e8a80316a5"}; /* {"IsRoot":false} */
            Data<UIElement> id_5e2f0621c62142c1b5972961c93cb725 = new Data<UIElement>() {InstanceName="id_5e2f0621c62142c1b5972961c93cb725",Lambda=() => mainCanvas}; /* {"IsRoot":false} */
            Scale resetScale = new Scale() {InstanceName="resetScale",AbsoluteScale=1,Reset=true}; /* {"IsRoot":false} */
            EventConnector id_82b26eeaba664ee7b2a2c0682e25ce08 = new EventConnector() {InstanceName="id_82b26eeaba664ee7b2a2c0682e25ce08"}; /* {"IsRoot":false} */
            DataFlowConnector<UIElement> id_57e7dd98a0874e83bbd5014f7e9c9ef5 = new DataFlowConnector<UIElement>() {InstanceName="id_57e7dd98a0874e83bbd5014f7e9c9ef5"}; /* {"IsRoot":false} */
            ApplyAction<UIElement> id_e1e6cf54f73d4f439c6f18b668a73f1a = new ApplyAction<UIElement>() {InstanceName="Reset mainCanvas position",Lambda=canvas =>{    WPFCanvas.SetLeft(canvas, 0);    WPFCanvas.SetTop(canvas, 0);}}; /* {"IsRoot":false} */
            Tab searchTab = new Tab(title:"Search") {InstanceName="searchTab"}; /* {"IsRoot":false} */
            Horizontal id_fed56a4aef6748178fa7078388643323 = new Horizontal() {InstanceName="id_fed56a4aef6748178fa7078388643323"}; /* {"IsRoot":false} */
            TextBox searchTextBox = new TextBox() {InstanceName="searchTextBox"}; /* {"IsRoot":false} */
            Button startSearchButton = new Button(title:"Search") {InstanceName="startSearchButton"}; /* {"IsRoot":false} */
            DataFlowConnector<string> id_00b0ca72bbce4ef4ba5cf395c666a26e = new DataFlowConnector<string>() {InstanceName="id_00b0ca72bbce4ef4ba5cf395c666a26e"}; /* {"IsRoot":false} */
            Data<string> id_5da1d2f5b13746f29802078592e59346 = new Data<string>() {InstanceName="id_5da1d2f5b13746f29802078592e59346"}; /* {"IsRoot":false} */
            Vertical id_cc0c82a2157f4b0291c812236a6e45ba = new Vertical() {InstanceName="id_cc0c82a2157f4b0291c812236a6e45ba"}; /* {"IsRoot":false} */
            ListDisplay id_3622556a1b37410691b51b83c004a315 = new ListDisplay() {InstanceName="id_3622556a1b37410691b51b83c004a315",ItemList=nodeSearchTextResults}; /* {"IsRoot":false} */
            Apply<int, ALANode> id_73274d9ce8d5414899772715a1d0f266 = new Apply<int, ALANode>() {InstanceName="id_73274d9ce8d5414899772715a1d0f266",Lambda=index =>{    var results = nodeSearchResults;    if (results.Count > index)    {        return results[index];    }    else    {        return null;    }}}; /* {"IsRoot":false} */
            DataFlowConnector<ALANode> id_fff8d82dbdd04da18793108f9b8dd5cf = new DataFlowConnector<ALANode>() {InstanceName="id_fff8d82dbdd04da18793108f9b8dd5cf"}; /* {"IsRoot":false} */
            ConvertToEvent<ALANode> id_75ecf8c2602c41829602707be8a8a481 = new ConvertToEvent<ALANode>() {InstanceName="id_75ecf8c2602c41829602707be8a8a481"}; /* {"IsRoot":false} */
            ApplyAction<ALANode> id_23a625377ea745ee8253482ee1f0d437 = new ApplyAction<ALANode>() {InstanceName="id_23a625377ea745ee8253482ee1f0d437",Lambda=selectedNode =>{    var nodes = mainGraph.Nodes.OfType<ALANode>();    foreach (var node in nodes)    {        node.Deselect();        node.ShowTypeTextMask(show: false);    }    selectedNode.FocusOnTypeDropDown();    selectedNode.HighlightNode();}}; /* {"IsRoot":false} */
            Apply<string, IEnumerable<ALANode>> id_5f1c0f0187eb4dc99f15254fd36fa9b6 = new Apply<string, IEnumerable<ALANode>>() {InstanceName="findNodesMatchingSearchQuery",Lambda=searchQuery =>{    nodeSearchResults.Clear();    nodeSearchTextResults.Clear();    return mainGraph.Nodes.OfType<ALANode>();}}; /* {"IsRoot":false} */
            ForEach<ALANode> id_8e347b7f5f3b4aa6b1c8f1966d0280a3 = new ForEach<ALANode>() {InstanceName="id_8e347b7f5f3b4aa6b1c8f1966d0280a3"}; /* {"IsRoot":false} */
            DataFlowConnector<ALANode> id_282744d2590b4d3e8b337d73c05e0823 = new DataFlowConnector<ALANode>() {InstanceName="id_282744d2590b4d3e8b337d73c05e0823"}; /* {"IsRoot":false} */
            DataFlowConnector<int> currentSearchResultIndex = new DataFlowConnector<int>() {InstanceName="currentSearchResultIndex"}; /* {"IsRoot":false} */
            ApplyAction<ALANode> id_2c9472651f984aa8ab763f327bcfa45e = new ApplyAction<ALANode>() {InstanceName="id_2c9472651f984aa8ab763f327bcfa45e",Lambda=node =>{    var i = currentSearchResultIndex.Data;    var total = mainGraph.Nodes.Count;    Logging.Message($"Searching node {i + 1}/{total}...");}}; /* {"IsRoot":false} */
            DataFlowConnector<string> currentSearchQuery = new DataFlowConnector<string>() {InstanceName="currentSearchQuery"}; /* {"IsRoot":false} */
            DispatcherData<ALANode> id_1c95fb3a139b4602bba7b10201112546 = new DispatcherData<ALANode>() {InstanceName="id_1c95fb3a139b4602bba7b10201112546"}; /* {"IsRoot":false} */
            DispatcherData<ALANode> id_01bdd051f2034331bd9f121029b0e2e8 = new DispatcherData<ALANode>() {InstanceName="id_01bdd051f2034331bd9f121029b0e2e8"}; /* {"IsRoot":false} */
            ApplyAction<ALANode> id_67bc4eb50bb04d9694a1a0d5ce65c9d9 = new ApplyAction<ALANode>() {InstanceName="id_67bc4eb50bb04d9694a1a0d5ce65c9d9",Lambda=node =>{    var query = currentSearchQuery.Data;    var caseSensitive = false;    var searchName = searchFilterNameChecked.Data;    var searchType = searchFilterTypeChecked.Data;    var searchInstanceName = searchFilterInstanceNameChecked.Data;    var searchProperties = searchFilterFieldsAndPropertiesChecked.Data;    if (node.IsMatch(query, caseSensitive, searchName, searchType, searchInstanceName, searchProperties))    {        nodeSearchResults.Add(node);        nodeSearchTextResults.Add($"{node.Model.FullType} {node.Model.Name}");    }    var currentIndex = currentSearchResultIndex.Data;    var total = mainGraph.Nodes.Count;    if (currentIndex == (total - 1))        Logging.Message($"Found {nodeSearchResults.Count} search results for \"{query}\"");}}; /* {"IsRoot":false} */
            MenuItem id_f526f560b3504a0b8115879e5d5354ff = new MenuItem(header:"Test ContextMenu") {InstanceName="Test ContextMenu"}; /* {"IsRoot":false} */
            ContextMenu id_dea56e5fd7174cd7983e8f2c837a941b = new ContextMenu() {InstanceName="id_dea56e5fd7174cd7983e8f2c837a941b"}; /* {"IsRoot":false} */
            UIConfig directoryExplorerConfig = new UIConfig() {InstanceName="directoryExplorerConfig"}; /* {"IsRoot":false} */
            DataFlowConnector<string> currentSelectedDirectoryTreeFilePath = new DataFlowConnector<string>() {InstanceName="currentSelectedDirectoryTreeFilePath"}; /* {"IsRoot":false} */
            MenuItem id_8b908f2be6094d5b8cd3dce5c5fc2b8b = new MenuItem(header:"Open code file") {InstanceName="Open file through directory tree"}; /* {"IsRoot":false} */
            Data<string> id_692716a735e44e948a8d14cd550c1276 = new Data<string>() {InstanceName="id_692716a735e44e948a8d14cd550c1276"}; /* {"IsRoot":false} */
            KeyEvent id_f77e477a71954e20a587ec6fb4d006ce = new KeyEvent(eventName:"KeyDown") {InstanceName="CTRL + F pressed",Key=Key.F,Modifiers=new Key[]{Key.LeftCtrl}}; /* {"IsRoot":false} */
            EventConnector id_87a897a783884990bf10e4d7a9e276b9 = new EventConnector() {InstanceName="id_87a897a783884990bf10e4d7a9e276b9"}; /* {"IsRoot":false} */
            DispatcherEvent id_9e6a74b0dbea488cba6027ee5187ad0f = new DispatcherEvent() {InstanceName="id_9e6a74b0dbea488cba6027ee5187ad0f",Priority=DispatcherPriority.Loaded}; /* {"IsRoot":false} */
            DispatcherEvent id_b55e77a5d78243bf9612ecb7cb20c2c7 = new DispatcherEvent() {InstanceName="id_b55e77a5d78243bf9612ecb7cb20c2c7",Priority=DispatcherPriority.Loaded}; /* {"IsRoot":false} */
            DispatcherEvent id_45593aeb91a145aa9d84d8b77a8d4d8e = new DispatcherEvent() {InstanceName="id_45593aeb91a145aa9d84d8b77a8d4d8e",Priority=DispatcherPriority.Loaded}; /* {"IsRoot":false} */
            UIConfig UIConfig_searchTab = new UIConfig() {InstanceName="UIConfig_searchTab"}; /* {"IsRoot":false} */
            UIConfig UIConfig_searchTextBox = new UIConfig() {InstanceName="UIConfig_searchTextBox"}; /* {"IsRoot":false} */
            EventLambda id_a690d6dd37ba4c98b5506777df6dc9db = new EventLambda() {InstanceName="id_a690d6dd37ba4c98b5506777df6dc9db",Lambda=() =>{    UIConfig_searchTab.Focus();}}; /* {"IsRoot":false} */
            EventLambda id_63db7722e48a4c5aabd905f75b0519b2 = new EventLambda() {InstanceName="id_63db7722e48a4c5aabd905f75b0519b2",Lambda=() =>{    UIConfig_searchTextBox.Focus();}}; /* {"IsRoot":false} */
            EventConnector id_006b07cc90c64e398b945bb43fdd4de9 = new EventConnector() {InstanceName="id_006b07cc90c64e398b945bb43fdd4de9"}; /* {"IsRoot":false} */
            Data<string> id_e7da19475fcc44bdaf4a64d05f92b771 = new Data<string>() {InstanceName="id_e7da19475fcc44bdaf4a64d05f92b771"}; /* {"IsRoot":false} */
            PopupWindow id_68cfe1cc12f948cab25289d853300813 = new PopupWindow(title:"Open diagram?") {Height=100,Resize=SizeToContent.WidthAndHeight,InstanceName="id_68cfe1cc12f948cab25289d853300813"}; /* {"IsRoot":false} */
            Vertical id_95ddd89b36d54db298eaa05165284569 = new Vertical() {InstanceName="id_95ddd89b36d54db298eaa05165284569"}; /* {"IsRoot":false} */
            Text id_939726bef757459b914412aead1bb5f9 = new Text(text:"") {InstanceName="id_939726bef757459b914412aead1bb5f9"}; /* {"IsRoot":false} */
            Horizontal id_c7dc32a5f12b41ad94a910a74de38827 = new Horizontal() {InstanceName="id_c7dc32a5f12b41ad94a910a74de38827"}; /* {"IsRoot":false} */
            UIConfig id_89ab09564cea4a8b93d8925e8234e44c = new UIConfig() {InstanceName="id_89ab09564cea4a8b93d8925e8234e44c",Width=50,HorizAlignment="right",RightMargin=5,BottomMargin=5}; /* {"IsRoot":false} */
            UIConfig id_c180a82fd3a6495a885e9dde61aaaef3 = new UIConfig() {InstanceName="id_c180a82fd3a6495a885e9dde61aaaef3",Width=50,HorizAlignment="left",RightMargin=5,BottomMargin=5}; /* {"IsRoot":false} */
            Button id_add742a4683f4dd0b34d8d0eebbe3f07 = new Button(title:"Yes") {InstanceName="id_add742a4683f4dd0b34d8d0eebbe3f07"}; /* {"IsRoot":false} */
            Button id_e82c1f80e1884a57b79c681462efd65d = new Button(title:"No") {InstanceName="id_e82c1f80e1884a57b79c681462efd65d"}; /* {"IsRoot":false} */
            EventConnector id_5fbec6b061cc428a8c00e5c2a652b89e = new EventConnector() {InstanceName="id_5fbec6b061cc428a8c00e5c2a652b89e"}; /* {"IsRoot":false} */
            EventConnector id_b0d86bb898944ded83ec7f58b9f4a1b8 = new EventConnector() {InstanceName="id_b0d86bb898944ded83ec7f58b9f4a1b8"}; /* {"IsRoot":false} */
            Data<string> id_721b5692fa5a4ba39f509fd7e4a6291b = new Data<string>() {InstanceName="id_721b5692fa5a4ba39f509fd7e4a6291b"}; /* {"IsRoot":false} */
            EditSetting id_1928c515b2414f6690c6924a76461081 = new EditSetting() {InstanceName="id_1928c515b2414f6690c6924a76461081",JSONPath="$..ApplicationCodeFilePath"}; /* {"IsRoot":false} */
            Data<object> id_1a403a85264c4074bc7ce5a71262c6c0 = new Data<object>() {InstanceName="id_1a403a85264c4074bc7ce5a71262c6c0",storedData=""}; /* {"IsRoot":false} */
            Horizontal id_de49d2fafc2140e996eb38fbf1e62103 = new Horizontal() {InstanceName="id_de49d2fafc2140e996eb38fbf1e62103"}; /* {"IsRoot":false} */
            Horizontal id_d890df432c1f4e60a62b8913a5069b34 = new Horizontal() {InstanceName="id_d890df432c1f4e60a62b8913a5069b34"}; /* {"IsRoot":false} */
            Apply<string, string> id_e4c9f92bbd6643a286683c9ff5f9fb3a = new Apply<string, string>() {InstanceName="id_e4c9f92bbd6643a286683c9ff5f9fb3a",Lambda=path => $"Default code file path is set to \"{path}\".\nOpen a diagram from this path?"}; /* {"IsRoot":false} */
            UIConfig id_5b134e68e31b40f4b3e95eb007a020dc = new UIConfig() {InstanceName="id_5b134e68e31b40f4b3e95eb007a020dc",HorizAlignment="middle",UniformMargin=5}; /* {"IsRoot":false} */
            UIConfig id_0fafdba1ad834904ac7330f95dffd966 = new UIConfig() {InstanceName="id_c180a82fd3a6495a885e9dde61aaaef3",HorizAlignment="left",BottomMargin=5}; /* {"IsRoot":false} */
            Button id_2bfcbb47c2c745578829e1b0f8287f42 = new Button(title:" No, and clear the setting ") {InstanceName="id_2bfcbb47c2c745578829e1b0f8287f42"}; /* {"IsRoot":false} */
            EventConnector id_1139c3821d834efc947d5c4e949cd1ba = new EventConnector() {InstanceName="id_1139c3821d834efc947d5c4e949cd1ba"}; /* {"IsRoot":false} */
            Horizontal id_4686253b1d7d4cd9a4d5bf03d6b7e380 = new Horizontal() {InstanceName="id_4686253b1d7d4cd9a4d5bf03d6b7e380"}; /* {"IsRoot":false} */
            Data<string> id_f140e9e4ef3f4c07898073fde207da99 = new Data<string>() {InstanceName="id_c01710b47a2a4deb824311c4dc46222d",storedData=SETTINGS_FILEPATH}; /* {"IsRoot":true} */
            UIConfig id_25a53022f6ab4e9284fd321e9535801b = new UIConfig() {InstanceName="id_25a53022f6ab4e9284fd321e9535801b",MaxHeight=700}; /* {"IsRoot":false} */
            UIConfig id_de10db4d6b8a426ba76b02959a58cb88 = new UIConfig() {InstanceName="id_de10db4d6b8a426ba76b02959a58cb88",HorizAlignment="middle",UniformMargin=5}; /* {"IsRoot":false} */
            MenuItem id_a9db513fb0e749bda7f42b03964e5dce = new MenuItem(header:"Code to Diagram") {InstanceName="Code to Diagram"}; /* {"IsRoot":false} */
            MenuItem id_efeb87ef1b3c4f9e8ed2f8193e6b78b1 = new MenuItem(header:"Diagram to Code") {InstanceName="Diagram to Code"}; /* {"IsRoot":false} */
            EventConnector startDiagramCreationProcess = new EventConnector() {InstanceName="startDiagramCreationProcess"}; /* {"IsRoot":false} */
            EventLambda id_db77c286e64241c48de4fad0dde80024 = new EventLambda() {InstanceName="id_f3bf83d06926453bb054330f899b605b",Lambda=() =>{    mainGraph.Clear();    mainCanvas.Children.Clear();    insertInstantiations.StartLandmark = extractALACode.Landmarks[0];    insertInstantiations.EndLandmark = extractALACode.Landmarks[1];    insertWireTos.StartLandmark = extractALACode.Landmarks[2];    insertWireTos.EndLandmark = extractALACode.Landmarks[3];}}; /* {"IsRoot":false} */
            Data<string> id_c9dbe185989e48c0869f984dd8e979f2 = new Data<string>() {InstanceName="id_c9dbe185989e48c0869f984dd8e979f2",Lambda=() =>{    if (!string.IsNullOrEmpty(setting_currentDiagramCodeFilePath.Data))    {        return setting_currentDiagramCodeFilePath.Data;    }    else    {        return latestCodeFilePath.Data;    }}}; /* {"IsRoot":false} */
            DataFlowConnector<string> id_17609c775b9c4dfcb1f01d427d2911ae = new DataFlowConnector<string>() {InstanceName="id_17609c775b9c4dfcb1f01d427d2911ae"}; /* {"IsRoot":false} */
            Apply<string, string> id_e778c13b2c894113a7aff7ecfffe48f7 = new Apply<string, string>() {InstanceName="id_e778c13b2c894113a7aff7ecfffe48f7",Lambda=path =>{    var sb = new StringBuilder();    if (!string.IsNullOrEmpty(currentDiagramName.Data))    {        sb.Append($"{currentDiagramName.Data} | ");    }    var fullPath = Path.GetFullPath(path);    if (!string.IsNullOrEmpty(fullPath))    {        sb.Append($"{fullPath}");    }    return sb.ToString();}}; /* {"IsRoot":false} */
            UIConfig id_e3837af93b584ca9874336851ff0cd31 = new UIConfig() {InstanceName="id_e3837af93b584ca9874336851ff0cd31",HorizAlignment="left"}; /* {"IsRoot":false} */
            UIConfig id_5c857c3a1a474ec19c0c3b054627c0a9 = new UIConfig() {InstanceName="id_5c857c3a1a474ec19c0c3b054627c0a9",HorizAlignment="right"}; /* {"IsRoot":false} */
            Text globalVersionNumberDisplay = new Text(text:$"v{VERSION_NUMBER}") {InstanceName="globalVersionNumberDisplay"}; /* {"IsRoot":false} */
            MenuItem id_053e6b41724c4dcaad0b79b8924d647d = new MenuItem(header:"Check for Updates") {InstanceName="Check for Updates"}; /* {"IsRoot":false} */
            ForEach<string> id_20566090f5054429aebed4d371c2a613 = new ForEach<string>() {InstanceName="id_20566090f5054429aebed4d371c2a613"}; /* {"IsRoot":false} */
            DataFlowConnector<string> id_97b81fc9cc04423192a12822a5a5a32e = new DataFlowConnector<string>() {InstanceName="id_97b81fc9cc04423192a12822a5a5a32e"}; /* {"IsRoot":false} */
            CodeParser id_cad49d55268145ab87788c650c6c5473 = new CodeParser() {InstanceName="id_cad49d55268145ab87788c650c6c5473"}; /* {"IsRoot":false} */
            ForEach<string> id_84cf83e5511c4bcb8f83ad289d20b08d = new ForEach<string>() {InstanceName="id_84cf83e5511c4bcb8f83ad289d20b08d"}; /* {"IsRoot":false} */
            Collection<string> availableProgrammingParadigms = new Collection<string>() {OutputLength=-2,OutputOnEvent=true,InstanceName="availableProgrammingParadigms"}; /* {"IsRoot":false} */
            ApplyAction<List<string>> id_16d8fb2a48ea4eef8839fc7aba053476 = new ApplyAction<List<string>>() {InstanceName="id_16d8fb2a48ea4eef8839fc7aba053476",Lambda=input => abstractionModelManager.ProgrammingParadigms = input}; /* {"IsRoot":false} */
            Cast<List<string>, IEnumerable<string>> id_6625f976171c480ebd8b750aeaf4fab1 = new Cast<List<string>, IEnumerable<string>>() {InstanceName="id_6625f976171c480ebd8b750aeaf4fab1"}; /* {"IsRoot":false} */
            FileReader id_4577a8f0f63b4772bdc4eb4cb8581070 = new FileReader() {InstanceName="id_4577a8f0f63b4772bdc4eb4cb8581070"}; /* {"IsRoot":false} */
            CodeParser id_d920e0f3fa2d4872af1ec6f3c058c233 = new CodeParser() {InstanceName="id_d920e0f3fa2d4872af1ec6f3c058c233"}; /* {"IsRoot":false} */
            DataFlowConnector<IEnumerable<string>> id_670ce4df65564e07912ef2ce63c38e11 = new DataFlowConnector<IEnumerable<string>>() {InstanceName="id_670ce4df65564e07912ef2ce63c38e11"}; /* {"IsRoot":false} */
            EventLambda id_9240933e26ea4cfdb07e6e7252bf7576 = new EventLambda() {InstanceName="id_9240933e26ea4cfdb07e6e7252bf7576",Lambda=() =>{    layoutDiagram.InitialY = layoutDiagram.LatestY;}}; /* {"IsRoot":false} */
            EventLambda id_afc4400ecf8b4f3e9aa1a57c346c80b2 = new EventLambda() {InstanceName="id_afc4400ecf8b4f3e9aa1a57c346c80b2",Lambda=() =>{    var edges = mainGraph.Edges;    foreach (var edge in edges)    {        (edge as ALAWire)?.Refresh();    }}}; /* {"IsRoot":false} */
            EventConnector id_2996cb469c4442d08b7e5ca2051336b1 = new EventConnector() {InstanceName="id_2996cb469c4442d08b7e5ca2051336b1"}; /* {"IsRoot":false} */
            Data<string> id_846c10ca3cc14138bea1d681b146865a = new Data<string>() {InstanceName="id_846c10ca3cc14138bea1d681b146865a",Lambda=() => extractALACode.CurrentDiagramName}; /* {"IsRoot":false} */
            Data<string> id_b6f2ab59cd0642afaf0fc124e6f9f055 = new Data<string>() {InstanceName="id_b6f2ab59cd0642afaf0fc124e6f9f055"}; /* {"IsRoot":false} */
            MenuItem id_4aff82900db2498e8b46be4a18b9fa8e = new MenuItem(header:"Open User Guide") {InstanceName="Open User Guide"}; /* {"IsRoot":false} */
            EventLambda id_322828528d644ff883d8787c8fb63e56 = new EventLambda() {InstanceName="Open Wiki page",Lambda=() =>{    Process.Start("https://github.com/arnab-sen/GALADE/wiki");}}; /* {"IsRoot":false} */
            UIConfig UIConfig_debugMainMenuItem = new UIConfig() {InstanceName="UIConfig_debugMainMenuItem",Visible=showDebugMenu}; /* {"IsRoot":false} */
            CheckBox id_cc3adf40cb654337b01f77ade1881b44 = new CheckBox(check:true) {InstanceName="id_cc3adf40cb654337b01f77ade1881b44"}; /* {"IsRoot":false} */
            EventConnector id_a61fc923019942cea819e1b8d1b10384 = new EventConnector() {InstanceName="id_a61fc923019942cea819e1b8d1b10384"}; /* {"IsRoot":false} */
            MenuItem id_09133302b430472dbe3cf9576d72bb3a = new MenuItem(header:"Show side panel") {InstanceName="Show side panel"}; /* {"IsRoot":false} */
            Cast<object, ALANode> id_8b99ce9b4c97466983fc1b14ef889ee8 = new Cast<object, ALANode>() {InstanceName="id_8b99ce9b4c97466983fc1b14ef889ee8"}; /* {"IsRoot":false} */
            MenuItem id_024172dbe8e2496b97e191244e493973 = new MenuItem(header:"Jump to selected wire's source") {InstanceName="id_024172dbe8e2496b97e191244e493973"}; /* {"IsRoot":false} */
            Data<ALANode> id_7e64ef3262604943a2b4a086c5641d09 = new Data<ALANode>() {InstanceName="id_7e64ef3262604943a2b4a086c5641d09",Lambda=() => (mainGraph.Get("SelectedWire") as ALAWire)?.Source}; /* {"IsRoot":false} */
            ConditionalData<ALANode> id_35947f28d1454366ad8ac16e08020905 = new ConditionalData<ALANode>() {InstanceName="id_35947f28d1454366ad8ac16e08020905",Condition=input => input != null}; /* {"IsRoot":false} */
            MenuItem id_269ffcfe56874f4ba0876a93071234ae = new MenuItem(header:"Jump to selected wire's destination") {InstanceName="id_269ffcfe56874f4ba0876a93071234ae"}; /* {"IsRoot":false} */
            Data<ALANode> id_40173af405c9467bbc85c79a05b9da48 = new Data<ALANode>() {InstanceName="id_40173af405c9467bbc85c79a05b9da48",Lambda=() => (mainGraph.Get("SelectedWire") as ALAWire)?.Destination}; /* {"IsRoot":false} */
            UIConfig id_72e0f3f39c364bedb36a74a011e08747 = new UIConfig() {InstanceName="id_72e0f3f39c364bedb36a74a011e08747",HorizAlignment="left"}; /* {"IsRoot":false} */
            Horizontal id_0fd8aa1777474e3cafb81088519f3d97 = new Horizontal() {InstanceName="id_0fd8aa1777474e3cafb81088519f3d97"}; /* {"IsRoot":false} */
            CheckBox id_57dc97beb4024bf294c44fea26cc5c89 = new CheckBox(check:true) {InstanceName="id_57dc97beb4024bf294c44fea26cc5c89"}; /* {"IsRoot":false} */
            Text id_b6275330bff140168f4e68c87ed31b54 = new Text(text:"InstanceName") {InstanceName="id_b6275330bff140168f4e68c87ed31b54"}; /* {"IsRoot":false} */
            UIConfig id_ecd9f881354d40f485c3fadd9f577974 = new UIConfig() {InstanceName="id_ecd9f881354d40f485c3fadd9f577974",UniformMargin=2}; /* {"IsRoot":false} */
            Text id_889bfe8dee4d447d8ea45c19feaf5ca2 = new Text(text:"Filters:") {InstanceName="id_889bfe8dee4d447d8ea45c19feaf5ca2"}; /* {"IsRoot":false} */
            CheckBox id_abe0267c9c964e2194aa9c5bf84ac413 = new CheckBox(check:true) {InstanceName="id_abe0267c9c964e2194aa9c5bf84ac413"}; /* {"IsRoot":false} */
            Text id_edcc6a4999a24fc2ae4b190c5619351c = new Text(text:"Fields/Properties") {InstanceName="id_edcc6a4999a24fc2ae4b190c5619351c"}; /* {"IsRoot":false} */
            CheckBox id_6dd83767dc324c1bb4e34beafaac11fe = new CheckBox(check:true) {InstanceName="id_6dd83767dc324c1bb4e34beafaac11fe"}; /* {"IsRoot":false} */
            CheckBox id_7daf6ef76444402d9e9c6ed68f97a6c2 = new CheckBox(check:true) {InstanceName="id_7daf6ef76444402d9e9c6ed68f97a6c2"}; /* {"IsRoot":false} */
            Text id_0e0c54964c4641d2958e710121d0429a = new Text(text:"Type") {InstanceName="id_0e0c54964c4641d2958e710121d0429a"}; /* {"IsRoot":false} */
            Text id_39ae7418fea245fcaebd3a49b00d0683 = new Text(text:"Name") {InstanceName="id_39ae7418fea245fcaebd3a49b00d0683"}; /* {"IsRoot":false} */
            UIConfig id_cbdc03ac56ac4f179dd49e1312d7dca0 = new UIConfig() {InstanceName="id_cbdc03ac56ac4f179dd49e1312d7dca0",UniformMargin=2}; /* {"IsRoot":false} */
            UIConfig id_b868797a5ef6468abe35342f796a7376 = new UIConfig() {InstanceName="id_b868797a5ef6468abe35342f796a7376",UniformMargin=2}; /* {"IsRoot":false} */
            UIConfig id_c5fa777bee784429982813fd34ee9437 = new UIConfig() {InstanceName="id_c5fa777bee784429982813fd34ee9437",UniformMargin=2}; /* {"IsRoot":false} */
            UIConfig id_48456b7bb4cf40769ea65b77f071a7f8 = new UIConfig() {InstanceName="id_48456b7bb4cf40769ea65b77f071a7f8",UniformMargin=2}; /* {"IsRoot":false} */
            UIConfig UIConfig_mainCanvasDisplay = new UIConfig() {InstanceName="UIConfig_mainCanvasDisplay",AllowDrop=true}; /* {"IsRoot":false} */
            DragEvent id_dd7bf35a9a7c42059c340c211b761af9 = new DragEvent(eventName:"Drop") {InstanceName="id_dd7bf35a9a7c42059c340c211b761af9"}; /* {"IsRoot":false} */
            Apply<DragEventArgs, List<string>> getDroppedFilePaths = new Apply<DragEventArgs, List<string>>() {InstanceName="getDroppedFilePaths",Lambda=args =>{    var listOfFilePaths = new List<string>();    if (args.Data.GetDataPresent(DataFormats.FileDrop))    {        listOfFilePaths.AddRange((string[])args.Data.GetData(DataFormats.FileDrop));    }    return listOfFilePaths;}}; /* {"IsRoot":false} */
            Apply<List<string>, List<string>> addAbstractionsToAllNodes = new Apply<List<string>, List<string>>() {InstanceName="addAbstractionsToAllNodes",Lambda=paths =>{    var newModels = new List<AbstractionModel>();    foreach (var path in paths)    {        var model = abstractionModelManager.CreateAbstractionModelFromPath(path);        if (model != null)            newModels.Add(model);    }    var newModelTypes = newModels.Select(m => m.Type).Where(t => !availableAbstractions.Contains(t)).OrderBy(s => s).ToList();    var nodes = mainGraph.Nodes.OfType<ALANode>();    foreach (var node in nodes)    {        node.AvailableAbstractions.AddRange(newModelTypes);    }    availableAbstractions.AddRange(newModelTypes);    return newModelTypes;}}; /* {"IsRoot":false} */
            DataFlowConnector<List<string>> id_efd2a2dc177542c587c73a55def6fe3c = new DataFlowConnector<List<string>>() {InstanceName="id_efd2a2dc177542c587c73a55def6fe3c"}; /* {"IsRoot":false} */
            Apply<List<string>, string> id_3e341111f8224aa7b947f522ef1f65ab = new Apply<List<string>, string>() {InstanceName="Create status message regarding newly added abstraction models",Lambda=modelNames =>{    var sb = new StringBuilder();    sb.Append($"Successfully added {modelNames.Count} new abstraction types");    if (modelNames.Count == 0)    {        sb.Clear();        sb.Append("Error: No new abstraction types were added.");        sb.Append(" Please check if the desired types already exist by viewing any node's type dropdown.");        return sb.ToString();    }    else    {        sb.Append(": ");    }    var maxNames = 10;    sb.Append(modelNames.First());    var counter = 1;    foreach (var name in modelNames.Skip(1))    {        counter++;        if (counter > maxNames)        {            sb.Append(", ...");            return sb.ToString();        }        sb.Append($", {name}");    }    return sb.ToString();}}; /* {"IsRoot":false} */
            ApplyAction<string> updateStatusMessage = new ApplyAction<string>() {InstanceName="updateStatusMessage",Lambda=message => Logging.Message(message)}; /* {"IsRoot":false} */
            EventConnector id_0718ee88fded4b7b88258796df7db577 = new EventConnector() {InstanceName="id_0718ee88fded4b7b88258796df7db577"}; /* {"IsRoot":false} */
            HttpRequest id_c359484e1d7147a09d63c0671fa5f1dd = new HttpRequest(url:"https://api.github.com/repos/arnab-sen/GALADE/releases/latest") {InstanceName="id_c359484e1d7147a09d63c0671fa5f1dd",UserAgent=$"GALADE v{VERSION_NUMBER}",requestMethod=HttpMethod.Get}; /* {"IsRoot":false} */
            JSONParser id_db35acd5215c41849c685c49fba07a3d = new JSONParser() {InstanceName="id_db35acd5215c41849c685c49fba07a3d",JSONPath="$..tag_name"}; /* {"IsRoot":false} */
            Apply<string, bool> compareVersionNumbers = new Apply<string, bool>() {InstanceName="compareVersionNumbers",Lambda=version => version == $"v{VERSION_NUMBER}" || string.IsNullOrEmpty(version)}; /* {"IsRoot":false} */
            IfElse id_e33aaa2a4a5544a89931f05048e68406 = new IfElse() {InstanceName="id_e33aaa2a4a5544a89931f05048e68406"}; /* {"IsRoot":false} */
            Text id_b47ca3c51c95416383ba250af31ee564 = new Text(text:" | Latest version unknown - please check for updates") {InstanceName="id_b47ca3c51c95416383ba250af31ee564"}; /* {"IsRoot":false} */
            Text id_07f10e1650504d298bdceddff2402f31 = new Text(text:"") {InstanceName="id_07f10e1650504d298bdceddff2402f31"}; /* {"IsRoot":false} */
            Horizontal id_66a3103c3adc426fbc8473b66a8b0d22 = new Horizontal() {InstanceName="id_66a3103c3adc426fbc8473b66a8b0d22"}; /* {"IsRoot":false} */
            Text id_b1a5dcbe40654113b08efc4299c6fdc2 = new Text(text:"") {InstanceName="id_b1a5dcbe40654113b08efc4299c6fdc2"}; /* {"IsRoot":false} */
            Clock id_ae21c0350891480babdcd1efcb247295 = new Clock() {InstanceName="id_ae21c0350891480babdcd1efcb247295",Period=1000 * 60 * 30,SendInitialPulse=versionCheckSendInitialPulse}; /* {"IsRoot":false} */
            Data<string> id_34c59781fa2f4c5fb9102b7a65c461a0 = new Data<string>() {InstanceName="id_34c59781fa2f4c5fb9102b7a65c461a0",storedData=" | Up to date"}; /* {"IsRoot":false} */
            EventConnector id_a46f4ed8460e421b97525bd352b58d85 = new EventConnector() {InstanceName="id_a46f4ed8460e421b97525bd352b58d85"}; /* {"IsRoot":false} */
            Data<string> id_0e88688a360d451ab58c2fa25c9bf109 = new Data<string>() {InstanceName="id_0e88688a360d451ab58c2fa25c9bf109",Lambda=() => $" - Last checked at {Utilities.GetCurrentTime(includeDate: false)}"}; /* {"IsRoot":false} */
            EventConnector id_57972aa4bbc24e46b4b6171637d31440 = new EventConnector() {InstanceName="id_57972aa4bbc24e46b4b6171637d31440"}; /* {"IsRoot":false} */
            Data<string> id_76de2a3c1e5f4fbbbe8928be48e25847 = new Data<string>() {InstanceName="id_76de2a3c1e5f4fbbbe8928be48e25847",Lambda=() => $" | Update available ({latestVersion.Data})",storedData=$" | Update available ({latestVersion.Data})"}; /* {"IsRoot":false} */
            EventConnector id_cdeb94e2daee4057966eba31781ebd0d = new EventConnector() {InstanceName="id_cdeb94e2daee4057966eba31781ebd0d"}; /* {"IsRoot":false} */
            EventLambda id_45968f4d70794b7c994c8e0f6ee5093a = new EventLambda() {InstanceName="id_45968f4d70794b7c994c8e0f6ee5093a",Lambda=() =>{    abstractionModelManager.ClearAbstractions();    availableAbstractions?.Clear();}}; /* {"IsRoot":false} */
            MenuItem id_8ebb92deea4c4abf846371db834d9f87 = new MenuItem(header:"Open Releases page") {InstanceName="id_8ebb92deea4c4abf846371db834d9f87"}; /* {"IsRoot":false} */
            EventLambda id_835b587c7faf4fabbbe71010d28d9280 = new EventLambda() {InstanceName="id_835b587c7faf4fabbbe71010d28d9280",Lambda=() => Process.Start("https://github.com/arnab-sen/GALADE/releases")}; /* {"IsRoot":false} */
            MenuItem id_3a7125ae5c814928a55c2d29e7e8c132 = new MenuItem(header:"Use depth-first layout") {InstanceName="Use depth-first layout"}; /* {"IsRoot":false} */
            CheckBox id_11418b009831455983cbc07c8d116a1f = new CheckBox() {InstanceName="id_11418b009831455983cbc07c8d116a1f"}; /* {"IsRoot":false} */
            ApplyAction<bool> id_ce0bcc39dd764d1087816b79eefa76bf = new ApplyAction<bool>() {InstanceName="id_ce0bcc39dd764d1087816b79eefa76bf",Lambda=isChecked =>{}}; /* {"IsRoot":false} */
            EventConnector id_f8930a779bd44b0792fbd4a43b3874c6 = new EventConnector() {InstanceName="id_f8930a779bd44b0792fbd4a43b3874c6"}; /* {"IsRoot":false} */
            MenuItem id_943e3971561d493d97e38a8e29fb87dc = new MenuItem(header:"Use automatic layout when rewiring") {InstanceName="Use automatic layout when rewiring"}; /* {"IsRoot":false} */
            CheckBox id_954c2d01269c4632a4ddccd75cde9fde = new CheckBox(check:true) {InstanceName="id_954c2d01269c4632a4ddccd75cde9fde"}; /* {"IsRoot":false} */
            EventConnector id_cd6186e0fe844be586191519012bb72e = new EventConnector() {InstanceName="id_cd6186e0fe844be586191519012bb72e"}; /* {"IsRoot":false} */
            Data<bool> id_0f0046b6b91e447aa9bf0a223fd59038 = new Data<bool>() {InstanceName="id_0f0046b6b91e447aa9bf0a223fd59038"}; /* {"IsRoot":false} */
            IfElse id_edd3648585f44954b2df337f1b7a793b = new IfElse() {InstanceName="id_edd3648585f44954b2df337f1b7a793b"}; /* {"IsRoot":false} */
            EventConnector startGuaranteedLayoutProcess = new EventConnector() {InstanceName="startGuaranteedLayoutProcess"}; /* {"IsRoot":false} */
            EventLambda initialiseRightTreeLayout = new EventLambda() {InstanceName="initialiseRightTreeLayout",Lambda=() =>{    layoutDiagram.InitialY = 50;    layoutDiagram.Roots = mainGraph.Roots.OfType<ALANode>().ToList();    layoutDiagram.AllNodes = mainGraph.Nodes.OfType<ALANode>().ToList();}}; /* {"IsRoot":false} */
            UIConfig id_50349b82433f42ebb9d1ce591fc3bc35 = new UIConfig() {InstanceName="id_50349b82433f42ebb9d1ce591fc3bc35",ToolTipText="Uncheck to stop the diagram from rewiring whenever wires change source/destination, however automatic laying out will still occur when a new node is added.\nIf you wish to add a node without the diagram automatically laying out, use right click > Add Root to add a node at the current mouse position, with this unchecked."}; /* {"IsRoot":false} */
            Data<bool> id_27ff7a25d9034a45a229edef6610e214 = new Data<bool>() {InstanceName="id_27ff7a25d9034a45a229edef6610e214",storedData=true}; /* {"IsRoot":false} */
            DataFlowConnector<bool> id_d5c22176b9bb49dd91a1cb0a7e3f7196 = new DataFlowConnector<bool>() {InstanceName="id_d5c22176b9bb49dd91a1cb0a7e3f7196"}; /* {"IsRoot":false} */
            DataFlowConnector<bool> useAutomaticLayout = new DataFlowConnector<bool>() {InstanceName="useAutomaticLayout",Data=true}; /* {"IsRoot":false} */
            UIConfig id_87a535a0e11441af9072d6364a8aef74 = new UIConfig() {InstanceName="id_87a535a0e11441af9072d6364a8aef74"}; /* {"IsRoot":false} */
            EventConnector id_7356212bcc714c699681e8dffc853761 = new EventConnector() {InstanceName="id_7356212bcc714c699681e8dffc853761"}; /* {"IsRoot":false} */
            Data<Dictionary<string, ALANode>> getTreeParentsFromGraph = new Data<Dictionary<string, ALANode>>() {InstanceName="getTreeParentsFromGraph",Lambda=() =>{    var treeParents = new Dictionary<string, ALANode>();    foreach (var wire in mainGraph.Edges.OfType<ALAWire>())    {        var destId = wire.Destination.Id;        var sourceNode = wire.Source;        if (!treeParents.ContainsKey(destId))        {            treeParents[destId] = sourceNode;        }    }    return treeParents;}}; /* {"IsRoot":false} */
            ApplyAction<Dictionary<string, ALANode>> id_ec0f30ce468d4986abb9ad81abe73c17 = new ApplyAction<Dictionary<string, ALANode>>() {InstanceName="id_ec0f30ce468d4986abb9ad81abe73c17",Lambda=treeParents => layoutDiagram.TreeParents = treeParents}; /* {"IsRoot":false} */
            UIConfig id_ab1d0ec0d92f4befb1ff44bb72cc8e10 = new UIConfig() {InstanceName="id_ab1d0ec0d92f4befb1ff44bb72cc8e10",Visible=false}; /* {"IsRoot":true} */
            KeyEvent id_dd81a5ed9ff0413facf64a3ea65c2cf5 = new KeyEvent(eventName:"KeyDown") {InstanceName="id_dd81a5ed9ff0413facf64a3ea65c2cf5",Key=Key.Up,Modifiers=new Key[]{Key.LeftCtrl}}; /* {"IsRoot":false} */
            ApplyAction<KeyEventArgs> id_3c565e37c3c1486e91007c4d1d284367 = new ApplyAction<KeyEventArgs>() {InstanceName="id_3c565e37c3c1486e91007c4d1d284367",Lambda=args => args.Handled = true}; /* {"IsRoot":false} */
            KeyEvent id_cd5a0b075b9b47a4a371bc51c7f0aca3 = new KeyEvent(eventName:"KeyDown") {InstanceName="id_cd5a0b075b9b47a4a371bc51c7f0aca3",Key=Key.Down,Modifiers=new Key[]{Key.LeftCtrl}}; /* {"IsRoot":false} */
            ApplyAction<KeyEventArgs> id_29a954d80a1a43ca8739e70022ebf3ec = new ApplyAction<KeyEventArgs>() {InstanceName="id_29a954d80a1a43ca8739e70022ebf3ec",Lambda=args => args.Handled = true}; /* {"IsRoot":false} */
            DispatcherEvent id_2155bd03579a4918b01e6912a0f24188 = new DispatcherEvent() {InstanceName="id_2155bd03579a4918b01e6912a0f24188"}; /* {"IsRoot":false} */
            MenuItem id_68ab46b356b64dbfb61d305ea9eced6f = new MenuItem(header:"Tools") {InstanceName="Tools"}; /* {"IsRoot":false} */
            MenuItem id_7c21cf85883041b88e998ecc065cc4d4 = new MenuItem(header:"Create Instantiation Dictionary") {InstanceName="id_7c21cf85883041b88e998ecc065cc4d4"}; /* {"IsRoot":false} */
            UIConfig id_8eb5d9903d6941d285da2fc3d2ccfc3a = new UIConfig() {InstanceName="id_8eb5d9903d6941d285da2fc3d2ccfc3a",ToolTipText="Creates code that represents creating a dictionary of all non-reference instantiations,\nwhere each key is an instance name, and each value is the instance."}; /* {"IsRoot":false} */
            MenuItem id_180fa624d01c4759a83050e30426343a = new MenuItem(header:"Test TextEditor") {InstanceName="Test TextEditor"}; /* {"IsRoot":false} */
            PopupWindow id_5aec7a9782644198ab22d9ed7998ee15 = new PopupWindow(title:"Create Instantiation Dictionary") {Height=500,Width=1000,Resize=SizeToContent.WidthAndHeight,InstanceName="id_5aec7a9782644198ab22d9ed7998ee15"}; /* {"IsRoot":false} */
            TextBox id_23e510bd08224b64b10c378f0f8fcdfe = new TextBox() {InstanceName="id_23e510bd08224b64b10c378f0f8fcdfe",AcceptsReturn=true,AcceptsTab=true}; /* {"IsRoot":false} */
            EventConnector id_514f6109e8a24bc4b1ced57aaa255d90 = new EventConnector() {InstanceName="id_514f6109e8a24bc4b1ced57aaa255d90"}; /* {"IsRoot":false} */
            UIConfig id_1c8c1eff6c1042cdb09364f0d4e80cf5 = new UIConfig() {InstanceName="id_1c8c1eff6c1042cdb09364f0d4e80cf5",Width=1000,Height=500,MaxHeight=500,LeftMargin=20,RightMargin=20,BottomMargin=20}; /* {"IsRoot":false} */
            Apply<string, string> createInstanceDictionaryCode = new Apply<string, string>() {InstanceName="createInstanceDictionaryCode",Lambda=dictionaryName =>{    var instantiations = mainGraph.Nodes.OfType<ALANode>().Select(n => n.Model.Name).ToList();    var sb = new StringBuilder();    foreach (var instantiation in instantiations)    {        sb.AppendLine($"{dictionaryName}[\"{instantiation}\"] = {instantiation};");    }    return sb.ToString();}}; /* {"IsRoot":false} */
            Vertical id_a1b1ae6b9ca64970b5b8988be0b5dda7 = new Vertical() {InstanceName="id_a1b1ae6b9ca64970b5b8988be0b5dda7"}; /* {"IsRoot":false} */
            Horizontal id_65e62fc671b1436191ccdc2a2e8c8af8 = new Horizontal() {InstanceName="id_65e62fc671b1436191ccdc2a2e8c8af8"}; /* {"IsRoot":false} */
            UIConfig id_ca4344b0f1334536b8ba52fda7567809 = new UIConfig() {InstanceName="id_ca4344b0f1334536b8ba52fda7567809",UniformMargin=2}; /* {"IsRoot":false} */
            UIConfig id_e4615109bbba480cb0f7c11cc493cd84 = new UIConfig() {InstanceName="id_e4615109bbba480cb0f7c11cc493cd84",MinWidth=150,UniformMargin=2}; /* {"IsRoot":false} */
            Text id_740a947e8deb4a26868e4858d59387de = new Text(text:"Dictionary name:") {FontSize=16,InstanceName="id_740a947e8deb4a26868e4858d59387de"}; /* {"IsRoot":false} */
            TextBox id_a1163328ed694682ad454ff0f88e4dfe = new TextBox() {InstanceName="id_a1163328ed694682ad454ff0f88e4dfe"}; /* {"IsRoot":false} */
            DataFlowConnector<string> id_e7a7ac196c52416aa49fc77fe0503251 = new DataFlowConnector<string>() {InstanceName="id_e7a7ac196c52416aa49fc77fe0503251"}; /* {"IsRoot":false} */
            Data<string> id_df9b787cea7845f88e1faf65240adb4f = new Data<string>() {InstanceName="id_df9b787cea7845f88e1faf65240adb4f",storedData="_abstractionsDict"}; /* {"IsRoot":false} */
            UIConfig id_28f139af6d3941658d65e5c08a79006d = new UIConfig() {InstanceName="id_28f139af6d3941658d65e5c08a79006d",Width=100,UniformMargin=2}; /* {"IsRoot":false} */
            Button id_a96a45b9b88648ebbf6ea3d24f036269 = new Button(title:"Regenerate") {InstanceName="id_a96a45b9b88648ebbf6ea3d24f036269"}; /* {"IsRoot":false} */
            Data<string> id_b8f48b755a8545fcb626463d325ffe03 = new Data<string>() {InstanceName="id_b8f48b755a8545fcb626463d325ffe03"}; /* {"IsRoot":false} */
            UIConfig id_5b1aec35b5fd47e482a25168390fcd66 = new UIConfig() {InstanceName="id_5b1aec35b5fd47e482a25168390fcd66",HorizAlignment="middle",LeftMargin=10,TopMargin=10,RightMargin=10,BottomMargin=10}; /* {"IsRoot":false} */
            Data<ALAWire> id_61311ea1bf8d405db0411618a8e11114 = new Data<ALAWire>() {InstanceName="id_61311ea1bf8d405db0411618a8e11114",Lambda=() => mainGraph.Edges.OfType<ALAWire>().FirstOrDefault(wire => wire.Destination.Equals(mainGraph.Get("SelectedNode")))}; /* {"IsRoot":false} */
            ApplyAction<ALAWire> id_831cf2bc59df431e9171a3887608cfae = new ApplyAction<ALAWire>() {InstanceName="id_831cf2bc59df431e9171a3887608cfae",Lambda=selectedWire =>{    if (selectedWire == null)        return;    var currentIndexInSubList = -1;    var indices = new List<int>();    for (var i = 0; i < mainGraph.Edges.Count; i++)    {        var wire = mainGraph.Edges[i] as ALAWire;        if (wire == null)            continue;        if (wire.Equals(selectedWire))        {            currentIndexInSubList = indices.Count;        }        if (wire.Source.Equals(selectedWire.Source))        {            indices.Add(i);        }    }    if (currentIndexInSubList == -1 || !indices.Any() || currentIndexInSubList == 0)        return;    mainGraph.Edges.RemoveAll(o => o.Equals(selectedWire));    currentIndexInSubList--;    var newIndex = indices[currentIndexInSubList];    mainGraph.Edges.Insert(newIndex, selectedWire);    foreach (var wire in mainGraph.Edges.OfType<ALAWire>())    {        wire.Refresh();    }}}; /* {"IsRoot":false} */
            Data<ALAWire> id_b8876ba6078448999ae1746d34ce803e = new Data<ALAWire>() {InstanceName="id_b8876ba6078448999ae1746d34ce803e",Lambda=() => mainGraph.Edges.OfType<ALAWire>().FirstOrDefault(wire => wire.Destination.Equals(mainGraph.Get("SelectedNode")))}; /* {"IsRoot":false} */
            ApplyAction<ALAWire> id_cc2aa50e0aef463ca17350d36436f98d = new ApplyAction<ALAWire>() {InstanceName="id_cc2aa50e0aef463ca17350d36436f98d",Lambda=selectedWire =>{    if (selectedWire == null)        return;    var currentIndexInSubList = -1;    var indices = new List<int>();    for (var i = 0; i < mainGraph.Edges.Count; i++)    {        var wire = mainGraph.Edges[i] as ALAWire;        if (wire == null)            continue;        if (wire.Equals(selectedWire))        {            currentIndexInSubList = indices.Count;        }        if (wire.Source.Equals(selectedWire.Source))        {            indices.Add(i);        }    }    if (currentIndexInSubList == -1 || !indices.Any() || currentIndexInSubList == indices.Count - 1)        return;    mainGraph.Edges.RemoveAll(o => o.Equals(selectedWire));    currentIndexInSubList++;    var newIndex = indices[currentIndexInSubList];    mainGraph.Edges.Insert(newIndex, selectedWire);    foreach (var wire in mainGraph.Edges.OfType<ALAWire>())    {        wire.Refresh();    }}}; /* {"IsRoot":false} */
            EventConnector id_94be5f8fa9014fad81fa832cdfb41c27 = new EventConnector() {InstanceName="id_94be5f8fa9014fad81fa832cdfb41c27"}; /* {"IsRoot":false} */
            DispatcherEvent id_6377d8cb849a4a07b02d50789eab57a1 = new DispatcherEvent() {InstanceName="id_6377d8cb849a4a07b02d50789eab57a1"}; /* {"IsRoot":false} */
            EventConnector id_e3a05ca012df4e428f19f313109a576e = new EventConnector() {InstanceName="id_e3a05ca012df4e428f19f313109a576e"}; /* {"IsRoot":false} */
            DispatcherEvent id_6306c5f7aa3d41978599c00a5999b96f = new DispatcherEvent() {InstanceName="id_6306c5f7aa3d41978599c00a5999b96f"}; /* {"IsRoot":false} */
            ConvertToEvent<string> id_33d648af590b45139339fe533079ab12 = new ConvertToEvent<string>() {InstanceName="id_33d648af590b45139339fe533079ab12"}; /* {"IsRoot":false} */
            EventLambda id_3605f8d8e4624d84befb96fe76ebd3ac = new EventLambda() {InstanceName="id_c1a238e8a915400a98840a913ce99bf5",Lambda=() =>{    abstractionModelManager.ClearAbstractions();    availableAbstractions?.Clear();}}; /* {"IsRoot":false} */
            MultiMenu id_6e909cf4d2004e078eacacf80f1f2bff = new MultiMenu() {InstanceName="id_6e909cf4d2004e078eacacf80f1f2bff",ParentHeader="Open Recent Projects..."}; /* {"IsRoot":false} */
            DataFlowConnector<object> id_e2c110ecff0740989d3d30144f84a94b = new DataFlowConnector<object>() {InstanceName="id_e2c110ecff0740989d3d30144f84a94b"}; /* {"IsRoot":false} */
            ConvertToEvent<string> id_2b3a750d477d4e168aaa3ed0ae548650 = new ConvertToEvent<string>() {InstanceName="id_2b3a750d477d4e168aaa3ed0ae548650"}; /* {"IsRoot":false} */
            GetSetting id_6ecefc4cdc694ef2a46a8628cadc0e1d = new GetSetting(name:"RecentProjectPaths") {InstanceName="id_6ecefc4cdc694ef2a46a8628cadc0e1d"}; /* {"IsRoot":false} */
            Apply<string, List<string>> id_097392c5af294d32b5c928a590bad83b = new Apply<string, List<string>>() {InstanceName="id_097392c5af294d32b5c928a590bad83b",Lambda=json => JArray.Parse(json).Select(jt => jt.Value<string>()).ToList()}; /* {"IsRoot":false} */
            DataFlowConnector<List<string>> recentProjectPaths = new DataFlowConnector<List<string>>() {InstanceName="recentProjectPaths",Data=new List<string>()}; /* {"IsRoot":false} */
            EventConnector id_408df459fb4c4846920b1a1edd4ac9e6 = new EventConnector() {InstanceName="id_408df459fb4c4846920b1a1edd4ac9e6"}; /* {"IsRoot":false} */
            Data<object> id_e045b91666df454ca2f7985443af56c5 = new Data<object>() {InstanceName="id_e045b91666df454ca2f7985443af56c5"}; /* {"IsRoot":false} */
            Apply<string, object> id_ef711f01535e48e2b65274af24d732f6 = new Apply<string, object>() {InstanceName="id_ef711f01535e48e2b65274af24d732f6",Lambda=path =>{    var paths = recentProjectPaths.Data;    if (!paths.Contains(path))        paths.Add(path);    return new JArray(paths);}}; /* {"IsRoot":false} */
            EditSetting id_6c8e7b486e894c6ca6bebaf40775b8b4 = new EditSetting() {InstanceName="id_6c8e7b486e894c6ca6bebaf40775b8b4",JSONPath="$..RecentProjectPaths"}; /* {"IsRoot":false} */
            Cast<object, string> id_cb85f096416943cb9c08e4862f304568 = new Cast<object, string>() {InstanceName="id_cb85f096416943cb9c08e4862f304568"}; /* {"IsRoot":false} */
            Apply<object, List<string>> id_5d9313a0a895402cb6be531e87c9b606 = new Apply<object, List<string>>() {InstanceName="id_5d9313a0a895402cb6be531e87c9b606",Lambda=obj => (obj as JArray)?.Select(jt => jt.Value<string>()).ToList() ?? new List<string>()}; /* {"IsRoot":false} */
            DataFlowConnector<object> id_4ad460d4bd8d4a63ad7aca7ed9f1c945 = new DataFlowConnector<object>() {InstanceName="id_4ad460d4bd8d4a63ad7aca7ed9f1c945"}; /* {"IsRoot":false} */
            MenuItem id_d386225d5368436185ff7e18a6dfd91a = new MenuItem(header:"Paste") {InstanceName="id_d386225d5368436185ff7e18a6dfd91a"}; /* {"IsRoot":false} */
            TextClipboard id_355e5bd4d98745b2a42eb1266198128b = new TextClipboard() {InstanceName="id_355e5bd4d98745b2a42eb1266198128b"}; /* {"IsRoot":false} */
            Apply<string, string> id_ceae580b14444b1e82c23813f47a47cd = new Apply<string, string>() {InstanceName="id_ceae580b14444b1e82c23813f47a47cd",Lambda=json =>{    var jObj = JObject.Parse(json);    var instantiations = (jObj["Instantiations"] as JArray).Select(jt => jt.Value<string>()).ToList();    var wireTos = (jObj["WireTos"] as JArray).Select(jt => jt.Value<string>()).ToList();    var sb = new StringBuilder();    sb.AppendLine("class DummyClass {");    sb.AppendLine("void CreateWiring() {");    foreach (var inst in instantiations)    {        sb.AppendLine(inst);    }    foreach (var wireTo in wireTos)    {        sb.AppendLine(wireTo);    }    sb.AppendLine("}");    sb.AppendLine("}");    return sb.ToString();}}; /* {"IsRoot":false} */
            CreateDiagramFromCode pasteDiagramFromCode = new CreateDiagramFromCode() {InstanceName="pasteDiagramFromCode",Graph=mainGraph,Canvas=mainCanvas,ModelManager=abstractionModelManager,StateTransition=stateTransition,Update=true}; /* {"IsRoot":false} */
            DataFlowConnector<string> id_6180563898dc46da87f68e3da6bc7aa8 = new DataFlowConnector<string>() {InstanceName="id_6180563898dc46da87f68e3da6bc7aa8"}; /* {"IsRoot":false} */
            ConvertToEvent<string> id_6bc55844fa8f41db9a95118685504fd1 = new ConvertToEvent<string>() {InstanceName="id_6bc55844fa8f41db9a95118685504fd1"}; /* {"IsRoot":false} */
            UIConfig id_e372e7c636a14549bba7cb5992874716 = new UIConfig() {InstanceName="id_e372e7c636a14549bba7cb5992874716",Visible=false}; /* {"IsRoot":false} */
            // END AUTO-GENERATED INSTANTIATIONS FOR GALADE_Standalone

            // BEGIN AUTO-GENERATED WIRING FOR GALADE_Standalone
            mainWindow.WireTo(mainWindowVertical, "iuiStructure"); /* {"SourceType":"MainWindow","SourceIsReference":false,"DestinationType":"Vertical","DestinationIsReference":false} */
            mainWindow.WireTo(id_642ae4874d1e4fd2a777715cc1996b49, "appStart"); /* {"SourceType":"MainWindow","SourceIsReference":false,"DestinationType":"EventConnector","DestinationIsReference":false} */
            mainWindowVertical.WireTo(id_42967d39c2334aab9c23697d04177f8a, "children"); /* {"SourceType":"Vertical","SourceIsReference":false,"DestinationType":"MenuBar","DestinationIsReference":false} */
            mainCanvasDisplay.WireTo(id_581015f073614919a33126efd44bf477, "contextMenu"); /* {"SourceType":"CanvasDisplay","SourceIsReference":false,"DestinationType":"ContextMenu","DestinationIsReference":false} */
            mainCanvasDisplay.WireTo(id_855f86954b3e4776909cde23cd96d071, "eventHandlers"); /* {"SourceType":"CanvasDisplay","SourceIsReference":false,"DestinationType":"KeyEvent","DestinationIsReference":false} */
            mainCanvasDisplay.WireTo(id_ed16dd83790542f4bce1db7c9f2b928f, "eventHandlers"); /* {"SourceType":"CanvasDisplay","SourceIsReference":false,"DestinationType":"KeyEvent","DestinationIsReference":false} */
            mainCanvasDisplay.WireTo(id_bbd9df1f15ea4926b97567d08b6835dd, "eventHandlers"); /* {"SourceType":"CanvasDisplay","SourceIsReference":false,"DestinationType":"KeyEvent","DestinationIsReference":false} */
            mainCanvasDisplay.WireTo(id_6d1f4415e8d849e19f5d432ea96d9abb, "eventHandlers"); /* {"SourceType":"CanvasDisplay","SourceIsReference":false,"DestinationType":"MouseButtonEvent","DestinationIsReference":false} */
            mainCanvasDisplay.WireTo(id_44b41ddf67864f29ae9b59ed0bec2927, "eventHandlers"); /* {"SourceType":"CanvasDisplay","SourceIsReference":false,"DestinationType":"MouseButtonEvent","DestinationIsReference":false} */
            mainCanvasDisplay.WireTo(id_1de443ed1108447199237a8c0c584fcf, "eventHandlers"); /* {"SourceType":"CanvasDisplay","SourceIsReference":false,"DestinationType":"KeyEvent","DestinationIsReference":false} */
            mainCanvasDisplay.WireTo(id_2a7c8f3b6b5e4879ad5a35ff6d8538fd, "eventHandlers"); /* {"SourceType":"CanvasDisplay","SourceIsReference":false,"DestinationType":"MouseWheelEvent","DestinationIsReference":false} */
            mainCanvasDisplay.WireTo(id_a26b08b25184469db6f0c4987d4c68dd, "eventHandlers"); /* {"SourceType":"CanvasDisplay","SourceIsReference":false,"DestinationType":"KeyEvent","DestinationIsReference":false} */
            id_581015f073614919a33126efd44bf477.WireTo(id_57e6a33441c54bc89dc30a28898cb1c0, "children"); /* {"SourceType":"ContextMenu","SourceIsReference":false,"DestinationType":"MenuItem","DestinationIsReference":false} */
            id_581015f073614919a33126efd44bf477.WireTo(id_83c3db6e4dfa46518991f706f8425177, "children"); /* {"SourceType":"ContextMenu","SourceIsReference":false,"DestinationType":"MenuItem","DestinationIsReference":false} */
            id_57e6a33441c54bc89dc30a28898cb1c0.WireTo(id_5297a497d2de44e5bc0ea2c431cdcee6, "clickedEvent"); /* {"SourceType":"MenuItem","SourceIsReference":false,"DestinationType":"Data","DestinationIsReference":false} */
            id_8647cbf4ac4049a99204b0e3aa70c326.WireTo(startGuaranteedLayoutProcess, "eventOutput"); /* {"SourceType":"ConvertToEvent","SourceIsReference":false,"DestinationType":"EventConnector","DestinationIsReference":false} */
            id_7356212bcc714c699681e8dffc853761.WireTo(getTreeParentsFromGraph, "fanoutList"); /* {"SourceType":"EventConnector","SourceIsReference":false,"DestinationType":"Data","DestinationIsReference":false} */
            id_7356212bcc714c699681e8dffc853761.WireTo(layoutDiagram, "fanoutList"); /* {"SourceType":"EventConnector","SourceIsReference":false,"DestinationType":"RightTreeLayout","DestinationIsReference":false} */
            id_ed16dd83790542f4bce1db7c9f2b928f.WireTo(startGuaranteedLayoutProcess, "eventHappened"); /* {"SourceType":"KeyEvent","SourceIsReference":false,"DestinationType":"EventConnector","DestinationIsReference":false} */
            id_42967d39c2334aab9c23697d04177f8a.WireTo(id_f19494c1e76f460a9189c172ac98de60, "children"); /* {"SourceType":"MenuBar","SourceIsReference":false,"DestinationType":"MenuItem","DestinationIsReference":false} */
            id_f19494c1e76f460a9189c172ac98de60.WireTo(id_d59c0c09aeaf46c186317b9aeaf95e2e, "children"); /* {"SourceType":"MenuItem","SourceIsReference":false,"DestinationType":"MenuItem","DestinationIsReference":false} */
            id_f19494c1e76f460a9189c172ac98de60.WireTo(id_bb687ee0b7dd4b86a38a3f81ddbab75f, "children"); /* {"SourceType":"MenuItem","SourceIsReference":false,"DestinationType":"MenuItem","DestinationIsReference":false} */
            id_463b31fe2ac04972b5055a3ff2f74fe3.WireTo(id_a1f87102954345b69de6841053fce813, "selectedFolderPathOutput"); /* {"SourceType":"FolderBrowser","SourceIsReference":false,"DestinationType":"DataFlowConnector","DestinationIsReference":false} */
            id_63088b53f85b4e6bb564712c525e063c.WireTo(id_35fceab68423425195096666f27475e9, "foundFiles"); /* {"SourceType":"DirectorySearch","SourceIsReference":false,"DestinationType":"DataFlowConnector","DestinationIsReference":false} */
            id_a98457fc05fc4e84bfb827f480db93d3.WireTo(id_f5d3730393ab40d78baebcb9198808da, "output"); /* {"SourceType":"Apply","SourceIsReference":false,"DestinationType":"ForEach","DestinationIsReference":false} */
            id_f5d3730393ab40d78baebcb9198808da.WireTo(id_6bc94d5f257847ff8a9a9c45e02333b4, "elementOutput"); /* {"SourceType":"ForEach","SourceIsReference":false,"DestinationType":"ApplyAction","DestinationIsReference":false} */
            getProjectFolderPath.WireTo(id_ecfbf0b7599e4340b8b2f79b7d1e29cb, "filePathInput"); /* {"SourceType":"GetSetting","SourceIsReference":false,"DestinationType":"Data","DestinationIsReference":false} */
            getProjectFolderPath.WireTo(id_a1f87102954345b69de6841053fce813, "settingJsonOutput"); /* {"SourceType":"GetSetting","SourceIsReference":false,"DestinationType":"DataFlowConnector","DestinationIsReference":false} */
            id_bbd9df1f15ea4926b97567d08b6835dd.WireTo(id_6e249d6520104ca5a1a4d847a6c862a8, "senderOutput"); /* {"SourceType":"KeyEvent","SourceIsReference":false,"DestinationType":"ApplyAction","DestinationIsReference":false} */
            id_08d455bfa9744704b21570d06c3c5389.WireTo(id_843593fbc341437bb7ade21d0c7f6729, "children"); /* {"SourceType":"MenuItem","SourceIsReference":false,"DestinationType":"MenuItem","DestinationIsReference":false} */
            id_08d455bfa9744704b21570d06c3c5389.WireTo(id_a34c047df9ae4235a08b037fd9e48ab8, "children"); /* {"SourceType":"MenuItem","SourceIsReference":false,"DestinationType":"MenuItem","DestinationIsReference":false} */
            id_08d455bfa9744704b21570d06c3c5389.WireTo(id_96ab5fcf787a4e6d88af011f6e3daeae, "children"); /* {"SourceType":"MenuItem","SourceIsReference":false,"DestinationType":"MenuItem","DestinationIsReference":false} */
            id_843593fbc341437bb7ade21d0c7f6729.WireTo(id_91726b8a13804a0994e27315b0213fe8, "clickedEvent"); /* {"SourceType":"MenuItem","SourceIsReference":false,"DestinationType":"PopupWindow","DestinationIsReference":false} */
            id_91726b8a13804a0994e27315b0213fe8.WireTo(id_a2e6aa4f4d8e41b59616d63362768dde, "children"); /* {"SourceType":"PopupWindow","SourceIsReference":false,"DestinationType":"Box","DestinationIsReference":false} */
            id_a2e6aa4f4d8e41b59616d63362768dde.WireTo(id_826249b1b9d245709de6f3b24503be2d, "uiLayout"); /* {"SourceType":"Box","SourceIsReference":false,"DestinationType":"TextEditor","DestinationIsReference":false} */
            id_a1f87102954345b69de6841053fce813.WireTo(id_33d648af590b45139339fe533079ab12, "fanoutList"); /* {"SourceType":"DataFlowConnector","SourceIsReference":false,"DestinationType":"ConvertToEvent","DestinationIsReference":false} */
            id_a1f87102954345b69de6841053fce813.WireTo(id_63088b53f85b4e6bb564712c525e063c, "fanoutList"); /* {"SourceType":"DataFlowConnector","SourceIsReference":false,"DestinationType":"DirectorySearch","DestinationIsReference":false} */
            id_a1f87102954345b69de6841053fce813.WireTo(id_460891130e9e499184b84a23c2e43c9f, "fanoutList"); /* {"SourceType":"DataFlowConnector","SourceIsReference":false,"DestinationType":"Cast","DestinationIsReference":false} */
            id_6d1f4415e8d849e19f5d432ea96d9abb.WireTo(id_e7e60dd036af4a869e10a64b2c216104, "argsOutput"); /* {"SourceType":"MouseButtonEvent","SourceIsReference":false,"DestinationType":"ApplyAction","DestinationIsReference":false} */
            id_44b41ddf67864f29ae9b59ed0bec2927.WireTo(id_da4f1dedd74549e283777b5f7259ad7f, "argsOutput"); /* {"SourceType":"MouseButtonEvent","SourceIsReference":false,"DestinationType":"ApplyAction","DestinationIsReference":false} */
            id_368a7dc77fe24060b5d4017152492c1e.WireTo(id_2f4df1d9817246e5a9184857ec5a2bf8, "transitionOutput"); /* {"SourceType":"StateChangeListener","SourceIsReference":false,"DestinationType":"Apply","DestinationIsReference":false} */
            id_2f4df1d9817246e5a9184857ec5a2bf8.WireTo(id_c80f46b08d894d4faa674408bf846b3f, "output"); /* {"SourceType":"Apply","SourceIsReference":false,"DestinationType":"IfElse","DestinationIsReference":false} */
            id_c80f46b08d894d4faa674408bf846b3f.WireTo(startRightTreeLayoutProcess, "ifOutput"); /* {"SourceType":"IfElse","SourceIsReference":false,"DestinationType":"EventConnector","DestinationIsReference":false} */
            id_642ae4874d1e4fd2a777715cc1996b49.WireTo(id_cdeb94e2daee4057966eba31781ebd0d, "fanoutList"); /* {"SourceType":"EventConnector","SourceIsReference":false,"DestinationType":"EventConnector","DestinationIsReference":false} */
            id_642ae4874d1e4fd2a777715cc1996b49.WireTo(getProjectFolderPath, "fanoutList"); /* {"SourceType":"EventConnector","SourceIsReference":false,"DestinationType":"GetSetting","DestinationIsReference":false} */
            id_642ae4874d1e4fd2a777715cc1996b49.WireTo(id_368a7dc77fe24060b5d4017152492c1e, "fanoutList"); /* {"SourceType":"EventConnector","SourceIsReference":false,"DestinationType":"StateChangeListener","DestinationIsReference":false} */
            id_642ae4874d1e4fd2a777715cc1996b49.WireTo(id_f9b8e7f524a14884be753d19a351a285, "complete"); /* {"SourceType":"EventConnector","SourceIsReference":false,"DestinationType":"EventConnector","DestinationIsReference":false} */
            id_1de443ed1108447199237a8c0c584fcf.WireTo(id_46a4d6e6cfb940278eb27561c43cbf37, "eventHappened"); /* {"SourceType":"KeyEvent","SourceIsReference":false,"DestinationType":"EventLambda","DestinationIsReference":false} */
            id_83c3db6e4dfa46518991f706f8425177.WireTo(startRightTreeLayoutProcess, "clickedEvent"); /* {"SourceType":"MenuItem","SourceIsReference":false,"DestinationType":"EventConnector","DestinationIsReference":false} */
            id_5297a497d2de44e5bc0ea2c431cdcee6.WireTo(id_9bd4555e80434a7b91b65e0b386593b0, "dataOutput"); /* {"SourceType":"Data","SourceIsReference":false,"DestinationType":"Apply","DestinationIsReference":false} */
            id_9bd4555e80434a7b91b65e0b386593b0.WireTo(id_7fabbaae488340a59d940100d38e9447, "output"); /* {"SourceType":"Apply","SourceIsReference":false,"DestinationType":"ApplyAction","DestinationIsReference":false} */
            id_2810e4e86da348b98b39c987e6ecd7b6.WireTo(id_cf7df48ac3304a8894a7536261a3b474, "fileContentOutput"); /* {"SourceType":"FileReader","SourceIsReference":false,"DestinationType":"DataFlowConnector","DestinationIsReference":false} */
            id_f9b8e7f524a14884be753d19a351a285.WireTo(id_c4f838d19a6b4af9ac320799ebe9791f, "fanoutList"); /* {"SourceType":"EventConnector","SourceIsReference":false,"DestinationType":"EventLambda","DestinationIsReference":false} */
            id_0fd49143884d4a6e86e6ed0ea2f1b5b4.WireTo(id_f5d3730393ab40d78baebcb9198808da, "output"); /* {"SourceType":"Apply","SourceIsReference":false,"DestinationType":"ForEach","DestinationIsReference":false} */
            id_35fceab68423425195096666f27475e9.WireTo(id_8fc35564768b4a64a57dc321cc1f621f, "fanoutList"); /* {"SourceType":"DataFlowConnector","SourceIsReference":false,"DestinationType":"Apply","DestinationIsReference":false} */
            id_35fceab68423425195096666f27475e9.WireTo(id_0fd49143884d4a6e86e6ed0ea2f1b5b4, "fanoutList"); /* {"SourceType":"DataFlowConnector","SourceIsReference":false,"DestinationType":"Apply","DestinationIsReference":false} */
            id_35fceab68423425195096666f27475e9.WireTo(id_92effea7b90745299826cd566a0f2b88, "fanoutList"); /* {"SourceType":"DataFlowConnector","SourceIsReference":false,"DestinationType":"Apply","DestinationIsReference":false} */
            id_643997d9890f41d7a3fcab722aa48f89.WireTo(id_843620b3a9ed45bea231b841b52e5621, "dataOutput"); /* {"SourceType":"Data","SourceIsReference":false,"DestinationType":"DataFlowConnector","DestinationIsReference":false} */
            id_261d188e3ce64cc8a06f390ba51e092f.WireTo(id_04c07393f532472792412d2a555510b9, "dataOutput"); /* {"SourceType":"Data","SourceIsReference":false,"DestinationType":"DataFlowConnector","DestinationIsReference":false} */
            id_843620b3a9ed45bea231b841b52e5621.WireTo(id_39850a5c8e0941b3bfe846cbc45ebc90, "fanoutList"); /* {"SourceType":"DataFlowConnector","SourceIsReference":false,"DestinationType":"Scale","DestinationIsReference":false} */
            id_843620b3a9ed45bea231b841b52e5621.WireTo(id_841e8fee0e8a4f45819508b2086496cc, "fanoutList"); /* {"SourceType":"DataFlowConnector","SourceIsReference":false,"DestinationType":"ApplyAction","DestinationIsReference":false} */
            id_04c07393f532472792412d2a555510b9.WireTo(id_607ebc3589a34e86a6eee0c0639f57cc, "fanoutList"); /* {"SourceType":"DataFlowConnector","SourceIsReference":false,"DestinationType":"Scale","DestinationIsReference":false} */
            id_04c07393f532472792412d2a555510b9.WireTo(id_841e8fee0e8a4f45819508b2086496cc, "fanoutList"); /* {"SourceType":"DataFlowConnector","SourceIsReference":false,"DestinationType":"ApplyAction","DestinationIsReference":false} */
            id_33990435606f4bbc9ba1786ed05672ab.WireTo(id_6909a5f3b0e446d3bb0c1382dac1faa9, "output"); /* {"SourceType":"Apply","SourceIsReference":false,"DestinationType":"IfElse","DestinationIsReference":false} */
            id_6909a5f3b0e446d3bb0c1382dac1faa9.WireTo(id_643997d9890f41d7a3fcab722aa48f89, "ifOutput"); /* {"SourceType":"IfElse","SourceIsReference":false,"DestinationType":"Data","DestinationIsReference":false} */
            id_6909a5f3b0e446d3bb0c1382dac1faa9.WireTo(id_261d188e3ce64cc8a06f390ba51e092f, "elseOutput"); /* {"SourceType":"IfElse","SourceIsReference":false,"DestinationType":"Data","DestinationIsReference":false} */
            id_cf7df48ac3304a8894a7536261a3b474.WireTo(extractALACode, "fanoutList"); /* {"SourceType":"DataFlowConnector","SourceIsReference":false,"DestinationType":"ExtractALACode","DestinationIsReference":false} */
            id_a34c047df9ae4235a08b037fd9e48ab8.WireTo(generateCode, "clickedEvent"); /* {"SourceType":"MenuItem","SourceIsReference":false,"DestinationType":"EventConnector","DestinationIsReference":false} */
            id_b5364bf1c9cd46a28e62bb2eb0e11692.WireTo(insertInstantiations, "instantiations"); /* {"SourceType":"GenerateALACode","SourceIsReference":false,"DestinationType":"InsertFileCodeLines","DestinationIsReference":false} */
            id_b5364bf1c9cd46a28e62bb2eb0e11692.WireTo(insertWireTos, "wireTos"); /* {"SourceType":"GenerateALACode","SourceIsReference":false,"DestinationType":"InsertFileCodeLines","DestinationIsReference":false} */
            id_a3efe072d6b44816a631d90ccef5b71e.WireTo(id_fcfcb5f0ae544c968dcbc734ac1db51b, "filePathInput"); /* {"SourceType":"GetSetting","SourceIsReference":false,"DestinationType":"Data","DestinationIsReference":false} */
            id_f928bf426b204bc89ba97219c97df162.WireTo(id_c01710b47a2a4deb824311c4dc46222d, "filePathInput"); /* {"SourceType":"EditSetting","SourceIsReference":false,"DestinationType":"Data","DestinationIsReference":false} */
            id_f07ddae8b4ee431d8ede6c21e1fe01c5.WireTo(id_f928bf426b204bc89ba97219c97df162, "output"); /* {"SourceType":"Cast","SourceIsReference":false,"DestinationType":"EditSetting","DestinationIsReference":false} */
            id_17609c775b9c4dfcb1f01d427d2911ae.WireTo(id_f07ddae8b4ee431d8ede6c21e1fe01c5, "fanoutList"); /* {"SourceType":"DataFlowConnector","SourceIsReference":false,"DestinationType":"Cast","DestinationIsReference":false} */
            id_e2c110ecff0740989d3d30144f84a94b.WireTo(id_60229af56d92436996d2ee8d919083a3, "fanoutList"); /* {"SourceType":"DataFlowConnector","SourceIsReference":false,"DestinationType":"EditSetting","DestinationIsReference":false} */
            id_92effea7b90745299826cd566a0f2b88.WireTo(id_f5d3730393ab40d78baebcb9198808da, "output"); /* {"SourceType":"Apply","SourceIsReference":false,"DestinationType":"ForEach","DestinationIsReference":false} */
            id_c5fdc10d2ceb4577bef01977ee8e9dd1.WireTo(id_b9865ebcd2864642a96573ced52bbb7f, "dataOutput"); /* {"SourceType":"Data","SourceIsReference":false,"DestinationType":"DataFlowConnector","DestinationIsReference":false} */
            id_72140c92ac4f4255abe9d149068fa16f.WireTo(id_1d55a1faa3dd4f78ad22ac73051f5d2d, "fileContentOutput"); /* {"SourceType":"FileReader","SourceIsReference":false,"DestinationType":"DataFlowConnector","DestinationIsReference":false} */
            id_1d55a1faa3dd4f78ad22ac73051f5d2d.WireTo(insertInstantiations, "fanoutList"); /* {"SourceType":"DataFlowConnector","SourceIsReference":false,"DestinationType":"InsertFileCodeLines","DestinationIsReference":false} */
            id_a26b08b25184469db6f0c4987d4c68dd.WireTo(generateCode, "eventHappened"); /* {"SourceType":"KeyEvent","SourceIsReference":false,"DestinationType":"EventConnector","DestinationIsReference":false} */
            generateCode.WireTo(id_c5fdc10d2ceb4577bef01977ee8e9dd1, "fanoutList"); /* {"SourceType":"EventConnector","SourceIsReference":false,"DestinationType":"Data","DestinationIsReference":false} */
            generateCode.WireTo(id_5e77c28f15294641bb881592d2cd7ac9, "fanoutList"); /* {"SourceType":"EventConnector","SourceIsReference":false,"DestinationType":"EventLambda","DestinationIsReference":false} */
            generateCode.WireTo(id_b5364bf1c9cd46a28e62bb2eb0e11692, "fanoutList"); /* {"SourceType":"EventConnector","SourceIsReference":false,"DestinationType":"GenerateALACode","DestinationIsReference":false} */
            generateCode.WireTo(id_0e563f77c5754bdb8a75b7f55607e9b0, "complete"); /* {"SourceType":"EventConnector","SourceIsReference":false,"DestinationType":"EventConnector","DestinationIsReference":false} */
            id_60229af56d92436996d2ee8d919083a3.WireTo(id_58c03e4b18bb43de8106a4423ca54318, "filePathInput"); /* {"SourceType":"EditSetting","SourceIsReference":false,"DestinationType":"Data","DestinationIsReference":false} */
            id_2b42bd6059334bfabc3df1d047751d7a.WireTo(id_b9865ebcd2864642a96573ced52bbb7f, "filePathInput"); /* {"SourceType":"FileWriter","SourceIsReference":false,"DestinationType":"DataFlowConnector","DestinationIsReference":false} */
            id_b9865ebcd2864642a96573ced52bbb7f.WireTo(id_72140c92ac4f4255abe9d149068fa16f, "fanoutList"); /* {"SourceType":"DataFlowConnector","SourceIsReference":false,"DestinationType":"FileReader","DestinationIsReference":false} */
            insertInstantiations.WireTo(insertWireTos, "newFileContentsOutput"); /* {"SourceType":"InsertFileCodeLines","SourceIsReference":false,"DestinationType":"InsertFileCodeLines","DestinationIsReference":false} */
            insertWireTos.WireTo(id_2b42bd6059334bfabc3df1d047751d7a, "newFileContentsOutput"); /* {"SourceType":"InsertFileCodeLines","SourceIsReference":false,"DestinationType":"FileWriter","DestinationIsReference":false} */
            id_0e563f77c5754bdb8a75b7f55607e9b0.WireTo(insertInstantiations, "fanoutList"); /* {"SourceType":"EventConnector","SourceIsReference":false,"DestinationType":"InsertFileCodeLines","DestinationIsReference":false} */
            id_0e563f77c5754bdb8a75b7f55607e9b0.WireTo(insertWireTos, "fanoutList"); /* {"SourceType":"EventConnector","SourceIsReference":false,"DestinationType":"InsertFileCodeLines","DestinationIsReference":false} */
            id_0e563f77c5754bdb8a75b7f55607e9b0.WireTo(id_3f30a573358d4fd08c4c556281737360, "complete"); /* {"SourceType":"EventConnector","SourceIsReference":false,"DestinationType":"EventLambda","DestinationIsReference":false} */
            id_96ab5fcf787a4e6d88af011f6e3daeae.WireTo(id_026d2d87a422495aa46c8fc4bda7cdd7, "clickedEvent"); /* {"SourceType":"MenuItem","SourceIsReference":false,"DestinationType":"EventLambda","DestinationIsReference":false} */
            id_e3837af93b584ca9874336851ff0cd31.WireTo(globalMessageTextDisplay, "child"); /* {"SourceType":"UIConfig","SourceIsReference":false,"DestinationType":"Text","DestinationIsReference":false} */
            id_42c7f12c13804ec7b111291739be78f5.WireTo(createDiagramFromCode, "fanoutList"); /* {"SourceType":"DataFlowConnector","SourceIsReference":false,"DestinationType":"CreateDiagramFromCode","DestinationIsReference":false} */
            id_08d455bfa9744704b21570d06c3c5389.WireTo(id_6f93680658e04f8a9ab15337cee1eca3, "children"); /* {"SourceType":"MenuItem","SourceIsReference":false,"DestinationType":"MenuItem","DestinationIsReference":false} */
            id_a3efe072d6b44816a631d90ccef5b71e.WireTo(id_9f411cfea16b45ed9066dd8f2006e1f1, "settingJsonOutput"); /* {"SourceType":"GetSetting","SourceIsReference":false,"DestinationType":"FileReader","DestinationIsReference":false} */
            id_bb687ee0b7dd4b86a38a3f81ddbab75f.WireTo(id_db598ad59e5542a0adc5df67ced27f73, "clickedEvent"); /* {"SourceType":"MenuItem","SourceIsReference":false,"DestinationType":"EventConnector","DestinationIsReference":false} */
            id_db598ad59e5542a0adc5df67ced27f73.WireTo(id_14170585873a4fb6a7550bfb3ce8ecd4, "fanoutList"); /* {"SourceType":"EventConnector","SourceIsReference":false,"DestinationType":"FileBrowser","DestinationIsReference":false} */
            id_14170585873a4fb6a7550bfb3ce8ecd4.WireTo(id_9b866e4112fd4347a2a3e81441401dea, "selectedFilePathOutput"); /* {"SourceType":"FileBrowser","SourceIsReference":false,"DestinationType":"DataFlowConnector","DestinationIsReference":false} */
            id_9b866e4112fd4347a2a3e81441401dea.WireTo(setting_currentDiagramCodeFilePath, "fanoutList"); /* {"SourceType":"DataFlowConnector","SourceIsReference":false,"DestinationType":"DataFlowConnector","DestinationIsReference":false} */
            id_9f411cfea16b45ed9066dd8f2006e1f1.WireTo(id_cf7df48ac3304a8894a7536261a3b474, "fileContentOutput"); /* {"SourceType":"FileReader","SourceIsReference":false,"DestinationType":"DataFlowConnector","DestinationIsReference":false} */
            id_dcd4c90552dc4d3fb579833da87cd829.WireTo(id_5ddd02478c734777b9e6f1079b4b3d45, "delayedEvent"); /* {"SourceType":"DispatcherEvent","SourceIsReference":false,"DestinationType":"GetSetting","DestinationIsReference":false} */
            id_5ddd02478c734777b9e6f1079b4b3d45.WireTo(id_ecfbf0b7599e4340b8b2f79b7d1e29cb, "filePathInput"); /* {"SourceType":"GetSetting","SourceIsReference":false,"DestinationType":"Data","DestinationIsReference":false} */
            latestCodeFilePath.WireTo(id_d5d3af7a3c9a47bf9af3b1a1e1246267, "fanoutList"); /* {"SourceType":"DataFlowConnector","SourceIsReference":false,"DestinationType":"Apply","DestinationIsReference":false} */
            id_d5d3af7a3c9a47bf9af3b1a1e1246267.WireTo(id_2ce385b32256413ab2489563287afaac, "output"); /* {"SourceType":"Apply","SourceIsReference":false,"DestinationType":"IfElse","DestinationIsReference":false} */
            id_5ddd02478c734777b9e6f1079b4b3d45.WireTo(latestCodeFilePath, "settingJsonOutput"); /* {"SourceType":"GetSetting","SourceIsReference":false,"DestinationType":"DataFlowConnector","DestinationIsReference":false} */
            id_f9b8e7f524a14884be753d19a351a285.WireTo(id_dcd4c90552dc4d3fb579833da87cd829, "complete"); /* {"SourceType":"EventConnector","SourceIsReference":false,"DestinationType":"DispatcherEvent","DestinationIsReference":false} */
            mainCanvasDisplay.WireTo(id_0b4478e56d614ca091979014db65d076, "eventHandlers"); /* {"SourceType":"CanvasDisplay","SourceIsReference":false,"DestinationType":"MouseButtonEvent","DestinationIsReference":false} */
            id_0b4478e56d614ca091979014db65d076.WireTo(id_d90fbf714f5f4fdc9b43cbe4d5cebf1c, "senderOutput"); /* {"SourceType":"MouseButtonEvent","SourceIsReference":false,"DestinationType":"ApplyAction","DestinationIsReference":false} */
            mainWindowVertical.WireTo(mainHorizontal, "children"); /* {"SourceType":"Vertical","SourceIsReference":false,"DestinationType":"Horizontal","DestinationIsReference":false} */
            mainWindowVertical.WireTo(statusBarHorizontal, "children"); /* {"SourceType":"Vertical","SourceIsReference":false,"DestinationType":"Horizontal","DestinationIsReference":false} */
            mainHorizontal.WireTo(sidePanelHoriz, "children"); /* {"SourceType":"Horizontal","SourceIsReference":false,"DestinationType":"Horizontal","DestinationIsReference":false} */
            sidePanelHoriz.WireTo(id_987196dd20ab4721b0c193bb7a2064f4, "children"); /* {"SourceType":"Horizontal","SourceIsReference":false,"DestinationType":"Vertical","DestinationIsReference":false} */
            id_987196dd20ab4721b0c193bb7a2064f4.WireTo(id_7b250b222ca44ba2922547f03a4aef49, "children"); /* {"SourceType":"Vertical","SourceIsReference":false,"DestinationType":"TabContainer","DestinationIsReference":false} */
            id_7b250b222ca44ba2922547f03a4aef49.WireTo(directoryExplorerTab, "childrenTabs"); /* {"SourceType":"TabContainer","SourceIsReference":false,"DestinationType":"Tab","DestinationIsReference":false} */
            id_42967d39c2334aab9c23697d04177f8a.WireTo(id_4a42bbf671cd4dba8987bd656e5a2ced, "children"); /* {"SourceType":"MenuBar","SourceIsReference":false,"DestinationType":"MenuItem","DestinationIsReference":false} */
            id_a1f87102954345b69de6841053fce813.WireTo(id_2b3a750d477d4e168aaa3ed0ae548650, "fanoutList"); /* {"SourceType":"DataFlowConnector","SourceIsReference":false,"DestinationType":"ConvertToEvent","DestinationIsReference":false} */
            directoryExplorerConfig.WireTo(directoryTreeExplorer, "child"); /* {"SourceType":"UIConfig","SourceIsReference":false,"DestinationType":"DirectoryTree","DestinationIsReference":false} */
            id_a1f87102954345b69de6841053fce813.WireTo(directoryTreeExplorer, "fanoutList"); /* {"SourceType":"DataFlowConnector","SourceIsReference":false,"DestinationType":"DirectoryTree","DestinationIsReference":false} */
            directoryExplorerTab.WireTo(id_e8a68acda2aa4d54add689bd669589d3, "children"); /* {"SourceType":"Tab","SourceIsReference":false,"DestinationType":"Vertical","DestinationIsReference":false} */
            id_e8a68acda2aa4d54add689bd669589d3.WireTo(projectDirectoryTreeHoriz, "children"); /* {"SourceType":"Vertical","SourceIsReference":false,"DestinationType":"Horizontal","DestinationIsReference":false} */
            id_642ae4874d1e4fd2a777715cc1996b49.WireTo(id_08a51a5702e34a38af808db65a3a6eb3, "fanoutList"); /* {"SourceType":"EventConnector","SourceIsReference":false,"DestinationType":"StateChangeListener","DestinationIsReference":false} */
            id_08a51a5702e34a38af808db65a3a6eb3.WireTo(id_9d14914fdf0647bb8b4b20ea799e26c8, "stateChanged"); /* {"SourceType":"StateChangeListener","SourceIsReference":false,"DestinationType":"EventConnector","DestinationIsReference":false} */
            id_9d14914fdf0647bb8b4b20ea799e26c8.WireTo(unhighlightAllWires, "fanoutList"); /* {"SourceType":"EventConnector","SourceIsReference":false,"DestinationType":"EventLambda","DestinationIsReference":false} */
            id_2a7c8f3b6b5e4879ad5a35ff6d8538fd.WireTo(id_6d789ff1a0bc4a2d8e88733adc266be8, "argsOutput"); /* {"SourceType":"MouseWheelEvent","SourceIsReference":false,"DestinationType":"DataFlowConnector","DestinationIsReference":false} */
            id_6d789ff1a0bc4a2d8e88733adc266be8.WireTo(mouseWheelArgs, "fanoutList"); /* {"SourceType":"DataFlowConnector","SourceIsReference":false,"DestinationType":"DataFlowConnector","DestinationIsReference":false} */
            id_6d789ff1a0bc4a2d8e88733adc266be8.WireTo(id_33990435606f4bbc9ba1786ed05672ab, "fanoutList"); /* {"SourceType":"DataFlowConnector","SourceIsReference":false,"DestinationType":"Apply","DestinationIsReference":false} */
            id_6f93680658e04f8a9ab15337cee1eca3.WireTo(id_a236bd13c516401eb5a83a451a875dd0, "clickedEvent"); /* {"SourceType":"MenuItem","SourceIsReference":false,"DestinationType":"EventConnector","DestinationIsReference":false} */
            id_a236bd13c516401eb5a83a451a875dd0.WireTo(id_6fdaaf997d974e30bbb7c106c40e997c, "fanoutList"); /* {"SourceType":"EventConnector","SourceIsReference":false,"DestinationType":"EventLambda","DestinationIsReference":false} */
            id_a236bd13c516401eb5a83a451a875dd0.WireTo(id_a3efe072d6b44816a631d90ccef5b71e, "fanoutList"); /* {"SourceType":"EventConnector","SourceIsReference":false,"DestinationType":"GetSetting","DestinationIsReference":false} */
            createNewALANode.WireTo(latestAddedNode, "output"); /* {"SourceType":"Apply","SourceIsReference":false,"DestinationType":"DataFlowConnector","DestinationIsReference":false} */
            latestAddedNode.WireTo(createAndPaintALAWire, "fanoutList"); /* {"SourceType":"DataFlowConnector","SourceIsReference":false,"DestinationType":"Apply","DestinationIsReference":false} */
            id_855f86954b3e4776909cde23cd96d071.WireTo(id_ad29db53c0d64d4b8be9e31474882158, "eventHappened"); /* {"SourceType":"KeyEvent","SourceIsReference":false,"DestinationType":"EventConnector","DestinationIsReference":false} */
            id_08d455bfa9744704b21570d06c3c5389.WireTo(id_86a7f0259b204907a092da0503eb9873, "children"); /* {"SourceType":"MenuItem","SourceIsReference":false,"DestinationType":"MenuItem","DestinationIsReference":false} */
            id_86a7f0259b204907a092da0503eb9873.WireTo(id_3710469340354a1bbb4b9d3371c9c012, "clickedEvent"); /* {"SourceType":"MenuItem","SourceIsReference":false,"DestinationType":"FolderBrowser","DestinationIsReference":false} */
            id_3710469340354a1bbb4b9d3371c9c012.WireTo(testDirectoryTree, "selectedFolderPathOutput"); /* {"SourceType":"FolderBrowser","SourceIsReference":false,"DestinationType":"DirectoryTree","DestinationIsReference":false} */
            id_08d455bfa9744704b21570d06c3c5389.WireTo(testSimulateKeyboard, "children"); /* {"SourceType":"MenuItem","SourceIsReference":false,"DestinationType":"MenuItem","DestinationIsReference":false} */
            testSimulateKeyboard.WireTo(id_52b8f2c28c2e40cabedbd531171c779a, "clickedEvent"); /* {"SourceType":"MenuItem","SourceIsReference":false,"DestinationType":"EventConnector","DestinationIsReference":false} */
            id_52b8f2c28c2e40cabedbd531171c779a.WireTo(id_5c31090d2c954aa7b4a10e753bdfc03a, "fanoutList"); /* {"SourceType":"EventConnector","SourceIsReference":false,"DestinationType":"SimulateKeyboard","DestinationIsReference":false} */
            id_52b8f2c28c2e40cabedbd531171c779a.WireTo(id_86ecd8f953324e34adc6238338f75db5, "fanoutList"); /* {"SourceType":"EventConnector","SourceIsReference":false,"DestinationType":"SimulateKeyboard","DestinationIsReference":false} */
            id_52b8f2c28c2e40cabedbd531171c779a.WireTo(id_63e463749abe41d28d05b877479070f8, "fanoutList"); /* {"SourceType":"EventConnector","SourceIsReference":false,"DestinationType":"SimulateKeyboard","DestinationIsReference":false} */
            id_52b8f2c28c2e40cabedbd531171c779a.WireTo(id_66e516b6027649e1995a531d03c0c518, "fanoutList"); /* {"SourceType":"EventConnector","SourceIsReference":false,"DestinationType":"SimulateKeyboard","DestinationIsReference":false} */
            mainCanvasDisplay.WireTo(id_8863f404bed34d47922654bd0190259c, "eventHandlers"); /* {"SourceType":"CanvasDisplay","SourceIsReference":false,"DestinationType":"KeyEvent","DestinationIsReference":false} */
            id_8863f404bed34d47922654bd0190259c.WireTo(cloneSelectedNodeModel, "eventHappened"); /* {"SourceType":"KeyEvent","SourceIsReference":false,"DestinationType":"Data","DestinationIsReference":false} */
            id_024b1810c2d24db3b9fac1ccce2fad9e.WireTo(id_0f802a208aad42209777c13b2e61fe56, "fanoutList"); /* {"SourceType":"DataFlowConnector","SourceIsReference":false,"DestinationType":"ApplyAction","DestinationIsReference":false} */
            mainCanvasDisplay.WireTo(id_7363c80d952e4246aba050e007287444, "eventHandlers"); /* {"SourceType":"CanvasDisplay","SourceIsReference":false,"DestinationType":"KeyEvent","DestinationIsReference":false} */
            createDummyAbstractionModel.WireTo(createNewALANode, "dataOutput"); /* {"SourceType":"Data","SourceIsReference":false,"DestinationType":"Apply","DestinationIsReference":false} */
            createAndPaintALAWire.WireTo(id_8647cbf4ac4049a99204b0e3aa70c326, "output"); /* {"SourceType":"Apply","SourceIsReference":false,"DestinationType":"ConvertToEvent","DestinationIsReference":false} */
            id_7363c80d952e4246aba050e007287444.WireTo(id_5a22e32e96e641d49c6fb4bdf6fcd94b, "eventHappened"); /* {"SourceType":"KeyEvent","SourceIsReference":false,"DestinationType":"EventConnector","DestinationIsReference":false} */
            id_ad29db53c0d64d4b8be9e31474882158.WireTo(createDummyAbstractionModel, "fanoutList"); /* {"SourceType":"EventConnector","SourceIsReference":false,"DestinationType":"Data","DestinationIsReference":false} */
            id_5a22e32e96e641d49c6fb4bdf6fcd94b.WireTo(createDummyAbstractionModel, "fanoutList"); /* {"SourceType":"EventConnector","SourceIsReference":false,"DestinationType":"Data","DestinationIsReference":false} */
            id_5a22e32e96e641d49c6fb4bdf6fcd94b.WireTo(id_0945b34f58a146ff983962f595f57fb2, "complete"); /* {"SourceType":"EventConnector","SourceIsReference":false,"DestinationType":"DispatcherEvent","DestinationIsReference":false} */
            id_0945b34f58a146ff983962f595f57fb2.WireTo(id_36c5f05380b04b378de94534411f3f88, "delayedEvent"); /* {"SourceType":"DispatcherEvent","SourceIsReference":false,"DestinationType":"EventLambda","DestinationIsReference":false} */
            id_7363c80d952e4246aba050e007287444.WireTo(id_4341066281bc4015a668a3bbbcb7256b, "argsOutput"); /* {"SourceType":"KeyEvent","SourceIsReference":false,"DestinationType":"ApplyAction","DestinationIsReference":false} */
            cloneSelectedNodeModel.WireTo(id_024b1810c2d24db3b9fac1ccce2fad9e, "dataOutput"); /* {"SourceType":"Data","SourceIsReference":false,"DestinationType":"DataFlowConnector","DestinationIsReference":false} */
            id_08d455bfa9744704b21570d06c3c5389.WireTo(id_2c933997055b4122bdb77945f1abb560, "children"); /* {"SourceType":"MenuItem","SourceIsReference":false,"DestinationType":"MenuItem","DestinationIsReference":false} */
            id_2c933997055b4122bdb77945f1abb560.WireTo(id_0eea701e0bc84c42a9f17ccc200ef2ef, "clickedEvent"); /* {"SourceType":"MenuItem","SourceIsReference":false,"DestinationType":"Data","DestinationIsReference":false} */
            id_08d455bfa9744704b21570d06c3c5389.WireTo(id_29ed401eb9c240d98bf5c6d1f00c5c76, "children"); /* {"SourceType":"MenuItem","SourceIsReference":false,"DestinationType":"MenuItem","DestinationIsReference":false} */
            id_29ed401eb9c240d98bf5c6d1f00c5c76.WireTo(id_fa857dd7432e406c8c6c642152b37730, "clickedEvent"); /* {"SourceType":"MenuItem","SourceIsReference":false,"DestinationType":"Data","DestinationIsReference":false} */
            id_a2d71044048840b0a69356270e6520ac.WireTo(id_42c7f12c13804ec7b111291739be78f5, "dataOutput"); /* {"SourceType":"Data","SourceIsReference":false,"DestinationType":"DataFlowConnector","DestinationIsReference":false} */
            id_42c7f12c13804ec7b111291739be78f5.WireTo(id_409be365df274cc6a7a124e8a80316a5, "fanoutList"); /* {"SourceType":"DataFlowConnector","SourceIsReference":false,"DestinationType":"ConvertToEvent","DestinationIsReference":false} */
            id_57e7dd98a0874e83bbd5014f7e9c9ef5.WireTo(resetScale, "fanoutList"); /* {"SourceType":"DataFlowConnector","SourceIsReference":false,"DestinationType":"Scale","DestinationIsReference":false} */
            id_409be365df274cc6a7a124e8a80316a5.WireTo(id_82b26eeaba664ee7b2a2c0682e25ce08, "eventOutput"); /* {"SourceType":"ConvertToEvent","SourceIsReference":false,"DestinationType":"EventConnector","DestinationIsReference":false} */
            id_82b26eeaba664ee7b2a2c0682e25ce08.WireTo(id_5e2f0621c62142c1b5972961c93cb725, "fanoutList"); /* {"SourceType":"EventConnector","SourceIsReference":false,"DestinationType":"Data","DestinationIsReference":false} */
            id_0eea701e0bc84c42a9f17ccc200ef2ef.WireTo(resetViewOnNode, "dataOutput"); /* {"SourceType":"Data","SourceIsReference":false,"DestinationType":"ApplyAction","DestinationIsReference":false} */
            id_fa857dd7432e406c8c6c642152b37730.WireTo(resetViewOnNode, "dataOutput"); /* {"SourceType":"Data","SourceIsReference":false,"DestinationType":"ApplyAction","DestinationIsReference":false} */
            id_5e2f0621c62142c1b5972961c93cb725.WireTo(id_57e7dd98a0874e83bbd5014f7e9c9ef5, "dataOutput"); /* {"SourceType":"Data","SourceIsReference":false,"DestinationType":"DataFlowConnector","DestinationIsReference":false} */
            id_57e7dd98a0874e83bbd5014f7e9c9ef5.WireTo(id_e1e6cf54f73d4f439c6f18b668a73f1a, "fanoutList"); /* {"SourceType":"DataFlowConnector","SourceIsReference":false,"DestinationType":"ApplyAction","DestinationIsReference":false} */
            id_cc0c82a2157f4b0291c812236a6e45ba.WireTo(id_fed56a4aef6748178fa7078388643323, "children"); /* {"SourceType":"Vertical","SourceIsReference":false,"DestinationType":"Horizontal","DestinationIsReference":false} */
            searchTextBox.WireTo(id_00b0ca72bbce4ef4ba5cf395c666a26e, "textOutput"); /* {"SourceType":"TextBox","SourceIsReference":false,"DestinationType":"DataFlowConnector","DestinationIsReference":false} */
            startSearchButton.WireTo(id_5da1d2f5b13746f29802078592e59346, "eventButtonClicked"); /* {"SourceType":"Button","SourceIsReference":false,"DestinationType":"Data","DestinationIsReference":false} */
            id_5da1d2f5b13746f29802078592e59346.WireTo(id_00b0ca72bbce4ef4ba5cf395c666a26e, "inputDataB"); /* {"SourceType":"Data","SourceIsReference":false,"DestinationType":"DataFlowConnector","DestinationIsReference":false} */
            id_e8a68acda2aa4d54add689bd669589d3.WireTo(projectDirectoryOptionsHoriz, "children"); /* {"SourceType":"Vertical","SourceIsReference":false,"DestinationType":"Horizontal","DestinationIsReference":false} */
            searchTextBox.WireTo(id_5da1d2f5b13746f29802078592e59346, "eventEnterPressed"); /* {"SourceType":"TextBox","SourceIsReference":false,"DestinationType":"Data","DestinationIsReference":false} */
            searchTab.WireTo(id_cc0c82a2157f4b0291c812236a6e45ba, "children"); /* {"SourceType":"Tab","SourceIsReference":false,"DestinationType":"Vertical","DestinationIsReference":false} */
            id_3622556a1b37410691b51b83c004a315.WireTo(id_73274d9ce8d5414899772715a1d0f266, "selectedIndex"); /* {"SourceType":"ListDisplay","SourceIsReference":false,"DestinationType":"Apply","DestinationIsReference":false} */
            id_73274d9ce8d5414899772715a1d0f266.WireTo(id_fff8d82dbdd04da18793108f9b8dd5cf, "output"); /* {"SourceType":"Apply","SourceIsReference":false,"DestinationType":"DataFlowConnector","DestinationIsReference":false} */
            id_fff8d82dbdd04da18793108f9b8dd5cf.WireTo(id_75ecf8c2602c41829602707be8a8a481, "fanoutList"); /* {"SourceType":"DataFlowConnector","SourceIsReference":false,"DestinationType":"ConvertToEvent","DestinationIsReference":false} */
            id_fff8d82dbdd04da18793108f9b8dd5cf.WireTo(id_23a625377ea745ee8253482ee1f0d437, "fanoutList"); /* {"SourceType":"DataFlowConnector","SourceIsReference":false,"DestinationType":"ApplyAction","DestinationIsReference":false} */
            id_75ecf8c2602c41829602707be8a8a481.WireTo(id_5e2f0621c62142c1b5972961c93cb725, "eventOutput"); /* {"SourceType":"ConvertToEvent","SourceIsReference":false,"DestinationType":"Data","DestinationIsReference":false} */
            id_fff8d82dbdd04da18793108f9b8dd5cf.WireTo(resetViewOnNode, "fanoutList"); /* {"SourceType":"DataFlowConnector","SourceIsReference":false,"DestinationType":"ApplyAction","DestinationIsReference":false} */
            currentSearchQuery.WireTo(id_5f1c0f0187eb4dc99f15254fd36fa9b6, "fanoutList"); /* {"SourceType":"DataFlowConnector","SourceIsReference":false,"DestinationType":"Apply","DestinationIsReference":false} */
            id_5f1c0f0187eb4dc99f15254fd36fa9b6.WireTo(id_8e347b7f5f3b4aa6b1c8f1966d0280a3, "output"); /* {"SourceType":"Apply","SourceIsReference":false,"DestinationType":"ForEach","DestinationIsReference":false} */
            id_8e347b7f5f3b4aa6b1c8f1966d0280a3.WireTo(id_282744d2590b4d3e8b337d73c05e0823, "elementOutput"); /* {"SourceType":"ForEach","SourceIsReference":false,"DestinationType":"DataFlowConnector","DestinationIsReference":false} */
            id_1c95fb3a139b4602bba7b10201112546.WireTo(id_2c9472651f984aa8ab763f327bcfa45e, "delayedData"); /* {"SourceType":"DispatcherData","SourceIsReference":false,"DestinationType":"ApplyAction","DestinationIsReference":false} */
            id_8e347b7f5f3b4aa6b1c8f1966d0280a3.WireTo(currentSearchResultIndex, "indexOutput"); /* {"SourceType":"ForEach","SourceIsReference":false,"DestinationType":"DataFlowConnector","DestinationIsReference":false} */
            id_5da1d2f5b13746f29802078592e59346.WireTo(currentSearchQuery, "dataOutput"); /* {"SourceType":"Data","SourceIsReference":false,"DestinationType":"DataFlowConnector","DestinationIsReference":false} */
            id_282744d2590b4d3e8b337d73c05e0823.WireTo(id_1c95fb3a139b4602bba7b10201112546, "fanoutList"); /* {"SourceType":"DataFlowConnector","SourceIsReference":false,"DestinationType":"DispatcherData","DestinationIsReference":false} */
            id_282744d2590b4d3e8b337d73c05e0823.WireTo(id_01bdd051f2034331bd9f121029b0e2e8, "fanoutList"); /* {"SourceType":"DataFlowConnector","SourceIsReference":false,"DestinationType":"DispatcherData","DestinationIsReference":false} */
            id_01bdd051f2034331bd9f121029b0e2e8.WireTo(id_67bc4eb50bb04d9694a1a0d5ce65c9d9, "delayedData"); /* {"SourceType":"DispatcherData","SourceIsReference":false,"DestinationType":"ApplyAction","DestinationIsReference":false} */
            id_08d455bfa9744704b21570d06c3c5389.WireTo(id_f526f560b3504a0b8115879e5d5354ff, "children"); /* {"SourceType":"MenuItem","SourceIsReference":false,"DestinationType":"MenuItem","DestinationIsReference":false} */
            id_f526f560b3504a0b8115879e5d5354ff.WireTo(id_dea56e5fd7174cd7983e8f2c837a941b, "clickedEvent"); /* {"SourceType":"MenuItem","SourceIsReference":false,"DestinationType":"ContextMenu","DestinationIsReference":false} */
            projectDirectoryTreeHoriz.WireTo(directoryExplorerConfig, "children"); /* {"SourceType":"Horizontal","SourceIsReference":false,"DestinationType":"UIConfig","DestinationIsReference":false} */
            directoryTreeExplorer.WireTo(currentSelectedDirectoryTreeFilePath, "selectedFullPath"); /* {"SourceType":"DirectoryTree","SourceIsReference":false,"DestinationType":"DataFlowConnector","DestinationIsReference":false} */
            directoryExplorerConfig.WireTo(id_8b908f2be6094d5b8cd3dce5c5fc2b8b, "contextMenuChildren"); /* {"SourceType":"UIConfig","SourceIsReference":false,"DestinationType":"MenuItem","DestinationIsReference":false} */
            id_8b908f2be6094d5b8cd3dce5c5fc2b8b.WireTo(id_692716a735e44e948a8d14cd550c1276, "clickedEvent"); /* {"SourceType":"MenuItem","SourceIsReference":false,"DestinationType":"Data","DestinationIsReference":false} */
            id_692716a735e44e948a8d14cd550c1276.WireTo(currentSelectedDirectoryTreeFilePath, "inputDataB"); /* {"SourceType":"Data","SourceIsReference":false,"DestinationType":"DataFlowConnector","DestinationIsReference":false} */
            id_692716a735e44e948a8d14cd550c1276.WireTo(id_9b866e4112fd4347a2a3e81441401dea, "dataOutput"); /* {"SourceType":"Data","SourceIsReference":false,"DestinationType":"DataFlowConnector","DestinationIsReference":false} */
            id_0d4d34a2cd6749759ac0c2708ddf0cbc.WireTo(id_692716a735e44e948a8d14cd550c1276, "eventButtonClicked"); /* {"SourceType":"Button","SourceIsReference":false,"DestinationType":"Data","DestinationIsReference":false} */
            mainCanvasDisplay.WireTo(id_f77e477a71954e20a587ec6fb4d006ce, "eventHandlers"); /* {"SourceType":"CanvasDisplay","SourceIsReference":false,"DestinationType":"KeyEvent","DestinationIsReference":false} */
            id_f77e477a71954e20a587ec6fb4d006ce.WireTo(id_87a897a783884990bf10e4d7a9e276b9, "eventHappened"); /* {"SourceType":"KeyEvent","SourceIsReference":false,"DestinationType":"EventConnector","DestinationIsReference":false} */
            id_87a897a783884990bf10e4d7a9e276b9.WireTo(id_9e6a74b0dbea488cba6027ee5187ad0f, "fanoutList"); /* {"SourceType":"EventConnector","SourceIsReference":false,"DestinationType":"DispatcherEvent","DestinationIsReference":false} */
            id_87a897a783884990bf10e4d7a9e276b9.WireTo(id_b55e77a5d78243bf9612ecb7cb20c2c7, "fanoutList"); /* {"SourceType":"EventConnector","SourceIsReference":false,"DestinationType":"DispatcherEvent","DestinationIsReference":false} */
            id_87a897a783884990bf10e4d7a9e276b9.WireTo(id_45593aeb91a145aa9d84d8b77a8d4d8e, "fanoutList"); /* {"SourceType":"EventConnector","SourceIsReference":false,"DestinationType":"DispatcherEvent","DestinationIsReference":false} */
            id_b55e77a5d78243bf9612ecb7cb20c2c7.WireTo(id_a690d6dd37ba4c98b5506777df6dc9db, "delayedEvent"); /* {"SourceType":"DispatcherEvent","SourceIsReference":false,"DestinationType":"EventLambda","DestinationIsReference":false} */
            id_45593aeb91a145aa9d84d8b77a8d4d8e.WireTo(id_63db7722e48a4c5aabd905f75b0519b2, "delayedEvent"); /* {"SourceType":"DispatcherEvent","SourceIsReference":false,"DestinationType":"EventLambda","DestinationIsReference":false} */
            id_2ce385b32256413ab2489563287afaac.WireTo(id_006b07cc90c64e398b945bb43fdd4de9, "ifOutput"); /* {"SourceType":"IfElse","SourceIsReference":false,"DestinationType":"EventConnector","DestinationIsReference":false} */
            id_006b07cc90c64e398b945bb43fdd4de9.WireTo(id_e7da19475fcc44bdaf4a64d05f92b771, "fanoutList"); /* {"SourceType":"EventConnector","SourceIsReference":false,"DestinationType":"Data","DestinationIsReference":false} */
            id_006b07cc90c64e398b945bb43fdd4de9.WireTo(id_68cfe1cc12f948cab25289d853300813, "fanoutList"); /* {"SourceType":"EventConnector","SourceIsReference":false,"DestinationType":"PopupWindow","DestinationIsReference":false} */
            id_68cfe1cc12f948cab25289d853300813.WireTo(id_95ddd89b36d54db298eaa05165284569, "children"); /* {"SourceType":"PopupWindow","SourceIsReference":false,"DestinationType":"Vertical","DestinationIsReference":false} */
            id_e7da19475fcc44bdaf4a64d05f92b771.WireTo(latestCodeFilePath, "inputDataB"); /* {"SourceType":"Data","SourceIsReference":false,"DestinationType":"DataFlowConnector","DestinationIsReference":false} */
            id_89ab09564cea4a8b93d8925e8234e44c.WireTo(id_add742a4683f4dd0b34d8d0eebbe3f07, "child"); /* {"SourceType":"UIConfig","SourceIsReference":false,"DestinationType":"Button","DestinationIsReference":false} */
            id_c180a82fd3a6495a885e9dde61aaaef3.WireTo(id_e82c1f80e1884a57b79c681462efd65d, "child"); /* {"SourceType":"UIConfig","SourceIsReference":false,"DestinationType":"Button","DestinationIsReference":false} */
            id_add742a4683f4dd0b34d8d0eebbe3f07.WireTo(id_5fbec6b061cc428a8c00e5c2a652b89e, "eventButtonClicked"); /* {"SourceType":"Button","SourceIsReference":false,"DestinationType":"EventConnector","DestinationIsReference":false} */
            id_2bfcbb47c2c745578829e1b0f8287f42.WireTo(id_b0d86bb898944ded83ec7f58b9f4a1b8, "eventButtonClicked"); /* {"SourceType":"Button","SourceIsReference":false,"DestinationType":"EventConnector","DestinationIsReference":false} */
            id_5fbec6b061cc428a8c00e5c2a652b89e.WireTo(id_68cfe1cc12f948cab25289d853300813, "fanoutList"); /* {"SourceType":"EventConnector","SourceIsReference":false,"DestinationType":"PopupWindow","DestinationIsReference":false} */
            id_b0d86bb898944ded83ec7f58b9f4a1b8.WireTo(id_68cfe1cc12f948cab25289d853300813, "fanoutList"); /* {"SourceType":"EventConnector","SourceIsReference":false,"DestinationType":"PopupWindow","DestinationIsReference":false} */
            id_5fbec6b061cc428a8c00e5c2a652b89e.WireTo(id_721b5692fa5a4ba39f509fd7e4a6291b, "fanoutList"); /* {"SourceType":"EventConnector","SourceIsReference":false,"DestinationType":"Data","DestinationIsReference":false} */
            id_721b5692fa5a4ba39f509fd7e4a6291b.WireTo(latestCodeFilePath, "inputDataB"); /* {"SourceType":"Data","SourceIsReference":false,"DestinationType":"DataFlowConnector","DestinationIsReference":false} */
            id_721b5692fa5a4ba39f509fd7e4a6291b.WireTo(id_9b866e4112fd4347a2a3e81441401dea, "dataOutput"); /* {"SourceType":"Data","SourceIsReference":false,"DestinationType":"DataFlowConnector","DestinationIsReference":false} */
            id_b0d86bb898944ded83ec7f58b9f4a1b8.WireTo(id_1a403a85264c4074bc7ce5a71262c6c0, "fanoutList"); /* {"SourceType":"EventConnector","SourceIsReference":false,"DestinationType":"Data","DestinationIsReference":false} */
            id_1a403a85264c4074bc7ce5a71262c6c0.WireTo(id_1928c515b2414f6690c6924a76461081, "dataOutput"); /* {"SourceType":"Data","SourceIsReference":false,"DestinationType":"EditSetting","DestinationIsReference":false} */
            id_c7dc32a5f12b41ad94a910a74de38827.WireTo(id_d890df432c1f4e60a62b8913a5069b34, "children"); /* {"SourceType":"Horizontal","SourceIsReference":false,"DestinationType":"Horizontal","DestinationIsReference":false} */
            id_e7da19475fcc44bdaf4a64d05f92b771.WireTo(id_e4c9f92bbd6643a286683c9ff5f9fb3a, "dataOutput"); /* {"SourceType":"Data","SourceIsReference":false,"DestinationType":"Apply","DestinationIsReference":false} */
            id_e4c9f92bbd6643a286683c9ff5f9fb3a.WireTo(id_939726bef757459b914412aead1bb5f9, "output"); /* {"SourceType":"Apply","SourceIsReference":false,"DestinationType":"Text","DestinationIsReference":false} */
            id_de49d2fafc2140e996eb38fbf1e62103.WireTo(id_89ab09564cea4a8b93d8925e8234e44c, "children"); /* {"SourceType":"Horizontal","SourceIsReference":false,"DestinationType":"UIConfig","DestinationIsReference":false} */
            id_de49d2fafc2140e996eb38fbf1e62103.WireTo(id_c180a82fd3a6495a885e9dde61aaaef3, "children"); /* {"SourceType":"Horizontal","SourceIsReference":false,"DestinationType":"UIConfig","DestinationIsReference":false} */
            id_95ddd89b36d54db298eaa05165284569.WireTo(id_5b134e68e31b40f4b3e95eb007a020dc, "children"); /* {"SourceType":"Vertical","SourceIsReference":false,"DestinationType":"UIConfig","DestinationIsReference":false} */
            id_5b134e68e31b40f4b3e95eb007a020dc.WireTo(id_939726bef757459b914412aead1bb5f9, "child"); /* {"SourceType":"UIConfig","SourceIsReference":false,"DestinationType":"Text","DestinationIsReference":false} */
            id_95ddd89b36d54db298eaa05165284569.WireTo(id_c7dc32a5f12b41ad94a910a74de38827, "children"); /* {"SourceType":"Vertical","SourceIsReference":false,"DestinationType":"Horizontal","DestinationIsReference":false} */
            id_de49d2fafc2140e996eb38fbf1e62103.WireTo(id_0fafdba1ad834904ac7330f95dffd966, "children"); /* {"SourceType":"Horizontal","SourceIsReference":false,"DestinationType":"UIConfig","DestinationIsReference":false} */
            id_0fafdba1ad834904ac7330f95dffd966.WireTo(id_2bfcbb47c2c745578829e1b0f8287f42, "child"); /* {"SourceType":"UIConfig","SourceIsReference":false,"DestinationType":"Button","DestinationIsReference":false} */
            id_e82c1f80e1884a57b79c681462efd65d.WireTo(id_1139c3821d834efc947d5c4e949cd1ba, "eventButtonClicked"); /* {"SourceType":"Button","SourceIsReference":false,"DestinationType":"EventConnector","DestinationIsReference":false} */
            id_1139c3821d834efc947d5c4e949cd1ba.WireTo(id_68cfe1cc12f948cab25289d853300813, "fanoutList"); /* {"SourceType":"EventConnector","SourceIsReference":false,"DestinationType":"PopupWindow","DestinationIsReference":false} */
            id_c7dc32a5f12b41ad94a910a74de38827.WireTo(id_de49d2fafc2140e996eb38fbf1e62103, "children"); /* {"SourceType":"Horizontal","SourceIsReference":false,"DestinationType":"Horizontal","DestinationIsReference":false} */
            id_c7dc32a5f12b41ad94a910a74de38827.WireTo(id_4686253b1d7d4cd9a4d5bf03d6b7e380, "children"); /* {"SourceType":"Horizontal","SourceIsReference":false,"DestinationType":"Horizontal","DestinationIsReference":false} */
            id_1928c515b2414f6690c6924a76461081.WireTo(id_f140e9e4ef3f4c07898073fde207da99, "filePathInput"); /* {"SourceType":"EditSetting","SourceIsReference":false,"DestinationType":"Data","DestinationIsReference":false} */
            id_25a53022f6ab4e9284fd321e9535801b.WireTo(id_3622556a1b37410691b51b83c004a315, "child"); /* {"SourceType":"UIConfig","SourceIsReference":false,"DestinationType":"ListDisplay","DestinationIsReference":false} */
            projectDirectoryOptionsHoriz.WireTo(id_de10db4d6b8a426ba76b02959a58cb88, "children"); /* {"SourceType":"Horizontal","SourceIsReference":false,"DestinationType":"UIConfig","DestinationIsReference":false} */
            id_de10db4d6b8a426ba76b02959a58cb88.WireTo(id_0d4d34a2cd6749759ac0c2708ddf0cbc, "child"); /* {"SourceType":"UIConfig","SourceIsReference":false,"DestinationType":"Button","DestinationIsReference":false} */
            id_7b250b222ca44ba2922547f03a4aef49.WireTo(UIConfig_searchTab, "childrenTabs"); /* {"SourceType":"TabContainer","SourceIsReference":false,"DestinationType":"UIConfig","DestinationIsReference":false} */
            UIConfig_searchTab.WireTo(searchTab, "child"); /* {"SourceType":"UIConfig","SourceIsReference":false,"DestinationType":"Tab","DestinationIsReference":false} */
            id_fed56a4aef6748178fa7078388643323.WireTo(UIConfig_searchTextBox, "children"); /* {"SourceType":"Horizontal","SourceIsReference":false,"DestinationType":"UIConfig","DestinationIsReference":false} */
            UIConfig_searchTextBox.WireTo(searchTextBox, "child"); /* {"SourceType":"UIConfig","SourceIsReference":false,"DestinationType":"TextBox","DestinationIsReference":false} */
            id_fed56a4aef6748178fa7078388643323.WireTo(startSearchButton, "children"); /* {"SourceType":"Horizontal","SourceIsReference":false,"DestinationType":"Button","DestinationIsReference":false} */
            id_42967d39c2334aab9c23697d04177f8a.WireTo(id_a9db513fb0e749bda7f42b03964e5dce, "children"); /* {"SourceType":"MenuBar","SourceIsReference":false,"DestinationType":"MenuItem","DestinationIsReference":false} */
            id_42967d39c2334aab9c23697d04177f8a.WireTo(id_efeb87ef1b3c4f9e8ed2f8193e6b78b1, "children"); /* {"SourceType":"MenuBar","SourceIsReference":false,"DestinationType":"MenuItem","DestinationIsReference":false} */
            id_efeb87ef1b3c4f9e8ed2f8193e6b78b1.WireTo(generateCode, "clickedEvent"); /* {"SourceType":"MenuItem","SourceIsReference":false,"DestinationType":"EventConnector","DestinationIsReference":false} */
            startDiagramCreationProcess.WireTo(id_db77c286e64241c48de4fad0dde80024, "fanoutList"); /* {"SourceType":"EventConnector","SourceIsReference":false,"DestinationType":"EventLambda","DestinationIsReference":false} */
            startDiagramCreationProcess.WireTo(id_a2d71044048840b0a69356270e6520ac, "fanoutList"); /* {"SourceType":"EventConnector","SourceIsReference":false,"DestinationType":"Data","DestinationIsReference":false} */
            startDiagramCreationProcess.WireTo(startRightTreeLayoutProcess, "fanoutList"); /* {"SourceType":"EventConnector","SourceIsReference":false,"DestinationType":"EventConnector","DestinationIsReference":false} */
            id_a9db513fb0e749bda7f42b03964e5dce.WireTo(id_c9dbe185989e48c0869f984dd8e979f2, "clickedEvent"); /* {"SourceType":"MenuItem","SourceIsReference":false,"DestinationType":"Data","DestinationIsReference":false} */
            setting_currentDiagramCodeFilePath.WireTo(id_17609c775b9c4dfcb1f01d427d2911ae, "fanoutList"); /* {"SourceType":"DataFlowConnector","SourceIsReference":false,"DestinationType":"DataFlowConnector","DestinationIsReference":false} */
            id_17609c775b9c4dfcb1f01d427d2911ae.WireTo(id_2810e4e86da348b98b39c987e6ecd7b6, "fanoutList"); /* {"SourceType":"DataFlowConnector","SourceIsReference":false,"DestinationType":"FileReader","DestinationIsReference":false} */
            id_c9dbe185989e48c0869f984dd8e979f2.WireTo(id_17609c775b9c4dfcb1f01d427d2911ae, "dataOutput"); /* {"SourceType":"Data","SourceIsReference":false,"DestinationType":"DataFlowConnector","DestinationIsReference":false} */
            id_e778c13b2c894113a7aff7ecfffe48f7.WireTo(mainWindow, "output"); /* {"SourceType":"Apply","SourceIsReference":false,"DestinationType":"MainWindow","DestinationIsReference":false} */
            statusBarHorizontal.WireTo(id_e3837af93b584ca9874336851ff0cd31, "children"); /* {"SourceType":"Horizontal","SourceIsReference":false,"DestinationType":"UIConfig","DestinationIsReference":false} */
            id_66a3103c3adc426fbc8473b66a8b0d22.WireTo(globalVersionNumberDisplay, "children"); /* {"SourceType":"Horizontal","SourceIsReference":false,"DestinationType":"Text","DestinationIsReference":false} */
            id_42967d39c2334aab9c23697d04177f8a.WireTo(id_053e6b41724c4dcaad0b79b8924d647d, "children"); /* {"SourceType":"MenuBar","SourceIsReference":false,"DestinationType":"MenuItem","DestinationIsReference":false} */
            id_4577a8f0f63b4772bdc4eb4cb8581070.WireTo(id_97b81fc9cc04423192a12822a5a5a32e, "fileContentOutput"); /* {"SourceType":"FileReader","SourceIsReference":false,"DestinationType":"DataFlowConnector","DestinationIsReference":false} */
            id_97b81fc9cc04423192a12822a5a5a32e.WireTo(id_6bc94d5f257847ff8a9a9c45e02333b4, "fanoutList"); /* {"SourceType":"DataFlowConnector","SourceIsReference":false,"DestinationType":"ApplyAction","DestinationIsReference":false} */
            id_97b81fc9cc04423192a12822a5a5a32e.WireTo(id_cad49d55268145ab87788c650c6c5473, "fanoutList"); /* {"SourceType":"DataFlowConnector","SourceIsReference":false,"DestinationType":"CodeParser","DestinationIsReference":false} */
            id_6625f976171c480ebd8b750aeaf4fab1.WireTo(id_84cf83e5511c4bcb8f83ad289d20b08d, "output"); /* {"SourceType":"Cast","SourceIsReference":false,"DestinationType":"ForEach","DestinationIsReference":false} */
            id_20566090f5054429aebed4d371c2a613.WireTo(availableProgrammingParadigms, "complete"); /* {"SourceType":"ForEach","SourceIsReference":false,"DestinationType":"Collection","DestinationIsReference":false} */
            availableProgrammingParadigms.WireTo(id_16d8fb2a48ea4eef8839fc7aba053476, "listOutput"); /* {"SourceType":"Collection","SourceIsReference":false,"DestinationType":"ApplyAction","DestinationIsReference":false} */
            id_cad49d55268145ab87788c650c6c5473.WireTo(id_6625f976171c480ebd8b750aeaf4fab1, "interfaces"); /* {"SourceType":"CodeParser","SourceIsReference":false,"DestinationType":"Cast","DestinationIsReference":false} */
            id_20566090f5054429aebed4d371c2a613.WireTo(id_4577a8f0f63b4772bdc4eb4cb8581070, "elementOutput"); /* {"SourceType":"ForEach","SourceIsReference":false,"DestinationType":"FileReader","DestinationIsReference":false} */
            id_84cf83e5511c4bcb8f83ad289d20b08d.WireTo(id_d920e0f3fa2d4872af1ec6f3c058c233, "elementOutput"); /* {"SourceType":"ForEach","SourceIsReference":false,"DestinationType":"CodeParser","DestinationIsReference":false} */
            id_d920e0f3fa2d4872af1ec6f3c058c233.WireTo(availableProgrammingParadigms, "name"); /* {"SourceType":"CodeParser","SourceIsReference":false,"DestinationType":"Collection","DestinationIsReference":false} */
            id_8fc35564768b4a64a57dc321cc1f621f.WireTo(id_670ce4df65564e07912ef2ce63c38e11, "output"); /* {"SourceType":"Apply","SourceIsReference":false,"DestinationType":"DataFlowConnector","DestinationIsReference":false} */
            id_670ce4df65564e07912ef2ce63c38e11.WireTo(id_20566090f5054429aebed4d371c2a613, "fanoutList"); /* {"SourceType":"DataFlowConnector","SourceIsReference":false,"DestinationType":"ForEach","DestinationIsReference":false} */
            extractALACode.WireTo(startDiagramCreationProcess, "diagramSelected"); /* {"SourceType":"ExtractALACode","SourceIsReference":false,"DestinationType":"EventConnector","DestinationIsReference":false} */
            layoutDiagram.WireTo(id_9240933e26ea4cfdb07e6e7252bf7576, "complete"); /* {"SourceType":"RightTreeLayout","SourceIsReference":false,"DestinationType":"EventLambda","DestinationIsReference":false} */
            id_2155bd03579a4918b01e6912a0f24188.WireTo(id_afc4400ecf8b4f3e9aa1a57c346c80b2, "delayedEvent"); /* {"SourceType":"DispatcherEvent","SourceIsReference":false,"DestinationType":"EventLambda","DestinationIsReference":false} */
            id_35fceab68423425195096666f27475e9.WireTo(id_a98457fc05fc4e84bfb827f480db93d3, "fanoutList"); /* {"SourceType":"DataFlowConnector","SourceIsReference":false,"DestinationType":"Apply","DestinationIsReference":false} */
            id_670ce4df65564e07912ef2ce63c38e11.WireTo(id_f5d3730393ab40d78baebcb9198808da, "fanoutList"); /* {"SourceType":"DataFlowConnector","SourceIsReference":false,"DestinationType":"ForEach","DestinationIsReference":false} */
            id_db77c286e64241c48de4fad0dde80024.WireTo(id_2996cb469c4442d08b7e5ca2051336b1, "complete"); /* {"SourceType":"EventLambda","SourceIsReference":false,"DestinationType":"EventConnector","DestinationIsReference":false} */
            id_2996cb469c4442d08b7e5ca2051336b1.WireTo(id_846c10ca3cc14138bea1d681b146865a, "fanoutList"); /* {"SourceType":"EventConnector","SourceIsReference":false,"DestinationType":"Data","DestinationIsReference":false} */
            id_846c10ca3cc14138bea1d681b146865a.WireTo(currentDiagramName, "dataOutput"); /* {"SourceType":"Data","SourceIsReference":false,"DestinationType":"DataFlowConnector","DestinationIsReference":false} */
            id_2996cb469c4442d08b7e5ca2051336b1.WireTo(id_b6f2ab59cd0642afaf0fc124e6f9f055, "complete"); /* {"SourceType":"EventConnector","SourceIsReference":false,"DestinationType":"Data","DestinationIsReference":false} */
            id_b6f2ab59cd0642afaf0fc124e6f9f055.WireTo(id_17609c775b9c4dfcb1f01d427d2911ae, "inputDataB"); /* {"SourceType":"Data","SourceIsReference":false,"DestinationType":"DataFlowConnector","DestinationIsReference":false} */
            id_b6f2ab59cd0642afaf0fc124e6f9f055.WireTo(id_e778c13b2c894113a7aff7ecfffe48f7, "dataOutput"); /* {"SourceType":"Data","SourceIsReference":false,"DestinationType":"Apply","DestinationIsReference":false} */
            id_4aff82900db2498e8b46be4a18b9fa8e.WireTo(id_322828528d644ff883d8787c8fb63e56, "clickedEvent"); /* {"SourceType":"MenuItem","SourceIsReference":false,"DestinationType":"EventLambda","DestinationIsReference":false} */
            id_42967d39c2334aab9c23697d04177f8a.WireTo(UIConfig_debugMainMenuItem, "children"); /* {"SourceType":"MenuBar","SourceIsReference":false,"DestinationType":"UIConfig","DestinationIsReference":false} */
            UIConfig_debugMainMenuItem.WireTo(id_08d455bfa9744704b21570d06c3c5389, "child"); /* {"SourceType":"UIConfig","SourceIsReference":false,"DestinationType":"MenuItem","DestinationIsReference":false} */
            id_cc3adf40cb654337b01f77ade1881b44.WireTo(sidePanelHoriz, "isChecked"); /* {"SourceType":"CheckBox","SourceIsReference":false,"DestinationType":"Horizontal","DestinationIsReference":false} */
            id_cc3adf40cb654337b01f77ade1881b44.WireTo(id_a61fc923019942cea819e1b8d1b10384, "check"); /* {"SourceType":"CheckBox","SourceIsReference":false,"DestinationType":"EventConnector","DestinationIsReference":false} */
            id_9e6a74b0dbea488cba6027ee5187ad0f.WireTo(id_a61fc923019942cea819e1b8d1b10384, "delayedEvent"); /* {"SourceType":"DispatcherEvent","SourceIsReference":false,"DestinationType":"EventConnector","DestinationIsReference":false} */
            id_4a42bbf671cd4dba8987bd656e5a2ced.WireTo(id_09133302b430472dbe3cf9576d72bb3a, "children"); /* {"SourceType":"MenuItem","SourceIsReference":false,"DestinationType":"MenuItem","DestinationIsReference":false} */
            id_09133302b430472dbe3cf9576d72bb3a.WireTo(id_cc3adf40cb654337b01f77ade1881b44, "icon"); /* {"SourceType":"MenuItem","SourceIsReference":false,"DestinationType":"CheckBox","DestinationIsReference":false} */
            id_09133302b430472dbe3cf9576d72bb3a.WireTo(id_cc3adf40cb654337b01f77ade1881b44, "clickedEvent"); /* {"SourceType":"MenuItem","SourceIsReference":false,"DestinationType":"CheckBox","DestinationIsReference":false} */
            mainHorizontal.WireTo(UIConfig_canvasDisplayHoriz, "children"); /* {"SourceType":"Horizontal","SourceIsReference":false,"DestinationType":"UIConfig","DestinationIsReference":false} */
            UIConfig_canvasDisplayHoriz.WireTo(canvasDisplayHoriz, "child"); /* {"SourceType":"UIConfig","SourceIsReference":false,"DestinationType":"Horizontal","DestinationIsReference":false} */
            latestAddedNode.WireTo(id_8b99ce9b4c97466983fc1b14ef889ee8, "fanoutList"); /* {"SourceType":"DataFlowConnector","SourceIsReference":false,"DestinationType":"Cast","DestinationIsReference":false} */
            id_8b99ce9b4c97466983fc1b14ef889ee8.WireTo(id_fff8d82dbdd04da18793108f9b8dd5cf, "output"); /* {"SourceType":"Cast","SourceIsReference":false,"DestinationType":"DataFlowConnector","DestinationIsReference":false} */
            id_581015f073614919a33126efd44bf477.WireTo(id_024172dbe8e2496b97e191244e493973, "children"); /* {"SourceType":"ContextMenu","SourceIsReference":false,"DestinationType":"MenuItem","DestinationIsReference":false} */
            id_024172dbe8e2496b97e191244e493973.WireTo(id_7e64ef3262604943a2b4a086c5641d09, "clickedEvent"); /* {"SourceType":"MenuItem","SourceIsReference":false,"DestinationType":"Data","DestinationIsReference":false} */
            id_35947f28d1454366ad8ac16e08020905.WireTo(id_fff8d82dbdd04da18793108f9b8dd5cf, "conditionMetOutput"); /* {"SourceType":"ConditionalData","SourceIsReference":false,"DestinationType":"DataFlowConnector","DestinationIsReference":false} */
            id_7e64ef3262604943a2b4a086c5641d09.WireTo(id_35947f28d1454366ad8ac16e08020905, "dataOutput"); /* {"SourceType":"Data","SourceIsReference":false,"DestinationType":"ConditionalData","DestinationIsReference":false} */
            id_581015f073614919a33126efd44bf477.WireTo(id_269ffcfe56874f4ba0876a93071234ae, "children"); /* {"SourceType":"ContextMenu","SourceIsReference":false,"DestinationType":"MenuItem","DestinationIsReference":false} */
            id_269ffcfe56874f4ba0876a93071234ae.WireTo(id_40173af405c9467bbc85c79a05b9da48, "clickedEvent"); /* {"SourceType":"MenuItem","SourceIsReference":false,"DestinationType":"Data","DestinationIsReference":false} */
            id_40173af405c9467bbc85c79a05b9da48.WireTo(id_35947f28d1454366ad8ac16e08020905, "dataOutput"); /* {"SourceType":"Data","SourceIsReference":false,"DestinationType":"ConditionalData","DestinationIsReference":false} */
            id_cc0c82a2157f4b0291c812236a6e45ba.WireTo(id_72e0f3f39c364bedb36a74a011e08747, "children"); /* {"SourceType":"Vertical","SourceIsReference":false,"DestinationType":"UIConfig","DestinationIsReference":false} */
            id_cc0c82a2157f4b0291c812236a6e45ba.WireTo(id_25a53022f6ab4e9284fd321e9535801b, "children"); /* {"SourceType":"Vertical","SourceIsReference":false,"DestinationType":"UIConfig","DestinationIsReference":false} */
            id_72e0f3f39c364bedb36a74a011e08747.WireTo(id_0fd8aa1777474e3cafb81088519f3d97, "child"); /* {"SourceType":"UIConfig","SourceIsReference":false,"DestinationType":"Horizontal","DestinationIsReference":false} */
            id_57dc97beb4024bf294c44fea26cc5c89.WireTo(id_b6275330bff140168f4e68c87ed31b54, "content"); /* {"SourceType":"CheckBox","SourceIsReference":false,"DestinationType":"Text","DestinationIsReference":false} */
            id_0fd8aa1777474e3cafb81088519f3d97.WireTo(id_ecd9f881354d40f485c3fadd9f577974, "children"); /* {"SourceType":"Horizontal","SourceIsReference":false,"DestinationType":"UIConfig","DestinationIsReference":false} */
            id_ecd9f881354d40f485c3fadd9f577974.WireTo(id_889bfe8dee4d447d8ea45c19feaf5ca2, "child"); /* {"SourceType":"UIConfig","SourceIsReference":false,"DestinationType":"Text","DestinationIsReference":false} */
            id_cbdc03ac56ac4f179dd49e1312d7dca0.WireTo(id_abe0267c9c964e2194aa9c5bf84ac413, "child"); /* {"SourceType":"UIConfig","SourceIsReference":false,"DestinationType":"CheckBox","DestinationIsReference":false} */
            id_abe0267c9c964e2194aa9c5bf84ac413.WireTo(id_edcc6a4999a24fc2ae4b190c5619351c, "content"); /* {"SourceType":"CheckBox","SourceIsReference":false,"DestinationType":"Text","DestinationIsReference":false} */
            id_b868797a5ef6468abe35342f796a7376.WireTo(id_6dd83767dc324c1bb4e34beafaac11fe, "child"); /* {"SourceType":"UIConfig","SourceIsReference":false,"DestinationType":"CheckBox","DestinationIsReference":false} */
            id_c5fa777bee784429982813fd34ee9437.WireTo(id_7daf6ef76444402d9e9c6ed68f97a6c2, "child"); /* {"SourceType":"UIConfig","SourceIsReference":false,"DestinationType":"CheckBox","DestinationIsReference":false} */
            id_7daf6ef76444402d9e9c6ed68f97a6c2.WireTo(id_0e0c54964c4641d2958e710121d0429a, "content"); /* {"SourceType":"CheckBox","SourceIsReference":false,"DestinationType":"Text","DestinationIsReference":false} */
            id_6dd83767dc324c1bb4e34beafaac11fe.WireTo(id_39ae7418fea245fcaebd3a49b00d0683, "content"); /* {"SourceType":"CheckBox","SourceIsReference":false,"DestinationType":"Text","DestinationIsReference":false} */
            id_0fd8aa1777474e3cafb81088519f3d97.WireTo(id_b868797a5ef6468abe35342f796a7376, "children"); /* {"SourceType":"Horizontal","SourceIsReference":false,"DestinationType":"UIConfig","DestinationIsReference":false} */
            id_0fd8aa1777474e3cafb81088519f3d97.WireTo(id_c5fa777bee784429982813fd34ee9437, "children"); /* {"SourceType":"Horizontal","SourceIsReference":false,"DestinationType":"UIConfig","DestinationIsReference":false} */
            id_0fd8aa1777474e3cafb81088519f3d97.WireTo(id_48456b7bb4cf40769ea65b77f071a7f8, "children"); /* {"SourceType":"Horizontal","SourceIsReference":false,"DestinationType":"UIConfig","DestinationIsReference":false} */
            id_48456b7bb4cf40769ea65b77f071a7f8.WireTo(id_57dc97beb4024bf294c44fea26cc5c89, "child"); /* {"SourceType":"UIConfig","SourceIsReference":false,"DestinationType":"CheckBox","DestinationIsReference":false} */
            id_0fd8aa1777474e3cafb81088519f3d97.WireTo(id_cbdc03ac56ac4f179dd49e1312d7dca0, "children"); /* {"SourceType":"Horizontal","SourceIsReference":false,"DestinationType":"UIConfig","DestinationIsReference":false} */
            id_6dd83767dc324c1bb4e34beafaac11fe.WireTo(searchFilterNameChecked, "isChecked"); /* {"SourceType":"CheckBox","SourceIsReference":false,"DestinationType":"DataFlowConnector","DestinationIsReference":false} */
            id_7daf6ef76444402d9e9c6ed68f97a6c2.WireTo(searchFilterTypeChecked, "isChecked"); /* {"SourceType":"CheckBox","SourceIsReference":false,"DestinationType":"DataFlowConnector","DestinationIsReference":false} */
            id_57dc97beb4024bf294c44fea26cc5c89.WireTo(searchFilterInstanceNameChecked, "isChecked"); /* {"SourceType":"CheckBox","SourceIsReference":false,"DestinationType":"DataFlowConnector","DestinationIsReference":false} */
            id_abe0267c9c964e2194aa9c5bf84ac413.WireTo(searchFilterFieldsAndPropertiesChecked, "isChecked"); /* {"SourceType":"CheckBox","SourceIsReference":false,"DestinationType":"DataFlowConnector","DestinationIsReference":false} */
            UIConfig_mainCanvasDisplay.WireTo(mainCanvasDisplay, "child"); /* {"SourceType":"UIConfig","SourceIsReference":false,"DestinationType":"CanvasDisplay","DestinationIsReference":false} */
            canvasDisplayHoriz.WireTo(UIConfig_mainCanvasDisplay, "children"); /* {"SourceType":"Horizontal","SourceIsReference":false,"DestinationType":"UIConfig","DestinationIsReference":false} */
            mainCanvasDisplay.WireTo(id_dd7bf35a9a7c42059c340c211b761af9, "eventHandlers"); /* {"SourceType":"CanvasDisplay","SourceIsReference":false,"DestinationType":"DragEvent","DestinationIsReference":false} */
            id_dd7bf35a9a7c42059c340c211b761af9.WireTo(getDroppedFilePaths, "argsOutput"); /* {"SourceType":"DragEvent","SourceIsReference":false,"DestinationType":"Apply","DestinationIsReference":false} */
            id_efd2a2dc177542c587c73a55def6fe3c.WireTo(addAbstractionsToAllNodes, "fanoutList"); /* {"SourceType":"DataFlowConnector","SourceIsReference":false,"DestinationType":"Apply","DestinationIsReference":false} */
            getDroppedFilePaths.WireTo(id_efd2a2dc177542c587c73a55def6fe3c, "output"); /* {"SourceType":"Apply","SourceIsReference":false,"DestinationType":"DataFlowConnector","DestinationIsReference":false} */
            addAbstractionsToAllNodes.WireTo(id_3e341111f8224aa7b947f522ef1f65ab, "output"); /* {"SourceType":"Apply","SourceIsReference":false,"DestinationType":"Apply","DestinationIsReference":false} */
            id_3e341111f8224aa7b947f522ef1f65ab.WireTo(updateStatusMessage, "output"); /* {"SourceType":"Apply","SourceIsReference":false,"DestinationType":"ApplyAction","DestinationIsReference":false} */
            id_053e6b41724c4dcaad0b79b8924d647d.WireTo(id_0718ee88fded4b7b88258796df7db577, "clickedEvent"); /* {"SourceType":"MenuItem","SourceIsReference":false,"DestinationType":"EventConnector","DestinationIsReference":false} */
            id_0718ee88fded4b7b88258796df7db577.WireTo(id_c359484e1d7147a09d63c0671fa5f1dd, "fanoutList"); /* {"SourceType":"EventConnector","SourceIsReference":false,"DestinationType":"HttpRequest","DestinationIsReference":false} */
            id_c359484e1d7147a09d63c0671fa5f1dd.WireTo(id_db35acd5215c41849c685c49fba07a3d, "responseJsonOutput"); /* {"SourceType":"HttpRequest","SourceIsReference":false,"DestinationType":"JSONParser","DestinationIsReference":false} */
            compareVersionNumbers.WireTo(id_e33aaa2a4a5544a89931f05048e68406, "output"); /* {"SourceType":"Apply","SourceIsReference":false,"DestinationType":"IfElse","DestinationIsReference":false} */
            id_66a3103c3adc426fbc8473b66a8b0d22.WireTo(id_b47ca3c51c95416383ba250af31ee564, "children"); /* {"SourceType":"Horizontal","SourceIsReference":false,"DestinationType":"Text","DestinationIsReference":false} */
            id_66a3103c3adc426fbc8473b66a8b0d22.WireTo(id_07f10e1650504d298bdceddff2402f31, "children"); /* {"SourceType":"Horizontal","SourceIsReference":false,"DestinationType":"Text","DestinationIsReference":false} */
            id_5c857c3a1a474ec19c0c3b054627c0a9.WireTo(id_66a3103c3adc426fbc8473b66a8b0d22, "child"); /* {"SourceType":"UIConfig","SourceIsReference":false,"DestinationType":"Horizontal","DestinationIsReference":false} */
            statusBarHorizontal.WireTo(id_b1a5dcbe40654113b08efc4299c6fdc2, "children"); /* {"SourceType":"Horizontal","SourceIsReference":false,"DestinationType":"Text","DestinationIsReference":false} */
            statusBarHorizontal.WireTo(id_5c857c3a1a474ec19c0c3b054627c0a9, "children"); /* {"SourceType":"Horizontal","SourceIsReference":false,"DestinationType":"UIConfig","DestinationIsReference":false} */
            id_642ae4874d1e4fd2a777715cc1996b49.WireTo(id_ae21c0350891480babdcd1efcb247295, "fanoutList"); /* {"SourceType":"EventConnector","SourceIsReference":false,"DestinationType":"Clock","DestinationIsReference":false} */
            id_ae21c0350891480babdcd1efcb247295.WireTo(id_0718ee88fded4b7b88258796df7db577, "eventHappened"); /* {"SourceType":"Clock","SourceIsReference":false,"DestinationType":"EventConnector","DestinationIsReference":false} */
            id_a46f4ed8460e421b97525bd352b58d85.WireTo(id_34c59781fa2f4c5fb9102b7a65c461a0, "fanoutList"); /* {"SourceType":"EventConnector","SourceIsReference":false,"DestinationType":"Data","DestinationIsReference":false} */
            id_e33aaa2a4a5544a89931f05048e68406.WireTo(id_a46f4ed8460e421b97525bd352b58d85, "ifOutput"); /* {"SourceType":"IfElse","SourceIsReference":false,"DestinationType":"EventConnector","DestinationIsReference":false} */
            id_a46f4ed8460e421b97525bd352b58d85.WireTo(id_0e88688a360d451ab58c2fa25c9bf109, "fanoutList"); /* {"SourceType":"EventConnector","SourceIsReference":false,"DestinationType":"Data","DestinationIsReference":false} */
            id_e33aaa2a4a5544a89931f05048e68406.WireTo(id_57972aa4bbc24e46b4b6171637d31440, "elseOutput"); /* {"SourceType":"IfElse","SourceIsReference":false,"DestinationType":"EventConnector","DestinationIsReference":false} */
            id_57972aa4bbc24e46b4b6171637d31440.WireTo(id_0e88688a360d451ab58c2fa25c9bf109, "fanoutList"); /* {"SourceType":"EventConnector","SourceIsReference":false,"DestinationType":"Data","DestinationIsReference":false} */
            id_34c59781fa2f4c5fb9102b7a65c461a0.WireTo(id_b47ca3c51c95416383ba250af31ee564, "dataOutput"); /* {"SourceType":"Data","SourceIsReference":false,"DestinationType":"Text","DestinationIsReference":false} */
            id_0e88688a360d451ab58c2fa25c9bf109.WireTo(id_07f10e1650504d298bdceddff2402f31, "dataOutput"); /* {"SourceType":"Data","SourceIsReference":false,"DestinationType":"Text","DestinationIsReference":false} */
            id_76de2a3c1e5f4fbbbe8928be48e25847.WireTo(id_b47ca3c51c95416383ba250af31ee564, "dataOutput"); /* {"SourceType":"Data","SourceIsReference":false,"DestinationType":"Text","DestinationIsReference":false} */
            id_db35acd5215c41849c685c49fba07a3d.WireTo(latestVersion, "jsonOutput"); /* {"SourceType":"JSONParser","SourceIsReference":false,"DestinationType":"DataFlowConnector","DestinationIsReference":false} */
            latestVersion.WireTo(compareVersionNumbers, "fanoutList"); /* {"SourceType":"DataFlowConnector","SourceIsReference":false,"DestinationType":"Apply","DestinationIsReference":false} */
            id_57972aa4bbc24e46b4b6171637d31440.WireTo(id_76de2a3c1e5f4fbbbe8928be48e25847, "fanoutList"); /* {"SourceType":"EventConnector","SourceIsReference":false,"DestinationType":"Data","DestinationIsReference":false} */
            id_d59c0c09aeaf46c186317b9aeaf95e2e.WireTo(id_463b31fe2ac04972b5055a3ff2f74fe3, "clickedEvent"); /* {"SourceType":"MenuItem","SourceIsReference":false,"DestinationType":"FolderBrowser","DestinationIsReference":false} */
            id_cdeb94e2daee4057966eba31781ebd0d.WireTo(id_45968f4d70794b7c994c8e0f6ee5093a, "fanoutList"); /* {"SourceType":"EventConnector","SourceIsReference":false,"DestinationType":"EventLambda","DestinationIsReference":false} */
            id_42967d39c2334aab9c23697d04177f8a.WireTo(id_8ebb92deea4c4abf846371db834d9f87, "children"); /* {"SourceType":"MenuBar","SourceIsReference":false,"DestinationType":"MenuItem","DestinationIsReference":false} */
            id_42967d39c2334aab9c23697d04177f8a.WireTo(id_4aff82900db2498e8b46be4a18b9fa8e, "children"); /* {"SourceType":"MenuBar","SourceIsReference":false,"DestinationType":"MenuItem","DestinationIsReference":false} */
            id_8ebb92deea4c4abf846371db834d9f87.WireTo(id_835b587c7faf4fabbbe71010d28d9280, "clickedEvent"); /* {"SourceType":"MenuItem","SourceIsReference":false,"DestinationType":"EventLambda","DestinationIsReference":false} */
            id_ab1d0ec0d92f4befb1ff44bb72cc8e10.WireTo(id_3a7125ae5c814928a55c2d29e7e8c132, "child"); /* {"SourceType":"UIConfig","SourceIsReference":false,"DestinationType":"MenuItem","DestinationIsReference":false} */
            id_f8930a779bd44b0792fbd4a43b3874c6.WireTo(id_11418b009831455983cbc07c8d116a1f, "fanoutList"); /* {"SourceType":"EventConnector","SourceIsReference":false,"DestinationType":"CheckBox","DestinationIsReference":false} */
            id_87a535a0e11441af9072d6364a8aef74.WireTo(id_11418b009831455983cbc07c8d116a1f, "child"); /* {"SourceType":"UIConfig","SourceIsReference":false,"DestinationType":"CheckBox","DestinationIsReference":false} */
            id_3a7125ae5c814928a55c2d29e7e8c132.WireTo(id_f8930a779bd44b0792fbd4a43b3874c6, "clickedEvent"); /* {"SourceType":"MenuItem","SourceIsReference":false,"DestinationType":"EventConnector","DestinationIsReference":false} */
            id_f8930a779bd44b0792fbd4a43b3874c6.WireTo(startRightTreeLayoutProcess, "fanoutList"); /* {"SourceType":"EventConnector","SourceIsReference":false,"DestinationType":"EventConnector","DestinationIsReference":false} */
            id_943e3971561d493d97e38a8e29fb87dc.WireTo(id_954c2d01269c4632a4ddccd75cde9fde, "icon"); /* {"SourceType":"MenuItem","SourceIsReference":false,"DestinationType":"CheckBox","DestinationIsReference":false} */
            id_943e3971561d493d97e38a8e29fb87dc.WireTo(id_cd6186e0fe844be586191519012bb72e, "clickedEvent"); /* {"SourceType":"MenuItem","SourceIsReference":false,"DestinationType":"EventConnector","DestinationIsReference":false} */
            id_cd6186e0fe844be586191519012bb72e.WireTo(id_954c2d01269c4632a4ddccd75cde9fde, "fanoutList"); /* {"SourceType":"EventConnector","SourceIsReference":false,"DestinationType":"CheckBox","DestinationIsReference":false} */
            startRightTreeLayoutProcess.WireTo(id_0f0046b6b91e447aa9bf0a223fd59038, "fanoutList"); /* {"SourceType":"EventConnector","SourceIsReference":false,"DestinationType":"Data","DestinationIsReference":false} */
            startGuaranteedLayoutProcess.WireTo(id_4a268943755348b68ee2cb6b71f73c40, "fanoutList"); /* {"SourceType":"EventConnector","SourceIsReference":false,"DestinationType":"DispatcherEvent","DestinationIsReference":false} */
            startRightTreeLayoutProcess.WireTo(id_1e62a1e411c9464c94ee234dd9dd3fdc, "complete"); /* {"SourceType":"EventConnector","SourceIsReference":false,"DestinationType":"EventLambda","DestinationIsReference":false} */
            id_edd3648585f44954b2df337f1b7a793b.WireTo(startGuaranteedLayoutProcess, "ifOutput"); /* {"SourceType":"IfElse","SourceIsReference":false,"DestinationType":"EventConnector","DestinationIsReference":false} */
            id_4a268943755348b68ee2cb6b71f73c40.WireTo(initialiseRightTreeLayout, "delayedEvent"); /* {"SourceType":"DispatcherEvent","SourceIsReference":false,"DestinationType":"EventLambda","DestinationIsReference":false} */
            id_4a42bbf671cd4dba8987bd656e5a2ced.WireTo(id_50349b82433f42ebb9d1ce591fc3bc35, "children"); /* {"SourceType":"MenuItem","SourceIsReference":false,"DestinationType":"UIConfig","DestinationIsReference":false} */
            id_50349b82433f42ebb9d1ce591fc3bc35.WireTo(id_943e3971561d493d97e38a8e29fb87dc, "child"); /* {"SourceType":"UIConfig","SourceIsReference":false,"DestinationType":"MenuItem","DestinationIsReference":false} */
            id_f9b8e7f524a14884be753d19a351a285.WireTo(id_27ff7a25d9034a45a229edef6610e214, "fanoutList"); /* {"SourceType":"EventConnector","SourceIsReference":false,"DestinationType":"Data","DestinationIsReference":false} */
            id_27ff7a25d9034a45a229edef6610e214.WireTo(id_d5c22176b9bb49dd91a1cb0a7e3f7196, "dataOutput"); /* {"SourceType":"Data","SourceIsReference":false,"DestinationType":"DataFlowConnector","DestinationIsReference":false} */
            id_954c2d01269c4632a4ddccd75cde9fde.WireTo(useAutomaticLayout, "isChecked"); /* {"SourceType":"CheckBox","SourceIsReference":false,"DestinationType":"DataFlowConnector","DestinationIsReference":false} */
            id_3a7125ae5c814928a55c2d29e7e8c132.WireTo(id_87a535a0e11441af9072d6364a8aef74, "icon"); /* {"SourceType":"MenuItem","SourceIsReference":false,"DestinationType":"UIConfig","DestinationIsReference":false} */
            id_11418b009831455983cbc07c8d116a1f.WireTo(id_ce0bcc39dd764d1087816b79eefa76bf, "isChecked"); /* {"SourceType":"CheckBox","SourceIsReference":false,"DestinationType":"ApplyAction","DestinationIsReference":false} */
            id_0f0046b6b91e447aa9bf0a223fd59038.WireTo(id_edd3648585f44954b2df337f1b7a793b, "dataOutput"); /* {"SourceType":"Data","SourceIsReference":false,"DestinationType":"IfElse","DestinationIsReference":false} */
            id_0f0046b6b91e447aa9bf0a223fd59038.WireTo(useAutomaticLayout, "inputDataB"); /* {"SourceType":"Data","SourceIsReference":false,"DestinationType":"DataFlowConnector","DestinationIsReference":false} */
            initialiseRightTreeLayout.WireTo(id_7356212bcc714c699681e8dffc853761, "complete"); /* {"SourceType":"EventLambda","SourceIsReference":false,"DestinationType":"EventConnector","DestinationIsReference":false} */
            getTreeParentsFromGraph.WireTo(id_ec0f30ce468d4986abb9ad81abe73c17, "dataOutput"); /* {"SourceType":"Data","SourceIsReference":false,"DestinationType":"ApplyAction","DestinationIsReference":false} */
            id_4a42bbf671cd4dba8987bd656e5a2ced.WireTo(id_ab1d0ec0d92f4befb1ff44bb72cc8e10, "children"); /* {"SourceType":"MenuItem","SourceIsReference":false,"DestinationType":"UIConfig","DestinationIsReference":false} */
            mainCanvasDisplay.WireTo(id_dd81a5ed9ff0413facf64a3ea65c2cf5, "eventHandlers"); /* {"SourceType":"CanvasDisplay","SourceIsReference":false,"DestinationType":"KeyEvent","DestinationIsReference":false} */
            id_dd81a5ed9ff0413facf64a3ea65c2cf5.WireTo(id_3c565e37c3c1486e91007c4d1d284367, "argsOutput"); /* {"SourceType":"KeyEvent","SourceIsReference":false,"DestinationType":"ApplyAction","DestinationIsReference":false} */
            mainCanvasDisplay.WireTo(id_cd5a0b075b9b47a4a371bc51c7f0aca3, "eventHandlers"); /* {"SourceType":"CanvasDisplay","SourceIsReference":false,"DestinationType":"KeyEvent","DestinationIsReference":false} */
            id_cd5a0b075b9b47a4a371bc51c7f0aca3.WireTo(id_29a954d80a1a43ca8739e70022ebf3ec, "argsOutput"); /* {"SourceType":"KeyEvent","SourceIsReference":false,"DestinationType":"ApplyAction","DestinationIsReference":false} */
            id_9240933e26ea4cfdb07e6e7252bf7576.WireTo(id_2155bd03579a4918b01e6912a0f24188, "complete"); /* {"SourceType":"EventLambda","SourceIsReference":false,"DestinationType":"DispatcherEvent","DestinationIsReference":false} */
            id_42967d39c2334aab9c23697d04177f8a.WireTo(id_68ab46b356b64dbfb61d305ea9eced6f, "children"); /* {"SourceType":"MenuBar","SourceIsReference":false,"DestinationType":"MenuItem","DestinationIsReference":false} */
            id_8eb5d9903d6941d285da2fc3d2ccfc3a.WireTo(id_7c21cf85883041b88e998ecc065cc4d4, "child"); /* {"SourceType":"UIConfig","SourceIsReference":false,"DestinationType":"MenuItem","DestinationIsReference":false} */
            id_68ab46b356b64dbfb61d305ea9eced6f.WireTo(id_8eb5d9903d6941d285da2fc3d2ccfc3a, "children"); /* {"SourceType":"MenuItem","SourceIsReference":false,"DestinationType":"UIConfig","DestinationIsReference":false} */
            id_08d455bfa9744704b21570d06c3c5389.WireTo(id_180fa624d01c4759a83050e30426343a, "children"); /* {"SourceType":"MenuItem","SourceIsReference":false,"DestinationType":"MenuItem","DestinationIsReference":false} */
            id_514f6109e8a24bc4b1ced57aaa255d90.WireTo(id_df9b787cea7845f88e1faf65240adb4f, "fanoutList"); /* {"SourceType":"EventConnector","SourceIsReference":false,"DestinationType":"Data","DestinationIsReference":false} */
            id_514f6109e8a24bc4b1ced57aaa255d90.WireTo(id_5aec7a9782644198ab22d9ed7998ee15, "fanoutList"); /* {"SourceType":"EventConnector","SourceIsReference":false,"DestinationType":"PopupWindow","DestinationIsReference":false} */
            id_1c8c1eff6c1042cdb09364f0d4e80cf5.WireTo(id_23e510bd08224b64b10c378f0f8fcdfe, "child"); /* {"SourceType":"UIConfig","SourceIsReference":false,"DestinationType":"TextBox","DestinationIsReference":false} */
            id_7c21cf85883041b88e998ecc065cc4d4.WireTo(id_514f6109e8a24bc4b1ced57aaa255d90, "clickedEvent"); /* {"SourceType":"MenuItem","SourceIsReference":false,"DestinationType":"EventConnector","DestinationIsReference":false} */
            id_180fa624d01c4759a83050e30426343a.WireTo(id_514f6109e8a24bc4b1ced57aaa255d90, "clickedEvent"); /* {"SourceType":"MenuItem","SourceIsReference":false,"DestinationType":"EventConnector","DestinationIsReference":false} */
            createInstanceDictionaryCode.WireTo(id_23e510bd08224b64b10c378f0f8fcdfe, "output"); /* {"SourceType":"Apply","SourceIsReference":false,"DestinationType":"TextBox","DestinationIsReference":false} */
            id_5b1aec35b5fd47e482a25168390fcd66.WireTo(id_65e62fc671b1436191ccdc2a2e8c8af8, "child"); /* {"SourceType":"UIConfig","SourceIsReference":false,"DestinationType":"Horizontal","DestinationIsReference":false} */
            id_a1b1ae6b9ca64970b5b8988be0b5dda7.WireTo(id_5b1aec35b5fd47e482a25168390fcd66, "children"); /* {"SourceType":"Vertical","SourceIsReference":false,"DestinationType":"UIConfig","DestinationIsReference":false} */
            id_a1b1ae6b9ca64970b5b8988be0b5dda7.WireTo(id_1c8c1eff6c1042cdb09364f0d4e80cf5, "children"); /* {"SourceType":"Vertical","SourceIsReference":false,"DestinationType":"UIConfig","DestinationIsReference":false} */
            id_df9b787cea7845f88e1faf65240adb4f.WireTo(createInstanceDictionaryCode, "dataOutput"); /* {"SourceType":"Data","SourceIsReference":false,"DestinationType":"Apply","DestinationIsReference":false} */
            id_5aec7a9782644198ab22d9ed7998ee15.WireTo(id_a1b1ae6b9ca64970b5b8988be0b5dda7, "children"); /* {"SourceType":"PopupWindow","SourceIsReference":false,"DestinationType":"Vertical","DestinationIsReference":false} */
            id_65e62fc671b1436191ccdc2a2e8c8af8.WireTo(id_ca4344b0f1334536b8ba52fda7567809, "children"); /* {"SourceType":"Horizontal","SourceIsReference":false,"DestinationType":"UIConfig","DestinationIsReference":false} */
            id_65e62fc671b1436191ccdc2a2e8c8af8.WireTo(id_e4615109bbba480cb0f7c11cc493cd84, "children"); /* {"SourceType":"Horizontal","SourceIsReference":false,"DestinationType":"UIConfig","DestinationIsReference":false} */
            id_ca4344b0f1334536b8ba52fda7567809.WireTo(id_740a947e8deb4a26868e4858d59387de, "child"); /* {"SourceType":"UIConfig","SourceIsReference":false,"DestinationType":"Text","DestinationIsReference":false} */
            id_e4615109bbba480cb0f7c11cc493cd84.WireTo(id_a1163328ed694682ad454ff0f88e4dfe, "child"); /* {"SourceType":"UIConfig","SourceIsReference":false,"DestinationType":"TextBox","DestinationIsReference":false} */
            id_a1163328ed694682ad454ff0f88e4dfe.WireTo(id_e7a7ac196c52416aa49fc77fe0503251, "textOutput"); /* {"SourceType":"TextBox","SourceIsReference":false,"DestinationType":"DataFlowConnector","DestinationIsReference":false} */
            id_65e62fc671b1436191ccdc2a2e8c8af8.WireTo(id_28f139af6d3941658d65e5c08a79006d, "children"); /* {"SourceType":"Horizontal","SourceIsReference":false,"DestinationType":"UIConfig","DestinationIsReference":false} */
            id_28f139af6d3941658d65e5c08a79006d.WireTo(id_a96a45b9b88648ebbf6ea3d24f036269, "child"); /* {"SourceType":"UIConfig","SourceIsReference":false,"DestinationType":"Button","DestinationIsReference":false} */
            id_a96a45b9b88648ebbf6ea3d24f036269.WireTo(id_b8f48b755a8545fcb626463d325ffe03, "eventButtonClicked"); /* {"SourceType":"Button","SourceIsReference":false,"DestinationType":"Data","DestinationIsReference":false} */
            id_b8f48b755a8545fcb626463d325ffe03.WireTo(id_e7a7ac196c52416aa49fc77fe0503251, "inputDataB"); /* {"SourceType":"Data","SourceIsReference":false,"DestinationType":"DataFlowConnector","DestinationIsReference":false} */
            id_b8f48b755a8545fcb626463d325ffe03.WireTo(createInstanceDictionaryCode, "dataOutput"); /* {"SourceType":"Data","SourceIsReference":false,"DestinationType":"Apply","DestinationIsReference":false} */
            id_a1163328ed694682ad454ff0f88e4dfe.WireTo(id_b8f48b755a8545fcb626463d325ffe03, "eventEnterPressed"); /* {"SourceType":"TextBox","SourceIsReference":false,"DestinationType":"Data","DestinationIsReference":false} */
            id_94be5f8fa9014fad81fa832cdfb41c27.WireTo(id_61311ea1bf8d405db0411618a8e11114, "fanoutList"); /* {"SourceType":"EventConnector","SourceIsReference":false,"DestinationType":"Data","DestinationIsReference":false} */
            id_61311ea1bf8d405db0411618a8e11114.WireTo(id_831cf2bc59df431e9171a3887608cfae, "dataOutput"); /* {"SourceType":"Data","SourceIsReference":false,"DestinationType":"ApplyAction","DestinationIsReference":false} */
            id_e3a05ca012df4e428f19f313109a576e.WireTo(id_b8876ba6078448999ae1746d34ce803e, "fanoutList"); /* {"SourceType":"EventConnector","SourceIsReference":false,"DestinationType":"Data","DestinationIsReference":false} */
            id_b8876ba6078448999ae1746d34ce803e.WireTo(id_cc2aa50e0aef463ca17350d36436f98d, "dataOutput"); /* {"SourceType":"Data","SourceIsReference":false,"DestinationType":"ApplyAction","DestinationIsReference":false} */
            id_dd81a5ed9ff0413facf64a3ea65c2cf5.WireTo(id_94be5f8fa9014fad81fa832cdfb41c27, "eventHappened"); /* {"SourceType":"KeyEvent","SourceIsReference":false,"DestinationType":"EventConnector","DestinationIsReference":false} */
            id_94be5f8fa9014fad81fa832cdfb41c27.WireTo(id_6377d8cb849a4a07b02d50789eab57a1, "fanoutList"); /* {"SourceType":"EventConnector","SourceIsReference":false,"DestinationType":"DispatcherEvent","DestinationIsReference":false} */
            id_6377d8cb849a4a07b02d50789eab57a1.WireTo(startGuaranteedLayoutProcess, "delayedEvent"); /* {"SourceType":"DispatcherEvent","SourceIsReference":false,"DestinationType":"EventConnector","DestinationIsReference":false} */
            id_cd5a0b075b9b47a4a371bc51c7f0aca3.WireTo(id_e3a05ca012df4e428f19f313109a576e, "eventHappened"); /* {"SourceType":"KeyEvent","SourceIsReference":false,"DestinationType":"EventConnector","DestinationIsReference":false} */
            id_e3a05ca012df4e428f19f313109a576e.WireTo(id_6306c5f7aa3d41978599c00a5999b96f, "fanoutList"); /* {"SourceType":"EventConnector","SourceIsReference":false,"DestinationType":"DispatcherEvent","DestinationIsReference":false} */
            id_6306c5f7aa3d41978599c00a5999b96f.WireTo(startGuaranteedLayoutProcess, "delayedEvent"); /* {"SourceType":"DispatcherEvent","SourceIsReference":false,"DestinationType":"EventConnector","DestinationIsReference":false} */
            id_33d648af590b45139339fe533079ab12.WireTo(id_3605f8d8e4624d84befb96fe76ebd3ac, "eventOutput"); /* {"SourceType":"ConvertToEvent","SourceIsReference":false,"DestinationType":"EventLambda","DestinationIsReference":false} */
            id_f19494c1e76f460a9189c172ac98de60.WireTo(id_6e909cf4d2004e078eacacf80f1f2bff, "children"); /* {"SourceType":"MenuItem","SourceIsReference":false,"DestinationType":"MultiMenu","DestinationIsReference":false} */
            id_460891130e9e499184b84a23c2e43c9f.WireTo(id_e2c110ecff0740989d3d30144f84a94b, "output"); /* {"SourceType":"Cast","SourceIsReference":false,"DestinationType":"DataFlowConnector","DestinationIsReference":false} */
            id_408df459fb4c4846920b1a1edd4ac9e6.WireTo(id_6ecefc4cdc694ef2a46a8628cadc0e1d, "fanoutList"); /* {"SourceType":"EventConnector","SourceIsReference":false,"DestinationType":"GetSetting","DestinationIsReference":false} */
            id_6ecefc4cdc694ef2a46a8628cadc0e1d.WireTo(id_097392c5af294d32b5c928a590bad83b, "settingJsonOutput"); /* {"SourceType":"GetSetting","SourceIsReference":false,"DestinationType":"Apply","DestinationIsReference":false} */
            id_097392c5af294d32b5c928a590bad83b.WireTo(recentProjectPaths, "output"); /* {"SourceType":"Apply","SourceIsReference":false,"DestinationType":"DataFlowConnector","DestinationIsReference":false} */
            id_2b3a750d477d4e168aaa3ed0ae548650.WireTo(id_408df459fb4c4846920b1a1edd4ac9e6, "eventOutput"); /* {"SourceType":"ConvertToEvent","SourceIsReference":false,"DestinationType":"EventConnector","DestinationIsReference":false} */
            id_408df459fb4c4846920b1a1edd4ac9e6.WireTo(id_e045b91666df454ca2f7985443af56c5, "fanoutList"); /* {"SourceType":"EventConnector","SourceIsReference":false,"DestinationType":"Data","DestinationIsReference":false} */
            id_e045b91666df454ca2f7985443af56c5.WireTo(id_e2c110ecff0740989d3d30144f84a94b, "inputDataB"); /* {"SourceType":"Data","SourceIsReference":false,"DestinationType":"DataFlowConnector","DestinationIsReference":false} */
            id_cb85f096416943cb9c08e4862f304568.WireTo(id_ef711f01535e48e2b65274af24d732f6, "output"); /* {"SourceType":"Cast","SourceIsReference":false,"DestinationType":"Apply","DestinationIsReference":false} */
            id_4ad460d4bd8d4a63ad7aca7ed9f1c945.WireTo(id_6c8e7b486e894c6ca6bebaf40775b8b4, "fanoutList"); /* {"SourceType":"DataFlowConnector","SourceIsReference":false,"DestinationType":"EditSetting","DestinationIsReference":false} */
            id_4ad460d4bd8d4a63ad7aca7ed9f1c945.WireTo(id_5d9313a0a895402cb6be531e87c9b606, "fanoutList"); /* {"SourceType":"DataFlowConnector","SourceIsReference":false,"DestinationType":"Apply","DestinationIsReference":false} */
            id_6c8e7b486e894c6ca6bebaf40775b8b4.WireTo(id_ecfbf0b7599e4340b8b2f79b7d1e29cb, "filePathInput"); /* {"SourceType":"EditSetting","SourceIsReference":false,"DestinationType":"Data","DestinationIsReference":false} */
            id_e045b91666df454ca2f7985443af56c5.WireTo(id_cb85f096416943cb9c08e4862f304568, "dataOutput"); /* {"SourceType":"Data","SourceIsReference":false,"DestinationType":"Cast","DestinationIsReference":false} */
            id_ef711f01535e48e2b65274af24d732f6.WireTo(id_4ad460d4bd8d4a63ad7aca7ed9f1c945, "output"); /* {"SourceType":"Apply","SourceIsReference":false,"DestinationType":"DataFlowConnector","DestinationIsReference":false} */
            id_5d9313a0a895402cb6be531e87c9b606.WireTo(id_6e909cf4d2004e078eacacf80f1f2bff, "output"); /* {"SourceType":"Apply","SourceIsReference":false,"DestinationType":"MultiMenu","DestinationIsReference":false} */
            id_6ecefc4cdc694ef2a46a8628cadc0e1d.WireTo(id_fcfcb5f0ae544c968dcbc734ac1db51b, "filePathInput"); /* {"SourceType":"GetSetting","SourceIsReference":false,"DestinationType":"Data","DestinationIsReference":false} */
            id_6e909cf4d2004e078eacacf80f1f2bff.WireTo(id_a1f87102954345b69de6841053fce813, "selectedLabel"); /* {"SourceType":"MultiMenu","SourceIsReference":false,"DestinationType":"DataFlowConnector","DestinationIsReference":false} */
            id_e372e7c636a14549bba7cb5992874716.WireTo(id_d386225d5368436185ff7e18a6dfd91a, "child"); /* {"SourceType":"UIConfig","SourceIsReference":false,"DestinationType":"MenuItem","DestinationIsReference":false} */
            id_d386225d5368436185ff7e18a6dfd91a.WireTo(id_355e5bd4d98745b2a42eb1266198128b, "clickedEvent"); /* {"SourceType":"MenuItem","SourceIsReference":false,"DestinationType":"TextClipboard","DestinationIsReference":false} */
            id_355e5bd4d98745b2a42eb1266198128b.WireTo(id_ceae580b14444b1e82c23813f47a47cd, "contentOutput"); /* {"SourceType":"TextClipboard","SourceIsReference":false,"DestinationType":"Apply","DestinationIsReference":false} */
            id_6180563898dc46da87f68e3da6bc7aa8.WireTo(pasteDiagramFromCode, "fanoutList"); /* {"SourceType":"DataFlowConnector","SourceIsReference":false,"DestinationType":"CreateDiagramFromCode","DestinationIsReference":false} */
            id_ceae580b14444b1e82c23813f47a47cd.WireTo(id_6180563898dc46da87f68e3da6bc7aa8, "output"); /* {"SourceType":"Apply","SourceIsReference":false,"DestinationType":"DataFlowConnector","DestinationIsReference":false} */
            id_6180563898dc46da87f68e3da6bc7aa8.WireTo(id_6bc55844fa8f41db9a95118685504fd1, "fanoutList"); /* {"SourceType":"DataFlowConnector","SourceIsReference":false,"DestinationType":"ConvertToEvent","DestinationIsReference":false} */
            id_6bc55844fa8f41db9a95118685504fd1.WireTo(startRightTreeLayoutProcess, "eventOutput"); /* {"SourceType":"ConvertToEvent","SourceIsReference":false,"DestinationType":"EventConnector","DestinationIsReference":false} */
            id_581015f073614919a33126efd44bf477.WireTo(id_e372e7c636a14549bba7cb5992874716, "children"); /* {"SourceType":"ContextMenu","SourceIsReference":false,"DestinationType":"UIConfig","DestinationIsReference":false} */
            // END AUTO-GENERATED WIRING FOR GALADE_Standalone

            _mainWindow = mainWindow;

            // BEGIN MANUAL INSTANTIATIONS
            // END MANUAL INSTANTIATIONS

            // BEGIN MANUAL WIRING
            // END MANUAL WIRING

        }

        private Application()
        {
            CreateWiring();
        }
    }
}