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
using VRage.Library;



namespace IngameScript
{
    partial class Program : MyGridProgram
    {
        public Program()
        {
            subsystemManager = new SubsystemManager(this);
            Runtime.UpdateFrequency = UpdateFrequency.Update1;

            // Add subsystems
            IntelSlaveSubsystem intelSubsystem = new IntelSlaveSubsystem();
            subsystemManager.AddSubsystem("intel", intelSubsystem);

            SensorSubsystem sensorSubsystem = new SensorSubsystem(intelSubsystem);
            subsystemManager.AddSubsystem("sensor", sensorSubsystem);
            subsystemManager.AddSubsystem("sensorswivel", new SwivelSubsystem("[SN]", sensorSubsystem));

            subsystemManager.DeserializeManager(Storage);
        }

        MyCommandLine commandLine = new MyCommandLine();

        SubsystemManager subsystemManager;

        public void Save()
        {
            string v = subsystemManager.SerializeManager();
            Me.CustomData = v;
            Storage = v;
        }

        public void Main(string argument, UpdateType updateSource)
        {
            subsystemManager.UpdateTime();
            if (commandLine.TryParse(argument))
            {
                subsystemManager.Command(commandLine.Argument(0), commandLine.Argument(1), commandLine.ArgumentCount > 2 ? commandLine.Argument(2) : null);
            }
            else
            {
                subsystemManager.Update(updateSource);
                Echo(subsystemManager.GetStatus());
            }
        }
    }
}