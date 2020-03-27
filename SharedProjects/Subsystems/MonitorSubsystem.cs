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
    public enum MonitorOptions
    {
        Hydrogen,
        Power,
        Cargo,
    }

    public interface IMonitorSubsystem
    {
        float GetPercentage(MonitorOptions options);
    }
    public class MonitorSubsystem : ISubsystem, IMonitorSubsystem
    {
        #region ISubsystem
        public UpdateFrequency UpdateFrequency => UpdateFrequency.Update100;

        public void Command(TimeSpan timestamp, string command, object argument)
        {
        }

        public void DeserializeSubsystem(string serialized)
        {
        }

        public string GetStatus()
        {
            return $"{hydrogenPercent} {powerPercent} {inventoryPercent}";
        }

        public string SerializeSubsystem()
        {
            return string.Empty;
        }

        public void Setup(MyGridProgram program, string name)
        {
            Program = program;
            GetParts();
            ParseConfigs();
        }

        public void Update(TimeSpan timestamp, UpdateFrequency updateFlags)
        {
            UpdatePercentages();
            UpdateAntenna();
        }
        #endregion

        #region IMonitorSubsystem
        public float GetPercentage(MonitorOptions option)
        {
            if (option == MonitorOptions.Cargo) return inventoryPercent;
            else if (option == MonitorOptions.Hydrogen) return hydrogenPercent;
            else return powerPercent;
        }
        #endregion

        MyGridProgram Program;

        List<IMyInventory> Inventories = new List<IMyInventory>();
        List<IMyGasTank> HydrogenTanks = new List<IMyGasTank>();
        List<IMyBatteryBlock> Batteries = new List<IMyBatteryBlock>();

        float inventoryPercent;
        float hydrogenPercent;
        float powerPercent;

        float hydrogenFill = 1;

        IMyBeacon Beacon;

        StringBuilder antennaBuilder = new StringBuilder();

        void GetParts()
        {
            Inventories.Clear();
            HydrogenTanks.Clear();
            Batteries.Clear();
            Beacon = null;
            Program.GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(null, CollectParts);
        }

        bool CollectParts(IMyTerminalBlock block)
        {
            if (!block.IsSameConstructAs(Program.Me)) return false;

            if (block.HasInventory && block.CustomName.Contains("<M>")) Inventories.Add(block.GetInventory(block.InventoryCount - 1));
            if (block is IMyGasTank && 
                (block.BlockDefinition.SubtypeId == "LargeHydrogenTank" ||
                block.BlockDefinition.SubtypeId == "SmallHydrogenTank" || 
                block.BlockDefinition.SubtypeId == "LargeHydrogenTankSmall" || 
                block.BlockDefinition.SubtypeId == "SmallHydrogenTankSmall")) 
                HydrogenTanks.Add((IMyGasTank)block);
            if (block is IMyBatteryBlock)
                Batteries.Add((IMyBatteryBlock)block);
            if (block is IMyBeacon)
                Beacon = (IMyBeacon)block;

            return false;
        }

        // [Monitor]
        // HydrogenFillPercent = 100
        private void ParseConfigs()
        {
            MyIni Parser = new MyIni();
            MyIniParseResult result;
            if (!Parser.TryParse(Program.Me.CustomData, out result))
                return;

            var hFill = Parser.Get("Monitor", "HydrogenFillPercent").ToInt16();

            if (hFill != 0) hydrogenFill = hFill / 100f;
        }

        void UpdatePercentages()
        {
            float currentVal = 0;
            float totalVal = 0;

            if (Inventories.Count == 0) inventoryPercent = 1;
            else
            {
                foreach (var inv in Inventories)
                {
                    currentVal += (float)inv.CurrentVolume;
                    totalVal += (float)inv.MaxVolume;
                }

                inventoryPercent = currentVal / totalVal;
            }

            if (HydrogenTanks.Count == 0) hydrogenPercent = 1;
            else
            {
                currentVal = 0;
                totalVal = 0;

                foreach (var tank in HydrogenTanks)
                {
                    currentVal += tank.Capacity * (float)tank.FilledRatio;
                    totalVal += tank.Capacity;
                }

                hydrogenPercent = currentVal / totalVal;

                if (hydrogenPercent > hydrogenFill)
                {
                    foreach (var tank in HydrogenTanks)
                    {
                        if (tank.Stockpile) tank.Stockpile = false;
                    }
                }
            }

            if (Batteries.Count == 0) powerPercent = 1;
            else
            {
                currentVal = 0;
                totalVal = 0;

                foreach (var bat in Batteries)
                {
                    currentVal += bat.CurrentStoredPower;
                    totalVal += bat.MaxStoredPower;
                }

                powerPercent = currentVal / totalVal;
            }
        }

        void UpdateAntenna()
        {
            if (Beacon == null) return;
            antennaBuilder.Clear();
            antennaBuilder.Append("H:").Append((int)(hydrogenPercent * 100)).Append("|P:").Append((int)(powerPercent * 100)).Append("|C:").Append((int)(inventoryPercent * 100));
            Beacon.CustomName = antennaBuilder.ToString();
        }
    }
}
