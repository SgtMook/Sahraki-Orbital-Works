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
            subsystemManager.AddSubsystem("turret", new TurretSubsystem(IntelProvider));
            subsystemManager.AddSubsystem("loader", new CombatLoaderSubsystem());
            subsystemManager.AddSubsystem("utility", new UtilitySubsystem());

            if (iniParser.Get("RoverMSM", "Hover").ToBoolean(false))
            {
                var helidrive = new HeliDriveSubsystem();
                subsystemManager.AddSubsystem("heli", helidrive);

                if (iniParser.Get("RoverMSM", "heliCAP").ToBoolean(false))
                    subsystemManager.AddSubsystem("heliCAP", new HeliCombatAutopilotSubsystem(helidrive, IntelProvider));
            }

            if (iniParser.Get("RoverMSM", "Landpedo").ToBoolean(false))
            {
                subsystemManager.AddSubsystem("landpedo", new LandpedoSubsystem(IntelProvider));
            }

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
