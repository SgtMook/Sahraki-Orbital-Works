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
            IntelMasterSubsystem intelSubsystem = new IntelMasterSubsystem();
            subsystemManager.AddSubsystem("intel", intelSubsystem);

            LookingGlassSubsystem lookingGlassSubsystem = new LookingGlassSubsystem(intelSubsystem, "[S1]");
            LookingGlassPlugin_Command CommandPlugin = new LookingGlassPlugin_Command();
            lookingGlassSubsystem.AddPlugin("command", CommandPlugin);
            subsystemManager.AddSubsystem("lookingglass1", lookingGlassSubsystem);
            subsystemManager.AddSubsystem("sensorswivel1", new SwivelSubsystem("[SN1]", lookingGlassSubsystem));

            LookingGlassSubsystem lookingGlassSubsystem2 = new LookingGlassSubsystem(intelSubsystem, "[S2]");
            lookingGlassSubsystem2.AddPlugin("command", CommandPlugin);
            subsystemManager.AddSubsystem("lookingglass2", lookingGlassSubsystem2);
            subsystemManager.AddSubsystem("sensorswivel2", new SwivelSubsystem("[SN2]", lookingGlassSubsystem2));

            subsystemManager.AddSubsystem("hangar", new HangarSubsystem(intelSubsystem));

            subsystemManager.AddSubsystem("scanner", new ScannerSubsystem(intelSubsystem, "SCN"));
            subsystemManager.AddSubsystem("scanner2", new ScannerSubsystem(intelSubsystem, "SCN2"));

            subsystemManager.AddCommandMultiplexor("lookingglass", "lookingglass1");
            subsystemManager.AddCommandMultiplexor("lookingglass", "lookingglass2");

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
