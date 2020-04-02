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

            return new HoneybeeMiningTask(Program, MiningSystem, Autopilot, AgentSubsystem, target, host, IntelProvider, MonitorSubsystem, DockingSubsystem, DockTaskGenerator, UndockTaskGenerator);
        }
        #endregion

        MyGridProgram Program;
        HoneybeeMiningSystem MiningSystem;
        IAutopilot Autopilot;
        IAgentSubsystem AgentSubsystem;
        IDockingSubsystem DockingSubsystem;
        DockTaskGenerator DockTaskGenerator;
        UndockFirstTaskGenerator UndockTaskGenerator;
        IIntelProvider IntelProvider;
        IMonitorSubsystem MonitorSubsystem;

        public HoneybeeMiningTaskGenerator(MyGridProgram program, HoneybeeMiningSystem miningSystem, IAutopilot autopilot, IAgentSubsystem agentSubsystem, IDockingSubsystem dockingSubsystem, DockTaskGenerator dockTaskGenerator, UndockFirstTaskGenerator undockTaskGenerator, IIntelProvider intelProvder, IMonitorSubsystem monitorSubsystem)
        {
            Program = program;
            MiningSystem = miningSystem;
            Autopilot = autopilot;
            AgentSubsystem = agentSubsystem;
            DockTaskGenerator = dockTaskGenerator;
            UndockTaskGenerator = undockTaskGenerator;
            IntelProvider = intelProvder;
            MonitorSubsystem = monitorSubsystem;
            DockingSubsystem = dockingSubsystem;
        }
    }


    public class HoneybeeMiningTask : ITask
    {
        #region ITask
        public TaskStatus Status { get; private set; }

        public void Do(Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence> IntelItems, TimeSpan canonicalTime, Profiler profiler)
        {
            AgentSubsystem.Status = state.ToString();
            if (MiningSystem.Recalling > 0 || currentPosition >= minePositions.Length)
            {
                Recalling = true;
            }
            if (Recalling && state < 3) state = 3;
            if (state == 1) // Diving to surface of asteroid
            {
                MineTask.Do(IntelItems, canonicalTime, profiler);
                MineTask.Destination.MaxSpeed = Autopilot.GetMaxSpeedFromBrakingDistance(kFarSensorDist);

                if (MineTask.Status == TaskStatus.Complete || !MiningSystem.SensorsFarClear())
                {
                    EntryPoint = Autopilot.Reference.WorldMatrix.Translation + MineTask.Destination.Direction * (kFarSensorDist - 10);
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

                if (GoHomeCheck() || MiningSystem.SensorsFarClear() || (Autopilot.Reference.WorldMatrix.Translation - MiningEnd).LengthSquared() < 20)
                {
                    if ((Autopilot.Reference.WorldMatrix.Translation - MiningEnd).LengthSquared() < 20) currentPosition++;
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
                        state = 10;
                    }
                }
            }
            else if (state == 10) // Resuming to approach point
            {
                if (GoHomeCheck() || Recalling) state = 4;
                else
                {
                    LeadTask.Destination.Position = ApproachPoint;
                    LeadTask.Do(IntelItems, canonicalTime, profiler);
                    if (LeadTask.Status == TaskStatus.Complete)
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
                if (GoHomeCheck() || Recalling) state = 4;
                else
                {
                    LeadTask.Do(IntelItems, canonicalTime, profiler);
                    if (LeadTask.Status == TaskStatus.Complete)
                    {
                        state = 1;
                        MineTask.Destination.Position = SurfacePoint + (Perpendicular * minePositions[currentPosition].X * MiningSystem.OffsetDist + Perpendicular.Cross(MineTask.Destination.Direction) * minePositions[currentPosition].Y * MiningSystem.OffsetDist) - MineTask.Destination.Direction * MiningSystem.CloseDist;
                        MiningEnd = SurfacePoint + (Perpendicular * minePositions[currentPosition].X * MiningSystem.OffsetDist + Perpendicular.Cross(MineTask.Destination.Direction) * minePositions[currentPosition].Y * MiningSystem.OffsetDist) + MineTask.Destination.Direction * MiningDepth;
                    }
                }
            }
            else if (state == 4) // Going home
            {
                if (DockingSubsystem.HomeID == -1)
                {
                    state = 9999;
                }
                else
                {
                    if (HomeTask == null)
                    {
                        HomeTask = DockTaskGenerator.GenerateMoveToAndDockTask(MyTuple.Create(IntelItemType.NONE, (long)0), IntelItems, 40);
                    }
                    HomeTask.Do(IntelItems, canonicalTime, profiler);
                    if (HomeTask.Status != TaskStatus.Incomplete)
                    {
                        HomeTask = null;
                        state = 5;
                    }
                }
            }
            else if (state == 5) // Waiting for refuel/unload
            {
                if (Recalling) state = 9999;
                if ((Program.Me.WorldMatrix.Translation - EntryPoint).LengthSquared() > MiningSystem.CancelDist * MiningSystem.CancelDist) state = 9999;
                if (LeaveHomeCheck()) state = 6;
            }
            else if (state == 6) // Undocking
            { 
                if (DockingSubsystem.Connector.Status == MyShipConnectorStatus.Connected)
                {
                    if (UndockTask == null)
                    {
                        UndockTask = UndockTaskGenerator.GenerateUndockTask(canonicalTime);
                    }
                }

                if (UndockTask != null)
                {
                    UndockTask.Do(IntelItems, canonicalTime, profiler);
                    if (UndockTask.Status != TaskStatus.Incomplete)
                    {
                        UndockTask = null;
                        state = 10;
                    }
                }
                else
                {
                    state = 10;
                }
            }
            else if (state == 9999)
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
        IDockingSubsystem DockingSubsystem;
        MyTuple<IntelItemType, long> IntelKey;
        AsteroidIntel Host;
        Vector3D EntryPoint;
        Vector3D ExitPoint;
        Vector3D ApproachPoint;
        Vector3D MiningEnd;
        Vector3D Perpendicular;
        Vector3D SurfacePoint;
        ITask HomeTask = null;
        ITask UndockTask = null;

        DockTaskGenerator DockTaskGenerator;
        UndockFirstTaskGenerator UndockTaskGenerator;

        int currentPosition = 0;
        
        double MiningDepth;
        double SurfaceDist;

        int state = 6;

        bool Recalling = false;

        const int kFarSensorDist = 40;

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
            new Vector2I(-2,2),
            new Vector2I(-3,-2),
            new Vector2I(-3,-1),
            new Vector2I(-3,0),
            new Vector2I(-3,1),
            new Vector2I(-3,2),
            new Vector2I(-3,3),
            new Vector2I(-2,3),
            new Vector2I(-1,3),
            new Vector2I(-0,3),
            new Vector2I(1,3),
            new Vector2I(2,3),
            new Vector2I(3,3),
            new Vector2I(3,2),
            new Vector2I(3,1),
            new Vector2I(3,0),
            new Vector2I(3,-1),
            new Vector2I(3,-2),
            new Vector2I(3,-3),
        };

        public HoneybeeMiningTask(MyGridProgram program, HoneybeeMiningSystem miningSystem, IAutopilot autopilot, IAgentSubsystem agentSubsystem, Waypoint target, AsteroidIntel host, IIntelProvider intelProvider, IMonitorSubsystem monitorSubsystem, IDockingSubsystem dockingSubsystem, DockTaskGenerator dockTaskGenerator, UndockFirstTaskGenerator undockTaskGenerator)
        {
            Program = program;
            MiningSystem = miningSystem;
            Autopilot = autopilot;
            AgentSubsystem = agentSubsystem;
            MonitorSubsystem = monitorSubsystem;
            Host = host;
            MiningDepth = MiningSystem.MineDepth;
            DockingSubsystem = dockingSubsystem;

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

            DockTaskGenerator = dockTaskGenerator;
            UndockTaskGenerator = undockTaskGenerator;
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

        private bool LeaveHomeCheck()
        {
            return MonitorSubsystem.GetPercentage(MonitorOptions.Cargo) < 0.01 &&
                   MonitorSubsystem.GetPercentage(MonitorOptions.Hydrogen) > 0.9 &&
                   MonitorSubsystem.GetPercentage(MonitorOptions.Power) > 0.4;
        }

        private Vector3D GetPerpendicular(Vector3D vector)
        {
            Vector3D result = new Vector3D(1, 1, -(vector.X + vector.Y) / vector.Z);
            result.Normalize();
            return result;
        }
    }
}
