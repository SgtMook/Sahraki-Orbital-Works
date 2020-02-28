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
            Runtime.UpdateFrequency = UpdateFrequency.Update1;

            GetParts();

            statusBuilder.Clear();
            statusBuilder.AppendLine("=== Monitor ===");
            statusBuilder.Append("Thrusters: ").AppendLine(thrusters.Count.ToString());
            statusBuilder.Append("Gyros: ").AppendLine(gyros.Count.ToString());
            statusBuilder.Append("Controllers: ").AppendLine(controllers.Count.ToString());
            statusBuilder.Append("Other PBs: ").AppendLine(monitorTargets.Count.ToString());
            statusBuilder.AppendLine("===============");

            setupString = statusBuilder.ToString();
        }

        public void Save()
        {
        }

        public void Main(string argument, UpdateType updateSource)
        {
            run++;
            if (ticksPerCheck == run)
            {
                run = 0;
                CheckSystems();
                elipses++;
                if (elipses == 4) elipses = 0;
                statusBuilder.Clear();
                statusBuilder.Append(setupString);
                statusBuilder.Append("Running").Append('.', elipses);
                Echo(statusBuilder.ToString());
            }
        }

        private void CheckSystems()
        {
            bool AOK = true;
            foreach (var block in monitorTargets)
            {
                if (!block.IsWorking)
                {
                    AOK = false;
                    break;
                }
            }

            if (!AOK)
            {
                foreach (var thruster in thrusters) thruster.ThrustOverride = 0;
                foreach (var gyro in gyros) gyro.GyroOverride = false;
                foreach (var controller in controllers) controller.DampenersOverride = true;
                foreach (var monitorTarget in monitorTargets) monitorTarget.Enabled = false;
            }
        }

        List<IMyThrust> thrusters = new List<IMyThrust>();
        List<IMyGyro> gyros = new List<IMyGyro>();
        List<IMyShipController> controllers = new List<IMyShipController>();
        List<IMyProgrammableBlock> monitorTargets = new List<IMyProgrammableBlock>();

        StringBuilder statusBuilder = new StringBuilder();

        int run = 0;
        int ticksPerCheck = 60;

        int elipses = 0;

        string setupString;


        void GetParts()
        {
            GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(null, CollectParts);
        }

        private bool CollectParts(IMyTerminalBlock block)
        {
            if (!Me.IsSameConstructAs(block)) return false;
            if (block is IMyThrust) thrusters.Add((IMyThrust)block);
            if (block is IMyGyro) gyros.Add((IMyGyro)block);
            if (block is IMyShipController) controllers.Add((IMyShipController)block);
            if (block is IMyProgrammableBlock && block != Me) monitorTargets.Add((IMyProgrammableBlock)block);

            return false;
        }
    }
}
