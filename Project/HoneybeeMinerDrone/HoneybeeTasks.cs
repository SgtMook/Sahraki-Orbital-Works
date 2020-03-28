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

            return new HoneyMiningTask(Program, MiningSystem, Autopilot, AgentSubsystem, target, host, dockTask, IntelProvider, MonitorSubsystem);
        }
        #endregion

        MyGridProgram Program;
        HoneybeeMiningSystem MiningSystem;
        IAutopilot Autopilot;
        IAgentSubsystem AgentSubsystem;
        DockTaskGenerator DockTaskGenerator;
        IIntelProvider IntelProvider;
        IMonitorSubsystem MonitorSubsystem;

        public HoneybeeMiningTaskGenerator(MyGridProgram program, HoneybeeMiningSystem miningSystem, IAutopilot autopilot, IAgentSubsystem agentSubsystem, DockTaskGenerator dockTaskGenerator, IIntelProvider intelProvder, IMonitorSubsystem monitorSubsystem)
        {
            Program = program;
            MiningSystem = miningSystem;
            Autopilot = autopilot;
            AgentSubsystem = agentSubsystem;
            DockTaskGenerator = dockTaskGenerator;
            IntelProvider = intelProvder;
            MonitorSubsystem = monitorSubsystem;
        }
    }


    public class HoneyMiningTask : ITask
    {
        #region ITask
        public TaskStatus Status { get; private set; }

        public void Do(Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence> IntelItems, TimeSpan canonicalTime, Profiler profiler)
        {
            if (state < 3 && MiningSystem.Recalling > 0)
            {
                state = 3;
            }
            if (state == 0) // Approaching asteroid
            {
                LeadTask.Do(IntelItems, canonicalTime, profiler);
                if (LeadTask.Status == TaskStatus.Complete) state = 1;
            }
            else if (state == 1) // Diving to surface of asteroid
            {
                MineTask.Do(IntelItems, canonicalTime, profiler);

                if (MineTask.Status == TaskStatus.Complete || !MiningSystem.SensorsClear())
                {
                    EntryPoint = Autopilot.Reference.WorldMatrix.Translation;
                    MineTask.Destination.MaxSpeed = 1f;
                    state = 2;
                }
            }
            else if (state == 2) // Boring tunnel
            {
                if (MiningSystem.SensorsClear())
                    MineTask.Destination.Position = MiningEnd;
                else if (MiningSystem.SensorsBack())
                    MineTask.Destination.Position = EntryPoint;
                else
                    MineTask.Destination.Position = Vector3D.Zero;

                MiningSystem.Drill();
                MineTask.Do(IntelItems, canonicalTime, profiler);

                if (GoHomeCheck() || (Autopilot.Reference.WorldMatrix.Translation - MiningEnd).LengthSquared() < 20)
                {
                    state = 3;
                    MineTask.Destination.MaxSpeed = 1;
                }
            }
            else if (state == 3) // Exiting tunnel
            {
                MiningSystem.StopDrill();
                if (MineTask.Destination.Position != ExitPoint) MineTask.Destination.Position = EntryPoint;
                MineTask.Do(IntelItems, canonicalTime, profiler);
                if (MineTask.Status == TaskStatus.Complete)
                {
                    if (MineTask.Destination.Position == EntryPoint)
                    {
                        MineTask.Destination.Position = ExitPoint;
                        MineTask.Destination.MaxSpeed = 100;
                    }
                    else
                    {
                        if (GoHomeCheck()) state = 4;
                        else state = 10;
                    }
                }
            }
            else if (state == 10) // Resuming to approach point
            {
                LeadTask.Destination.Position = ApproachPoint;
                LeadTask.Do(IntelItems, canonicalTime, profiler);
                if (LeadTask.Status == TaskStatus.Complete)
                {
                    currentPosition++;
                    if (currentPosition >= minePositions.Length) state = 4;
                    else
                    {
                        LeadTask.Destination.Position = ApproachPoint + (Perpendicular * minePositions[currentPosition].X * MiningSystem.OffsetDist + Perpendicular.Cross(MineTask.Destination.Direction) * minePositions[currentPosition].Y * MiningSystem.OffsetDist);
                        LeadTask.Destination.MaxSpeed = 10;
                        ExitPoint = LeadTask.Destination.Position;
                        state = 11;
                    }
                }
            }
            else if (state == 11) // Search for the digging spot
            {
                if (GoHomeCheck()) state = 4;
                LeadTask.Do(IntelItems, canonicalTime, profiler);
                if (LeadTask.Status == TaskStatus.Complete)
                {
                    state = 1;
                    MineTask.Destination.Position = SurfacePoint + (Perpendicular * minePositions[currentPosition].X * MiningSystem.OffsetDist + Perpendicular.Cross(MineTask.Destination.Direction) * minePositions[currentPosition].Y * MiningSystem.OffsetDist) - MineTask.Destination.Direction * MiningSystem.CloseDist;
                    MiningEnd = SurfacePoint + (Perpendicular * minePositions[currentPosition].X * MiningSystem.OffsetDist + Perpendicular.Cross(MineTask.Destination.Direction) * minePositions[currentPosition].Y * MiningSystem.OffsetDist) + MineTask.Destination.Direction * MiningDepth;
                }
            }
            else if (state == 4) // Going home
            {
                HomeTask.Do(IntelItems, canonicalTime, profiler);
                if (HomeTask.Status != TaskStatus.Incomplete) state = 5;
            }
            else
            {
                Status = TaskStatus.Complete;
            }
        }

        public string Name => "HoneyMiningTask";
        #endregion

        WaypointTask LeadTask;
        WaypointTask MineTask;
        MyGridProgram Program;
        HoneybeeMiningSystem MiningSystem;
        IAutopilot Autopilot;
        IAgentSubsystem AgentSubsystem;
        IMonitorSubsystem MonitorSubsystem;
        MyTuple<IntelItemType, long> IntelKey;
        AsteroidIntel Host;
        Vector3D EntryPoint;
        Vector3D ExitPoint;
        Vector3D ApproachPoint;
        Vector3D MiningEnd;
        Vector3D Perpendicular;
        Vector3D SurfacePoint;
        ITask HomeTask;

        int currentPosition = 0;
        
        double MiningDepth;
        double SurfaceDist;

        int state = 0;

        Vector2I[] minePositions = new Vector2I[]
        {
            new Vector2I(0,0),
            new Vector2I(0,1),
            new Vector2I(1,0),
            new Vector2I(0,-1),
            new Vector2I(-1,0),
            new Vector2I(1,1),
            new Vector2I(1,-1),
            new Vector2I(-1,1),
            new Vector2I(-1,-1),
            new Vector2I(-1,2),
            new Vector2I(0,2),
            new Vector2I(1,2),
            new Vector2I(2,2),
            new Vector2I(2,1),
            new Vector2I(2,0),
            new Vector2I(2,-1),
            new Vector2I(2,-2),
            new Vector2I(1,-2),
            new Vector2I(0,-2),
            new Vector2I(-1,-2),
            new Vector2I(-2,-2),
            new Vector2I(-2,-1),
            new Vector2I(-2,0),
            new Vector2I(-2,1),
            new Vector2I(-2,2)
        };

        public HoneyMiningTask(MyGridProgram program, HoneybeeMiningSystem miningSystem, IAutopilot autopilot, IAgentSubsystem agentSubsystem, Waypoint target, AsteroidIntel host, ITask homeTask, IIntelProvider intelProvider, IMonitorSubsystem monitorSubsystem)
        {
            Program = program;
            MiningSystem = miningSystem;
            Autopilot = autopilot;
            AgentSubsystem = agentSubsystem;
            MonitorSubsystem = monitorSubsystem;
            Host = host;
            MiningDepth = MiningSystem.MineDepth;

            Status = TaskStatus.Incomplete;

            double lDoc, det;
            GetSphereLineIntersects(host.Position, host.Radius, target.Position, target.Direction, out lDoc, out det);
            Perpendicular = GetPerpendicular(target.Direction);

            if (det < 0)
            {
                Status = TaskStatus.Aborted;
                state = -1;
                return;
            }

            SurfaceDist = -lDoc + Math.Sqrt(det);

            ApproachPoint = target.Position + target.Direction * SurfaceDist;
            ExitPoint = ApproachPoint;

            EntryPoint = target.Position + target.Direction * miningSystem.CloseDist;
            MiningEnd = target.Position - target.Direction * MiningDepth;

            SurfacePoint = target.Position;

            LeadTask = new WaypointTask(Program, Autopilot, new Waypoint(), WaypointTask.AvoidObstacleMode.SmartEnter);
            MineTask = new WaypointTask(Program, Autopilot, new Waypoint(), WaypointTask.AvoidObstacleMode.DoNotAvoid);

            LeadTask.Destination.Position = ApproachPoint;
            LeadTask.Destination.Direction = target.Direction * -1;
            LeadTask.Destination.DirectionUp = Perpendicular;
            intelProvider.ReportFleetIntelligence(LeadTask.Destination, TimeSpan.FromSeconds(1));
            MineTask.Destination.Direction = target.Direction * -1;
            MineTask.Destination.DirectionUp = Perpendicular;
            MineTask.Destination.Position = EntryPoint;

            AgentSubsystem.Status = Perpendicular.Dot(target.Direction).ToString() + " " + Perpendicular.Cross(MineTask.Destination.Direction).Dot(target.Direction).ToString();

            HomeTask = homeTask;
        }

        // https://en.wikipedia.org/wiki/Line%E2%80%93sphere_intersection
        private void GetSphereLineIntersects(Vector3D center, double radius, Vector3D lineStart, Vector3D lineDirection, out double lDoc, out double det)
        {
            lDoc = Vector3.Dot(lineDirection, lineStart - center);
            det = lDoc * lDoc - ((lineStart - center).LengthSquared() - radius * radius);
        }

        private bool GoHomeCheck()
        {
            return MonitorSubsystem.GetPercentage(MonitorOptions.Cargo) > 0.96 ||
                   MonitorSubsystem.GetPercentage(MonitorOptions.Hydrogen) < 0.2 ||
                   MonitorSubsystem.GetPercentage(MonitorOptions.Power) < 0.2;
        }

        private Vector3D GetPerpendicular(Vector3D vector)
        {
            Vector3D result = new Vector3D(1, 1, -(vector.X + vector.Y) / vector.Z);
            result.Normalize();
            return result;
        }
    }
}
