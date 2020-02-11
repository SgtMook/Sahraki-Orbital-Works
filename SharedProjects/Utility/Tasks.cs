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
    /// <summary>
    /// A task generator consumes commands in the form of a type and an intel ID, and emits a task
    /// </summary>
    public interface ITaskGenerator
    {
        TaskType AcceptedTypes { get; }
        ITask GenerateTask(TaskType type, MyTuple<IntelItemType, long> intelKey, Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence> IntelItems, TimeSpan canonicalTime, long myID);
    }

    #region WaypointTaskGenerator
    public class WaypointTaskGenerator : ITaskGenerator
    {
        #region ITaskGenerator
        public TaskType AcceptedTypes => TaskType.Move | TaskType.SmartMove;

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
                return new WaypointTask(Program, Autopilot, w, DebugIntelProvider);
            }

        }
        #endregion

        readonly IAutopilot Autopilot;
        readonly MyGridProgram Program;

        readonly IIntelProvider DebugIntelProvider;

        public WaypointTaskGenerator(MyGridProgram program, IAutopilot autopilot, IIntelProvider debugIntelProvider)
        {
            Program = program;
            Autopilot = autopilot;
            DebugIntelProvider = debugIntelProvider;
        }
    }
    #endregion
    #endregion

    #region Tasks
    public interface ITask
    {
        void Do(Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence> IntelItems, TimeSpan canonicalTime);
        TaskStatus Status { get; }
    }

    #region NullTask
    public struct NullTask : ITask
    {
        public TaskStatus Status => TaskStatus.Aborted;
        public void Do(Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence> IntelItems, TimeSpan canonicalTime)
        {
        }
    }
    #endregion

    #region WaypointTask
    public class WaypointTask : ITask
    {

        #region ITask
        public TaskStatus Status
        {
            get
            {
                return Autopilot.AtWaypoint(Destination) ? TaskStatus.Complete : TaskStatus.Incomplete;
            }
        }

        public void Do(Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence> IntelItems, TimeSpan canonicalTime)
        {
            // PlotPath(IntelItems, canonicalTime);
            Autopilot.Move(PlotPath(IntelItems, canonicalTime));
        }
        #endregion

        readonly MyGridProgram Program;
        readonly IAutopilot Autopilot;
        public Waypoint Destination;

        readonly IIntelProvider DebugIntelProvider;

        readonly Queue<Vector3> Path;

        readonly List<IFleetIntelligence> IntelScratchpad;
        readonly List<Vector3> PositionScratchpad;

        public WaypointTask(MyGridProgram program, IAutopilot pilotSubsystem, Waypoint waypoint, IIntelProvider debugIntelProvider)
        {
            Autopilot = pilotSubsystem;
            Program = program;
            Destination = waypoint;
            DebugIntelProvider = debugIntelProvider;
            Path = new Queue<Vector3>();
            Path.Enqueue(Destination.Position);
            IntelScratchpad = new List<IFleetIntelligence>();
            PositionScratchpad = new List<Vector3>();
        }

        Vector3 PlotPath(Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence> IntelItems, TimeSpan canonicalTime)
        {
            Vector3 o = Autopilot.Controller.CubeGrid.WorldMatrix.Translation;
            Vector3 targetPosition = Destination.GetPositionFromCanonicalTime(canonicalTime);
            float SafetyRadius = (float)(Autopilot.Controller.CubeGrid.WorldAABB.Max - Autopilot.Controller.CubeGrid.WorldAABB.Center).Length();
            float brakingDist = Autopilot.GetBrakingDistance();

            IntelScratchpad.Clear();
            PositionScratchpad.Clear();

            bool inObstacle = false;

            foreach (var kvp in IntelItems)
            {
                if (kvp.Key.Item2 == Program.Me.CubeGrid.EntityId) continue;

                if (kvp.Value.IntelItemType == IntelItemType.Asteroid || kvp.Value.IntelItemType == IntelItemType.Friendly || kvp.Value.IntelItemType == IntelItemType.Enemy)
                {
                    Vector3 c = kvp.Value.GetPositionFromCanonicalTime(canonicalTime);
                    float r = kvp.Value.Radius + SafetyRadius;
                    float distTo = (c - o).Length();
                    if (distTo < r + brakingDist + Autopilot.Controller.GetShipSpeed() * 0.16)
                    {
                        IntelScratchpad.Add(kvp.Value);
                        PositionScratchpad.Add(c);
                        if (distTo < r)
                        {
                            inObstacle = true;
                            break;
                        }
                    }
                }
            }

            Vector3 target = Destination.Position;

            if (inObstacle)
            {
                var dir = o - PositionScratchpad.Last();
                dir.Normalize();
                target = dir * (IntelScratchpad.Last().Radius + SafetyRadius * 2) + PositionScratchpad.Last();
                var w = new Waypoint
                {
                    Position = target,
                    Name = "[OBS] Escape"
                };
                DebugIntelProvider.ReportFleetIntelligence(w, canonicalTime);
            }
            else if (IntelScratchpad.Count > 0)
            {
                bool targetClear;

                int iter = 0;
                do
                {
                    iter += 1;
                    targetClear = true;
                    IFleetIntelligence closestObstacle = null;
                    float closestDist = float.MaxValue;
                    float closestApporoach = 0;

                    var l = target - o;
                    float d = l.Length();
                    l.Normalize();

                    for (int i = 0; i < IntelScratchpad.Count; i++)
                    {
                        float lDoc = Vector3.Dot(l, o - PositionScratchpad[i]);
                        float det = lDoc * lDoc - ((o - PositionScratchpad[i]).LengthSquared() - IntelScratchpad[i].Radius * IntelScratchpad[i].Radius);
                        if (det > 0 && -lDoc > 0 && -lDoc < d) //  
                        {
                            closestObstacle = IntelScratchpad[i];
                            var distIntersect = -lDoc - (float)Math.Sqrt(det);

                            if (closestDist > distIntersect)
                            {
                                closestDist = distIntersect;
                                closestApporoach = -lDoc;
                                closestObstacle = IntelScratchpad[i];
                            }
                        }
                    }

                    if (closestDist != float.MaxValue)
                    {
                        var c = l * closestApporoach + o;
                        Vector3 dir = c - closestObstacle.GetPositionFromCanonicalTime(canonicalTime);
                        dir.Normalize();
                        var v = dir * (closestObstacle.Radius + SafetyRadius) * 2 + closestObstacle.GetPositionFromCanonicalTime(canonicalTime);
                        v += v - o;

                        targetClear = false;
                        target = v;
                    }

                } while (!targetClear);

                var w = new Waypoint
                {
                    Position = target,
                    Name = "[OBS]" + iter.ToString()
                };
                DebugIntelProvider.ReportFleetIntelligence(w, canonicalTime);
            }

            return target;
        }
    }
    #endregion
    #endregion
}
