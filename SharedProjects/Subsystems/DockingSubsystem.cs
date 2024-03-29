﻿using Sandbox.Game.EntityComponents;
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
            if (command == "dock")
            {
                if (Connector.Status != MyShipConnectorStatus.Unconnected || (argument is string && (string)argument == "force")) Dock();
            }
            if (command == "undock") Undock();
            if (command == "savetemplate") SaveMainframePositionToMerge();
        }

        public void CommandV2(TimeSpan timestamp, CommandLine command)
        {
        }
        public void DeserializeSubsystem(string serialized)
        {
        }

        public string GetStatus()
        {
            //if (Connector == null) return "NO CONNECTOR";
            //else if (DirectionIndicator == null) return "NO INDICATOR";
            //return "AOK";
            return Connector.CustomName.ToString();
        }

        public string SerializeSubsystem()
        {
            return "";
        }
        public void Setup(ExecutionContext context, string name)
        {
            Context = context;

            GetParts();
            if (Connector.Status == MyShipConnectorStatus.Connected) HomeID = Connector.OtherConnector.EntityId;
            IntelProvider.AddIntelMutator(this);
            ParseConfigs();
        }

        public void Update(TimeSpan timestamp, UpdateFrequency updateFlags)
        {
            if (timestamp.TotalSeconds < 2) return; // We just started up, wait up to two seconds to receive intel
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
                if (dock.OwnerID != Context.Reference.CubeGrid.EntityId) HomeID = -1;
            }
        }
        #endregion

        #region IDockingSubsystem
        public IMyShipConnector Connector { get; set; }
        public IMyInteriorLight DirectionIndicator { get; set; }

        public long HomeID { get; set; }

        public HangarTags HangarTags = HangarTags.None;

        IMyShipMergeBlock Merge;

        public void Dock(bool fake = false)
        {
            if (fake) return;
            Connector.Connect();
            foreach (var block in TurnOnOffList) block.Enabled = false;
            foreach (var bat in Batteries) bat.ChargeMode = ChargeMode.Recharge;
            foreach (var tank in Tanks) tank.Stockpile = true;
            if (Merge != null) Merge.Enabled = false;
            if (LoaderSubsystem != null) LoaderSubsystem.QueueReload = 2;
        }
        public void Undock(bool fake = false)
        {
            if (fake) return;
            foreach (var block in TurnOnOffList) block.Enabled = true;
            foreach (var bat in Batteries) bat.ChargeMode = ChargeMode.Auto;
            foreach (var tank in Tanks) tank.Stockpile = false;
            if (Merge != null) Merge.Enabled = false;
            Connector.Disconnect();
        }
        #endregion

        ExecutionContext Context;

        List<IMyFunctionalBlock> TurnOnOffList = new List<IMyFunctionalBlock>();
        List<IMyBatteryBlock> Batteries = new List<IMyBatteryBlock>();
        List<IMyGasTank> Tanks = new List<IMyGasTank>();

        IIntelProvider IntelProvider;

        CombatLoaderSubsystem LoaderSubsystem;

        public DockingSubsystem(IIntelProvider intelProvider, CombatLoaderSubsystem loader = null)
        {
            IntelProvider = intelProvider;
            LoaderSubsystem = loader;
        }

        void GetParts()
        {
            Connector = null;
            DirectionIndicator = null;
            TurnOnOffList.Clear();
            Batteries.Clear();
            Tanks.Clear();
            Context.Terminal.GetBlocksOfType<IMyTerminalBlock>(null, CollectParts);
        }

        bool CollectParts(IMyTerminalBlock block)
        {
            if (block.CustomName.Contains("[X]")) return false;
            if (!block.IsSameConstructAs(Context.Reference)) return false;

            if (block.CubeGrid.EntityId != Context.Reference.CubeGrid.EntityId) return false;
            if (block is IMyShipConnector && (Connector == null || block.CustomName.Contains("[D]"))) Connector = (IMyShipConnector)block;
            if (block is IMyShipMergeBlock && (Merge == null || block.CustomName.Contains("[D]"))) Merge = (IMyShipMergeBlock)block;
            if (block is IMyInteriorLight && (DirectionIndicator == null || block.CustomName.Contains("[D]"))) DirectionIndicator = (IMyInteriorLight)block;

            if (block is IMyRadioAntenna) TurnOnOffList.Add((IMyFunctionalBlock)block);
            if (block is IMyThrust) TurnOnOffList.Add((IMyFunctionalBlock)block);
            if (block is IMyCameraBlock) TurnOnOffList.Add((IMyFunctionalBlock)block);
            if (block is IMyGyro) TurnOnOffList.Add((IMyFunctionalBlock)block);
            if (block is IMyLargeTurretBase) TurnOnOffList.Add((IMyFunctionalBlock)block);
            if (block is IMyBatteryBlock) Batteries.Add((IMyBatteryBlock)block);
            if (block is IMySmallGatlingGun) TurnOnOffList.Add((IMySmallGatlingGun)block);
            if (block is IMyTimerBlock) TurnOnOffList.Add((IMyFunctionalBlock)block);
            if (block is IMyReactor) TurnOnOffList.Add((IMyFunctionalBlock)block);
            if (block is IMyGasTank) Tanks.Add((IMyGasTank)block);

            return false;
        }

        // [Docking]
        // Tags = ABCDE
        void ParseConfigs()
        {
            MyIni Parser = new MyIni();
            if (!Parser.TryParse(Context.Reference.CustomData))
                return;

            string tagString = Parser.Get("Docking", "Tags").ToString();
            if (tagString.Contains("A")) HangarTags |= HangarTags.A;
            if (tagString.Contains("B")) HangarTags |= HangarTags.B;
            if (tagString.Contains("C")) HangarTags |= HangarTags.C;
            if (tagString.Contains("D")) HangarTags |= HangarTags.D;
            if (tagString.Contains("E")) HangarTags |= HangarTags.E;
            if (tagString.Contains("F")) HangarTags |= HangarTags.F;
            if (tagString.Contains("G")) HangarTags |= HangarTags.G;
            if (tagString.Contains("H")) HangarTags |= HangarTags.H;
            if (tagString.Contains("I")) HangarTags |= HangarTags.I;
            if (tagString.Contains("J")) HangarTags |= HangarTags.J;
        }

        #region IOwnIntelMutator
        public void ProcessIntel(FriendlyShipIntel myIntel)
        {
            myIntel.HomeID = HomeID;
            if (Connector.Status == MyShipConnectorStatus.Connected)
            {
                myIntel.Radius = 0;
                myIntel.AgentStatus |= AgentStatus.Docked;
                if (Connector.OtherConnector.EntityId == HomeID) myIntel.AgentStatus |= AgentStatus.DockedAtHome;
            }
            myIntel.HangarTags = HangarTags;
        }
        #endregion

        void SaveMainframePositionToMerge()
        {
            Context.Terminal.GetBlocksOfType<IMyTerminalBlock>(null, SetupMerge);
        }

        bool SetupMerge(IMyTerminalBlock block)
        {
            if (block.CubeGrid.EntityId != Context.Reference.CubeGrid.EntityId) return false;
            if (!(block is IMyShipMergeBlock)) return false;
            if (!block.CustomName.StartsWith("[RL]")) return false;

            var merge = (IMyShipMergeBlock)block;

            merge.CustomData = GridTerminalHelper.BlockBytePosToBase64(Context.Reference, merge);
            return false;
        }
    }
}
