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
using VRage.Library;

using System.Collections.Immutable;

namespace IngameScript
{
    partial class Program : MyGridProgram
    {
        public ScannerNetworkSubsystem SensorSubsystem;
        ExecutionContext context;
        public Program()
        {
            context = new ExecutionContext(this);

            iniParser.Clear();
            MyIniParseResult result;
            iniParser.TryParse(context.Reference.CustomData, out result);


            subsystemManager = new SubsystemManager(context);
            Runtime.UpdateFrequency = UpdateFrequency.Update1;

            IntelSubsystem IntelProvider = new IntelSubsystem();
            SensorSubsystem = new ScannerNetworkSubsystem(IntelProvider);

            subsystemManager.AddSubsystem("intel", IntelProvider);
            subsystemManager.AddSubsystem("sensor", SensorSubsystem);
            AtmoDrive drive = new AtmoDrive(IntelProvider.Controller, 5, context.Reference);
            drive.MaxAngleDegrees = 20;
            subsystemManager.AddSubsystem("autopilot", drive);
            subsystemManager.AddSubsystem("MACCAP", new MACCombatAutopilotSubsystem(drive, IntelProvider));
            subsystemManager.DeserializeManager(Storage);
        }

        CommandLine commandLine = new CommandLine();

        SubsystemManager subsystemManager;

        public void Save()
        {
            string v = subsystemManager.SerializeManager();
            Storage = v;
        }

        public void Main(string argument, UpdateType updateSource)
        {
            context.UpdateTime();
            if (commandLine.TryParse(argument))
            {
                subsystemManager.CommandV2(commandLine);
            }
            else
            {
                subsystemManager.Update(updateSource);
                Echo(subsystemManager.GetStatus());
            }
        }

        MyIni iniParser = new MyIni();
    }
}
