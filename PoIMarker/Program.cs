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
        public Program()
        {
            Runtime.UpdateFrequency = UpdateFrequency.None;
        }

        public void Save()
        {
        }

        public void Main(string argument, UpdateType updateSource)
        {
            GetParts();
            SavePoIs();

            PartsOfInterest.Clear();
            Base = null;
        }

        List<IMyTerminalBlock> PartsOfInterest = new List<IMyTerminalBlock>();
        IMyShipMergeBlock Base;

        void GetParts()
        {
            GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(null, CollectParts);
        }

        bool CollectParts(IMyTerminalBlock block)
        {
            if (!Me.IsSameConstructAs(block)) return false;
            
            if (block is IMyRadioAntenna || block is IMyGyro || block is IMyThrust || 
                block is IMyBatteryBlock || block is IMySmallGatlingGun || 
                block is IMyLargeTurretBase || block is IMyShipController || 
                block is IMyWarhead || block is IMyMotorStator || block is IMyShipConnector || block is IMyInteriorLight ||
                block is IMyCameraBlock)
                PartsOfInterest.Add(block);
            if (block is IMyShipMergeBlock && block.CustomName.Contains("<BASE>")) Base = (IMyShipMergeBlock)block;

            return false;
        }

        void SavePoIs()
        {
            Base.CustomData = GridTerminalHelper.BlockListBytePosToBase64(PartsOfInterest, Base);
        }
    }
}
