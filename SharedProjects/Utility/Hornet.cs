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
    // This is a Hornet class attack drone
    public class Hornet
    {
        public SubsystemManager SubsystemManager;

        public Hornet(IMyTerminalBlock reference, MyGridProgram program)
        {
            SubsystemManager = new SubsystemManager(program);

            AutopilotSubsystem autopilotSubsystem = new AutopilotSubsystem();
            IntelSubsystem intelSubsystem = new IntelSubsystem();
            DockingSubsystem dockingSubsystem = new DockingSubsystem(intelSubsystem);
            HornetCombatSubsystem combatSubsystem = new HornetCombatSubsystem(intelSubsystem);
            MonitorSubsystem monitorSubsystem = new MonitorSubsystem(intelSubsystem);
            StatusIndicatorSubsystem indicatorSubsystem = new StatusIndicatorSubsystem(dockingSubsystem, intelSubsystem);
            AgentSubsystem agentSubsystem = new AgentSubsystem(intelSubsystem, AgentClass.Fighter);
            UndockFirstTaskGenerator undockingTaskGenerator = new UndockFirstTaskGenerator(program, autopilotSubsystem, dockingSubsystem);
            ScannerNetworkSubsystem scannerSubsystem = new ScannerNetworkSubsystem(intelSubsystem);

            SubsystemManager.AddSubsystem("autopilot", autopilotSubsystem);
            SubsystemManager.AddSubsystem("docking", dockingSubsystem);
            SubsystemManager.AddSubsystem("intel", intelSubsystem);
            SubsystemManager.AddSubsystem("combat", combatSubsystem);
            SubsystemManager.AddSubsystem("monitor", monitorSubsystem);
            SubsystemManager.AddSubsystem("indicator", indicatorSubsystem);

            undockingTaskGenerator.AddTaskGenerator(new WaypointTaskGenerator(program, autopilotSubsystem));
            undockingTaskGenerator.AddTaskGenerator(new DockTaskGenerator(program, autopilotSubsystem, dockingSubsystem));
            undockingTaskGenerator.AddTaskGenerator(new HornetAttackTaskGenerator(program, combatSubsystem, autopilotSubsystem, agentSubsystem, monitorSubsystem, intelSubsystem));

            agentSubsystem.AddTaskGenerator(undockingTaskGenerator);
            agentSubsystem.AddTaskGenerator(new SetHomeTaskGenerator(program, dockingSubsystem));

            SubsystemManager.AddSubsystem("agent", agentSubsystem);
            SubsystemManager.AddSubsystem("scanner", new ScannerNetworkSubsystem(intelSubsystem));
        }

        public void Update(UpdateType updateSource)
        {
            SubsystemManager.UpdateTime();
            SubsystemManager.Update(updateSource);
        }
    }
}
