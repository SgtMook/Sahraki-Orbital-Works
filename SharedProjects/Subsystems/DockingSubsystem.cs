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
        IMyInteriorLight DirectionIndicator { get; }

        long HomeID { get; set; }
    }
    public class DockingSubsystem : ISubsystem, IDockingSubsystem, IOwnIntelMutator
    {
        #region ISubsystem
        public UpdateFrequency UpdateFrequency => UpdateFrequency.Update100;

        public void Command(TimeSpan timestamp, string command, object argument)
        {
            if (command == "dock") Dock();
            if (command == "undock") Undock();
        }

        public void DeserializeSubsystem(string serialized)
        {
            HomeID = long.Parse(serialized);
        }

        public string GetStatus()
        {
            //if (Connector == null) return "NO CONNECTOR";
            //else if (DirectionIndicator == null) return "NO INDICATOR";
            //return "AOK";
            return HomeID.ToString();
        }

        public string SerializeSubsystem()
        {
            return HomeID.ToString();
        }

        public void Setup(MyGridProgram program, string name)
        {
            Program = program;
            GetParts();
            IntelProvider.AddIntelMutator(this);
        }

        public void Update(TimeSpan timestamp, UpdateFrequency updateFlags)
        {
            if (timestamp.TotalSeconds < 1) return; // We just started up, wait up to one second to receive intel
            if (IntelProvider != null && HomeID != -1)
            {
                var intelItems = IntelProvider.GetFleetIntelligences(timestamp);
                var intelKey = MyTuple.Create(IntelItemType.Dock, HomeID);
                if (!intelItems.ContainsKey(intelKey))
                {
                    HomeID = -1;
                    return;
                }
                var dock = (DockIntel)intelItems[intelKey];
                if (dock.OwnerID != Program.Me.CubeGrid.EntityId) HomeID = -1;
            }
        }
        #endregion

        #region IDockingSubsystem
        public IMyShipConnector Connector { get; set; }
        public IMyInteriorLight DirectionIndicator { get; set; }

        public long HomeID { get; set; }

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
        #endregion

        MyGridProgram Program;
        List<IMyTerminalBlock> getBlocksScratchPad = new List<IMyTerminalBlock>();

        IIntelProvider IntelProvider;

        public DockingSubsystem(IIntelProvider intelProvider = null)
        {
            IntelProvider = intelProvider;
        }

        bool SetBlockToDock(IMyTerminalBlock block)
        {
            if (!Program.Me.IsSameConstructAs(block)) return false;
            if (block is IMyThrust) ((IMyThrust)block).Enabled = false;
            if (block is IMyGasTank) ((IMyGasTank)block).Stockpile = true;
            if (block is IMyBatteryBlock) ((IMyBatteryBlock)block).ChargeMode = ChargeMode.Recharge;
            if (block is IMyShipConnector) return true;
            if (block is IMyRadioAntenna) ((IMyRadioAntenna)block).Enabled = false;
            if (block is IMyGyro) ((IMyGyro)block).Enabled = false;
            if (block is IMyCameraBlock) ((IMyCameraBlock)block).Enabled = false;

            return false;
        }

        bool SetBlockToUndock(IMyTerminalBlock block)
        {
            if (!Program.Me.IsSameConstructAs(block)) return false;
            if (block is IMyThrust) ((IMyThrust)block).Enabled = true;
            if (block is IMyGasTank) ((IMyGasTank)block).Stockpile = false;
            if (block is IMyBatteryBlock) ((IMyBatteryBlock)block).ChargeMode = ChargeMode.Discharge;
            if (block is IMyShipConnector) return true;
            if (block is IMyRadioAntenna) ((IMyRadioAntenna)block).Enabled = true;
            if (block is IMyGyro) ((IMyGyro)block).Enabled = true;
            if (block is IMyCameraBlock) ((IMyCameraBlock)block).Enabled = true;
            return false;
        }

        void GetParts()
        {
            Connector = null;
            DirectionIndicator = null;
            Program.GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(null, CollectParts);
        }

        bool CollectParts(IMyTerminalBlock block)
        {
            if (!block.IsSameConstructAs(Program.Me)) return false;

            if (block is IMyShipConnector) Connector = (IMyShipConnector)block;
            if (block is IMyInteriorLight) DirectionIndicator = (IMyInteriorLight)block;

            return false;
        }

        #region IOwnIntelMutator
        public void ProcessIntel(FriendlyShipIntel myIntel)
        {
            myIntel.HomeID = HomeID;
        }
        #endregion
    }
}
