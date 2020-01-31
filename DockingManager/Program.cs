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
        #region Connect With Shared
        void MySave(StringBuilder builder)
        {
        }

        void MyLoad()
        {

        }

        void MyProgram()
        {
            commands["dock"] = Dock;
            commands["undock"] = Undock;
            commands["requestextend"] = RequestExtend;
            commands["requestretract"] = RequestRetract;
            commands["requestcalibrate"] = RequestCalibrate;
            updates[ScriptState.Standby] = StandBy;
            Runtime.UpdateFrequency = UpdateFrequency.None;
        }

        void MySetupCommands(MenuRemote root)
        {

        }

        // My Updates Functions
        void StandBy()
        {
        }

        // My Command Functions
        void Dock()
        {
            GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(getBlocksScratchPad, SetBlockToDock);
            ((IMyShipConnector)getBlocksScratchPad[0]).Connect();
            getBlocksScratchPad.Clear();
        }
        void Undock()
        {
            GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(getBlocksScratchPad, SetBlockToUndock);
            ((IMyShipConnector)getBlocksScratchPad[0]).Disconnect();
            getBlocksScratchPad.Clear();
        }

        void RequestExtend()
        {
            GridTerminalSystem.GetBlocksOfType<IMyShipConnector>(getBlocksScratchPad, SameConstructAsMe);

            var connector = (IMyShipConnector)getBlocksScratchPad[0];

            if (connector.Status == MyShipConnectorStatus.Connected)
                connector.OtherConnector.CustomData = "Extend";
        }

        void RequestRetract()
        {
            GridTerminalSystem.GetBlocksOfType<IMyShipConnector>(getBlocksScratchPad, SameConstructAsMe);
            
            var connector = (IMyShipConnector)getBlocksScratchPad[0];

            if (connector.Status == MyShipConnectorStatus.Connected)
                connector.OtherConnector.CustomData = "Retract";
        }

        void RequestCalibrate()
        {
            GridTerminalSystem.GetBlocksOfType<IMyShipConnector>(getBlocksScratchPad, SameConstructAsMe);

            var connector = (IMyShipConnector)getBlocksScratchPad[0];

            if (connector.Status == MyShipConnectorStatus.Connected)
                connector.OtherConnector.CustomData = "Calibrate";
        }

        bool SetBlockToDock(IMyTerminalBlock block)
        {
            if (!Me.IsSameConstructAs(block)) return false;
            if (block is IMyThrust) ((IMyThrust)block).Enabled = false;
            if (block is IMyGasTank) ((IMyGasTank)block).Stockpile = true;
            if (block.BlockDefinition.SubtypeId == "SmallBlockBatteryBlock") ((IMyBatteryBlock)block).ChargeMode = ChargeMode.Recharge;
            if (block is IMyShipConnector) return true;
            if (block is IMyRadioAntenna) ((IMyRadioAntenna)block).Enabled = false;
            if (block is IMyGyro) ((IMyGyro)block).Enabled = false;

            return false;
        }

        bool SetBlockToUndock(IMyTerminalBlock block)
        {
            if (!Me.IsSameConstructAs(block)) return false;
            if (block is IMyThrust) ((IMyThrust)block).Enabled = true;
            if (block is IMyGasTank) ((IMyGasTank)block).Stockpile = false;
            if (block.BlockDefinition.SubtypeId == "SmallBlockBatteryBlock") ((IMyBatteryBlock)block).ChargeMode = ChargeMode.Discharge;
            if (block is IMyShipConnector) return true;
            if (block is IMyRadioAntenna) ((IMyRadioAntenna)block).Enabled = true;
            if (block is IMyGyro) ((IMyGyro)block).Enabled = true;
            return false;
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
