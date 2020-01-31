using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using System.Text;
using System;
using VRage.Collections;
using VRage.Game.Components;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ObjectBuilders.Definitions;
using VRage.Game;
using VRage;
using VRageMath;

namespace IngameScript
{
    partial class Program : MyGridProgram
    {
        #region Waypoint
        public struct Waypoint
        {
            public Vector3 Position;
            public Vector3 Direction;
            public float MaxSpeed;
            public string Name;
            public string ReferenceMode;
        }

        static string SerializeWaypoint(Waypoint w)
        {
            return $"{w.Position.ToString()}|{w.Direction.ToString()}|{w.MaxSpeed.ToString()}|{w.Name}|{w.ReferenceMode}";
        }

        Waypoint DeserializeWaypoint(string s)
        {
            string[] split = s.Split('|');
            Waypoint w = new Waypoint();
            w.Position = StringToVector3(split[0]);
            w.Direction = StringToVector3(split[1]);
            w.MaxSpeed = float.Parse(split[2]);
            w.Name = split[3];
            w.ReferenceMode = split[4];
            return w;
        }

        public static Vector3 StringToVector3(string sVector)
        {
            sVector = sVector.Substring(1, sVector.Length - 2);
            string[] sArray = sVector.Split(' ');
            Vector3 result = new Vector3(
                float.Parse(sArray[0].Substring(2)),
                float.Parse(sArray[1].Substring(2)),
                float.Parse(sArray[2].Substring(2)));
            return result;
        }
        #endregion

        #region HangarManager
        class Hangar
        {
            public enum HangarState
            {
                Extended,
                Extending,
                Retracted,
                Retracting,
                Calibrate
            }

            private IMyShipConnector connector;
            private IMyPistonBase extender;
            private IMyMotorAdvancedStator rotor;
            private List<IMyAirtightHangarDoor> gates = new List<IMyAirtightHangarDoor>();
            private List<IMyInteriorLight> lights = new List<IMyInteriorLight>();
            private IMyTextPanel display;
            StringBuilder statusBuilder = new StringBuilder();

            private const float kRotorSpeedMulti = 0.6f;
            private const double kAngleOffset = Math.PI / 2;

            public HangarState state = HangarState.Retracted;

            public void AddPart(IMyTerminalBlock part)
            {
                if (part is IMyShipConnector) connector = (IMyShipConnector)part;
                if (part is IMyPistonBase) extender = (IMyPistonBase)part;
                if (part is IMyMotorAdvancedStator) rotor = (IMyMotorAdvancedStator)part;
                if (part is IMyAirtightHangarDoor) gates.Add((IMyAirtightHangarDoor)part);
                if (part is IMyTextPanel) display = (IMyTextPanel)part;
                if (part is IMyInteriorLight)
                {
                    IMyInteriorLight light = (IMyInteriorLight)part;
                    lights.Add(light);
                    light.Intensity = 2f;
                    light.Radius = 12f;
                }
            }

            public string GetStatus()
            {
                statusBuilder.Clear();

                statusBuilder.Append("CON: ");
                statusBuilder.Append(connector != null ? "AOK" : "LST");
                statusBuilder.Append(" | EXT: ");
                statusBuilder.Append(extender != null ? "AOK" : "LST");
                statusBuilder.Append(" | ROT: ");
                statusBuilder.Append(rotor != null ? "AOK" : "LST");
                statusBuilder.Append(" | GTS: ");
                statusBuilder.Append(gates.Count.ToString());

                statusBuilder.AppendLine();

                statusBuilder.AppendLine($"State: {state.ToString()}");

                statusBuilder.AppendLine($"Rotor: {rotor.Angle}");
                statusBuilder.AppendLine($"Target: {getWorldBoxTargetAngle()}");

                /*
                if (connector.Status == MyShipConnectorStatus.Connected)
                {
                    var dockedShipBoundingBox = connector.OtherConnector.CubeGrid.WorldAABB;
                    var dockedShipCenterDirection = dockedShipBoundingBox.Center - rotor.Top.WorldMatrix.Translation;
                    var myDir = Vector3D.TransformNormal(dockedShipCenterDirection, MatrixD.Transpose(rotor.Top.WorldMatrix));
                    myDir.Y = 0;
                    myDir.Normalize();
                    statusBuilder.AppendLine($"Debug: {myDir.X}");
                    statusBuilder.AppendLine($"Debug: {myDir.Z}");

                    statusBuilder.AppendLine();

                    var dirVector = new Vector3D(1, 0, 0);
                    var worldDirVector = Vector3D.TransformNormal(dirVector, connector.OtherConnector.WorldMatrix);
                    myDir = Vector3D.TransformNormal(worldDirVector, MatrixD.Transpose(rotor.Top.WorldMatrix));
                    statusBuilder.AppendLine($"Debug: {myDir.X}");
                    statusBuilder.AppendLine($"Debug: {myDir.Z}");
                    statusBuilder.AppendLine($"Target 2: {getWorldBoxTargetAngle()}");
                }*/

                return statusBuilder.ToString();
            }

            public void SetCommand(string command)
            {
                connector.CustomData = command;
            }

            public void Update()
            {
                try
                {
                    string input = connector.CustomData;
                    connector.CustomData = string.Empty;

                    if (state == HangarState.Retracted)
                    {
                        for (int i = 0; i < lights.Count; i++)
                            lights[i].Color = Color.White;
                    }
                    else
                    {
                        for (int i = 0; i < lights.Count; i++)
                            lights[i].Color = Color.Red;
                    }

                    if (state == HangarState.Extending)
                    {
                        if (input.Equals("Retract"))
                            state = HangarState.Retracting;

                        bool gatesOK = true;
                        bool pistonOK = false;
                        for (int i = 0; i < gates.Count; i++)
                        {
                            if (gates[i].Status != DoorStatus.Open)
                            {
                                gatesOK = false;
                                gates[i].OpenDoor();
                            }
                        }

                        if (gatesOK)
                        {
                            extender.Velocity = 1;
                            if (extender.CurrentPosition == extender.MaxLimit)
                                pistonOK = true;
                        }

                        if (pistonOK)
                            state = HangarState.Extended;
                    }
                    else if (state == HangarState.Retracting)
                    {
                        if (input.Equals("Extend"))
                            state = HangarState.Extending;

                        bool rotorOK = false;
                        bool pistonOK = false;
                        bool gatesOK = false;

                        double targetAngle = getWorldBoxTargetAngle();
                        rotorOK = connector.Status != MyShipConnectorStatus.Connected || Math.Abs(AngleSubtract(rotor.Angle, targetAngle)) < 0.001;

                        if (!rotorOK) MoveRotorTowards(targetAngle);

                        if (rotorOK)
                        {
                            rotor.TargetVelocityRad = 0;
                            extender.Velocity = -1;
                            if (extender.CurrentPosition == extender.MinLimit)
                                pistonOK = true;
                        }

                        if (pistonOK)
                        {
                            gatesOK = true;
                            for (int i = 0; i < gates.Count; i++)
                            {
                                if (gates[i].Status != DoorStatus.Closed)
                                {
                                    gatesOK = false;
                                    gates[i].CloseDoor();
                                }
                            }
                        }

                        if (gatesOK)
                            state = HangarState.Retracted;
                    }
                    else if (state == HangarState.Calibrate)
                    {
                        bool rotorOK = false;

                        double targetAngle = getWorldBoxTargetAngle();
                        rotorOK = connector.Status != MyShipConnectorStatus.Connected || Math.Abs(AngleSubtract(rotor.Angle, targetAngle)) < 0.001;

                        if (!rotorOK) MoveRotorTowards(targetAngle);

                        if (rotorOK)
                        {
                            rotor.TargetVelocityRad = 0;
                            state = HangarState.Extended;
                        }
                    }
                    else if (state == HangarState.Extended)
                    {
                        if (input.Equals("Retract"))
                            state = HangarState.Retracting;
                        if (input.Equals("Calibrate"))
                            state = HangarState.Calibrate;
                        if (input.Equals("Launch") && connector.Status == MyShipConnectorStatus.Connected)
                            connector.OtherConnector.CustomData = "Launch";
                    }
                    else if (state == HangarState.Retracted)
                    {
                        var angle = getWorldBoxTargetAngle();
                        if (Math.Abs(AngleSubtract(rotor.Angle, angle)) > 0.001)
                            MoveRotorTowards(angle);
                        if (input.Equals("Extend"))
                            state = HangarState.Extending;
                    }
                }
                catch
                {

                }

                display.ClearImagesFromSelection();
                display.WriteText(GetStatus());
            }

            /*double getTargetAngle()
            {
                if (connector.Status != MyShipConnectorStatus.Connected) return 0;
                var dirVector = new Vector3D(1, 0, 0);
                var worldDirVector = Vector3D.TransformNormal(dirVector, connector.OtherConnector.WorldMatrix);
                var myDir = Vector3D.TransformNormal(worldDirVector, MatrixD.Transpose(rotor.Top.WorldMatrix));
                double offset = 0;
                double.TryParse(connector.OtherConnector.CustomData, out offset);

                return -1 * Math.Atan(myDir.Z / myDir.X) - (myDir.X > 0 ? 0 : Math.PI) + offset * Math.PI / 180;
            }*/

            double getWorldBoxTargetAngle()
            {
                if (connector.Status != MyShipConnectorStatus.Connected) return 0;
                var dockedShipBoundingBox = connector.OtherConnector.CubeGrid.WorldAABB;
                var dockedShipCenterDirection = dockedShipBoundingBox.Center - rotor.Top.WorldMatrix.Translation;
                var myDir = Vector3D.TransformNormal(dockedShipCenterDirection, MatrixD.Transpose(rotor.Top.WorldMatrix));

                myDir.Y = 0;
                myDir.Normalize();

                return Math.Atan(myDir.X / myDir.Z) + (myDir.Z < 0 ? 0 : Math.PI) - kAngleOffset;
            }

            public void SaveHangar()
            {
                if (extender != null)
                    extender.CustomData = state.ToString();
            }

            public void LoadHangar()
            {
                if (extender != null)
                    Enum.TryParse<HangarState>(extender.CustomData, true, out state);
            }

            public Waypoint GetDockingWaypointA()
            {
                Waypoint w = new Waypoint();

                var connectorFront = Vector3D.TransformNormal(Vector3.Forward, connector.WorldMatrix);
                var connectorPos = connector.WorldMatrix.Translation;
                w.Position = connectorPos + connectorFront * 20;

                w.Direction = -connectorFront;
                w.Direction.Normalize();

                w.MaxSpeed = -1;

                w.Name = "DockA";

                w.ReferenceMode = "Dock";

                return w;
            }

            public Waypoint GetDockingWaypointB()
            {
                Waypoint w = new Waypoint();

                var connectorFront = Vector3D.TransformNormal(Vector3.Forward, connector.WorldMatrix);
                var connectorPos = connector.WorldMatrix.Translation;
                w.Position = connectorPos + connectorFront * 1;

                w.Direction = -connectorFront;
                w.Direction.Normalize();

                w.MaxSpeed = 20;

                w.Name = "DockB";

                w.ReferenceMode = "Dock";

                return w;
            }

            public void WriteDockingWaypointAToRotor()
            {
                var w = GetDockingWaypointA();
                var wb = GetDockingWaypointB();

                StringBuilder b = new StringBuilder();
                b.AppendLine(SerializeWaypoint(w));
                b.AppendLine(SerializeWaypoint(wb));
                rotor.CustomData = b.ToString();
            }

            #region Angle Utilities
            double AngleSubtract(double s, double t)
            {
                double deltaA = t - s;
                deltaA = CustomMod(deltaA + Math.PI, Math.PI * 2) - Math.PI;
                return deltaA;
            }

            double CustomMod(double n, double d)
            {
                return (n % d + d) % d;
            }

            void MoveRotorTowards(double angle)
            {
                rotor.TargetVelocityRad = (float)Math.Min(Math.PI, Math.Max(AngleSubtract(rotor.Angle, angle) * kRotorSpeedMulti / Math.PI * 2, - Math.PI));
            }
            #endregion
        }


        const int kNumHangars = 4;
        const string kHangarTag = "[H"; // Hangar components must be tagged [H0], [H1], etc

        List<Hangar> hangars = new List<Hangar>();

        void AutoGetHangars()
        {
            hangars.Clear();

            for (int i = 0; i < kNumHangars; i++)
                hangars.Add(new Hangar());

            GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(null, CollectHangarParts);
        }

        // My Updates Functions
        void StandBy()
        {
            for (int i = 0; i < hangars.Count; i++)
            {
                hangars[i].Update();
                echoBuilder.AppendLine($"{i}: {hangars[i].GetStatus()}");
            }
        }

        // My Command Functions

        void SendCommandToHangar()
        {
            int n = int.Parse(commandLine.Argument(1));
            hangars[n].SetCommand(commandLine.Argument(2));
        }

        void WriteDocks()
        {
            foreach (Hangar h in hangars)
                h.WriteDockingWaypointAToRotor();
        }

        #region Connect With Shared
        bool CollectHangarParts(IMyTerminalBlock block)
        {
            if (block.IsSameConstructAs(Me) && block.CustomName.StartsWith(kHangarTag))
            {
                int index;
                if (int.TryParse(block.CustomName.Substring(2, 1), out index) && index < kNumHangars)
                    hangars[index].AddPart(block);
            }
            return false;
        }

        void MySave(StringBuilder builder)
        {
            for (int i = 0; i < kNumHangars; i++)
                hangars[i].SaveHangar();
        }

        void MyLoad()
        {
            for (int i = 0; i < kNumHangars; i++)
                hangars[i].LoadHangar();
        }

        void MyProgram()
        {
            updates[ScriptState.Standby] = StandBy;
            commands["sendcommandtohangar"] = SendCommandToHangar;
            commands["writedocks"] = WriteDocks;
            Runtime.UpdateFrequency = UpdateFrequency.Update100;

            AutoGetHangars();
        }

        void MySetupCommands(MenuRemote root)
        {

        }
        #endregion

        #endregion

        #region Shared Scripts

        // Displayer
        List<IMyTextPanel> displays = new List<IMyTextPanel>();
        List<IMyTerminalBlock> surfaceProviders = new List<IMyTerminalBlock>();
        List<int> surfaceIndices = new List<int>();

        // Setup and utility
        bool isSetup = false;
        StringBuilder setupBuilder = new StringBuilder();
        StringBuilder echoBuilder = new StringBuilder();
        MyCommandLine commandLine = new MyCommandLine();
        Dictionary<string, Action> commands = new Dictionary<string, Action>(StringComparer.OrdinalIgnoreCase);
        Dictionary<ScriptState, Action> updates = new Dictionary<ScriptState, Action>();
        List<IMyTerminalBlock> getBlocksScratchPad = new List<IMyTerminalBlock>();


        // Script States
        enum ScriptState
        {
            Standby,
        }
        ScriptState currentState = ScriptState.Standby;

        public Program()
        {
            commands["connectdisplay"] = ConnectDisplay;
            commands["disconnectdisplay"] = DisonnectDisplay;
            commands["send"] = SendData;
            commands["getremotecommands"] = RemoteSendCommands;

            MyProgram();

            Load();
        }

        #region Save and Load
        int _currentLine = 0;
        string[] loadArray;
        public void Save()
        {
            StringBuilder storageBuilder = new StringBuilder();

            storageBuilder.AppendLine(displays.Count.ToString());

            for (int i = 0; i < displays.Count; i++)
                storageBuilder.AppendLine(displays[i].EntityId.ToString());

            storageBuilder.AppendLine(surfaceProviders.Count.ToString());

            for (int i = 0; i < surfaceProviders.Count; i++)
                storageBuilder.AppendLine($"{surfaceProviders[i].EntityId.ToString()} {surfaceIndices[i]}");

            MySave(storageBuilder);

            Me.GetSurface(1).WriteText(storageBuilder.ToString());
        }

        public void Load()
        {
            _currentLine = 0;
            var loadBuilder = new StringBuilder();
            Me.GetSurface(1).ReadText(loadBuilder);
            loadArray = loadBuilder.ToString().Split(
                new[] { "\r\n", "\r", "\n" },
                StringSplitOptions.None
            );

            // First line is the number of hooked displays
            int count = 0;
            int.TryParse(NextStorageLine(), out count);
            for (int i = 0; i < count; i++)
                ConnectDisplay(long.Parse(NextStorageLine()), 0);

            // Number of hooked surface providers
            count = 0;
            int.TryParse(NextStorageLine(), out count);
            for (int i = 0; i < count; i++)
            {
                string[] split = NextStorageLine().Split(' ');
                ConnectDisplay(long.Parse(split[0]), int.Parse(split[1]));
            }

            MyLoad();
        }

        private string NextStorageLine()
        {
            _currentLine += 1;
            if (loadArray.Length >= _currentLine)
                return loadArray[_currentLine - 1];
            return String.Empty;
        }
        #endregion

        public void Main(string argument, UpdateType updateSource)
        {
            if (commandLine.TryParse(argument))
            {
                Action commandAction;

                string command = commandLine.Argument(0);
                if (command == null)
                {
                    Echo("No command specified");
                }
                else if (commands.TryGetValue(command, out commandAction))
                {
                    // We have found a command. Invoke it.
                    commandAction();
                }
                else
                {
                    echoBuilder.AppendLine($"Unknown command {command}");
                }
            }
            else
            {
                echoBuilder.Clear();
                echoBuilder.Append(setupBuilder.ToString());

                Action updateAction;
                if (updates.TryGetValue(currentState, out updateAction))
                {
                    updateAction();
                }

                doDisplay();

            }
        }

        // Helpers
        void doDisplay()
        {
            for (int i = 0; i < displays.Count; i++)
                displays[i].WriteText(echoBuilder.ToString());
            for (int i = 0; i < surfaceProviders.Count; i++)
                ((IMyTextSurfaceProvider)surfaceProviders[i]).GetSurface(surfaceIndices[i]).WriteText(echoBuilder.ToString());

            base.Echo(echoBuilder.ToString());
        }

        private bool SameConstructAsMe(IMyTerminalBlock block)
        {
            return block.IsSameConstructAs(Me);
        }

        // Remote

        struct MenuRemote
        {
            public string name;
            public List<MenuRemote> subMenues;
            public string remoteCommand;

            public MenuRemote(string name, string command)
            {
                this.name = name;
                this.remoteCommand = command;
                this.subMenues = new List<MenuRemote>();
            }

            public string Serialize()
            {
                StringBuilder builder = new StringBuilder();
                builder.Append($"{name}{{");
                for (int i = 0; i < subMenues.Count; i++)
                {
                    string v = subMenues[i].Serialize();
                    builder.Append($"{{{v.Length.ToString()}}}{v}");
                }
                builder.Append($"}}{remoteCommand}");
                return builder.ToString();
            }
        }
        void SendData()
        {
            long targetId;
            string data = commandLine.Argument(1);
            long.TryParse(commandLine.Argument(2), out targetId);
            SendData(targetId, data);
        }

        private void SendData(long targetId, string data)
        {
            ((IMyProgrammableBlock)GridTerminalSystem.GetBlockWithId(targetId)).CustomData = data;
        }

        void RemoteSendCommands()
        {
            long targetId;
            long.TryParse(commandLine.Argument(1), out targetId);

            MenuRemote remoteMenuRoot;

            // Setup remote commands
            remoteMenuRoot = new MenuRemote("root", "");

            // Setup display
            var displaysMenu = new MenuRemote($"Displays ({displays.Count + surfaceProviders.Count})", "");
            displaysMenu.subMenues.Add(GetConnectDisplayRemote());
            displaysMenu.subMenues.Add(GetDisconnectDisplayRemote());

            remoteMenuRoot.subMenues.Add(displaysMenu);

            MySetupCommands(remoteMenuRoot);

            SendData(targetId, remoteMenuRoot.Serialize());
        }

        #region Remote Display Setup
        private MenuRemote GetConnectDisplayRemote()
        {
            MenuRemote connectDisplayRemote = new MenuRemote("Connect >", "");

            // Get surface providers
            GridTerminalSystem.GetBlocksOfType<IMyTextSurfaceProvider>(getBlocksScratchPad, SameConstructAsMe);
            for (int i = 0; i < getBlocksScratchPad.Count; i++)
            {
                var provider = (IMyTextSurfaceProvider)getBlocksScratchPad[i];
                var subItem = new MenuRemote(getBlocksScratchPad[i].CustomName, "");

                for (int j = 0; j < provider.SurfaceCount; j++)
                {
                    subItem.subMenues.Add(new MenuRemote($"{provider.GetSurface(j).DisplayName} >", $"connectdisplay {getBlocksScratchPad[i].EntityId} {j}"));
                }

                connectDisplayRemote.subMenues.Add(subItem);
            }
            getBlocksScratchPad.Clear();

            // Get panels
            GridTerminalSystem.GetBlocksOfType<IMyTextPanel>(getBlocksScratchPad, SameConstructAsMe);
            for (int i = 0; i < getBlocksScratchPad.Count; i++)
            {
                var panel = (IMyTextPanel)getBlocksScratchPad[i];
                var subItem = new MenuRemote(getBlocksScratchPad[i].CustomName, "");
                subItem.subMenues.Add(new MenuRemote($"{getBlocksScratchPad[i].DisplayName}", $"connectdisplay {getBlocksScratchPad[i].EntityId} 0"));

                connectDisplayRemote.subMenues.Add(subItem);
            }

            getBlocksScratchPad.Clear();
            return connectDisplayRemote;
        }

        private MenuRemote GetDisconnectDisplayRemote()
        {
            MenuRemote disconnectDisplayRemote = new MenuRemote("Disconnect >", "");

            for (int i = 0; i < surfaceProviders.Count; i++)
                disconnectDisplayRemote.subMenues.Add(new MenuRemote(surfaceProviders[i].CustomName, $"disconnectdisplay {surfaceProviders[i].EntityId}"));
            for (int i = 0; i < displays.Count; i++)
                disconnectDisplayRemote.subMenues.Add(new MenuRemote(((IMyTerminalBlock)displays[i]).CustomName, $"disconnectdisplay {displays[i].EntityId}"));

            return disconnectDisplayRemote;
        }

        void ConnectDisplay()
        {
            ConnectDisplay(long.Parse(commandLine.Argument(1)), int.Parse(commandLine.Argument(2)));
        }

        void ConnectDisplay(long id, int subId)
        {
            try
            {
                var block = GridTerminalSystem.GetBlockWithId(id);
                if (block is IMyTextPanel) displays.Add((IMyTextPanel)block);
                else
                {
                    surfaceProviders.Add(block);
                    surfaceIndices.Add(subId);
                }
            }
            catch (Exception e)
            {
                setupBuilder.AppendLine(e.ToString());
            }
        }

        void DisonnectDisplay()
        {
            DisconnectDisplay(long.Parse(commandLine.Argument(1)));
        }

        void DisconnectDisplay(long id)
        {
            for (int i = 0; i < surfaceProviders.Count; i++)
            {
                if (surfaceProviders[i].EntityId == id)
                {
                    ((IMyTextSurfaceProvider)surfaceProviders[i]).GetSurface(surfaceIndices[i]).WriteText("");
                    surfaceProviders.RemoveAt(i);
                    surfaceIndices.RemoveAt(i);
                    return;
                }
            }

            for (int i = 0; i < displays.Count; i++)
            {
                if (displays[i].EntityId == id)
                {
                    displays[i].WriteText("");
                    displays.RemoveAt(i);
                    return;
                }
            }
        }
        #endregion

        #endregion
    }
}
