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
    // Canonical command format is (int TaskType, MyTuple<int, long> IGCIntelKey, int CommandType)
    public enum CommandType
    {
        Override,
        Enqueue,
        DoFirst
    }

    public class AgentSubsystem : ISubsystem
    {
        #region ISubsystem
        public UpdateFrequency UpdateFrequency => UpdateFrequency.Update10;

        public void Command(TimeSpan timestamp, string command, object argument)
        {
        }

        public void DeserializeSubsystem(string serialized)
        {
        }

        public string GetStatus()
        {
            return CommandChannelTag;
        }

        public string SerializeSubsystem()
        {
            return string.Empty;
        }

        public void Setup(MyGridProgram program, string name)
        {
            Program = program;
            CommandChannelTag = program.Me.CubeGrid.EntityId.ToString() + "-COMMAND";
            CommandListener = program.IGC.RegisterBroadcastListener(CommandChannelTag);
        }

        public void Update(TimeSpan timestamp, UpdateFrequency updateFlags)
        {
            if ((updateFlags & UpdateFrequency.Update10) != 0)
            {
                GetCommands(timestamp);
                DoTasks(timestamp);
            }
        }
        #endregion

        MyGridProgram Program;
        IMyBroadcastListener CommandListener;
        string CommandChannelTag;

        IIntelProvider IntelProvider;

        Dictionary<TaskType, ITaskGenerator> TaskGenerators = new Dictionary<TaskType, ITaskGenerator>();
        TaskType AvailableTasks = TaskType.None;

        Queue<ITask> TaskQueue = new Queue<ITask>();

        StringBuilder DebugBuilder = new StringBuilder();

        public AgentSubsystem(IIntelProvider intelProvider)
        {
            IntelProvider = intelProvider;
        }

        public void AddTaskGenerator(ITaskGenerator taskGenerator)
        {
            AvailableTasks |= taskGenerator.AcceptedTypes;
            for (int i = 0; i < 30; i++)
            {
                if ((1 << i & (int)taskGenerator.AcceptedTypes) != 0)
                    TaskGenerators[(TaskType)(1 << i)] = taskGenerator;
            }
        }

        private void DoTasks(TimeSpan timestamp)
        {
            if (TaskQueue.Count == 0) return;
            ITask currentTask = TaskQueue.Peek();
            currentTask.Do(IntelProvider.GetFleetIntelligences(), timestamp + IntelProvider.CanonicalTimeDiff);
            if (currentTask.Status == TaskStatus.Complete)
                TaskQueue.Dequeue();
            else if (currentTask.Status == TaskStatus.Aborted)
                TaskQueue.Dequeue();
        }

        private void GetCommands(TimeSpan timestamp)
        {
            while (CommandListener.HasPendingMessage)
            {
                var msg = CommandListener.AcceptMessage();
                if (msg.Data is MyTuple<int, MyTuple<int, long>, bool>)
                    AddTaskFromCommand(timestamp, (MyTuple<int, MyTuple<int, long>, int>)msg.Data);
            }
        }

        private void AddTaskFromCommand(TimeSpan timestamp, MyTuple<int, MyTuple<int, long>, int> command)
        {
            if ((CommandType)command.Item3 == CommandType.Override)
            {
                TaskQueue.Clear();
            }
            if (TaskGenerators.ContainsKey((TaskType)command.Item1))
            {
                TaskQueue.Enqueue(TaskGenerators[(TaskType)command.Item1].GenerateTask((TaskType)command.Item1, MyTuple.Create((IntelItemType)command.Item2.Item1, command.Item2.Item2), IntelProvider.GetFleetIntelligences(), timestamp + IntelProvider.CanonicalTimeDiff));
            }
        }
    }
}
