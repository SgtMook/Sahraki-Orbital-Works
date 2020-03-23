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
        void Dock(bool fake = false);
        void Undock(bool fake = false);

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

        public void Dock(bool fake = false)
        {
            if (fake) return;
            Connector.Connect();
            foreach (var block in TurnOnOffList) block.Enabled = false;
            foreach (var bat in Batteries) bat.ChargeMode = ChargeMode.Recharge;
            foreach (var tank in Tanks) tank.Stockpile = true;
        }
        public void Undock(bool fake = false)
        {
            if (fake) return;
            foreach (var block in TurnOnOffList) block.Enabled = true;
            foreach (var bat in Batteries) bat.ChargeMode = ChargeMode.Auto;
            foreach (var tank in Tanks) tank.Stockpile = false;
            Connector.Disconnect();
        }
        #endregion

        MyGridProgram Program;
        List<IMyTerminalBlock> getBlocksScratchPad = new List<IMyTerminalBlock>();
        List<IMyFunctionalBlock> TurnOnOffList = new List<IMyFunctionalBlock>();
        List<IMyBatteryBlock> Batteries = new List<IMyBatteryBlock>();
        List<IMyGasTank> Tanks = new List<IMyGasTank>();

        IIntelProvider IntelProvider;

        public DockingSubsystem(IIntelProvider intelProvider)
        {
            IntelProvider = intelProvider;
        }

        void GetParts()
        {
            Connector = null;
            DirectionIndicator = null;
            Program.GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(null, CollectParts);
        }

        bool CollectParts(IMyTerminalBlock block)
        {
            if (block.CubeGrid.EntityId != Program.Me.CubeGrid.EntityId) return false;
            if (block is IMyShipConnector) Connector = (IMyShipConnector)block;
            if (block is IMyInteriorLight) DirectionIndicator = (IMyInteriorLight)block;

            if (block is IMyThrust) TurnOnOffList.Add((IMyFunctionalBlock)block);
            if (block is IMyRadioAntenna) TurnOnOffList.Add((IMyFunctionalBlock)block);
            if (block is IMyGyro) TurnOnOffList.Add((IMyFunctionalBlock)block);
            if (block is IMyCameraBlock) TurnOnOffList.Add((IMyFunctionalBlock)block);
            if (block is IMyLargeTurretBase) TurnOnOffList.Add((IMyFunctionalBlock)block);

            if (block is IMyBatteryBlock) Batteries.Add((IMyBatteryBlock)block);
            if (block is IMyGasTank) Tanks.Add((IMyGasTank)block);

            return false;
        }

        #region IOwnIntelMutator
        public void ProcessIntel(FriendlyShipIntel myIntel)
        {
            myIntel.HomeID = HomeID;
            if (Connector.Status == MyShipConnectorStatus.Connected) myIntel.Radius = 0;
        }
        #endregion
    }
}
