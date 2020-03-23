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
    // Forward drilling tunnel miner
    // Emu compatible
    // System components:
    // Multiple drills
    // Multiple sensors
    // Multiple containers
    public class HoneybeeMiningSystem : ISubsystem
    {
        #region ISubsystem
        public UpdateFrequency UpdateFrequency => UpdateFrequency.Update100;

        public void Command(TimeSpan timestamp, string command, object argument)
        {
            if (command == "recall") Recalling = 2;
        }

        public void DeserializeSubsystem(string serialized)
        {
        }

        public string GetStatus()
        {
            return string.Empty;
        }

        public string SerializeSubsystem()
        {
            return string.Empty;
        }

        public void Setup(MyGridProgram program, string name)
        {
            Program = program;
            GetParts();
            UpdateCargo();
            ParseConfigs();
        }

        public void Update(TimeSpan timestamp, UpdateFrequency updateFlags)
        {
            UpdateDrills();
            UpdateCargo();
        }
        #endregion

        public int CloseDist = 15;

        MyGridProgram Program;

        List<IMyShipDrill> Drills = new List<IMyShipDrill>();
        List<IMySensorBlock> Sensors = new List<IMySensorBlock>();
        List<IMyCargoContainer> Cargos = new List<IMyCargoContainer>();

        List<MyDetectedEntityInfo> DetectedEntityScratchpad = new List<MyDetectedEntityInfo>();

        int drillCounter = -1;

        double TotalCargoVolume = 0;
        double CurrentCargoVolume = 0;

        public int Recalling = 0;

        void GetParts()
        {
            Program.GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(null, CollectParts);
        }

        private bool CollectParts(IMyTerminalBlock block)
        {
            if (!Program.Me.IsSameConstructAs(block)) return false;

            if (block is IMyShipDrill)
                Drills.Add((IMyShipDrill)block);

            if (block is IMySensorBlock)
                Sensors.Add((IMySensorBlock)block);

            if (block is IMyCargoContainer)
                Cargos.Add((IMyCargoContainer)block);

            return false;
        }

        // [Honeybee]
        // CloseDist = 15
        private void ParseConfigs()
        {
            MyIni Parser = new MyIni();
            MyIniParseResult result;
            if (!Parser.TryParse(Program.Me.CustomData, out result))
                return;

            var dist = Parser.Get("Honeybee", "CloseDist").ToInt16();
            if (dist != 0) CloseDist = dist;
        }

        private void UpdateCargo()
        {
            TotalCargoVolume = 0;
            CurrentCargoVolume = 0;
            foreach (var cargo in Cargos)
            {
                TotalCargoVolume += (double)cargo.GetInventory(0).MaxVolume;
                CurrentCargoVolume += (double)cargo.GetInventory(0).CurrentVolume;
            }
            foreach (var drill in Drills)
            {
                TotalCargoVolume += (double)drill.GetInventory(0).MaxVolume;
                CurrentCargoVolume += (double)drill.GetInventory(0).CurrentVolume;
            }

            if (Recalling > 0) Recalling--;
        }

        private void UpdateDrills()
        {
            if (drillCounter > 0) drillCounter--;
            if (drillCounter == 0) StopDrill();
        }

        #region Public accessors
        public void Drill()
        {
            if (drillCounter == -1)
            {
                foreach (var drill in Drills)
                {
                    drill.Enabled = true;
                }
            }
            drillCounter = 2;
        }
        public void StopDrill()
        {
            foreach (var drill in Drills)
            {
                drill.Enabled = false;
            }
            drillCounter = -1;
        }

        public double PercentageFilled()
        {
            return CurrentCargoVolume / TotalCargoVolume;
        }

        public bool SensorsClear()
        {
            DetectedEntityScratchpad.Clear();
            foreach (var sensor in Sensors) sensor.DetectedEntities(DetectedEntityScratchpad);
            return DetectedEntityScratchpad.Count == 0;
        }
        #endregion
    }
}
