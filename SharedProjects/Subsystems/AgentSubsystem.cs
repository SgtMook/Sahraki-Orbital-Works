﻿using Sandbox.Game.EntityComponents;
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
    // Canonical command format is (int TaskType, MyTuple<int, long> IGCIntelKey, int CommandType, int Arguments)
    public enum CommandType
    {
        Override,
        Enqueue,
        DoFirst
    }

    public enum AgentClass
    {
        None,
        Drone,
        Fighter,
        Bomber,
        Miner,
        Last
    }

    [Flags]
    public enum AgentStatus
    {
        None = 0,
        Idle = 1 << 0, // This agent is ready to receive and execute a command
        DockedAtHome = 1 << 1, // This agent is docked and at home
        Docked = 1 << 2, // This agent is docked
        Engaged = 1 << 10, // This agent is engaged in combat
        Recalling = 1 << 11, // This agent is returning to base
    }

    public interface IAgentSubsystem
    {
        string CommandChannelTag { get; }
        TaskType AvailableTasks { get; }

        AgentClass AgentClass { get; }

        string Status { get; set; }

        void AddTask(TaskType taskType, MyTuple<IntelItemType, long> intelKey, CommandType commandType, int arguments, TimeSpan canonicalTime);
    }

    public class AgentSubsystem : ISubsystem, IAgentSubsystem, IOwnIntelMutator
    {
        #region ISubsystem
        public UpdateFrequency UpdateFrequency => UpdateFrequency.Update10;

        public void Command(TimeSpan timestamp, string command, object argument)
        {
        }

        public void CommandV2(TimeSpan timestamp, CommandLine command)
        {

        }
        public void DeserializeSubsystem(string serialized)
        {
        }

        public string GetStatus()
        {
            var DebugBuilder = Context.SharedStringBuilder;
            DebugBuilder.Clear();
            DebugBuilder.AppendLine($"TASKS {TaskQueue.Count}");

            foreach (var task in TaskQueue)
            {
                DebugBuilder.AppendLine(task.Name);
            }
            // profiler.PrintSectionBreakdown(DebugBuilder);
            return DebugBuilder.ToString();
        }

        public string SerializeSubsystem()
        {
            return string.Empty;
        }

        public void Setup(ExecutionContext context, string name)
        {
            Context = context;
            
            CommandChannelTag = Context.Reference.CubeGrid.EntityId.ToString() + "-COMMAND";
            CommandListener = Context.IGC.RegisterBroadcastListener(CommandChannelTag);
            //profiler = new Profiler(Program.Runtime, PROFILER_HISTORY_COUNT, PROFILER_NEW_VALUE_FACTOR);
            AddTask(TaskType.None, MyTuple.Create(IntelItemType.NONE, (long)0), CommandType.DoFirst, 2, TimeSpan.Zero);
            AddTaskFromCommand(TimeSpan.Zero, MyTuple.Create(-1, MyTuple.Create(0, (long)0), 0, 0));
        }

        public void Update(TimeSpan timestamp, UpdateFrequency updateFlags)
        {
            //profiler.StartSectionWatch("Baseline");
            //profiler.StopSectionWatch("Baseline");
            //
            //if ((updateFlags & UpdateFrequency.Update10) != 0) run++;
            //
            //profiler.StartSectionWatch("GetCommands");
            //if ((updateFlags & UpdateFrequency.Update10) != 0) GetCommands(timestamp);
            //profiler.StopSectionWatch("GetCommands");
            //
            //profiler.StartSectionWatch("Do Tasks");
            //if ((updateFlags & UpdateFrequency.Update10) != 0 && run % 3 == 0) DoTasks(timestamp);
            //profiler.StopSectionWatch("Do Tasks");
            //
            //profiler.StartSectionWatch("Add Tasks");
            //if ((updateFlags & UpdateFrequency.Update10) != 0) TryAddTaskFromWaitingCommand(timestamp);
            //profiler.StopSectionWatch("Add Tasks");

            if ((updateFlags & UpdateFrequency.Update10) != 0)
            {
                run++;
                if (run % 3 == 0)
                {
                    GetCommands(timestamp);
                    TryAddTaskFromWaitingCommand(timestamp);
                    DoTasks(timestamp);
                }
            }
        }
        #endregion

        #region IAgentSubsystem
        public string CommandChannelTag { get; set; }
        public TaskType AvailableTasks { get; set; }
        public AgentClass AgentClass { get; set; }

        public void AddTask(TaskType taskType, MyTuple<IntelItemType, long> intelKey, CommandType commandType, int arguments, TimeSpan canonicalTime)
        {
            if (commandType == CommandType.Override)
            {
                TaskQueue.Clear();
            }
            if (TaskGenerators.ContainsKey(taskType))
            {
                ITask Task = TaskGenerators[taskType].GenerateTask(taskType, intelKey, IntelProvider.GetFleetIntelligences(canonicalTime - IntelProvider.CanonicalTimeDiff), canonicalTime, Context.Reference.CubeGrid.EntityId);
                if (Task is NullTask) return;
                TaskQueue.Enqueue(Task);
            }
        }

        #endregion

        #region IOwnIntelMutator
        public void ProcessIntel(FriendlyShipIntel myIntel)
        {
            if (AvailableTasks != TaskType.None)
            {
                myIntel.CommandChannelTag = CommandChannelTag;
                myIntel.AcceptedTaskTypes = AvailableTasks;
                myIntel.AgentClass = AgentClass;
                if (TaskQueue.Count == 0) myIntel.AgentStatus |= AgentStatus.Idle;
                else if (TaskQueue.Peek() is MoveToAndDockTask) myIntel.AgentStatus |= AgentStatus.Recalling;
            }
        }
        #endregion

        //const double PROFILER_NEW_VALUE_FACTOR = 0.01;
        //const int PROFILER_HISTORY_COUNT = (int)(1 / PROFILER_NEW_VALUE_FACTOR);
        //Profiler profiler;

        ExecutionContext Context;
        IMyBroadcastListener CommandListener;

        IIntelProvider IntelProvider;

        Dictionary<TaskType, ITaskGenerator> TaskGenerators = new Dictionary<TaskType, ITaskGenerator>();

        public Queue<ITask> TaskQueue = new Queue<ITask>();

        MyTuple<int, MyTuple<int, long>, int, int>? WaitingCommand = null;
        TimeSpan WaitingCommandTimestamp;
        readonly TimeSpan kCommandWaitTimeout = TimeSpan.FromSeconds(1);

        int run = 0;

        public string Status { get; set; }

        public AgentSubsystem(IIntelProvider intelProvider, AgentClass agentClass)
        {
            IntelProvider = intelProvider;
            IntelProvider.AddIntelMutator(this);
            AgentClass = agentClass;
        }

        public void AddTaskGenerator(ITaskGenerator taskGenerator)
        {
            AvailableTasks |= taskGenerator.AcceptedTypes;
            for (int i = 0; i < 30; i++)
            {
                if ((1 << i & (int)taskGenerator.AcceptedTypes) != 0)
                    TaskGenerators[(TaskType)(1 << i)] = taskGenerator;
            }

            // JIT?
            taskGenerator.GenerateTask(TaskType.None, MyTuple.Create(IntelItemType.NONE, (long)0), new Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence>(), TimeSpan.Zero, 0);
        }

        void DoTasks(TimeSpan timestamp)
        {
            while (true)
            {
                if (TaskQueue.Count == 0) return;
                ITask currentTask = TaskQueue.Peek();
                //profiler.StartSectionWatch(currentTask.Name);
                currentTask.Do(IntelProvider.GetFleetIntelligences(timestamp), timestamp + IntelProvider.CanonicalTimeDiff, null);
                //profiler.StopSectionWatch(currentTask.Name);
                if (currentTask.Status == TaskStatus.Complete)
                {
                    TaskQueue.Dequeue();
                }
                else if (currentTask.Status == TaskStatus.Aborted)
                {
                    TaskQueue.Dequeue();
                }
                else
                {
                    break;
                }
            }
        }

        void GetCommands(TimeSpan timestamp)
        {
            while (CommandListener.HasPendingMessage)
            {
                var msg = CommandListener.AcceptMessage();
                if (msg.Data is MyTuple<int, MyTuple<int, long>, int, int>)
                {
                    WaitingCommand = (MyTuple<int, MyTuple<int, long>, int, int>)msg.Data;
                    WaitingCommandTimestamp = timestamp;
                    TryAddTaskFromWaitingCommand(timestamp);
                }
            }
        }

        void TryAddTaskFromWaitingCommand(TimeSpan timestamp)
        {
            if (WaitingCommandTimestamp + kCommandWaitTimeout < timestamp) WaitingCommand = null;
            if (WaitingCommand == null) return;

            var wCommand = (MyTuple<int, MyTuple<int, long>, int, int>)WaitingCommand;

            var intelItems = IntelProvider.GetFleetIntelligences(timestamp);
            var intelKey = MyTuple.Create((IntelItemType)wCommand.Item2.Item1, wCommand.Item2.Item2);
            if (!intelItems.ContainsKey(intelKey) && intelKey.Item1 != IntelItemType.NONE) return; // None denotes special commands

            AddTaskFromCommand(timestamp, wCommand);
            WaitingCommand = null;
        }

        void AddTaskFromCommand(TimeSpan timestamp, MyTuple<int, MyTuple<int, long>, int, int> command)
        {
            if (timestamp == TimeSpan.Zero) return;
            AddTask((TaskType)command.Item1, MyTuple.Create((IntelItemType)command.Item2.Item1, command.Item2.Item2), (CommandType)command.Item3, command.Item4, timestamp + IntelProvider.CanonicalTimeDiff);
        }
    }
}
