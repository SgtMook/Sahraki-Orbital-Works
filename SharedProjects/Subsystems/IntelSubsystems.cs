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

        bool HasMaster { get; }
    }

    // ABOLISH SLAVERY ====================================================================================================================================================================================================================================================
    // TODO: Save/load serializations
    public class IntelSlaveSubsystem : ISubsystem, IIntelProvider
    {

        #region ISubsystem
        public UpdateFrequency UpdateFrequency => UpdateFrequency.Update1 | UpdateFrequency.Update10 | UpdateFrequency.Update100;

        public void Command(TimeSpan timestamp, string command, object argument)
        {
            if (command == "sync") GetTimeMessage(timestamp);
            if (command == "wipe")
            {
                IntelItems.Clear();
                Timestamps.Clear();
            }
        }

        public string GetStatus()
        {
            debugBuilder.Clear();
            debugBuilder.AppendLine(IsMaster ? "MASTER" : "SLAVE");
            return debugBuilder.ToString();
        }

        public void DeserializeSubsystem(string serialized)
        {
            if (IsMaster)
            {
                MyStringReader reader = new MyStringReader(serialized);
                while (reader.HasNextLine)
                {
                    var s = reader.NextLine();
                    if (s == string.Empty) return;
                    ReportFleetIntelligence(AsteroidIntel.DeserializeAsteroid(s), TimeSpan.Zero);
                }
            }
        }

        public string SerializeSubsystem()
        {
            if (IsMaster)
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

            AsteroidIntel.IGCUnpack(AsteroidIntel.IGCPackGeneric(new AsteroidIntel()).Item3);
            FriendlyShipIntel.IGCUnpack(FriendlyShipIntel.IGCPackGeneric(new FriendlyShipIntel()).Item3);
            EnemyShipIntel.IGCUnpack(EnemyShipIntel.IGCPackGeneric(new EnemyShipIntel()).Item3);
            DockIntel.IGCUnpack(DockIntel.IGCPackGeneric(new DockIntel()).Item3);
            Waypoint.IGCUnpack(Waypoint.IGCPackGeneric(new Waypoint()).Item3);

            FleetIntelligenceUtil.ReceiveAndUpdateFleetIntelligenceSyncPackage(123, IntelItems, ref KeyScratchpad, CanonicalTimeSourceID);
            FleetIntelligenceUtil.ReceiveAndUpdateFleetIntelligence(123, IntelItems, CanonicalTimeSourceID);

            GetTimeMessage(TimeSpan.Zero);
            GetFleetIntelligences(TimeSpan.Zero);
            CheckOrSendTimeMessage(TimeSpan.Zero);

            Program = program;
            ReportListener = Program.IGC.RegisterBroadcastListener(FleetIntelligenceUtil.IntelReportChannelTag);
            PriorityRequestListener = Program.IGC.RegisterBroadcastListener(FleetIntelligenceUtil.IntelPriorityRequestChannelTag);
            GetParts();
            UpdateMyIntel(TimeSpan.Zero);
            ParseConfigs();
        }
    
        public void Update(TimeSpan timestamp, UpdateFrequency updateFlags)
        {
            if (IsMaster && (updateFlags & UpdateFrequency.Update1) != 0) UpdateIntelFromReports(timestamp);

            if ((updateFlags & UpdateFrequency.Update10) != 0)
            {
                if (runs % 3 == 0)
                {
                    if (!IsMaster) GetSyncMessages(timestamp);
                    CheckOrSendTimeMessage(timestamp);
                    UpdateMyIntel(timestamp);
                }
                runs++;
            }
            if ((updateFlags & UpdateFrequency.Update100) != 0)
            {
                TimeoutIntelItems(timestamp);

                if (IsMaster)
                {
                    ReceivePriorityRequests();
                    SendPriorities();
                }
                else
                {
                    UpdatePriorities();
                }
            }
        }

        public int GetPriority(long EnemyID)
        {
            if (IsMaster)
            {
                int priority;
                if (!MasterEnemyPriorities.TryGetValue(EnemyID, out priority)) priority = 2;
                return priority;
            }
            else
            {
                if (EnemyPrioritiesOverride.ContainsKey(EnemyID)) return EnemyPrioritiesOverride[EnemyID];
                if (EnemyPriorities == null || !EnemyPriorities.ContainsKey(EnemyID)) return 2;
                return EnemyPriorities[EnemyID];
            }
        }

        public void SetPriority(long EnemyID, int value)
        {
            if (IsMaster)
            {
                MasterEnemyPriorities[EnemyID] = value;
            }
            else 
            {
                EnemyPrioritiesOverride[EnemyID] = value;
                EnemyPrioritiesKeepSet.Add(EnemyID);
                Program.IGC.SendBroadcastMessage(FleetIntelligenceUtil.IntelPriorityRequestChannelTag, MyTuple.Create(EnemyID, value));
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
            if (CanonicalTimeSourceID != 0 && !IsMaster)
            {
                FleetIntelligenceUtil.PackAndBroadcastFleetIntelligence(Program.IGC, item, CanonicalTimeSourceID);
            }
            MyTuple<IntelItemType, long> intelKey = FleetIntelligenceUtil.GetIntelItemKey(item);
            Timestamps[intelKey] = timestamp;
            if (!IntelItems.ContainsKey(intelKey) || IntelItems[intelKey] != item) IntelItems[intelKey] = item;
        }

        public TimeSpan CanonicalTimeDiff { get; set; } // Add this to timestamp to get canonical time

        public bool HasMaster
        {
            get
            {
                return CanonicalTimeSourceID != 0;
            }
        }

        public void ReportCommand(FriendlyShipIntel agent, TaskType taskType, MyTuple<IntelItemType, long> targetKey, TimeSpan timestamp)
        {
            if (agent.ID == Program.Me.CubeGrid.EntityId && MyAgent != null)
            {
                MyAgent.AddTask(taskType, targetKey, CommandType.Override, 0, timestamp + CanonicalTimeDiff);
            }
            else
            {
                Program.IGC.SendBroadcastMessage(agent.CommandChannelTag, MyTuple.Create((int)taskType, MyTuple.Create((int)targetKey.Item1, targetKey.Item2), (int)CommandType.Override, 0));
            }
        }
        public void AddIntelMutator(IOwnIntelMutator processor)
        {
            intelProcessors.Add(processor);
        }
        #endregion

        private const double kOneTick = 16.6666666;
        MyGridProgram Program;
        IMyBroadcastListener SyncListener;
        IMyBroadcastListener ReportListener;
        IMyBroadcastListener PriorityRequestListener;
        IMyBroadcastListener PriorityListener;

        Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence> IntelItems = new Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence>();
        Dictionary<MyTuple<IntelItemType, long>, TimeSpan> Timestamps = new Dictionary<MyTuple<IntelItemType, long>, TimeSpan>();
        List<MyTuple<IntelItemType, long>> KeyScratchpad = new List<MyTuple<IntelItemType, long>>();

        StringBuilder debugBuilder = new StringBuilder();

        TimeSpan kIntelTimeout = TimeSpan.FromSeconds(5);

        long CanonicalTimeSourceID;
        int CanonicalTimeSourceRank;
        IMyBroadcastListener TimeListener;

        long HighestRankID;
        int HighestRank;

        IMyShipController controller;

        HashSet<IOwnIntelMutator> intelProcessors = new HashSet<IOwnIntelMutator>();

        ImmutableDictionary<long, int> EnemyPriorities = null;
        Dictionary<long, int> MasterEnemyPriorities = new Dictionary<long, int>();
        IGCSyncPacker IGCSyncPacker = new IGCSyncPacker();

        Dictionary<long, int> EnemyPrioritiesOverride = new Dictionary<long, int>();
        HashSet<long> EnemyPrioritiesKeepSet = new HashSet<long>();
        List<long> EnemyPriorityClearScratchpad = new List<long>();

        int runs = 0;

        public AgentSubsystem MyAgent;

        bool IsMaster = false;
        int Rank = 0;

        float RadiusMulti = 1;

        public IntelSlaveSubsystem(int rank = 0)
        {
            Rank = rank;
        }

        // [Intel]
        // RadiusMulti = 1
        // Rank = 0
        private void ParseConfigs()
        {
            MyIni Parser = new MyIni();
            MyIniParseResult result;
            if (!Parser.TryParse(Program.Me.CustomData, out result))
                return;

            var flo = Parser.Get("Intel", "RadiusMulti").ToDecimal();
            if (flo != 0) RadiusMulti = (float)flo;

            var num = Parser.Get("Intel", "Rank").ToInt16();
            if (num != 0) Rank = num;
        }

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
            myIntel.CurrentPosition = cubeGrid.WorldAABB.Center;
            myIntel.Radius = (float)(cubeGrid.WorldAABB.Max - cubeGrid.WorldAABB.Center).Length() * RadiusMulti + 20;
            myIntel.CurrentCanonicalTime = timestamp + CanonicalTimeDiff;
            myIntel.ID = cubeGrid.EntityId;
            myIntel.AgentStatus = AgentStatus.None;

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

        private void GetTimeMessage(TimeSpan timestamp)
        {
            MyIGCMessage? msg = null;

            while (TimeListener.HasPendingMessage)
                msg = TimeListener.AcceptMessage();

            if (msg != null)
            {
                var tMsg = (MyIGCMessage)msg;
                var unpacked = (MyTuple<double, int>)tMsg.Data;

                if (unpacked.Item2 > CanonicalTimeSourceRank || (unpacked.Item2 == CanonicalTimeSourceRank && tMsg.Source > CanonicalTimeSourceID))
                {
                    CanonicalTimeSourceRank = unpacked.Item2;
                    CanonicalTimeSourceID = tMsg.Source;
                }

                if (CanonicalTimeSourceID == tMsg.Source)
                {
                    CanonicalTimeDiff = TimeSpan.FromMilliseconds(unpacked.Item1 + kOneTick) - timestamp;
                }

                if (unpacked.Item2 > HighestRank || (unpacked.Item2 == HighestRank && tMsg.Source > HighestRankID))
                {
                    HighestRank = unpacked.Item2;
                    HighestRankID = tMsg.Source;
                    if (IsMaster) Demote(HighestRankID);
                }
            }
        }

        private void CheckOrSendTimeMessage(TimeSpan timestamp)
        {
            if (timestamp == TimeSpan.Zero) return;

            if (IsMaster) FleetIntelligenceUtil.PackAndBroadcastFleetIntelligenceSyncPackage(Program.IGC, IntelItems, Program.IGC.Me, IGCSyncPacker);

            if (Rank > 0)
            {
                Program.IGC.SendBroadcastMessage(FleetIntelligenceUtil.TimeChannelTag, MyTuple.Create(timestamp.TotalMilliseconds, Rank));
            }

            if (HighestRankID == Program.IGC.Me && !IsMaster && Rank > 0)
            {
                Promote();
            }

            if (HighestRankID != CanonicalTimeSourceID)
            {
                CanonicalTimeSourceRank = 0;
                CanonicalTimeSourceID = 0;
            }

            HighestRank = Rank;
            HighestRankID = Program.IGC.Me;
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
                MasterEnemyPriorities[unpacked.Item1] = newPriority;
            }
        }

        private void SendPriorities()
        {
            Program.IGC.SendBroadcastMessage(FleetIntelligenceUtil.IntelPriorityChannelTag, MasterEnemyPriorities.ToImmutableDictionary());
        }

        private void Promote()
        {
            IsMaster = true;
            CanonicalTimeSourceID = Program.Me.EntityId;
            if (EnemyPriorities != null) foreach(var kvp in EnemyPriorities) MasterEnemyPriorities.Add(kvp.Key, kvp.Value);
            CanonicalTimeDiff = TimeSpan.Zero;
        }

        private void Demote(long newMasterID)
        {
            IsMaster = false;
            CanonicalTimeSourceID = newMasterID;
            EnemyPriorities = null;
            MasterEnemyPriorities.Clear();
        }
    }
}
