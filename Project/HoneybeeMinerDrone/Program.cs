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

namespace IngameScript
{
    // Forward drilling tunnel miner
    // Emu compatible
    partial class Program : MyGridProgram
    {
        public Program()
        {
            subsystemManager = new SubsystemManager(this);
            Runtime.UpdateFrequency = UpdateFrequency.Update1;

            // Add subsystems
            AutopilotSubsystem autopilotSubsystem = new AutopilotSubsystem();
            IntelSubsystem intelSubsystem = new IntelSubsystem();
            DockingSubsystem dockingSubsystem = new DockingSubsystem(intelSubsystem);
            HoneybeeMiningSystem miningSubsystem = new HoneybeeMiningSystem();
            MonitorSubsystem monitorSubsystem = new MonitorSubsystem(intelSubsystem);

            subsystemManager.AddSubsystem("autopilot", autopilotSubsystem);
            subsystemManager.AddSubsystem("docking", dockingSubsystem);
            subsystemManager.AddSubsystem("intel", intelSubsystem);
            subsystemManager.AddSubsystem("mining", miningSubsystem);
            subsystemManager.AddSubsystem("monitor", monitorSubsystem);

            AgentSubsystem agentSubsystem = new AgentSubsystem(intelSubsystem, AgentClass.Miner);

            UndockFirstTaskGenerator undockingTaskGenerator = new UndockFirstTaskGenerator(this, autopilotSubsystem, dockingSubsystem);
            undockingTaskGenerator.AddTaskGenerator(new WaypointTaskGenerator(this, autopilotSubsystem));
            DockTaskGenerator dockTaskGenerator = new DockTaskGenerator(this, autopilotSubsystem, dockingSubsystem);
            undockingTaskGenerator.AddTaskGenerator(dockTaskGenerator);

            agentSubsystem.AddTaskGenerator(undockingTaskGenerator);
            agentSubsystem.AddTaskGenerator(new SetHomeTaskGenerator(this, dockingSubsystem));
            agentSubsystem.AddTaskGenerator(new HoneybeeMiningTaskGenerator(this, miningSubsystem, autopilotSubsystem, agentSubsystem, dockingSubsystem, dockTaskGenerator, undockingTaskGenerator, intelSubsystem, monitorSubsystem));
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
                var s = subsystemManager.GetStatus();
                if (!string.IsNullOrEmpty(s)) Echo(s);
            }
        }
    }
}
