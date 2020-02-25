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
    public class HoneybeeMiningTaskGenerator : ITaskGenerator
    {
        #region ITaskGenerator
        public TaskType AcceptedTypes => TaskType.Mine;

        public ITask GenerateTask(TaskType type, MyTuple<IntelItemType, long> intelKey, Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence> IntelItems, TimeSpan canonicalTime, long myID)
        {
            Autopilot.SetStatus("Mining Task Generating");
            if (type != TaskType.Mine) return new NullTask();
            Autopilot.SetStatus("Mining Task Generating 2");
            if (!IntelItems.ContainsKey(intelKey)) return new NullTask();
            Autopilot.SetStatus("Mining Task Generating 3");
            if (intelKey.Item1 != IntelItemType.Waypoint) return new NullTask();
            Autopilot.SetStatus("Mining Task Generating 4");

            var target = (Waypoint)IntelItems[intelKey];

            // Make sure we actually have the asteroid we are supposed to be mining
            AsteroidIntel host = null;
            foreach (var kvp in IntelItems)
            {
                if (kvp.Key.Item1 != IntelItemType.Asteroid) continue;
                var dist = (kvp.Value.GetPositionFromCanonicalTime(canonicalTime) - target.GetPositionFromCanonicalTime(canonicalTime)).Length();
                if (dist > kvp.Value.Radius) continue;
                host = (AsteroidIntel)kvp.Value;
                break;
            }
            Autopilot.SetStatus("Mining Task Generating 5");

            if (host == null) return new NullTask();

            var dockTask = DockTaskGenerator.GenerateMoveToAndDockTask(MyTuple.Create(IntelItemType.NONE, (long)0), IntelItems, 40);

            Autopilot.SetStatus("Mining Task Generated");

            return new HoneyMiningTask(Program, MiningSystem, Autopilot, AgentSubsystem, target, host, dockTask);
        }
        #endregion

        MyGridProgram Program;
        HoneybeeMiningSystem MiningSystem;
        IAutopilot Autopilot;
        IAgentSubsystem AgentSubsystem;
        DockTaskGenerator DockTaskGenerator;

        public HoneybeeMiningTaskGenerator(MyGridProgram program, HoneybeeMiningSystem miningSystem, IAutopilot autopilot, IAgentSubsystem agentSubsystem, DockTaskGenerator dockTaskGenerator)
        {
            Program = program;
            MiningSystem = miningSystem;
            Autopilot = autopilot;
            AgentSubsystem = agentSubsystem;
            DockTaskGenerator = dockTaskGenerator;
        }
    }


    public class HoneyMiningTask : ITask
    {
        #region ITask
        public TaskStatus Status { get; private set; }

        public void Do(Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence> IntelItems, TimeSpan canonicalTime)
        {
            if (state == 0)
            {
                Autopilot.SetStatus("Stage 0");
                LeadTask.Do(IntelItems, canonicalTime);
                if (LeadTask.Status == TaskStatus.Complete) state = 1;
            }
            else if (state == 1)
            {
                Autopilot.SetStatus("Stage 1");
                if (MiningSystem.SensorsClear())
                    MineTask.Destination.Position = HostCenter;
                else
                    MineTask.Destination.Position = Vector3D.Zero;

                MiningSystem.Drill();
                MineTask.Do(IntelItems, canonicalTime);
                if (MiningSystem.PercentageFilled() > 0.92) state = 2;
            }
            else if (state == 2)
            {
                Autopilot.SetStatus("Stage 2");
                MiningSystem.StopDrill();
                MineTask.Destination.Position = LeadTask.Destination.Position;
                MineTask.Do(IntelItems, canonicalTime);
                if (MineTask.Status == TaskStatus.Complete) state = 3;
            }
            else if (state == 3)
            {
                Autopilot.SetStatus("Stage 3");
                HomeTask.Do(IntelItems, canonicalTime);
                if (HomeTask.Status != TaskStatus.Incomplete) state = 4;
            }
            else
            {
                Autopilot.SetStatus("Stage 4");
                Status = TaskStatus.Complete;
            }
        }
        #endregion

        WaypointTask LeadTask;
        WaypointTask MineTask;
        MyGridProgram Program;
        HoneybeeMiningSystem MiningSystem;
        IAutopilot Autopilot;
        IAgentSubsystem AgentSubsystem;
        MyTuple<IntelItemType, long> IntelKey;
        AsteroidIntel Host;
        bool TargetPositionSet = false;
        Vector3D HostCenter;
        ITask HomeTask;

        int state = 0;

        public HoneyMiningTask(MyGridProgram program, HoneybeeMiningSystem miningSystem, IAutopilot autopilot, IAgentSubsystem agentSubsystem, Waypoint target, AsteroidIntel host, ITask homeTask)
        {
            Program = program;
            MiningSystem = miningSystem;
            Autopilot = autopilot;
            AgentSubsystem = agentSubsystem;
            Host = host;

            Status = TaskStatus.Incomplete;

            var HostCenterToTarget = target.Position - host.Position;
            var HostCenterToTargetDist = HostCenterToTarget.Length();
            HostCenterToTarget.Normalize();

            var EntryPoint = host.Position + HostCenterToTarget * (HostCenterToTargetDist + 20);
            HostCenter = host.Position;

            LeadTask = new WaypointTask(Program, Autopilot, new Waypoint(), WaypointTask.AvoidObstacleMode.SmartEnter);
            MineTask = new WaypointTask(Program, Autopilot, new Waypoint(), WaypointTask.AvoidObstacleMode.DoNotAvoid);

            LeadTask.Destination.Direction = HostCenterToTarget * -1;
            LeadTask.Destination.Position = EntryPoint;
            MineTask.Destination.Direction = HostCenterToTarget * -1;
            MineTask.Destination.Position = HostCenter;

            MineTask.Destination.MaxSpeed = 1;

            HomeTask = homeTask;
        }
    }
}
