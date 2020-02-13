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


using System.Collections.Immutable;

namespace IngameScript
{
    /// <summary>
    /// This subsystem is capable of producing a dictionary of intel items
    /// </summary>
    public interface IIntelProvider
    {
        Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence> GetFleetIntelligences(TimeSpan timestamp);

        TimeSpan GetLastUpdatedTime(MyTuple<IntelItemType, long> key);

        void ReportFleetIntelligence(IFleetIntelligence item, TimeSpan LocalTime);

        TimeSpan CanonicalTimeDiff { get; }

        void SetAgentSubsystem(IAgentSubsystem agentSubsystem);
        void ReportCommand(FriendlyShipIntel agent, TaskType taskType, IFleetIntelligence target, TimeSpan LocalTime);
    }

    // Handles tracking, updating, and transmitting fleet intelligence
    // TODO: Serialize and deserialize intel items
    // TODO: Remove items as necessary
    // TODO: Support more intel types
    public class IntelMasterSubsystem : ISubsystem, IIntelProvider
    {
        #region ISubsystem

        public UpdateFrequency UpdateFrequency => UpdateFrequency.Update10 | UpdateFrequency.Update100;
        MyGridProgram Program;
        IMyBroadcastListener ReportListener;
        StringBuilder statusBuilder = new StringBuilder();

        public void Command(TimeSpan timestamp, string command, object argument)
        {
            if (command == "wipe")
            {
                IntelItems.Clear();
                Timestamps.Clear();
            }
        }


        public string GetStatus()
        {
            return debugBuilder.ToString();
        }

        public void DeserializeSubsystem(string serialized)
        {
        }

        public string SerializeSubsystem()
        {
            return string.Empty;
        }

        public void Setup(MyGridProgram program, string name)
        {
            Program = program;
            ReportListener = program.IGC.RegisterBroadcastListener(FleetIntelligenceUtil.IntelReportChannelTag);
            GetParts();
            UpdateMyIntel(TimeSpan.Zero);
        }

        public void Update(TimeSpan timestamp, UpdateFrequency updateFlags)
        {
            if ((updateFlags & UpdateFrequency.Update10) != 0)
            {
                UpdateIntelFromReports(timestamp);
                SendSyncMessage(timestamp);
                UpdateMyIntel(timestamp);
            }
            if ((updateFlags & UpdateFrequency.Update100) != 0)
            {
                TimeoutIntelItems(timestamp);
            }
        }
        #endregion

        #region IIntelProvider
        public Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence> GetFleetIntelligences(TimeSpan timestamp)
        {
            return IntelItems;
        }

        public TimeSpan GetLastUpdatedTime(MyTuple<IntelItemType, long> key)
        {
            if (!Timestamps.ContainsKey(key))
                return TimeSpan.MaxValue;
            return Timestamps[key];
        }

        public void ReportFleetIntelligence(IFleetIntelligence item, TimeSpan timestamp)
        {
            MyTuple<IntelItemType, long> key = MyTuple.Create(item.IntelItemType, item.ID);
            IntelItems[key] = item;
            Timestamps[key] = timestamp;
        }

        public TimeSpan CanonicalTimeDiff => TimeSpan.Zero;

        public void SetAgentSubsystem(IAgentSubsystem agentSubsystem)
        {
            AgentSubsystem = agentSubsystem;
        }

        public void ReportCommand(FriendlyShipIntel agent, TaskType taskType, IFleetIntelligence target, TimeSpan timestamp)
        {
            SendSyncMessage(timestamp);
            Program.IGC.SendBroadcastMessage(agent.CommandChannelTag, MyTuple.Create((int)taskType, MyTuple.Create((int)target.IntelItemType, target.ID), (int)CommandType.Override, 0));
        }
        #endregion

        #region Debug
        StringBuilder debugBuilder = new StringBuilder();
        #endregion

        Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence> IntelItems = new Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence>();
        Dictionary<MyTuple<IntelItemType, long>, TimeSpan> Timestamps = new Dictionary<MyTuple<IntelItemType, long>, TimeSpan>();

        IMyShipController controller;

        IAgentSubsystem AgentSubsystem;

        List<MyTuple<IntelItemType, long>> KeyScratchpad = new List<MyTuple<IntelItemType, long>>();
        TimeSpan kIntelTimeout = TimeSpan.FromSeconds(5);

        void GetParts()
        {
            controller = null;
            Program.GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(null, CollectParts);
        }

        private bool CollectParts(IMyTerminalBlock block)
        {
            if (!Program.Me.IsSameConstructAs(block)) return false;

            if (block is IMyShipController)
                controller = (IMyShipController)block;

            return false;
        }

        void UpdateMyIntel(TimeSpan timestamp)
        {
            if (controller == null) return;
            FriendlyShipIntel myIntel;
            IMyCubeGrid cubeGrid = Program.Me.CubeGrid;
            var key = MyTuple.Create(IntelItemType.Friendly, cubeGrid.EntityId);
            if (IntelItems.ContainsKey(key))
                myIntel = (FriendlyShipIntel)IntelItems[key];
            else
                myIntel = new FriendlyShipIntel();

            myIntel.DisplayName = cubeGrid.DisplayName;
            myIntel.CurrentVelocity = controller.GetShipVelocities().LinearVelocity;
            myIntel.CurrentPosition = cubeGrid.GetPosition();
            myIntel.Radius = (float)(cubeGrid.WorldAABB.Max - cubeGrid.WorldAABB.Center).Length();
            myIntel.CurrentCanonicalTime = timestamp;
            myIntel.ID = cubeGrid.EntityId;

            if (AgentSubsystem != null && AgentSubsystem.AvailableTasks != TaskType.None)
            {
                myIntel.CommandChannelTag = AgentSubsystem.CommandChannelTag;
                myIntel.AcceptedTaskTypes = AgentSubsystem.AvailableTasks;
                myIntel.AgentClass = AgentSubsystem.AgentClass;
            }

            ReportFleetIntelligence(myIntel, timestamp);
        }

        private void TimeoutIntelItems(TimeSpan timestamp)
        {
            foreach (var kvp in Timestamps)
            {
                if (kvp.Key.Item1 == IntelItemType.Asteroid) continue;
                if ((kvp.Value + kIntelTimeout) < timestamp)
                {
                    KeyScratchpad.Add(kvp.Key);
                }
            }

            foreach (var key in KeyScratchpad)
            {
                IntelItems.Remove(key);
                Timestamps.Remove(key);
            }

            KeyScratchpad.Clear();
        }

        private void SendSyncMessage(TimeSpan timestamp)
        {
            FleetIntelligenceUtil.PackAndBroadcastFleetIntelligenceSyncPackage(Program.IGC, IntelItems, Program.IGC.Me);
            Program.IGC.SendBroadcastMessage(FleetIntelligenceUtil.TimeChannelTag, timestamp.TotalMilliseconds);
        }

        private void UpdateIntelFromReports(TimeSpan timestamp)
        {
            while (ReportListener.HasPendingMessage)
            {
                var msg = ReportListener.AcceptMessage();
                var updateKey = FleetIntelligenceUtil.ReceiveAndUpdateFleetIntelligence(msg.Data, IntelItems, Program.IGC.Me);
                if (updateKey.Item1 != IntelItemType.NONE)
                {
                    Timestamps[updateKey] = timestamp;
                }
            }
        }
    }

    // TODO: Save/load serializations
    // TODO: Send command, probably through master
    public class IntelSlaveSubsystem : ISubsystem, IIntelProvider
    {

        #region ISubsystem
        public UpdateFrequency UpdateFrequency => UpdateFrequency.Update10 | UpdateFrequency.Update100;

        public void Command(TimeSpan timestamp, string command, object argument)
        {
            if (command == "sync") GetTimeMessage(timestamp);
        }

        public string GetStatus()
        {
            return debugBuilder.ToString();
        }
    
        public void DeserializeSubsystem(string serialized)
        {
        }    
    
        public string SerializeSubsystem()
        {
            return string.Empty;
        }
    
        public void Setup(MyGridProgram program, string name)
        {
            Program = program;
            SyncListener = program.IGC.RegisterBroadcastListener(FleetIntelligenceUtil.IntelSyncChannelTag);
            TimeListener = program.IGC.RegisterBroadcastListener(FleetIntelligenceUtil.TimeChannelTag);
            TimeListener.SetMessageCallback($"{name} sync");
            GetParts();
        }
    
        public void Update(TimeSpan timestamp, UpdateFrequency updateFlags)
        {
            if ((updateFlags & UpdateFrequency.Update10) != 0)
            {
                GetSyncMessages(timestamp);
                UpdateMyIntel(timestamp);
            }
            if ((updateFlags & UpdateFrequency.Update100) != 0)
            {
                TimeoutIntelItems(timestamp);
            }
        }

        #endregion

        #region IIntelProvider
        public Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence> GetFleetIntelligences(TimeSpan timestamp)
        {
            GetSyncMessages(timestamp);
            return IntelItems;
        }

        public TimeSpan GetLastUpdatedTime(MyTuple<IntelItemType, long> key)
        {
            if (!Timestamps.ContainsKey(key))
                return TimeSpan.MaxValue;
            return Timestamps[key];
        }

        public void ReportFleetIntelligence(IFleetIntelligence item, TimeSpan timestamp)
        {
            if (CanonicalTimeSourceID == 0) return;
            FleetIntelligenceUtil.PackAndBroadcastFleetIntelligence(Program.IGC, item, CanonicalTimeSourceID);
            Timestamps[FleetIntelligenceUtil.GetIntelItemKey(item)] = timestamp;
            IntelItems[FleetIntelligenceUtil.GetIntelItemKey(item)] = item;
        }

        public TimeSpan CanonicalTimeDiff { get; set; } // Add this to timestamp to get canonical time

        public void SetAgentSubsystem(IAgentSubsystem agentSubsystem)
        {
            AgentSubsystem = agentSubsystem;
        }

        public void ReportCommand(FriendlyShipIntel agent, TaskType taskType, IFleetIntelligence target, TimeSpan timestamp)
        {
            //TODO: Probably gets the master to send it?
        }
        #endregion

        private const double kOneTick = 16.6666666;
        MyGridProgram Program;
        IMyBroadcastListener SyncListener;

        Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence> IntelItems = new Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence>();
        Dictionary<MyTuple<IntelItemType, long>, TimeSpan> Timestamps = new Dictionary<MyTuple<IntelItemType, long>, TimeSpan>();
        List<MyTuple<IntelItemType, long>> KeyScratchpad = new List<MyTuple<IntelItemType, long>>();

        StringBuilder debugBuilder = new StringBuilder();

        TimeSpan kIntelTimeout = TimeSpan.FromSeconds(5);

        long CanonicalTimeSourceID;
        IMyBroadcastListener TimeListener;

        IMyShipController controller;

        IAgentSubsystem AgentSubsystem;

        void GetParts()
        {
            controller = null;
            Program.GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(null, CollectParts);
        }

        private bool CollectParts(IMyTerminalBlock block)
        {
            if (!Program.Me.IsSameConstructAs(block)) return false;

            if (block is IMyShipController)
                controller = (IMyShipController)block;

            return false;
        }

        void UpdateMyIntel(TimeSpan timestamp)
        {
            if (controller == null) return;
            FriendlyShipIntel myIntel;
            IMyCubeGrid cubeGrid = Program.Me.CubeGrid;
            var key = MyTuple.Create(IntelItemType.Friendly, cubeGrid.EntityId);
            if (IntelItems.ContainsKey(key))
                myIntel = (FriendlyShipIntel)IntelItems[key];
            else
                myIntel = new FriendlyShipIntel();

            myIntel.DisplayName = cubeGrid.DisplayName;
            myIntel.CurrentVelocity = controller.GetShipVelocities().LinearVelocity;
            myIntel.CurrentPosition = cubeGrid.GetPosition();
            myIntel.Radius = (float)(cubeGrid.WorldAABB.Max - cubeGrid.WorldAABB.Center).Length();
            myIntel.CurrentCanonicalTime = timestamp + CanonicalTimeDiff;
            myIntel.ID = cubeGrid.EntityId;

            if (AgentSubsystem != null && AgentSubsystem.AvailableTasks != TaskType.None)
            {
                myIntel.CommandChannelTag = AgentSubsystem.CommandChannelTag;
                myIntel.AcceptedTaskTypes = AgentSubsystem.AvailableTasks;
                myIntel.AgentClass = AgentSubsystem.AgentClass;
            }

            ReportFleetIntelligence(myIntel, timestamp);
        }

        private void GetSyncMessages(TimeSpan timestamp)
        {
            while (SyncListener.HasPendingMessage)
            {
                var msg = SyncListener.AcceptMessage();
                if (msg.Source == CanonicalTimeSourceID)
                    FleetIntelligenceUtil.ReceiveAndUpdateFleetIntelligenceSyncPackage(msg.Data, IntelItems, ref KeyScratchpad, CanonicalTimeSourceID);
            }

            foreach (var key in KeyScratchpad)
            {
                Timestamps[key] = timestamp;
            }

            KeyScratchpad.Clear();
        }

        private void GetTimeMessage(TimeSpan timestamp)
        {
            MyIGCMessage? msg = null;

            while (TimeListener.HasPendingMessage)
                msg = TimeListener.AcceptMessage();

            if (msg != null)
            {
                var tMsg = (MyIGCMessage)msg;
                CanonicalTimeSourceID = tMsg.Source;
                CanonicalTimeDiff = TimeSpan.FromMilliseconds((double)tMsg.Data + kOneTick) - timestamp;
            }
        }

        private void TimeoutIntelItems(TimeSpan timestamp)
        {
            foreach (var kvp in Timestamps)
            {
                if ((kvp.Value + kIntelTimeout) < timestamp)
                {
                    KeyScratchpad.Add(kvp.Key);
                }
            }

            foreach (var key in KeyScratchpad)
            {
                IntelItems.Remove(key);
                Timestamps.Remove(key);
            }

            KeyScratchpad.Clear();
        }
    }
}
