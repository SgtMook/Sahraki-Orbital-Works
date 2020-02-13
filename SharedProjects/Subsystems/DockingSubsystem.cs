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
    public interface IDockingSubsystem
    {
        void Dock();
        void Undock();

        IMyShipConnector Connector { get; }
    }
    public class DockingSubsystem : ISubsystem, IDockingSubsystem
    {
        #region ISubsystem
        public UpdateFrequency UpdateFrequency => UpdateFrequency.Update100;

        public void Command(TimeSpan timestamp, string command, object argument)
        {
            if (command == "dock") Dock();
            if (command == "undock") Undock();
            if (command == "requestextend") RequestExtend();
            if (command == "requestretract") RequestRetract();
            if (command == "requestcalibrate") RequestCalibrate();
        }

        public void DeserializeSubsystem(string serialized)
        {
        }

        public string GetStatus()
        {
            if (Connector == null) return "NO CONNECTOR";
            return "AOK";
        }

        public string SerializeSubsystem()
        {
            return string.Empty;
        }

        public void Setup(MyGridProgram program, string name)
        {
            Program = program;

            Program.GridTerminalSystem.GetBlocksOfType<IMyShipConnector>(getBlocksScratchPad, SameConstructAsMe);

            Connector = (IMyShipConnector)getBlocksScratchPad[0];
        }

        public void Update(TimeSpan timestamp, UpdateFrequency updateFlags)
        {
        }
        #endregion

        #region IDockingSubsystem
        public IMyShipConnector Connector { get; set; }

        public void Dock()
        {
            Program.GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(getBlocksScratchPad, SetBlockToDock);
            ((IMyShipConnector)getBlocksScratchPad[0]).Connect();
            getBlocksScratchPad.Clear();
        }
        public void Undock()
        {
            Program.GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(getBlocksScratchPad, SetBlockToUndock);
            ((IMyShipConnector)getBlocksScratchPad[0]).Disconnect();
            getBlocksScratchPad.Clear();
        }

        MyGridProgram Program;
        List<IMyTerminalBlock> getBlocksScratchPad = new List<IMyTerminalBlock>();
        #endregion

        void RequestExtend()
        {
            if (Connector.Status == MyShipConnectorStatus.Connected)
                Connector.OtherConnector.CustomData = "Extend";
        }

        void RequestRetract()
        {
            if (Connector.Status == MyShipConnectorStatus.Connected)
                Connector.OtherConnector.CustomData = "Retract";
        }

        void RequestCalibrate()
        {
            if (Connector.Status == MyShipConnectorStatus.Connected)
                Connector.OtherConnector.CustomData = "Calibrate";
        }

        bool SetBlockToDock(IMyTerminalBlock block)
        {
            if (!Program.Me.IsSameConstructAs(block)) return false;
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
            if (!Program.Me.IsSameConstructAs(block)) return false;
            if (block is IMyThrust) ((IMyThrust)block).Enabled = true;
            if (block is IMyGasTank) ((IMyGasTank)block).Stockpile = false;
            if (block.BlockDefinition.SubtypeId == "SmallBlockBatteryBlock") ((IMyBatteryBlock)block).ChargeMode = ChargeMode.Discharge;
            if (block is IMyShipConnector) return true;
            if (block is IMyRadioAntenna) ((IMyRadioAntenna)block).Enabled = true;
            if (block is IMyGyro) ((IMyGyro)block).Enabled = true;
            return false;
        }

        bool SameConstructAsMe(IMyTerminalBlock block)
        {
            return block.IsSameConstructAs(Program.Me);
        }
    }
}
