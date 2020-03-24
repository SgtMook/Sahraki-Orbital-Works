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
    // A task is distinct from an action by its scope. A task is typically composed of a series of actions
    [Flags]
    public enum TaskType
    {
        None = 0,
        Move = 1,
        SmartMove = 2,
        Dock = 4,
        SetHome = 8,
        Attack = 16,
        Mine = 32
    }

    public enum TaskStatus
    {
        None,
        Incomplete,
        Complete,
        Aborted
    }

    #region Task Generators
    // A task generator consumes commands in the form of a type and an intel ID, and emits a task
    public interface ITaskGenerator
    {
        TaskType AcceptedTypes { get; }
        ITask GenerateTask(TaskType type, MyTuple<IntelItemType, long> intelKey, Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence> IntelItems, TimeSpan canonicalTime, long myID);

    }

    public class WaypointTaskGenerator : ITaskGenerator
    {
        #region ITaskGenerator
        public TaskType AcceptedTypes => TaskType.Move; // | TaskType.SmartMove;

        public ITask GenerateTask(TaskType type, MyTuple<IntelItemType, long> intelKey, Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence> IntelItems, TimeSpan canonicalTime, long myID)
        {
            if (type != TaskType.Move && type != TaskType.SmartMove) return new NullTask();

            if (type == TaskType.SmartMove)
            {
                // TODO: Implement
                return new NullTask();
            }
            else
            { 
                if (intelKey.Item1 != IntelItemType.Waypoint || !IntelItems.ContainsKey(intelKey)) return new NullTask();

                Waypoint w = (Waypoint)IntelItems[intelKey];
                return new WaypointTask(Program, Autopilot, w);
            }

        }
        #endregion

        readonly IAutopilot Autopilot;
        readonly MyGridProgram Program;

        public WaypointTaskGenerator(MyGridProgram program, IAutopilot autopilot)
        {
            Program = program;
            Autopilot = autopilot;
        }
    }

    // Special command - (None, 0) - Dock with home
    public class DockTaskGenerator : ITaskGenerator
    {
        #region ITaskGenerator
        public TaskType AcceptedTypes => TaskType.Dock;

        public ITask GenerateTask(TaskType type, MyTuple<IntelItemType, long> intelKey, Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence> IntelItems, TimeSpan canonicalTime, long myID)
        {
            return GenerateMoveToAndDockTask(intelKey, IntelItems);
        }
        #endregion

        readonly IAutopilot Autopilot;
        readonly MyGridProgram Program;
        readonly IDockingSubsystem DockingSubsystem;

        readonly MoveToAndDockTask Task;

        WaypointTask holdTask;
        WaypointTask approachTask;
        WaypointTask enterTask;
        WaypointTask closeTask;
        DockTask dockTask;

        public DockTaskGenerator(MyGridProgram program, IAutopilot autopilot, IDockingSubsystem ds)
        {
            Program = program;
            Autopilot = autopilot;
            DockingSubsystem = ds;

            holdTask = new WaypointTask(Program, Autopilot, new Waypoint(), WaypointTask.AvoidObstacleMode.Avoid, DockingSubsystem.Connector);
            approachTask = new WaypointTask(Program, Autopilot, new Waypoint(), WaypointTask.AvoidObstacleMode.SmartEnter, DockingSubsystem.Connector);
            enterTask = new WaypointTask(Program, Autopilot, new Waypoint(), WaypointTask.AvoidObstacleMode.DoNotAvoid, DockingSubsystem.Connector);
            closeTask = new WaypointTask(Program, Autopilot, new Waypoint(), WaypointTask.AvoidObstacleMode.DoNotAvoid, DockingSubsystem.Connector);
            dockTask = new DockTask(DockingSubsystem);
            Task = new MoveToAndDockTask();
            Task.Reset(holdTask, approachTask, enterTask, closeTask, dockTask, MyTuple.Create(IntelItemType.NONE, (long)1234), Program, MyCubeSize.Small, DockingSubsystem.Connector, DockingSubsystem.DirectionIndicator);
            Task.Do(new Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence>(), TimeSpan.FromSeconds(1), null);
            dockTask.Do(new Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence>(), TimeSpan.Zero, null);
            holdTask.Do(new Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence>(), TimeSpan.Zero, null);
            new WaitTask().Do(new Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence>(), TimeSpan.Zero, null);
        }

        public ITask GenerateMoveToAndDockTask(MyTuple<IntelItemType, long> intelKey, Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence> IntelItems, float approachMaxSpeed = 100)
        {
            if (intelKey.Item1 == IntelItemType.NONE && intelKey.Item1 == 0)
            {
                intelKey = MyTuple.Create(IntelItemType.Dock, DockingSubsystem.HomeID);
            }

            if (intelKey.Item1 != IntelItemType.Dock)
            {
                return new NullTask();
            }

            if (!IntelItems.ContainsKey(intelKey))
            {
                return new NullTask();
            }
            var Dock = (DockIntel)IntelItems[intelKey];

            holdTask.Destination.MaxSpeed = approachMaxSpeed;

            Task.Reset(holdTask, approachTask, enterTask, closeTask, dockTask, intelKey, Program, DockingSubsystem.Connector.CubeGrid.GridSizeEnum, DockingSubsystem.Connector, DockingSubsystem.DirectionIndicator);

            return Task;
        }
    }

    // Special command (None, 0) - unset home
    public class SetHomeTaskGenerator : ITaskGenerator
    {
        #region ITaskGenerator
        public TaskType AcceptedTypes => TaskType.SetHome;
        public ITask GenerateTask(TaskType type, MyTuple<IntelItemType, long> intelKey, Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence> IntelItems, TimeSpan canonicalTime, long myID)
        {
            if (type != TaskType.SetHome || (intelKey.Item1 != IntelItemType.Dock && intelKey.Item1 != IntelItemType.NONE)) return new NullTask();
            return new SetHomeTask(intelKey, myID, Program, DockingSubsystem);
        }
        #endregion

        readonly MyGridProgram Program;
        readonly IDockingSubsystem DockingSubsystem;
        public SetHomeTaskGenerator(MyGridProgram program, IDockingSubsystem ds)
        {
            Program = program;
            DockingSubsystem = ds;
        }
    }


    public class UndockFirstTaskGenerator : ITaskGenerator
    {
        #region ITaskGenerator
        public TaskType AcceptedTypes { get; private set; }

        public ITask GenerateTask(TaskType type, MyTuple<IntelItemType, long> intelKey, Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence> IntelItems, TimeSpan canonicalTime, long myID)
        {
            if ((AcceptedTypes & type) == 0) return new NullTask();

            var mainTask = TaskGenerators[type].GenerateTask(type, intelKey, IntelItems, canonicalTime, myID);

            if (DockingSubsystem.Connector.Status != MyShipConnectorStatus.Connected) return mainTask;

            CompoundTask.Reset();
            UndockSeperationTask.Reset(canonicalTime);
            CompoundTask.TaskQueue.Enqueue(UndockSeperationTask);
            CompoundTask.TaskQueue.Enqueue(mainTask);

            return CompoundTask;
        }
        #endregion

        readonly IAutopilot Autopilot;
        readonly MyGridProgram Program;
        readonly IDockingSubsystem DockingSubsystem;

        CompoundTask CompoundTask = new CompoundTask();
        UndockSeperationTask UndockSeperationTask;

        public UndockFirstTaskGenerator(MyGridProgram program, IAutopilot autopilot, IDockingSubsystem dockingSubsystem)
        {
            Autopilot = autopilot;
            DockingSubsystem = dockingSubsystem;
            CompoundTask.Do(new Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence>(), TimeSpan.Zero, null);
            CompoundTask.Reset();

            UndockSeperationTask = new UndockSeperationTask(Autopilot, DockingSubsystem);
            UndockSeperationTask.Do(new Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence>(), TimeSpan.Zero, null);
            DockingSubsystem.Undock(true);
        }

        Dictionary<TaskType, ITaskGenerator> TaskGenerators = new Dictionary<TaskType, ITaskGenerator>();
        public void AddTaskGenerator(ITaskGenerator taskGenerator)
        {
            AcceptedTypes |= taskGenerator.AcceptedTypes;
            for (int i = 0; i < 30; i++)
            {
                if ((1 << i & (int)taskGenerator.AcceptedTypes) != 0)
                    TaskGenerators[(TaskType)(1 << i)] = taskGenerator;
            }
        }
    }
    #endregion

    #region Tasks
    public interface ITask
    {
        void Do(Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence> IntelItems, TimeSpan canonicalTime, Profiler profiler);
        TaskStatus Status { get; }
        string Name { get; }
    }

    public struct NullTask : ITask
    {
        public TaskStatus Status => TaskStatus.Aborted;

        public void Do(Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence> IntelItems, TimeSpan canonicalTime, Profiler profiler)
        {
        }

        public string Name => "NullTask";
    }

    // Don't ask why this is a struct
    public struct WaypointTask : ITask
    {

        #region ITask
        public TaskStatus Status
        {
            get
            {
                if (Autopilot.AtWaypoint(Destination))
                {
                    Autopilot.Clear();
                    return TaskStatus.Complete;
                }
                return TaskStatus.Incomplete;
            }
        }

        public void Do(Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence> IntelItems, TimeSpan canonicalTime, Profiler profiler)
        {
            if (!Cleared)
            {
                Autopilot.Clear();
                Cleared = true;
            }
            Autopilot.Move(ObstacleMode == AvoidObstacleMode.DoNotAvoid ? Destination.Position : WaypointHelper.PlotPath(IntelItems, canonicalTime, Autopilot, Destination, IntelScratchpad, PositionScratchpad, Program));
            Autopilot.Turn(Destination.Direction);
            Autopilot.Spin(Destination.DirectionUp);
            Autopilot.Drift(Destination.Velocity);
            Autopilot.SetMaxSpeed(Destination.MaxSpeed);
            Autopilot.Reference = MoveReference;

            if (canonicalTime == TimeSpan.Zero)
            {
                Autopilot.AtWaypoint(Destination);
                Autopilot.Clear();
            }
        }

        public string Name => "WaypointTask";
        #endregion

        public enum AvoidObstacleMode
        {
            Avoid,
            SmartEnter,
            DoNotAvoid,
        }

        readonly MyGridProgram Program;
        public readonly IAutopilot Autopilot;
        public Waypoint Destination;

        readonly List<IFleetIntelligence> IntelScratchpad;
        readonly List<Vector3> PositionScratchpad;

        readonly AvoidObstacleMode ObstacleMode;
        readonly IMyTerminalBlock MoveReference;

        bool Cleared;

        public WaypointTask(MyGridProgram program, IAutopilot pilotSubsystem, Waypoint waypoint, AvoidObstacleMode avoidObstacleMode = AvoidObstacleMode.SmartEnter, IMyTerminalBlock moveReference = null)
        {
            Autopilot = pilotSubsystem;
            Program = program;
            Destination = waypoint;
            IntelScratchpad = new List<IFleetIntelligence>(64);
            PositionScratchpad = new List<Vector3>(64);
            ObstacleMode = avoidObstacleMode;
            MoveReference = moveReference;
            Cleared = false;

            WaypointHelper.PlotPath(new Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence>(), TimeSpan.FromSeconds(1), Autopilot, Destination, IntelScratchpad, PositionScratchpad, Program);
        }
    }

    public class WaypointHelper
    {
        public static Vector3D PlotPath(Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence> IntelItems, TimeSpan canonicalTime, IAutopilot Autopilot, Waypoint Destination, List<IFleetIntelligence> IntelScratchpad, List<Vector3> PositionScratchpad, MyGridProgram Program)
        {
            if (Autopilot.Reference == null) return Vector3D.Zero;
            Vector3D o = Autopilot.Reference.WorldMatrix.Translation;
            Vector3D targetPosition = Destination.Position;
            float SafetyRadius = (float)(Autopilot.Controller.CubeGrid.WorldAABB.Max - Autopilot.Controller.CubeGrid.WorldAABB.Min).Length();
            float brakingDist = Autopilot.GetBrakingDistance();

            IntelScratchpad.Clear();
            PositionScratchpad.Clear();

            bool type1 = false;
            // Quick filter on intel items that might interfere with pathing at this time based on type and distance
            foreach (var kvp in IntelItems)
            {
                // If it's us, don't care
                if (kvp.Key.Item2 == Program.Me.CubeGrid.EntityId) continue;
                if (kvp.Value.Radius == 0) continue;

                // If it's an asteroid or a ship, we might be interested
                if (kvp.Value.IntelItemType == IntelItemType.Asteroid || kvp.Value.IntelItemType == IntelItemType.Friendly || kvp.Value.IntelItemType == IntelItemType.Enemy)
                {
                    Vector3D c = kvp.Value.GetPositionFromCanonicalTime(canonicalTime);
                    float r = kvp.Value.Radius + SafetyRadius;
                    double distTo = (c - o).Length();
                    // Check if distance is close enough. If so, shortlist this.
                    if (distTo < r + brakingDist + Autopilot.Controller.GetShipSpeed() * 0.16 + 100)
                    {
                        IntelScratchpad.Add(kvp.Value);
                        PositionScratchpad.Add(c);
                        // If distance is closer than, we are inside its bounding sphere and must escape unless our destination is also in the radius
                        if (distTo < r && (Destination.Position - c).Length() > r)
                        {
                            type1 = true;
                            break;
                        }
                    }
                }
            }
            Vector3D target = Destination.Position;

            if (type1)
            {
                // Escape maneuver - move directly away from center of bounding sphere
                var dir = o - PositionScratchpad.Last();
                dir.Normalize();
                target = dir * (IntelScratchpad.Last().Radius + SafetyRadius * 2) + PositionScratchpad.Last();
            }
            else if (IntelScratchpad.Count > 0)
            {
                bool targetClear;

                int iter = 0;

                // Find a clear path around any obstacles:
                do
                {
                    iter += 1;
                    targetClear = true;
                    IFleetIntelligence closestObstacle = null;
                    float closestDist = float.MaxValue;
                    float closestApporoach = 0;
                    bool closestType3 = false;

                    var l = target - o;
                    double d = l.Length();
                    l.Normalize();

                    // Go through each intel item we shortlisted earlier
                    for (int i = 0; i < IntelScratchpad.Count; i++)
                    {
                        float lDoc = Vector3.Dot(l, o - PositionScratchpad[i]);
                        double det = lDoc * lDoc - ((o - PositionScratchpad[i]).LengthSquared() - (IntelScratchpad[i].Radius + SafetyRadius) * (IntelScratchpad[i].Radius + SafetyRadius));

                        // Check if we intersect the sphere at all
                        if (det > 0)
                        {
                            // Check if this is a type 2 obstacle - that is, we enter its bounding sphere and the closest approach is some point along our path.
                            if (-lDoc > 0 && -lDoc < d)
                            {
                                closestObstacle = IntelScratchpad[i];
                                var distIntersect = -lDoc - (float)Math.Sqrt(det);

                                // Only care about the closest one. Hopefully this works well enough in practice.
                                if (closestDist > distIntersect)
                                {
                                    closestDist = distIntersect;
                                    closestApporoach = -lDoc;
                                    closestObstacle = IntelScratchpad[i];
                                    closestType3 = false;
                                }
                            }
                            // Check if this is a type 3 obstacle - that is, we enter its bonding sphere and the destination is inside
                            else if ((target - PositionScratchpad[i]).Length() < IntelScratchpad[i].Radius + SafetyRadius)
                            {
                                var distIntersect = -lDoc - (float)Math.Sqrt(det);
                                if (closestDist > distIntersect)
                                {
                                    closestDist = distIntersect;
                                    closestApporoach = -lDoc;
                                    closestObstacle = IntelScratchpad[i];
                                    closestType3 = true;
                                }
                            }
                        }
                    }

                    // If there is a potential collision
                    if (closestDist != float.MaxValue)
                    {
                        targetClear = false;
                        Vector3D closestObstaclePos = closestObstacle.GetPositionFromCanonicalTime(canonicalTime);
                        Vector3D v;

                        if (!closestType3)
                        {
                            var c = l * closestApporoach + o;
                            Vector3D dir = c - closestObstaclePos;
                            dir.Normalize();
                            v = dir * (closestObstacle.Radius + SafetyRadius * 2) + closestObstaclePos;
                            var vdir = v - o;
                            vdir.Normalize();
                            target = o + vdir * (o - Destination.Position).Length();
                        }
                        else
                        {
                            Vector3D dirCenterToDest = target - closestObstaclePos;
                            dirCenterToDest.Normalize();
                            Vector3D dirCenterToMe = o - closestObstaclePos;
                            var distToMe = dirCenterToMe.Length();
                            dirCenterToMe.Normalize();
                            var angle = Math.Acos(Vector3.Dot(dirCenterToDest, dirCenterToMe));

                            if (angle < 0.2)
                            {
                                target = Destination.Position;
                                break;
                            }
                            else if (angle > 0.6 && distToMe < (closestObstacle.Radius + SafetyRadius))
                            {
                                target = dirCenterToMe * (closestObstacle.Radius + SafetyRadius * 2) + closestObstaclePos;
                                break;
                            }
                            else
                            {
                                target = dirCenterToDest * (closestObstacle.Radius + SafetyRadius * 2) + closestObstaclePos;
                            }
                        }
                    }

                } while (!targetClear && iter < 5);
            }
            return target;
        }
    }

    public class UndockSeperationTask : ITask
    {
        #region ITask
        public TaskStatus Status { get; private set; }

        public void Do(Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence> IntelItems, TimeSpan canonicalTime, Profiler profiler)
        {
            if (canonicalTime == TimeSpan.Zero) return;
            if (DockingSubsystem.Connector.Status == MyShipConnectorStatus.Connected) DockingSubsystem.Undock();

            AutopilotSubsystem.Drift(Drift);
            AutopilotSubsystem.Controller.DampenersOverride = false;

            var deltaT = canonicalTime - StartTime;

            if ((DockingSubsystem.Connector.WorldMatrix.Translation - (ExpectedPosition + ExpectedVelocity * deltaT.TotalSeconds)).Length() > 80)
            {
                AutopilotSubsystem.Clear();
                Status = TaskStatus.Complete;
            }
        }

        public string Name => "UndockSeperationTask";
        #endregion

        TimeSpan StartTime;
        IAutopilot AutopilotSubsystem;
        IDockingSubsystem DockingSubsystem;

        Vector3D Drift;
        Vector3D ExpectedPosition;
        Vector3D ExpectedVelocity;

        public UndockSeperationTask(IAutopilot autopilotSubsystem, IDockingSubsystem dockingSubsystem)
        {
            AutopilotSubsystem = autopilotSubsystem;
            DockingSubsystem = dockingSubsystem;
        }

        public void Reset(TimeSpan canonicalTime)
        {
            Status = TaskStatus.Incomplete;
            StartTime = canonicalTime;
            ExpectedVelocity = AutopilotSubsystem.Controller.GetShipVelocities().LinearVelocity;
            Drift = ExpectedVelocity + (DockingSubsystem.Connector.Status == MyShipConnectorStatus.Connected ? (DockingSubsystem.Connector.OtherConnector.WorldMatrix.Forward) : (DockingSubsystem.Connector.WorldMatrix.Backward)) * 30;
            ExpectedPosition = DockingSubsystem.Connector.WorldMatrix.Translation;
        }
    }

    public struct DockTask : ITask
    {
        #region ITask
        public TaskStatus Status { get; private set; }

        public void Do(Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence> IntelItems, TimeSpan canonicalTime, Profiler profiler)
        {
            if (canonicalTime == TimeSpan.Zero) return;
            if (DockingSubsystem.Connector.Status == MyShipConnectorStatus.Connected)
            {
                if (Undock)
                    DockingSubsystem.Undock();
                Status = TaskStatus.Complete;
            }
            else if (DockingSubsystem.Connector.Status == MyShipConnectorStatus.Unconnected)
            {
                if (Undock)
                    Status = TaskStatus.Complete;
                else
                    Status = TaskStatus.Aborted;
            }
            else
            {
                if (!Undock)
                    DockingSubsystem.Dock();
                Status = TaskStatus.Complete;
            }
        }

        public string Name => "DockTask";
        #endregion

        public IDockingSubsystem DockingSubsystem;

        bool Undock;

        public DockTask(IDockingSubsystem dockingSubsystem, bool undock = false)
        {
            DockingSubsystem = dockingSubsystem;
            Status = TaskStatus.Incomplete;
            Undock = undock;
        }
    }
    public class WaitTask : ITask
    {
        #region ITask
        public TaskStatus Status { get; set; }

        public void Do(Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence> IntelItems, TimeSpan canonicalTime, Profiler profiler)
        {
        }

        public string Name => "WaitTask";
        #endregion
        public WaitTask()
        {
            Status = TaskStatus.Incomplete;
        }
    }

    public class CompoundTask : ITask
    {
        #region ITask
        public TaskStatus Status
        {
            get
            {
                if (TaskQueue.Count > 0)
                    return TaskStatus.Incomplete;
                else if (Aborted)
                    return TaskStatus.Aborted;
                else
                    return TaskStatus.Complete;
            }
        }

        public virtual void Do(Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence> IntelItems, TimeSpan canonicalTime, Profiler profiler)
        {
            while (true)
            {
                if (TaskQueue.Count == 0) return;
                var task = TaskQueue.Peek();
                task.Do(IntelItems, canonicalTime, profiler);
                if (task.Status == TaskStatus.Complete)
                    TaskQueue.Dequeue();
                else if (task.Status == TaskStatus.Aborted && !ContinueOnAbort)
                {
                    TaskQueue.Clear();
                    Aborted = true;
                    break;
                }
                else if (task.Status == TaskStatus.Aborted && ContinueOnAbort)
                    TaskQueue.Dequeue();
                else
                    break;
            }
        }

        public string Name => "CompoundTask";
        #endregion
        bool ContinueOnAbort;
        protected bool Aborted;
        public readonly Queue<ITask> TaskQueue;

        public CompoundTask(bool continueOnAbort = false)
        {
            ContinueOnAbort = continueOnAbort;
            Aborted = false;
            TaskQueue = new Queue<ITask>();
        }

        public void Reset()
        {
            Aborted = false;
            TaskQueue.Clear();
        }
    }
    public class MoveToAndDockTask : CompoundTask
    {
        WaypointTask EnterHoldingPattern;
        WaitTask WaitForClearance;
        WaypointTask ApproachEntrance;
        WaypointTask ApproachDock;
        WaypointTask FinalAdjustToDock;
        DockTask DockTask;
        MyTuple<IntelItemType, long> IntelKey;
        MyCubeSize DockSize;

        IMyTerminalBlock Connector;
        IMyTerminalBlock Indicator;
        MyGridProgram Program;
        public void Reset(WaypointTask holdTask, WaypointTask approachTask, WaypointTask enterTask, WaypointTask closeTask, DockTask dockTask, MyTuple<IntelItemType, long> intelKey, MyGridProgram program, MyCubeSize dockSize, IMyTerminalBlock connector, IMyTerminalBlock indicator = null)
        {
            Reset();
            if (indicator != null)
            {
                Indicator = indicator;
                Connector = connector;
            }
            EnterHoldingPattern = holdTask;
            WaitForClearance = new WaitTask();
            ApproachEntrance = approachTask;
            ApproachDock = enterTask;
            FinalAdjustToDock = closeTask;
            DockTask = dockTask;
            IntelKey = intelKey;
            DockSize = dockSize;
            Program = program;

            closeTask.Destination.MaxSpeed = 0.5f;
            enterTask.Destination.MaxSpeed = 20;

            TaskQueue.Enqueue(EnterHoldingPattern);
            TaskQueue.Enqueue(WaitForClearance);
            TaskQueue.Enqueue(ApproachEntrance);
            TaskQueue.Enqueue(ApproachDock);
            TaskQueue.Enqueue(FinalAdjustToDock);
            TaskQueue.Enqueue(DockTask);
        }

        public override void Do(Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence> IntelItems, TimeSpan canonicalTime, Profiler profiler)
        {
            if (!IntelItems.ContainsKey(IntelKey))
            {
                Aborted = true;
                TaskQueue.Clear();
                return;
            }

            DockIntel dock = (DockIntel)IntelItems[IntelKey];

            Vector3D dockPosition = dock.GetPositionFromCanonicalTime(canonicalTime);

            Vector3 approachPoint = dock.WorldMatrix.Forward * dock.UndockFar + dockPosition;
            Vector3 entryPoint = dock.WorldMatrix.Forward * (dock.UndockNear + (DockSize == MyCubeSize.Large ? 1.25f : 0.5f) + 1) + dockPosition;
            Vector3 closePoint = dock.WorldMatrix.Forward * (dock.UndockNear + (DockSize == MyCubeSize.Large ? 1.25f : 0.5f)) + dockPosition;

            Vector3 dockDirection = dock.WorldMatrix.Backward;

            Vector3D dockToMeDir = Program.Me.WorldMatrix.Translation - dockPosition;
            double dockToMeDist = dockToMeDir.Length();
            ApproachEntrance.Destination.Direction = dockDirection;
            if (dockToMeDist < 250 && dockToMeDist > 150)
            {
                EnterHoldingPattern.Destination.Position = Vector3D.Zero;
            }
            else
            {
                dockToMeDir.Normalize();
                Vector3 holdPoint = dockToMeDir * 200 + dockPosition;
                EnterHoldingPattern.Destination.Position = holdPoint;
                EnterHoldingPattern.Destination.Velocity = dock.GetVelocity();
            }

            ApproachEntrance.Destination.Direction = dockDirection;
            ApproachEntrance.Destination.Position = approachPoint;
            ApproachEntrance.Destination.Velocity = dock.GetVelocity();

            ApproachDock.Destination.Direction = dockDirection;
            ApproachDock.Destination.Position = entryPoint;
            ApproachDock.Destination.Velocity = dock.GetVelocity();

            FinalAdjustToDock.Destination.Direction = dockDirection;
            FinalAdjustToDock.Destination.Position = closePoint;
            FinalAdjustToDock.Destination.Velocity = dock.GetVelocity();

            if (Indicator != null && dock.IndicatorDir != Vector3D.Zero)
            {
                var tDir = Vector3D.TransformNormal(Vector3D.TransformNormal(dock.IndicatorDir, MatrixD.Transpose(MatrixD.CreateFromDir(Connector.WorldMatrix.Forward, Indicator.WorldMatrix.Forward))), Connector.WorldMatrix);
                ApproachEntrance.Destination.DirectionUp = tDir;
                ApproachDock.Destination.DirectionUp = tDir;
                FinalAdjustToDock.Destination.DirectionUp = tDir;
            }

            if (TaskQueue.Count < 6)
            {
                Program.IGC.SendBroadcastMessage(dock.HangarChannelTag, MyTuple.Create(Program.Me.CubeGrid.EntityId, dock.ID, (int)HangarRequest.RequestDock));
                if (dock.OwnerID == Program.Me.CubeGrid.EntityId && (dock.Status & HangarStatus.Docking) != 0)
                    WaitForClearance.Status = TaskStatus.Complete;
            }

            if (TaskQueue.Count < 3)
            {
                if (DockTask.DockingSubsystem.Connector.Status == MyShipConnectorStatus.Connectable)
                {
                    FinalAdjustToDock.Autopilot.Clear();
                    DockTask.Do(IntelItems, canonicalTime, profiler);
                    TaskQueue.Clear();
                }
            }

            base.Do(IntelItems, canonicalTime, profiler);
        }
    }

    public class SetHomeTask : ITask
    {
        #region ITask
        public TaskStatus Status { get; private set; }

        public void Do(Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence> IntelItems, TimeSpan canonicalTime, Profiler profiler)
        {
            if (IntelKey.Item1 == IntelItemType.NONE && IntelKey.Item2 == 0)
            {
                DockingSubsystem.HomeID = -1;
                Status = TaskStatus.Complete;
                return;
            }

            if (!IntelItems.ContainsKey(IntelKey))
            {
                Status = TaskStatus.Aborted;
                return;
            }

            var dock = (DockIntel)IntelItems[IntelKey];
            
            if (dock.OwnerID == MyId && (dock.Status & HangarStatus.Reserved) != 0)
            {
                Status = TaskStatus.Complete;
                DockingSubsystem.HomeID = dock.ID;
                return;
            }

            Program.IGC.SendBroadcastMessage(dock.HangarChannelTag, MyTuple.Create(MyId, dock.ID, (int)HangarRequest.Reserve));
        }

        public string Name => "SetHomeTask";
        #endregion

        readonly MyTuple<IntelItemType, long> IntelKey;
        readonly long MyId;
        readonly MyGridProgram Program;
        readonly IDockingSubsystem DockingSubsystem;

        public SetHomeTask(MyTuple<IntelItemType, long> intelKey, long myId, MyGridProgram program, IDockingSubsystem ds)
        {
            IntelKey = intelKey;
            MyId = myId;
            Program = program;
            DockingSubsystem = ds;
        }
    }

    #endregion
}
