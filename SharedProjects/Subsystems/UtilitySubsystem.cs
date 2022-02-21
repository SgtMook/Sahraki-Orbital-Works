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
    public class UtilitySubsystem : ISubsystem
    {
        #region ISubsystem
        public UpdateFrequency UpdateFrequency => UpdateFrequency.Update1;

        public void Command(TimeSpan timestamp, string command, object argument)
        {
        }

        public void CommandV2(TimeSpan timestamp, CommandLine command)
        {
        }

        public void DeserializeSubsystem(string serialized)
        {
        }

        public string GetStatus()
        {
            return O2Generators.Count().ToString();
        }

        public string SerializeSubsystem()
        {
            return string.Empty;
        }

        public void Setup(ExecutionContext context, string name)
        {
            Context = context;

            ParseConfigs();
            GetParts();
        }

        public void Update(TimeSpan timestamp, UpdateFrequency updateFlags)
        {
            runs++;

            if (runs % 10 == 0)
            {
                if (TurnOffWheels)
                    CheckWheels();
                if (TurnOffGyros)
                    CheckGyros();
                if (TurnOffHydros)
                    CheckHydros();
            }

            if (runs % 30 == 0)
            {
                if (AutoAddWheels)
                    AddWheels();
            }
        }
        #endregion
        const string kUtilitySection = "Utility";
        ExecutionContext Context;

        List<IMyMotorSuspension> Suspensions = new List<IMyMotorSuspension>();
        List<IMyShipController> Controllers = new List<IMyShipController>();
        List<IMyGasGenerator> O2Generators = new List<IMyGasGenerator>();
        List<IMyGasTank> GasTanks = new List<IMyGasTank>();
        List<IMyCockpit> Cockpits = new List<IMyCockpit>();
        List<IMyGyro> Gyros = new List<IMyGyro>();
        IMyTimerBlock AddWheelTimer;

        int runs = 0;

        bool TurnOffWheels;
        bool TurnOffGyros;
        bool TurnOffHydros;
        bool AutoAddWheels;

        public UtilitySubsystem()
        {
        }

        // [Utility]
        // TurnOffWheels = true
        // TurnOffGyros = true
        // TurnOffHydros = true
        // AutoAddWheels = true
        void ParseConfigs()
        {
            MyIni Parser = new MyIni();
            if (!Parser.TryParse(Context.Reference.CustomData))
                return;

            TurnOffWheels = Parser.Get(kUtilitySection, "TurnOffWheels").ToBoolean(true);
            TurnOffGyros = Parser.Get(kUtilitySection, "TurnOffGyros").ToBoolean(true);
            TurnOffHydros = Parser.Get(kUtilitySection, "TurnOffHydros").ToBoolean(true);
            AutoAddWheels = Parser.Get(kUtilitySection, "AutoAddWheels").ToBoolean(true);
        }

        void GetParts()
        {
            Context.Terminal.GetBlocksOfType<IMyTerminalBlock>(null, CollectBlocks);
        }

        bool CollectBlocks(IMyTerminalBlock block)
        {
            if (!block.IsSameConstructAs(Context.Reference)) return false;
            if (block.CustomName.Contains("[X]")) return false;
            if (block is IMyMotorSuspension) Suspensions.Add(block as IMyMotorSuspension);
            if (block is IMyShipController) Controllers.Add(block as IMyShipController);
            if (block is IMyGasGenerator) O2Generators.Add(block as IMyGasGenerator);
            if (block is IMyGasTank) GasTanks.Add(block as IMyGasTank);
            if (block is IMyCockpit) Cockpits.Add(block as IMyCockpit);
            if (block is IMyGyro) Gyros.Add(block as IMyGyro);
            if (block is IMyTimerBlock && block.CustomName.Contains("[W]")) AddWheelTimer = block as IMyTimerBlock;

            return false;
        }

        void CheckWheels()
        {
            if (Controllers.Count < 1) return;
            bool braking = Controllers[0].HandBrake || Controllers[0].CubeGrid.IsStatic;

            foreach (var suspension in Suspensions)
            {
                suspension.Enabled = !braking;
            }
        }

        void CheckGyros()
        {
            if (Controllers.Count < 1) return;
            var gyroOff = Context.Reference.CubeGrid.IsStatic || (Controllers[0].GetShipSpeed() < 0.1 && !Controllers[0].DampenersOverride);
            foreach (var gyro in Gyros)
            {
                if (gyro.Enabled == gyroOff)
                    gyro.Enabled = !gyroOff;
            }
        }

        void CheckHydros()
        {
            if (GasTanks.Count == 0 || O2Generators.Count == 0) return;
            bool gasFull = true;

            foreach (var tank in GasTanks)
            {
                if (tank.FilledRatio < 0.9)
                {
                    gasFull = false;
                    break;
                }
            }

            if (gasFull)
            {
                foreach (var cockpit in Cockpits)
                {
                    if (cockpit.OxygenFilledRatio < 0.9)
                    {
                        gasFull = false;
                        break;
                    }
                }
            }

            foreach (var generator in O2Generators)
            {
                generator.Enabled = !gasFull;
            }
        }

        void AddWheels()
        {
            if (AddWheelTimer != null)
            {
                foreach (var suspension in Suspensions)
                {
                    if (suspension.CubeGrid.EntityId == Context.Reference.CubeGrid.EntityId && !suspension.IsAttached)
                    {
                        AddWheelTimer.Trigger();
                        return;
                    }
                }
            }
        }
    }
}
