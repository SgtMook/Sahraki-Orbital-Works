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
    public class SolarArray
    {
        public enum Side
        {
            Port,
            Starboard
        }

        private enum State
        {
            Extending,
            Extended,
            Retracting,
            Retracted
        }

        Dictionary<Side, char> SideTags = new Dictionary<Side, char>();

        const string SolarTag = "[S-";
        const float PistonSpeed = 2f;
        const float RotorRateMulti = 2f;

        Side side;
        State state;

        // Panels that, if has more sun, we should rotate the entire array in this direction
        List<IMySolarPanel> panelsCW = new List<IMySolarPanel>();
        List<IMySolarPanel> panelsM = new List<IMySolarPanel>();
        List<IMySolarPanel> panelsCCW = new List<IMySolarPanel>();

        // Rotor that opens in this direction
        // Always unfold clockwise first
        // Always fold counter clockwise first
        // POSITIVE ROTATION = COUNTER CLOCKWISE!
        IMyMotorStator rotorCW;
        IMyMotorStator rotorM;
        IMyMotorStator rotorCCW;

        List<IMyPistonBase> forwardPistons = new List<IMyPistonBase>();
        List<IMyPistonBase> reversePistons = new List<IMyPistonBase>();
        List<IMyPistonBase> verticalPistons = new List<IMyPistonBase>();

        List<IMyAirtightHangarDoor> gates = new List<IMyAirtightHangarDoor>();

        StringBuilder statusBuilder = new StringBuilder();

        public SolarArray(Side side)
        {
            this.side = side;
            state = State.Retracted;

            SideTags[Side.Starboard] = 'S';
            SideTags[Side.Port] = 'P';
        }

        public void TryGetBlocks(IMyBlockGroup group)
        {
            panelsCW.Clear();
            panelsM.Clear();
            panelsCCW.Clear();
            forwardPistons.Clear();
            reversePistons.Clear();
            verticalPistons.Clear();
            group.GetBlocks(null, BlocksCollect);
        }

        private bool BlocksCollect(IMyTerminalBlock block)
        {
            if (!block.CustomName.StartsWith(SolarTag)) return false;
            if (block.CustomName[3] != SideTags[side]) return false;

            if (block is IMySolarPanel)
            {
                if (block.CustomName[4] == 'F')
                    (side == Side.Port ? panelsCW : panelsCCW).Add((IMySolarPanel)block);
                else if (block.CustomName[4] == 'B')
                    (side == Side.Port ? panelsCCW : panelsCW).Add((IMySolarPanel)block);
                else
                    panelsM.Add((IMySolarPanel)block);
            }

            if (block is IMyMotorStator)
            {
                if (block.CustomName[4] == 'F' && side == Side.Port) rotorCW = (IMyMotorStator)block;
                else if (block.CustomName[4] == 'B' && side == Side.Port) rotorCCW = (IMyMotorStator)block;
                else if (block.CustomName[4] == 'F' && side == Side.Starboard) rotorCCW = (IMyMotorStator)block;
                else if (block.CustomName[4] == 'B' && side == Side.Starboard) rotorCW = (IMyMotorStator)block;
                else rotorM = (IMyMotorStator)block;

                ((IMyMotorStator)block).RotorLock = true;
            }

            if (block is IMyPistonBase)
            {
                if (block.CustomName[4] == 'F') forwardPistons.Add((IMyPistonBase)block);
                if (block.CustomName[4] == 'R') reversePistons.Add((IMyPistonBase)block);
                if (block.CustomName[4] == 'V') verticalPistons.Add((IMyPistonBase)block);
            }

            if (block is IMyAirtightHangarDoor) gates.Add((IMyAirtightHangarDoor)block);

            return false;
        }

        public void Extend()
        {
            state = State.Extending;
        }

        public void Retract()
        {
            state = State.Retracting;
        }

        public void Update()
        {
            rotorCW.RotorLock = true;
            rotorM.RotorLock = true;
            rotorCCW.RotorLock = true;

            if (state == State.Extending)
            {
                bool gatesOK = false;
                bool extendOK = false;
                bool unfoldOK = false;

                if (!gatesOK)
                {
                    gatesOK = true;
                    for (int i = 0; i < gates.Count; i++)
                    {
                        gates[i].OpenDoor();
                        if (gates[i].Status != DoorStatus.Open) gatesOK = false;
                    }
                }

                if (gatesOK && !extendOK)
                {
                    extendOK = true;

                    for (int i = 0; i < forwardPistons.Count; i++)
                    {
                        forwardPistons[i].Velocity = PistonSpeed;
                        if (forwardPistons[i].CurrentPosition < forwardPistons[i].MaxLimit) extendOK = false;
                    }
                    for (int i = 0; i < reversePistons.Count; i++)
                    {
                        reversePistons[i].Velocity = -PistonSpeed;
                        if (reversePistons[i].CurrentPosition > reversePistons[i].MinLimit) extendOK = false;
                    }
                }

                if (extendOK)
                {
                    unfoldOK = true;

                    rotorCW.RotorLock = false;
                    MoveRotorTowards(rotorCW, rotorCW.LowerLimitRad);
                    if (!AngleAlmostEqual(rotorCW.Angle, rotorCW.LowerLimitRad)) unfoldOK = false;

                    for (int i = 0; i < verticalPistons.Count; i++)
                    {
                        verticalPistons[i].Velocity = PistonSpeed;
                        if (verticalPistons[i].CurrentPosition < verticalPistons[i].MaxLimit) unfoldOK = false;
                    }

                    if (Math.Abs(AngleSubtract(rotorCW.Angle, rotorCW.LowerLimitRad)) < Math.Abs(AngleSubtract(rotorCW.Angle, rotorCW.UpperLimitRad)))
                    {
                        rotorCCW.RotorLock = false;
                        MoveRotorTowards(rotorCCW, rotorCCW.UpperLimitRad);
                    }

                    if (!AngleAlmostEqual(rotorCCW.Angle, rotorCCW.UpperLimitRad)) unfoldOK = false;
                }

                if (unfoldOK) state = State.Extended;
            }
            else if (state == State.Retracting)
            {
                bool foldOK = false;
                bool extendOK = false;

                if (!foldOK)
                {
                    foldOK = true;

                    rotorCCW.RotorLock = false;
                    MoveRotorTowards(rotorCCW, rotorCCW.LowerLimitRad);
                    if (!AngleAlmostEqual(rotorCCW.Angle, rotorCCW.LowerLimitRad)) foldOK = false;

                    if (Math.Abs(AngleSubtract(rotorCCW.Angle, rotorCCW.LowerLimitRad)) < Math.Abs(AngleSubtract(rotorCCW.Angle, rotorCCW.UpperLimitRad)))
                    {
                        rotorCW.RotorLock = false;
                        MoveRotorTowards(rotorCW, rotorCW.UpperLimitRad);
                    }

                    if (!AngleAlmostEqual(rotorCW.Angle, rotorCW.UpperLimitRad)) foldOK = false;

                    for (int i = 0; i < verticalPistons.Count; i++)
                    {
                        verticalPistons[i].Velocity = -PistonSpeed;
                        if (verticalPistons[i].CurrentPosition > verticalPistons[i].MinLimit) foldOK = false;
                    }

                    rotorM.RotorLock = false;
                    MoveRotorTowards(rotorM, 0);
                    if (!AngleAlmostEqual(rotorM.Angle, 0)) foldOK = false;
                }

                if (foldOK && !extendOK)
                {
                    extendOK = true;

                    for (int i = 0; i < forwardPistons.Count; i++)
                    {
                        forwardPistons[i].Velocity = -PistonSpeed;
                        if (forwardPistons[i].CurrentPosition > forwardPistons[i].MinLimit) extendOK = false;
                    }
                    for (int i = 0; i < reversePistons.Count; i++)
                    {
                        reversePistons[i].Velocity = PistonSpeed;
                        if (reversePistons[i].CurrentPosition < reversePistons[i].MaxLimit) extendOK = false;
                    }

                }

                if (extendOK)
                {
                    bool gatesOK = true;
                    for (int i = 0; i < gates.Count; i++)
                    {
                        gates[i].CloseDoor();
                        if (gates[i].Status != DoorStatus.Closed) gatesOK = false;
                    }
                    if (gatesOK) state = State.Retracted;
                }
            }
            else if (state == State.Extended)
            {
                // Suntracking
                var CWTotal = TallyOutput(panelsCW);
                var CCWTotal = TallyOutput(panelsCCW);
                var MTotal = TallyOutput(panelsM);

                double targetAngle = rotorM.Angle;

                if (CWTotal > MTotal) targetAngle += Math.PI * 0.1;
                else if (CCWTotal > MTotal) targetAngle -= Math.PI * 0.1;

                rotorM.RotorLock = false;
                MoveRotorTowards(rotorM, targetAngle);
            }
        }

        private float TallyOutput(List<IMySolarPanel> panels)
        {
            float total = 0;
            for (int i = 0; i < panels.Count; i++)
                total += panels[i].MaxOutput;
            return total;
        }

        public string GetStatus()
        {
            statusBuilder.Clear();

            statusBuilder.AppendLine($"Status: {state.ToString()}");

            statusBuilder.AppendLine($"Gates: {gates.Count}");
            statusBuilder.AppendLine($"Pistons: {forwardPistons.Count + reversePistons.Count + verticalPistons.Count}");
            statusBuilder.AppendLine($"Panels: {panelsCW.Count + panelsCCW.Count + panelsM.Count}");

            if (rotorCW != null) statusBuilder.AppendLine($"Rotor CW Found");
            else statusBuilder.AppendLine($"Rotor CW Missing");
            if (rotorM != null) statusBuilder.AppendLine($"Rotor M Found");
            else statusBuilder.AppendLine($"Rotor M Missing");
            if (rotorCCW != null) statusBuilder.AppendLine($"Rotor CCW Found");
            else statusBuilder.AppendLine($"Rotor CCW Missing");

            return statusBuilder.ToString();
        }

        public void SaveArray()
        {
            if (rotorM != null)
                rotorM.CustomData = state.ToString();
        }

        public void LoadArray()
        {
            if (rotorM != null)
                Enum.TryParse<State>(rotorM.CustomData, true, out state);
        }

        #region Angle Utilities
        double AngleSubtract(double s, double t)
        {
            double deltaA = t - s;
            deltaA = CustomMod(deltaA + Math.PI, Math.PI * 2) - Math.PI;
            return deltaA;
        }

        bool AngleAlmostEqual(float a, float b)
        {
            return Math.Abs(AngleSubtract((double)a, (double)b)) < 0.001; 
        }

        double CustomMod(double n, double d)
        {
            return (n % d + d) % d;
        }

        void MoveRotorTowards(IMyMotorStator rotor, double angle)
        {
            rotor.TargetVelocityRad = (float)Math.Min(Math.PI, Math.Max(AngleSubtract(rotor.Angle, angle) * RotorRateMulti / Math.PI * 2, -Math.PI));
        }
        #endregion
    }

    partial class Program : MyGridProgram
    {
        const string starboardArrayGroupName = "[S-S] Starboard Solar Array";
        const string portArrayGroupName = "[S-P] Port Solar Array";

        SolarArray starboardArray = new SolarArray(SolarArray.Side.Starboard);
        SolarArray portArray = new SolarArray(SolarArray.Side.Port);

        #region Connect With Shared
        void MySave(StringBuilder builder)
        {
            starboardArray.SaveArray();
            portArray.SaveArray();
        }

        void MyLoad()
        {
            starboardArray.LoadArray();
            portArray.LoadArray();
        }

        void MyProgram()
        {
            updates[ScriptState.Standby] = StandBy;

            commands["extendarray"] = ExtendArray;
            commands["retractarray"] = RetractArray;

            Runtime.UpdateFrequency = UpdateFrequency.Update100;

            IMyBlockGroup starboardGroup = GridTerminalSystem.GetBlockGroupWithName(starboardArrayGroupName);
            if (starboardGroup != null) starboardArray.TryGetBlocks(starboardGroup);

            IMyBlockGroup portGroup = GridTerminalSystem.GetBlockGroupWithName(portArrayGroupName);
            if (portGroup != null) portArray.TryGetBlocks(portGroup);
        }

        void MySetupCommands(MenuRemote root)
        {

        }

        // My Updates Functions
        void StandBy()
        {
            echoBuilder.AppendLine("Port Array:");
            echoBuilder.AppendLine(portArray.GetStatus());
            echoBuilder.AppendLine("Starboard Array:");
            echoBuilder.AppendLine(starboardArray.GetStatus());

            starboardArray.Update();
            portArray.Update();
        }

        // My Command Functions
        void ExtendArray()
        {
            if (commandLine.Argument(1) == "port" || commandLine.Argument(1) == "both") portArray.Extend();
            if (commandLine.Argument(1) == "starboard" || commandLine.Argument(1) == "both") starboardArray.Extend();

        }

        void RetractArray()
        {
            if (commandLine.Argument(1) == "port" || commandLine.Argument(1) == "both") portArray.Retract();
            if (commandLine.Argument(1) == "starboard" || commandLine.Argument(1) == "both") starboardArray.Retract();

        }
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
