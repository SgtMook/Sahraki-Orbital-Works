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
            Autopilot.Move(AvoidObstacles ? PlotPath(IntelItems, canonicalTime) : Destination.Position);
            Autopilot.Turn(Destination.Direction);
            Autopilot.SetMaxSpeed(Destination.MaxSpeed);
            Autopilot.SetReference(MoveReference);
        }
        #endregion

        readonly MyGridProgram Program;
        readonly IAutopilot Autopilot;
        public Waypoint Destination;

        readonly IIntelProvider DebugIntelProvider;

        readonly List<IFleetIntelligence> IntelScratchpad;
        readonly List<Vector3> PositionScratchpad;

        readonly bool AvoidObstacles;
        readonly IMyTerminalBlock MoveReference;

        public WaypointTask(MyGridProgram program, IAutopilot pilotSubsystem, Waypoint waypoint, IIntelProvider debugIntelProvider, bool avoidObstacles = true, IMyTerminalBlock moveReference = null)
        {
            Autopilot = pilotSubsystem;
            Program = program;
            Destination = waypoint;
            DebugIntelProvider = debugIntelProvider;
            IntelScratchpad = new List<IFleetIntelligence>();
            PositionScratchpad = new List<Vector3>();
            AvoidObstacles = avoidObstacles;
            MoveReference = moveReference;
        }

        Vector3 PlotPath(Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence> IntelItems, TimeSpan canonicalTime)
        {
            Vector3 o = Autopilot.Controller.CubeGrid.WorldMatrix.Translation;
            Vector3 targetPosition = Destination.Position;
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
                    Vector3 c = kvp.Value.GetPositionFromCanonicalTime(canonicalTime);
                    float r = kvp.Value.Radius + SafetyRadius;
                    float distTo = (c - o).Length();
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

            Vector3 target = Destination.Position;

            if (type1)
            {
                // Escape maneuver - move directly away from center of bounding sphere
                var dir = o - PositionScratchpad.Last();
                dir.Normalize();
                target = dir * (IntelScratchpad.Last().Radius + SafetyRadius * 2) + PositionScratchpad.Last();
                //var w = new Waypoint
                //{
                //    Position = target,
                //    Name = "[OBS] Escape"
                //};
                //DebugIntelProvider.ReportFleetIntelligence(w, canonicalTime);
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
                    float d = l.Length();
                    l.Normalize();

                    // Go through each intel item we shortlisted earlier
                    for (int i = 0; i < IntelScratchpad.Count; i++)
                    {
                        float lDoc = Vector3.Dot(l, o - PositionScratchpad[i]);
                        float det = lDoc * lDoc - ((o - PositionScratchpad[i]).LengthSquared() - (IntelScratchpad[i].Radius + SafetyRadius) * (IntelScratchpad[i].Radius + SafetyRadius));

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
                        Vector3 closestObstaclePos = closestObstacle.GetPositionFromCanonicalTime(canonicalTime);
                        Vector3 v;

                        if (!closestType3)
                        {
                            var c = l * closestApporoach + o;
                            Vector3 dir = c - closestObstaclePos;
                            dir.Normalize();
                            v = dir * (closestObstacle.Radius + SafetyRadius * 2) + closestObstaclePos;
                            v += v - o;

                            target = v;

                        }
                        else
                        {
                            Vector3 dirCenterToDest = target - closestObstaclePos;
                            dirCenterToDest.Normalize();
                            Vector3 dirCenterToMe = o - closestObstaclePos;
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

                } while (!targetClear);

                //var w = new Waypoint
                //{
                //    Position = target,
                //    Name = "[OBS]" + iter.ToString()
                //};
                //DebugIntelProvider.ReportFleetIntelligence(w, canonicalTime);
            }

            return target;
        }
    }
    #endregion
    #endregion
}
