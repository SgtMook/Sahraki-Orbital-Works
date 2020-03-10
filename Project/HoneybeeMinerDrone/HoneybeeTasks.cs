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
            if (type != TaskType.Mine) return new NullTask();
            if (!IntelItems.ContainsKey(intelKey)) return new NullTask();
            if (intelKey.Item1 != IntelItemType.Waypoint) return new NullTask();

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

            if (host == null) return new NullTask();

            var dockTask = DockTaskGenerator.GenerateMoveToAndDockTask(MyTuple.Create(IntelItemType.NONE, (long)0), IntelItems, 40);

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
                LeadTask.Do(IntelItems, canonicalTime);
                if (LeadTask.Status == TaskStatus.Complete) state = 1;
            }
            else if (state == 1)
            {
                MineTask.Do(IntelItems, canonicalTime);
                if (MineTask.Status == TaskStatus.Complete || !MiningSystem.SensorsClear())
                {
                    EntryPoint = Autopilot.Reference.WorldMatrix.Translation;
                    MineTask.Destination.MaxSpeed = 1;
                    state = 2;
                }
            }
            else if (state == 2)
            {
                if (MiningSystem.SensorsClear())
                    MineTask.Destination.Position = MiningEnd;
                else
                    MineTask.Destination.Position = Vector3D.Zero;

                MiningSystem.Drill();
                MineTask.Do(IntelItems, canonicalTime);
                if (MiningSystem.PercentageFilled() > 0.92)
                {
                    state = 3;
                    MineTask.Destination.MaxSpeed = 40;
                }
            }
            else if (state == 3)
            {
                MiningSystem.StopDrill();
                MineTask.Destination.Position = ApproachPoint;
                MineTask.Do(IntelItems, canonicalTime);
                if (MineTask.Status == TaskStatus.Complete) state = 4;
            }
            else if (state == 4)
            {
                HomeTask.Do(IntelItems, canonicalTime);
                if (HomeTask.Status != TaskStatus.Incomplete) state = 5;
            }
            else
            {
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
        Vector3D EntryPoint;
        Vector3D ApproachPoint;
        Vector3D MiningEnd;
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

            double lDoc, det;
            GetSphereLineIntersects(host.Position, host.Radius, target.Position, target.Direction, out lDoc, out det);

            if (det < 0)
            {
                Status = TaskStatus.Aborted;
                state = -1;
                return;
            }

            var d = -lDoc + Math.Sqrt(det);

            ApproachPoint = target.Position + target.Direction * d;

            EntryPoint = target.Position + target.Direction * miningSystem.CloseDist;
            MiningEnd = target.Position - target.Direction * 100;

            LeadTask = new WaypointTask(Program, Autopilot, new Waypoint(), WaypointTask.AvoidObstacleMode.SmartEnter);
            MineTask = new WaypointTask(Program, Autopilot, new Waypoint(), WaypointTask.AvoidObstacleMode.DoNotAvoid);

            LeadTask.Destination.Direction = target.Direction * -1;
            LeadTask.Destination.Position = ApproachPoint;
            MineTask.Destination.Direction = target.Direction * -1;
            MineTask.Destination.Position = EntryPoint;

            HomeTask = homeTask;
        }

        // https://en.wikipedia.org/wiki/Line%E2%80%93sphere_intersection
        private void GetSphereLineIntersects(Vector3D center, double radius, Vector3D lineStart, Vector3D lineDirection, out double lDoc, out double det)
        {
            lDoc = Vector3.Dot(lineDirection, lineStart - center);
            det = lDoc * lDoc - ((lineStart - center).LengthSquared() - radius * radius);
        }
    }
}
