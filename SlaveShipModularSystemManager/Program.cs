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

using System.Collections.Immutable;

namespace IngameScript
{
    partial class Program : MyGridProgram
    {
        public Program()
        {
            subsystemManager = new SubsystemManager(this);
            Runtime.UpdateFrequency = UpdateFrequency.Update1;

            // Add subsystems
            IntelSubsystem intelSubsystem = new IntelSubsystem();
            subsystemManager.AddSubsystem("intel", intelSubsystem);
            AutopilotSubsystem autopilotSubsystem = new AutopilotSubsystem();
            subsystemManager.AddSubsystem("autopilot", autopilotSubsystem);
            DockingSubsystem dockingSubsystem = new DockingSubsystem(intelSubsystem);
            subsystemManager.AddSubsystem("docking", dockingSubsystem);
            MonitorSubsystem monitorSubsystem = new MonitorSubsystem(intelSubsystem);
            subsystemManager.AddSubsystem("monitor", monitorSubsystem);

            // LookingGlass setup
            LookingGlassNetworkSubsystem lookingGlassNetwork = new LookingGlassNetworkSubsystem(intelSubsystem, "LG", false, false);
            subsystemManager.AddSubsystem("lookingglass", lookingGlassNetwork);

            // Agent setup
            AgentSubsystem agentSubsystem = new AgentSubsystem(intelSubsystem, AgentClass.Drone);
            intelSubsystem.MyAgent = agentSubsystem;
            UndockFirstTaskGenerator undockingTaskGenerator = new UndockFirstTaskGenerator(this, autopilotSubsystem, dockingSubsystem);
            undockingTaskGenerator.AddTaskGenerator(new WaypointTaskGenerator(this, autopilotSubsystem));
            undockingTaskGenerator.AddTaskGenerator(new DockTaskGenerator(this, autopilotSubsystem, dockingSubsystem));
            agentSubsystem.AddTaskGenerator(undockingTaskGenerator);
            agentSubsystem.AddTaskGenerator(new SetHomeTaskGenerator(this, dockingSubsystem));
            
            subsystemManager.AddSubsystem("agent", agentSubsystem);

            subsystemManager.AddSubsystem("indicator", new StatusIndicatorSubsystem(dockingSubsystem, intelSubsystem));

            subsystemManager.DeserializeManager(Storage);
        }

        MyCommandLine commandLine = new MyCommandLine();

        SubsystemManager subsystemManager;

        public void Save()
        {
            string v = subsystemManager.SerializeManager();
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
