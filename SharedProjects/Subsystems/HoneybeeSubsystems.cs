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

        public void CommandV2(TimeSpan timestamp, CommandLine command)
        {

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

        public void Setup(ExecutionContext context, string name)
        {
            Context = context;

            GetParts();
            ParseConfigs();
        }

        public void Update(TimeSpan timestamp, UpdateFrequency updateFlags)
        {
            UpdateDrills();
            if (Recalling > 0) Recalling--;
        }
        #endregion

        public int CloseDist = 15;
        public int MineDepth = 100;
        public int OffsetDist = 10;
        public int CancelDist = 15000;

        ExecutionContext Context;

        public List<IMyShipDrill> Drills = new List<IMyShipDrill>();
        public List<IMySensorBlock> Sensors = new List<IMySensorBlock>();
        public List<IMySensorBlock> NearSensors = new List<IMySensorBlock>();
        public List<IMySensorBlock> FarSensors = new List<IMySensorBlock>();

        List<MyDetectedEntityInfo> DetectedEntityScratchpad = new List<MyDetectedEntityInfo>();

        int drillCounter = -1;

        public int Recalling = 0;

        void GetParts()
        {
            Drills.Clear();
            Sensors.Clear();
            Context.Terminal.GetBlocksOfType<IMyTerminalBlock>(null, CollectParts);
        }

        bool CollectParts(IMyTerminalBlock block)
        {
            if (Context.Reference.CubeGrid.EntityId != block.CubeGrid.EntityId) return false;

            if (block is IMyShipDrill) Drills.Add((IMyShipDrill)block);
            if (block is IMySensorBlock)
            {
                if (block.CustomName.Contains("[N]")) NearSensors.Add((IMySensorBlock)block);
                else if (block.CustomName.Contains("[F]")) FarSensors.Add((IMySensorBlock)block);
                else Sensors.Add((IMySensorBlock)block);
            }
            return false;
        }

        // [Honeybee]
        // CloseDist = 15
        // MineDepth = 100
        // OffsetDist = 10
        // CancelDist = 15000
        void ParseConfigs()
        {
            MyIni Parser = new MyIni();
            if (!Parser.TryParse(Context.Reference.CustomData))
                return;

            var dist = Parser.Get("Honeybee", "CloseDist").ToInt16();
            if (dist != 0) CloseDist = dist;

            dist = Parser.Get("Honeybee", "MineDepth").ToInt16();
            if (dist != 0) MineDepth = dist;

            dist = Parser.Get("Honeybee", "OffsetDist").ToInt16();
            if (dist != 0) OffsetDist = dist;

            var longDist = Parser.Get("Honeybee", "CancelDist").ToInt32();
            if (longDist != 0) CancelDist = longDist;
        }

        void UpdateDrills()
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

        public bool SensorsClear()
        {
            DetectedEntityScratchpad.Clear();
            foreach (var sensor in Sensors) sensor.DetectedEntities(DetectedEntityScratchpad);
            return DetectedEntityScratchpad.Count == 0;
        }
        public bool SensorsFarClear()
        {
            DetectedEntityScratchpad.Clear();
            foreach (var sensor in FarSensors) sensor.DetectedEntities(DetectedEntityScratchpad);
            return DetectedEntityScratchpad.Count == 0;
        }
        public bool SensorsBack()
        {
            DetectedEntityScratchpad.Clear();
            foreach (var sensor in NearSensors) sensor.DetectedEntities(DetectedEntityScratchpad);
            return DetectedEntityScratchpad.Count > 0;
        }

        public void SensorsOn()
        {
            foreach (var sensor in Sensors) sensor.Enabled = true;
            foreach (var sensor in FarSensors) sensor.Enabled = true;
            foreach (var sensor in NearSensors) sensor.Enabled = true;
        }
        #endregion
    }
}
