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
        public Program()
        {
            subsystemManager = new SubsystemManager(this);
            Runtime.UpdateFrequency = UpdateFrequency.Update1;

            // Add subsystems
            AutopilotSubsystem autopilotSubsystem = new AutopilotSubsystem();
            IntelSlaveSubsystem intelSubsystem = new IntelSlaveSubsystem();
            DockingSubsystem dockingSubsystem = new DockingSubsystem(intelSubsystem);
            HornetCombatSubsystem combatSubsystem = new HornetCombatSubsystem(intelSubsystem);
            MonitorSubsystem monitorSubsystem = new MonitorSubsystem(intelSubsystem);
            StatusIndicatorSubsystem indicatorSubsystem = new StatusIndicatorSubsystem(dockingSubsystem, intelSubsystem);

            subsystemManager.AddSubsystem("autopilot", autopilotSubsystem);
            subsystemManager.AddSubsystem("docking", dockingSubsystem);
            subsystemManager.AddSubsystem("intel", intelSubsystem);
            subsystemManager.AddSubsystem("combat", combatSubsystem);
            subsystemManager.AddSubsystem("monitor", monitorSubsystem);
            subsystemManager.AddSubsystem("indicator", indicatorSubsystem);

            AgentSubsystem agentSubsystem = new AgentSubsystem(intelSubsystem, AgentClass.Fighter);
            UndockFirstTaskGenerator undockingTaskGenerator = new UndockFirstTaskGenerator(this, autopilotSubsystem, dockingSubsystem);
            
            undockingTaskGenerator.AddTaskGenerator(new WaypointTaskGenerator(this, autopilotSubsystem));
            undockingTaskGenerator.AddTaskGenerator(new DockTaskGenerator(this, autopilotSubsystem, dockingSubsystem));
            undockingTaskGenerator.AddTaskGenerator(new HornetAttackTaskGenerator(this, combatSubsystem, autopilotSubsystem, agentSubsystem, monitorSubsystem, intelSubsystem));
            
            agentSubsystem.AddTaskGenerator(undockingTaskGenerator);
            agentSubsystem.AddTaskGenerator(new SetHomeTaskGenerator(this, dockingSubsystem));
            subsystemManager.AddSubsystem("agent", agentSubsystem);
            subsystemManager.AddSubsystem("scanner", new ScannerNetworkSubsystem(intelSubsystem));

            //subsystemManager.AddSubsystem("weapons", new DRMSubsystem(intelSubsystem));

            subsystemManager.DeserializeManager(Storage);
            Main("Fake", UpdateType.Script);
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
            if (argument == "FAKE") return;
            subsystemManager.UpdateTime();
            if (!string.IsNullOrEmpty(argument) && commandLine.TryParse(argument))
            {
                subsystemManager.Command(commandLine.Argument(0), commandLine.Argument(1), commandLine.ArgumentCount > 2 ? commandLine.Argument(2) : null);
            }
            else
            {
                subsystemManager.Update(updateSource);
                //profiler.UpdateRuntime();
                //builder.Clear();
                //profiler.PrintPerformance(builder);
                //Echo(builder.ToString());
                string s = subsystemManager.GetStatus();
                if (!string.IsNullOrEmpty(s)) Echo(s);
            }
        }
    }
}
