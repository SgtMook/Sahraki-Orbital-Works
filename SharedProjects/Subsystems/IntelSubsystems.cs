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
    public interface IOwnIntelMutator
    {
        void ProcessIntel(FriendlyShipIntel intel);
    }

    /// <summary>
    /// This subsystem is capable of producing a dictionary of intel items
    /// </summary>
    public interface IIntelProvider
    {
        Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence> GetFleetIntelligences(TimeSpan LocalTime);

        TimeSpan GetLastUpdatedTime(MyTuple<IntelItemType, long> key);

        void ReportFleetIntelligence(IFleetIntelligence item, TimeSpan LocalTime);

        TimeSpan CanonicalTimeDiff { get; }

        void AddIntelMutator(IOwnIntelMutator processor);
        void ReportCommand(FriendlyShipIntel agent, TaskType taskType, MyTuple<IntelItemType, long> targetKey, TimeSpan LocalTime);

        int GetPriority(long EnemyID);
        void SetPriority(long EnemyID, int value);
    }

    // Handles tracking, updating, and transmitting fleet intelligence
    // TODO: Serialize and deserialize intel items
    // TODO: Remove items as necessary
    public class IntelMasterSubsystem : ISubsystem, IIntelProvider
    {
        #region ISubsystem

        public UpdateFrequency UpdateFrequency => UpdateFrequency.Update10 | UpdateFrequency.Update100;

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
            MyStringReader reader = new MyStringReader(serialized);
            while (reader.HasNextLine)
            {
                var s = reader.NextLine();
                if (s == string.Empty) return;
                ReportFleetIntelligence(AsteroidIntel.DeserializeAsteroid(s), TimeSpan.Zero);
            }
        }

        public string SerializeSubsystem()
        {
            StringBuilder saveBuilder = new StringBuilder();

            foreach (var kvp in IntelItems)
            {
                if (kvp.Key.Item1 == IntelItemType.Asteroid)
                {
                    saveBuilder.AppendLine(kvp.Value.Serialize());
                }
            }

            return saveBuilder.ToString();
        }

        public void Setup(MyGridProgram program, string name)
        {
            Program = program;
            ReportListener = Program.IGC.RegisterBroadcastListener(FleetIntelligenceUtil.IntelReportChannelTag);
            PriorityRequestListener = Program.IGC.RegisterBroadcastListener(FleetIntelligenceUtil.IntelPriorityRequestChannelTag);
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
                ReceivePriorityRequests();
                SendPriorities();
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
            if (!IntelItems.ContainsKey(key) || IntelItems[key] != item) IntelItems[key] = item;
            Timestamps[key] = timestamp;
        }

        public TimeSpan CanonicalTimeDiff => TimeSpan.Zero;

        public void ReportCommand(FriendlyShipIntel agent, TaskType taskType, MyTuple<IntelItemType, long> targetKey, TimeSpan timestamp)
        {
            Program.IGC.SendBroadcastMessage(agent.CommandChannelTag, MyTuple.Create((int)taskType, MyTuple.Create((int)targetKey.Item1, targetKey.Item2), (int)CommandType.Override, 0));
        }

        public void AddIntelMutator(IOwnIntelMutator processor)
        {
            intelProcessors.Add(processor);
        }

        public int GetPriority(long EnemyID)
        {
            int priority;
            if (!EnemyPriorities.TryGetValue(EnemyID, out priority)) priority = 2;
            return priority;
        }
        public void SetPriority(long EnemyID, int value)
        {
            EnemyPriorities[EnemyID] = value;
        }
        #endregion

        #region Debug
        StringBuilder debugBuilder = new StringBuilder();
        #endregion

        MyGridProgram Program;
        IMyBroadcastListener ReportListener;
        IMyBroadcastListener PriorityRequestListener;

        Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence> IntelItems = new Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence>();
        Dictionary<MyTuple<IntelItemType, long>, TimeSpan> Timestamps = new Dictionary<MyTuple<IntelItemType, long>, TimeSpan>();

        IMyShipController controller;

        List<MyTuple<IntelItemType, long>> KeyScratchpad = new List<MyTuple<IntelItemType, long>>();
        TimeSpan kIntelTimeout = TimeSpan.FromSeconds(4);

        List<IOwnIntelMutator> intelProcessors = new List<IOwnIntelMutator>();

        Dictionary<long, int> EnemyPriorities = new Dictionary<long, int>();

        IGCSyncPacker IGCSyncPacker = new IGCSyncPacker();

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
            myIntel.Radius = (float)(cubeGrid.WorldAABB.Max - cubeGrid.WorldAABB.Center).Length() + 20;
            myIntel.CurrentCanonicalTime = timestamp;
            myIntel.ID = cubeGrid.EntityId;
            myIntel.HomeID = -1;

            foreach (var processor in intelProcessors)
                processor.ProcessIntel(myIntel);

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
            FleetIntelligenceUtil.PackAndBroadcastFleetIntelligenceSyncPackage(Program.IGC, IntelItems, Program.IGC.Me, IGCSyncPacker);
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

        private void ReceivePriorityRequests()
        {
            while (PriorityRequestListener.HasPendingMessage)
            {
                var msg = PriorityRequestListener.AcceptMessage();
                if (!(msg.Data is MyTuple<long, int>)) continue;
                MyTuple<long, int> unpacked = (MyTuple<long, int>)msg.Data;
                var newPriority = Math.Max(0, Math.Min(4, unpacked.Item2));
                EnemyPriorities[unpacked.Item1] = newPriority;
            }
        }

        private void SendPriorities()
        {
            Program.IGC.SendBroadcastMessage(FleetIntelligenceUtil.IntelPriorityChannelTag, EnemyPriorities.ToImmutableDictionary());
        }

    }

    // TODO: Save/load serializations
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
            PriorityListener = program.IGC.RegisterBroadcastListener(FleetIntelligenceUtil.IntelPriorityChannelTag);
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
                UpdatePriorities();
            }
        }

        public int GetPriority(long EnemyID)
        {
            if (EnemyPrioritiesOverride.ContainsKey(EnemyID)) return EnemyPrioritiesOverride[EnemyID];
            if (EnemyPriorities == null || !EnemyPriorities.ContainsKey(EnemyID)) return 2;
            return EnemyPriorities[EnemyID];
        }

        public void SetPriority(long EnemyID, int value)
        {
            EnemyPrioritiesOverride[EnemyID] = value;
            EnemyPrioritiesKeepSet.Add(EnemyID);
            Program.IGC.SendBroadcastMessage(FleetIntelligenceUtil.IntelPriorityRequestChannelTag, MyTuple.Create(EnemyID, value));
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
            MyTuple<IntelItemType, long> intelKey = FleetIntelligenceUtil.GetIntelItemKey(item);
            Timestamps[intelKey] = timestamp;
            if (!IntelItems.ContainsKey(intelKey) || IntelItems[intelKey] != item) IntelItems[intelKey] = item;
        }

        public TimeSpan CanonicalTimeDiff { get; set; } // Add this to timestamp to get canonical time

        public void ReportCommand(FriendlyShipIntel agent, TaskType taskType, MyTuple<IntelItemType, long> targetKey, TimeSpan timestamp)
        {
            Program.IGC.SendBroadcastMessage(agent.CommandChannelTag, MyTuple.Create((int)taskType, MyTuple.Create((int)targetKey.Item1, targetKey.Item2), (int)CommandType.Override, 0));
        }
        public void AddIntelMutator(IOwnIntelMutator processor)
        {
            intelProcessors.Add(processor);
        }
        #endregion

        private const double kOneTick = 16.6666666;
        MyGridProgram Program;
        IMyBroadcastListener SyncListener;

        IMyBroadcastListener PriorityListener;

        Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence> IntelItems = new Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence>();
        Dictionary<MyTuple<IntelItemType, long>, TimeSpan> Timestamps = new Dictionary<MyTuple<IntelItemType, long>, TimeSpan>();
        List<MyTuple<IntelItemType, long>> KeyScratchpad = new List<MyTuple<IntelItemType, long>>();

        StringBuilder debugBuilder = new StringBuilder();

        TimeSpan kIntelTimeout = TimeSpan.FromSeconds(5);

        long CanonicalTimeSourceID;
        IMyBroadcastListener TimeListener;

        IMyShipController controller;

        List<IOwnIntelMutator> intelProcessors = new List<IOwnIntelMutator>();

        ImmutableDictionary<long, int> EnemyPriorities = null;
        Dictionary<long, int> EnemyPrioritiesOverride = new Dictionary<long, int>();
        HashSet<long> EnemyPrioritiesKeepSet = new HashSet<long>();
        List<long> EnemyPriorityClearScratchpad = new List<long>();

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
            myIntel.Radius = (float)(cubeGrid.WorldAABB.Max - cubeGrid.WorldAABB.Center).Length() + 20;
            myIntel.CurrentCanonicalTime = timestamp + CanonicalTimeDiff;
            myIntel.ID = cubeGrid.EntityId;

            foreach (var processor in intelProcessors)
                processor.ProcessIntel(myIntel);

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

        private void UpdatePriorities()
        {
            EnemyPriorityClearScratchpad.Clear();
            foreach (var kvp in EnemyPrioritiesOverride)
                if (!EnemyPrioritiesKeepSet.Contains(kvp.Key)) EnemyPriorityClearScratchpad.Add(kvp.Key);
            foreach (var id in EnemyPriorityClearScratchpad)
                EnemyPrioritiesOverride.Remove(id);

            EnemyPrioritiesKeepSet.Clear();
            while (PriorityListener.HasPendingMessage)
            {
                var msg = PriorityListener.AcceptMessage();
                if (msg.Source == CanonicalTimeSourceID)
                {
                    EnemyPriorities = (ImmutableDictionary<long, int>)msg.Data;
                }
            }
        }
    }
}
