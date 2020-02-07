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

using SharedProjects.Utility;
using System.Collections.Immutable;

namespace SharedProjects.Subsystems
{
    /// <summary>
    /// This subsystem is capable of producing a dictionary of intel items
    /// </summary>
    public interface IIntelProvider
    {
        Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence> GetFleetIntelligences();

        TimeSpan GetLastUpdatedTime(MyTuple<IntelItemType, long> key);

        void ReportFleetIntelligence(IFleetIntelligence item, TimeSpan timestamp);
    }

    // Handles tracking, updating, and transmitting fleet intelligence
    // TODO: Serialize and deserialize intel items
    // TODO: Send sync messages containing all the fleet intelligences
    // TODO: Receive update messages
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
            statusBuilder.Clear();
            statusBuilder.AppendLine(ReportListener.MaxWaitingMessages.ToString());
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
        }

        public void Update(TimeSpan timestamp, UpdateFrequency updateFlags)
        {
            if ((updateFlags & UpdateFrequency.Update10) != 0)
                UpdateIntelFromReports(timestamp);
            if ((updateFlags & UpdateFrequency.Update100) != 0)
                SendSyncMessage(timestamp);
        }
        #endregion

        #region IIntelProvider
        public Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence> GetFleetIntelligences()
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
            IntelItems.Add(key, item);
            Timestamps.Add(key, timestamp);
        }
        #endregion

        #region Debug
        StringBuilder debugBuilder = new StringBuilder();
        #endregion

        Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence> IntelItems = new Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence>();
        Dictionary<MyTuple<IntelItemType, long>, TimeSpan> Timestamps = new Dictionary<MyTuple<IntelItemType, long>, TimeSpan>();

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
                    Timestamps.Add(updateKey, timestamp);
                }
            }
        }
    }

    // TODO: Build
    // TODO: Save/load serializations
    // TODO: Get sync
    public class IntelSlaveSubsystem : ISubsystem, IIntelProvider
    {

        #region ISubsystem
        public UpdateFrequency UpdateFrequency => UpdateFrequency.Update10 | UpdateFrequency.Update100;

        public void Command(TimeSpan timestamp, string command, object argument)
        {
            if (command == "sync")
            {
                GetTimeMessage(timestamp);
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
            SyncListener = program.IGC.RegisterBroadcastListener(FleetIntelligenceUtil.IntelSyncChannelTag);
            TimeListener = program.IGC.RegisterBroadcastListener(FleetIntelligenceUtil.TimeChannelTag);
            TimeListener.SetMessageCallback($"{name} sync");
        }
    
        public void Update(TimeSpan timestamp, UpdateFrequency updateFlags)
        {
            debugBuilder.Clear();
            debugBuilder.AppendLine(CanonicalTimeDiff.TotalMilliseconds.ToString());
            if ((updateFlags & UpdateFrequency.Update10) != 0)
                GetSyncMessages(timestamp);
            if ((updateFlags & UpdateFrequency.Update100) != 0)
                TimeoutIntelItems(timestamp);
        }

        #endregion

        #region IIntelProvider
        public Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence> GetFleetIntelligences()
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
            FleetIntelligenceUtil.PackAndBroadcastFleetIntelligence(Program.IGC, item, CanonicalTimeSourceID);
        }

        private const double kOneTick = 16.6666666;

        #endregion

        MyGridProgram Program;
        IMyBroadcastListener SyncListener;

        Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence> IntelItems = new Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence>();
        Dictionary<MyTuple<IntelItemType, long>, TimeSpan> Timestamps = new Dictionary<MyTuple<IntelItemType, long>, TimeSpan>();
        List<MyTuple<IntelItemType, long>> KeyScratchpad = new List<MyTuple<IntelItemType, long>>();

        StringBuilder debugBuilder = new StringBuilder();

        TimeSpan kIntelTimeout = TimeSpan.FromSeconds(5);

        long CanonicalTimeSourceID;
        TimeSpan CanonicalTimeDiff; // Add this to timestamp to get canonical time
        IMyBroadcastListener TimeListener;

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
