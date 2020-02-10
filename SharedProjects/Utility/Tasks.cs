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
    public struct WaypointTask : ITask
    {

        #region ITask
        public TaskStatus Status
        {
            get
            {
                return Autopilot.AtWaypoint(Waypoint) ? TaskStatus.Complete : TaskStatus.Incomplete;
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
        readonly Waypoint Waypoint;

        readonly IIntelProvider DebugIntelProvider;

        bool WaypointSet;

        public WaypointTask(MyGridProgram program, IAutopilot pilotSubsystem, Waypoint waypoint, IIntelProvider debugIntelProvider)
        {
            Autopilot = pilotSubsystem;
            Program = program;
            Waypoint = waypoint;
            WaypointSet = false;
            DebugIntelProvider = debugIntelProvider;
        }

        Vector3 PlotPath(Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence> IntelItems, TimeSpan canonicalTime)
        {
            Vector3 o = Autopilot.Controller.CubeGrid.WorldMatrix.Translation;
            Vector3 targetPosition = Waypoint.GetPositionFromCanonicalTime(canonicalTime);
            float SafetyRadius = (float)(Autopilot.Controller.CubeGrid.WorldAABB.Max - Autopilot.Controller.CubeGrid.WorldAABB.Center).Length();
            float brakingDist = Autopilot.GetBrakingDistance();

            var l = targetPosition - o;
            float d = l.Length();
            l.Normalize();

            IFleetIntelligence closestObstacle = null;
            float closestDist = float.MaxValue;
            float closestApporoach = 0;

            List<Waypoint> ws = new List<Waypoint>();

            foreach (var kvp in IntelItems)
            {
                if (kvp.Key.Item2 == Program.Me.CubeGrid.EntityId) continue;

                if (kvp.Value.IntelItemType == IntelItemType.Asteroid || kvp.Value.IntelItemType == IntelItemType.Friendly || kvp.Value.IntelItemType == IntelItemType.Enemy)
                {
                    Vector3 c = kvp.Value.GetPositionFromCanonicalTime(canonicalTime);
                    float r = kvp.Value.Radius + SafetyRadius;
                    float distTo = (c - o).Length();
                    if (distTo < r + brakingDist) // distTo < r + brakingDist
                    {
                        float lDoc = Vector3.Dot(l, o - c);
                        float det = lDoc * lDoc - ((o - c).LengthSquared() - r * r);
                        if (det > 0 && -lDoc > 0 && -lDoc < d) //  
                        {
                            closestObstacle = kvp.Value;
                            var distIntersect = -lDoc - (float)Math.Sqrt(det);

                            if (closestDist > distIntersect)
                            {
                                closestDist = distIntersect;
                                closestApporoach = -lDoc;
                                closestObstacle = kvp.Value;
                            }

                            //closestDist = -lDoc + (float)Math.Sqrt(det);
                            //var w = new Waypoint();
                            //w.Position = o + l * closestDist;
                            //w.Name = "[O1] " + closestObstacle.DisplayName;
                            //ws.Add(w);
                            //
                            //closestDist = -lDoc - (float)Math.Sqrt(det);
                            //w = new Waypoint();
                            //w.Position = o + l * closestDist;
                            //w.Name = "[O2] " + closestObstacle.DisplayName;
                            //ws.Add(w);
                            //
                            //closestDist = -lDoc;
                            //w = new Waypoint();
                            //w.Position = o + l * closestDist;
                            //w.Name = "[Oc] " + closestObstacle.DisplayName;
                            //ws.Add(w);
                        }
                    }
                }
            }

            //foreach (var w in ws)
            //{
            //    DebugIntelProvider.ReportFleetIntelligence(w, canonicalTime);
            //}

            if (closestDist != float.MaxValue)
            {
                var c = l * closestApporoach + o;
                Vector3 dir = c - closestObstacle.GetPositionFromCanonicalTime(canonicalTime);
                dir.Normalize();
                var v = dir * (closestObstacle.Radius + SafetyRadius * 2) + closestObstacle.GetPositionFromCanonicalTime(canonicalTime);

                var w = new Waypoint();
                w.Position = v;
                w.Name = "[O1] " + (closestObstacle.Radius + SafetyRadius * 2).ToString() + closestObstacle.DisplayName;
                DebugIntelProvider.ReportFleetIntelligence(w, canonicalTime);

                return v;
            }

            return targetPosition;
        }
    }
    #endregion
    #endregion
}
