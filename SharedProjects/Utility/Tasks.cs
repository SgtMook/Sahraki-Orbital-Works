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
        ITask GenerateTask(TaskType type, MyTuple<IntelItemType, long> intelKey, Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence> IntelItems, TimeSpan canonicalTime);
    }

    #region WaypointTaskGenerator
    public class WaypointTaskGenerator : ITaskGenerator
    {
        #region ITaskGenerator
        public TaskType AcceptedTypes => TaskType.Move | TaskType.SmartMove;

        public ITask GenerateTask(TaskType type, MyTuple<IntelItemType, long> intelKey, Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence> IntelItems, TimeSpan canonicalTime)
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
            if (!WaypointSet)
            {
                Autopilot.SetWaypoint(Waypoint);
                WaypointSet = true;
            }
        }
        #endregion

        readonly MyGridProgram Program;
        readonly IAutopilot Autopilot;
        readonly Waypoint Waypoint;

        bool WaypointSet;

        public WaypointTask(MyGridProgram program, IAutopilot pilotSubsystem, Waypoint waypoint)
        {
            Autopilot = pilotSubsystem;
            Program = program;
            Waypoint = waypoint;
            WaypointSet = false;
        }
    }
    #endregion
    #endregion
}
