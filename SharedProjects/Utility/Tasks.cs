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
        Attack = 8,
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

    public class DockTaskGenerator : ITaskGenerator
    {
        #region ITaskGenerator
        public TaskType AcceptedTypes => TaskType.Dock; // | TaskType.SmartMove;

        public ITask GenerateTask(TaskType type, MyTuple<IntelItemType, long> intelKey, Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence> IntelItems, TimeSpan canonicalTime, long myID)
        {
            if (intelKey.Item1 != IntelItemType.Dock)
            {
                return new NullTask();
            }
            if (!IntelItems.ContainsKey(intelKey))
            {
                return new NullTask();
            }
            var Dock = (DockIntel)IntelItems[intelKey];

            var approachTask = new WaypointTask(Program, Autopilot, new Waypoint(), WaypointTask.AvoidObstacleMode.SmartEnter, DockingSubsystem.Connector);
            var enterTask = new WaypointTask(Program, Autopilot, new Waypoint(), WaypointTask.AvoidObstacleMode.DoNotAvoid, DockingSubsystem.Connector);
            var closeTask = new WaypointTask(Program, Autopilot, new Waypoint(), WaypointTask.AvoidObstacleMode.DoNotAvoid, DockingSubsystem.Connector);
            var dockTask = new DockTask(DockingSubsystem);

            return new MoveToAndDockTask(approachTask, enterTask, closeTask, dockTask, intelKey, DockingSubsystem.Connector.CubeGrid.GridSizeEnum, DockingSubsystem.Connector, DockingSubsystem.DirectionIndicator);
        }
        #endregion

        readonly IAutopilot Autopilot;
        readonly MyGridProgram Program;
        readonly IDockingSubsystem DockingSubsystem;

        public DockTaskGenerator(MyGridProgram program, IAutopilot autopilot, IDockingSubsystem ds)
        {
            Program = program;
            Autopilot = autopilot;
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

            var task = new CompoundTask();
            task.TaskQueue.Enqueue(new DockTask(DockingSubsystem, true));
            var w = new Waypoint();
            w.Position = DockingSubsystem.Connector.WorldMatrix.Backward * 40 + DockingSubsystem.Connector.WorldMatrix.Translation;
            task.TaskQueue.Enqueue(new WaypointTask(Program, Autopilot, w, WaypointTask.AvoidObstacleMode.DoNotAvoid, DockingSubsystem.Connector));
            task.TaskQueue.Enqueue(mainTask);

            return task;
        }
        #endregion

        readonly IAutopilot Autopilot;
        readonly MyGridProgram Program;
        readonly IDockingSubsystem DockingSubsystem;
        public UndockFirstTaskGenerator(MyGridProgram program, IAutopilot autopilot, IDockingSubsystem dockingSubsystem)
        {
            Autopilot = autopilot;
            DockingSubsystem = dockingSubsystem;
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
        void Do(Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence> IntelItems, TimeSpan canonicalTime);
        TaskStatus Status { get; }
    }

    public struct NullTask : ITask
    {
        public TaskStatus Status => TaskStatus.Aborted;
        public void Do(Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence> IntelItems, TimeSpan canonicalTime)
        {
        }
    }
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

        public void Do(Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence> IntelItems, TimeSpan canonicalTime)
        {
            if (!Cleared)
            {
                Autopilot.Clear();
                Cleared = true;
            }
            Autopilot.Move(ObstacleMode == AvoidObstacleMode.DoNotAvoid ? Destination.Position : PlotPath(IntelItems, canonicalTime));
            Autopilot.Turn(Destination.Direction);
            Autopilot.Spin(Destination.DirectionUp);
            Autopilot.SetMaxSpeed(Destination.MaxSpeed);
            Autopilot.SetMoveReference(MoveReference);
        }
        #endregion

        public enum AvoidObstacleMode
        {
            Avoid,
            SmartEnter,
            DoNotAvoid,
        }

        readonly MyGridProgram Program;
        readonly IAutopilot Autopilot;
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
            IntelScratchpad = new List<IFleetIntelligence>();
            PositionScratchpad = new List<Vector3>();
            ObstacleMode = avoidObstacleMode;
            MoveReference = moveReference;
            Cleared = false;
        }

        Vector3D PlotPath(Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence> IntelItems, TimeSpan canonicalTime)
        {
            Vector3D o = Autopilot.Controller.CubeGrid.WorldMatrix.Translation;
            Vector3D targetPosition = Destination.Position;
            float SafetyRadius = (float)(Autopilot.Controller.CubeGrid.WorldAABB.Max - Autopilot.Controller.CubeGrid.WorldAABB.Center).Length();
            float brakingDist = Autopilot.GetBrakingDistance();

            IntelScratchpad.Clear();
            PositionScratchpad.Clear();

            bool type1 = false;

            // Quick filter on intel items that might interfere with pathing at this time based on type and distance
            foreach (var kvp in IntelItems)
            {
                // If it's us, don't care
                if (kvp.Key.Item2 == Program.Me.CubeGrid.EntityId) continue;

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

                            if (angle < 0.1)
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
    public struct DockTask : ITask
    {
        #region ITask
        public TaskStatus Status { get; private set; }

        public void Do(Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence> IntelItems, TimeSpan canonicalTime)
        {
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
        #endregion

        IDockingSubsystem DockingSubsystem;

        bool Undock;

        public DockTask(IDockingSubsystem dockingSubsystem, bool undock = false)
        {
            DockingSubsystem = dockingSubsystem;
            Status = TaskStatus.Incomplete;
            Undock = undock;
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

        public virtual void Do(Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence> IntelItems, TimeSpan canonicalTime)
        {
            var task = TaskQueue.Peek();
            task.Do(IntelItems, canonicalTime);
            if (task.Status == TaskStatus.Complete)
                TaskQueue.Dequeue();
            else if (task.Status == TaskStatus.Aborted && !ContinueOnAbort)
            {
                TaskQueue.Clear();
                Aborted = true;
            }
            else if (task.Status == TaskStatus.Aborted && ContinueOnAbort)
                TaskQueue.Dequeue();
        }
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
    }
    public class MoveToAndDockTask : CompoundTask
    {
        WaypointTask ApproachTask;
        WaypointTask EnterTask;
        WaypointTask CloseTask;
        DockTask DockTask;
        MyTuple<IntelItemType, long> IntelKey;
        MyCubeSize DockSize;

        IMyTerminalBlock Connector;
        IMyTerminalBlock Indicator;
        public MoveToAndDockTask(WaypointTask approachTask, WaypointTask enterTask, WaypointTask closeTask, DockTask dockTask, MyTuple<IntelItemType, long> intelKey, MyCubeSize dockSize, IMyTerminalBlock connector, IMyTerminalBlock indicator = null) : base (false)
        {
            if (indicator != null)
            {
                Indicator = indicator;
                Connector = connector;
            }
            ApproachTask = approachTask;
            EnterTask = enterTask;
            CloseTask = closeTask;
            DockTask = dockTask;
            IntelKey = intelKey;
            DockSize = dockSize;

            closeTask.Destination.MaxSpeed = 0.5f;

            TaskQueue.Enqueue(approachTask);
            TaskQueue.Enqueue(enterTask);
            TaskQueue.Enqueue(closeTask);
            TaskQueue.Enqueue(dockTask);
        }

        public override void Do(Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence> IntelItems, TimeSpan canonicalTime)
        {
            if (!IntelItems.ContainsKey(IntelKey))
            {
                Aborted = true;
                TaskQueue.Clear();
            }

            DockIntel dock = (DockIntel)IntelItems[IntelKey];

            Vector3 approachPoint = dock.WorldMatrix.Forward * dock.UndockFar + dock.GetPositionFromCanonicalTime(canonicalTime);
            Vector3 entryPoint = dock.WorldMatrix.Forward * (dock.UndockNear + (DockSize == MyCubeSize.Large ? 1.25f : 0.5f) + 1) + dock.GetPositionFromCanonicalTime(canonicalTime);
            Vector3 closePoint = dock.WorldMatrix.Forward * (dock.UndockNear + (DockSize == MyCubeSize.Large ? 1.25f : 0.5f)) + dock.GetPositionFromCanonicalTime(canonicalTime);

            Vector3 dockDirection = dock.WorldMatrix.Backward;

            ApproachTask.Destination.Direction = dockDirection;
            ApproachTask.Destination.Position = approachPoint;

            EnterTask.Destination.Direction = dockDirection;
            EnterTask.Destination.Position = entryPoint;

            CloseTask.Destination.Direction = dockDirection;
            CloseTask.Destination.Position = closePoint;

            if (Indicator != null && dock.IndicatorDir != Vector3D.Zero)
            {
                var tDir = Vector3D.TransformNormal(Vector3D.TransformNormal(dock.IndicatorDir, MatrixD.Transpose(MatrixD.CreateFromDir(Connector.WorldMatrix.Forward, Indicator.WorldMatrix.Forward))), Connector.WorldMatrix);
                ApproachTask.Destination.DirectionUp = tDir;// Vector3D.Transform(dock.IndicatorDir, indicatorRot);
                EnterTask.Destination.DirectionUp = tDir;// Vector3D.Transform(dock.IndicatorDir, indicatorRot);
                CloseTask.Destination.DirectionUp = tDir;// Vector3D.Transform(dock.IndicatorDir, indicatorRot);
            }

            base.Do(IntelItems, canonicalTime);
        }
    }
    #endregion
}
