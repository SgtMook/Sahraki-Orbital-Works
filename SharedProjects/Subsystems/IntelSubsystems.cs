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
        public int UpdateFrequency => 1;
        MyGridProgram Program;
        IMyBroadcastListener ReportListener;
        StringBuilder statusBuilder = new StringBuilder();

        int heartbeatCounter = 0;
        int heartbeatFrequency = 60;

        public void Command(string command, object argument)
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
            foreach (IFleetIntelligence intel in IntelItems.Values)
                statusBuilder.AppendLine(intel.Serialize());
            return debugBuilder.ToString();
        }

        public void DeserializeSubsystem(string serialized)
        {
        }

        public string SerializeSubsystem()
        {
            return string.Empty;
        }

        public void Setup(MyGridProgram program, SubsystemManager manager)
        {
            Program = program;
            ReportListener = program.IGC.RegisterBroadcastListener(FleetIntelligenceUtil.IntelReportChannelTag);
        }

        public void Update(TimeSpan timestamp)
        {
            UpdateIntelFromReports(timestamp);
            CheckHeartbeat();
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

        private void CheckHeartbeat()
        {
            heartbeatCounter++;
            if (heartbeatCounter >= heartbeatFrequency)
            {
                heartbeatCounter = 0;
                SendSyncMessage();
            }
        }

        private void SendSyncMessage()
        {
            FleetIntelligenceUtil.PackAndBroadcastFleetIntelligenceSyncPackage(Program.IGC, IntelItems);
        }

        private void UpdateIntelFromReports(TimeSpan timestamp)
        {
            while (ReportListener.HasPendingMessage)
            {
                var msg = ReportListener.AcceptMessage();
                var updateKey = FleetIntelligenceUtil.ReceiveAndUpdateFleetIntelligence(msg.Data, IntelItems);
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
        public int UpdateFrequency => 1;
    
        public void Command(string command, object argument)
        {
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
    
        public void Setup(MyGridProgram program, SubsystemManager manager)
        {
            Program = program;
            SyncListener = program.IGC.RegisterBroadcastListener(FleetIntelligenceUtil.IntelSyncChannelTag);
        }
    
        public void Update(TimeSpan timestamp)
        {
            executionCounter++;
            GetSyncMessages(timestamp);
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
            FleetIntelligenceUtil.PackAndBroadcastFleetIntelligence(Program.IGC, item);
        }


        #endregion
    
        MyGridProgram Program;
        IMyBroadcastListener SyncListener;

        Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence> IntelItems = new Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence>();
        Dictionary<MyTuple<IntelItemType, long>, TimeSpan> Timestamps = new Dictionary<MyTuple<IntelItemType, long>, TimeSpan>();
        List<MyTuple<IntelItemType, long>> KeyScratchpad = new List<MyTuple<IntelItemType, long>>();

        StringBuilder debugBuilder = new StringBuilder();

        long executionCounter = 0;

        TimeSpan kIntelTimeout = TimeSpan.FromSeconds(5);

        private void GetSyncMessages(TimeSpan timestamp)
        {
            while (SyncListener.HasPendingMessage)
            {
                var msg = SyncListener.AcceptMessage();
                FleetIntelligenceUtil.ReceiveAndUpdateFleetIntelligenceSyncPackage(msg.Data, IntelItems, ref KeyScratchpad);
            }

            foreach (var key in KeyScratchpad)
            {
                Timestamps[key] = timestamp;
            }

            KeyScratchpad.Clear();
        }

        private void TimeoutIntelItems(TimeSpan timestamp)
        {
            if (executionCounter % 60 == 0)
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
}
