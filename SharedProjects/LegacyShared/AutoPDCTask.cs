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
    public class AutoPDCTaskGenerator : ITaskGenerator
    {
        #region ITaskGenerator
        public TaskType AcceptedTypes => TaskType.Attack | TaskType.Picket;

        public ITask GenerateTask(TaskType type, MyTuple<IntelItemType, long> intelKey, Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence> IntelItems, TimeSpan canonicalTime, long myID)
        {
            return MyTask;
        }
        #endregion

        MyGridProgram Program;
        IAutopilot Autopilot;
        IIntelProvider IntelProvider;

        List<IMyMotorAdvancedStator> Rotors = new List<IMyMotorAdvancedStator>();

        public AutoPDCTask MyTask;

        public AutoPDCTaskGenerator(MyGridProgram program, IAutopilot autopilot, IIntelProvider intelProvider)
        {
            Program = program;
            Autopilot = autopilot;
            IntelProvider = intelProvider;

            program.GridTerminalSystem.GetBlocksOfType(Rotors, (IMyMotorAdvancedStator r) => { return r.CustomName.Contains("Elevation") && r.CubeGrid == program.Me.CubeGrid; });

            MyTask = new AutoPDCTask(Program, Autopilot, IntelProvider);
            MyTask.Do(new Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence>(), TimeSpan.Zero, null);
        }
    }


    public class AutoPDCTask : CompoundTask
    {
        WaypointTask Approach;
        WaypointTask FinalApproach;
        WaypointTask Unapproach;
        WaypointTask Return;
        MyTuple<IntelItemType, long> IntelKey;

        Waypoint StartPosition = new Waypoint();

        MyGridProgram Program;
        IAutopilot Autopilot;
        IIntelProvider IntelProvider;

        public AutoPDCTask(MyGridProgram program, IAutopilot autopilot, IIntelProvider intelProvider)
        {
            Program = program;
            Autopilot = autopilot;
            IntelProvider = intelProvider;
        }

        public void Reset(MyTuple<IntelItemType, long> intelKey, IMyMotorAdvancedStator Rotor)
        {
            Reset();

            Approach.MoveReference = Rotor;
            FinalApproach.MoveReference = Rotor;


            TaskQueue.Enqueue(Approach);
            TaskQueue.Enqueue(FinalApproach);
            TaskQueue.Enqueue(Unapproach);
            TaskQueue.Enqueue(Return);
        }

        public override void Do(Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence> IntelItems, TimeSpan canonicalTime, Profiler profiler)
        {
            if (!IntelItems.ContainsKey(IntelKey))
            {
                Aborted = true;
                TaskQueue.Clear();
                return;
            }



            base.Do(IntelItems, canonicalTime, profiler);
        }
    }
}
