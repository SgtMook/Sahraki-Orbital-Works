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
        void ReportCommand(FriendlyShipIntel agent, TaskType taskType, MyTuple<IntelItemType, long> targetKey, TimeSpan LocalTime, CommandType commandType = CommandType.Override);

        int GetPriority(long EnemyID);
        void SetPriority(long EnemyID, int value);

        bool HasMaster { get; }

        IMyShipController Controller { get; }
    }

    // ABOLISH SLAVERY ====================================================================================================================================================================================================================================================
    // TODO: Save/load serializations
    public class IntelSubsystem : ISubsystem, IIntelProvider
    {

        public UpdateFrequency UpdateFrequency => UpdateFrequency.Update1;

        public void Command(TimeSpan timestamp, string command, object argument)
        {
            if (command == "wipe")
            {
                IntelItems.Clear();
                Timestamps.Clear();
            }
        }

        public void CommandV2(TimeSpan timestamp, CommandLine command)
        {

        }

        public string GetStatus()
        {
            var debugBuilder = Context.SharedStringBuilder;

            debugBuilder.Clear();
            int enemies = 0, allies = 0, other = 0;
            foreach (var intel in IntelItems)
            {
                var IntelType = intel.Key.Item1;
                if (IntelType == IntelItemType.Enemy)
                    enemies++;
                else if (IntelType == IntelItemType.Friendly)
                    allies++;
                else
                    other++;
            }
            debugBuilder.AppendLine("E:"+enemies+"|A:"+allies+"|O:"+other);
            debugBuilder.AppendLine(canonicalTimeDiff.ToString());
            debugBuilder.AppendLine(Controller.CustomName);

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
                var saveBuilder = Context.SharedStringBuilder;
                saveBuilder.Clear();

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
        public void Setup(ExecutionContext context, string name )
        {
            Context = context;
            
            if (Host == null)
            {
                IGCBindings.Add(new FleetIntelligenceUtil.IGCIntelBinding<Waypoint,             MyTuple<int, long, MyTuple<Vector3D, Vector3D, Vector3D, float, string>>>());
                IGCBindings.Add(new FleetIntelligenceUtil.IGCIntelBinding<AsteroidIntel,        MyTuple<int, long, MyTuple<Vector3D, float, long>>>());
                IGCBindings.Add(new FleetIntelligenceUtil.IGCIntelBinding<FriendlyShipIntel,    MyTuple<int, long, MyTuple<MyTuple<Vector3D, Vector3D, double>, MyTuple<string, long, float, int>, MyTuple<int, string, int, int, Vector3I>, MyTuple<long, int>>>>());
                IGCBindings.Add(new FleetIntelligenceUtil.IGCIntelBinding<DockIntel,            MyTuple<int, long, MyTuple<MyTuple<MatrixD, float, float, Vector3D, double, Vector3D>, MyTuple<long, int, int, string>, MyTuple<long, string>>>>());
                IGCBindings.Add(new FleetIntelligenceUtil.IGCIntelBinding<EnemyShipIntel,       MyTuple<int, long, MyTuple<MyTuple<Vector3D, Vector3D, double, double>, MyTuple<string, long, float, int>>>>());

                foreach (var binding in IGCBindings)
                {
                    binding.ReceiveAndUpdateFleetIntelligenceSyncPackage(Context, 123, IntelItems, ref KeyScratchpad, CanonicalTimeSourceID);
                    binding.ReceiveAndUpdateFleetIntelligence(Context, 123, IntelItems, CanonicalTimeSourceID);
                    binding.JIT();
                }

                GetTimeMessage(TimeSpan.Zero);
                GetFleetIntelligences(TimeSpan.Zero);
                CheckOrSendTimeMessage(TimeSpan.Zero);
                UpdateMyIntel(TimeSpan.Zero);

                // Set up listeners
                ReportListener = Context.IGC.RegisterBroadcastListener(FleetIntelligenceUtil.IntelReportChannelTag);
                PriorityRequestListener = Context.IGC.RegisterBroadcastListener(FleetIntelligenceUtil.IntelPriorityRequestChannelTag);
                SyncListener = Context.IGC.RegisterBroadcastListener(FleetIntelligenceUtil.IntelSyncChannelTag);
                TimeListener = Context.IGC.RegisterBroadcastListener(FleetIntelligenceUtil.TimeChannelTag);
                PriorityListener = Context.IGC.RegisterBroadcastListener(FleetIntelligenceUtil.IntelPriorityChannelTag);
            }

            GetParts();

            ParseConfigs();
        }
    
        public void Update(TimeSpan timestamp, UpdateFrequency updateFlags)
        {
            if (IsMaster) UpdateIntelFromReports(timestamp);

            GetTimeMessage(timestamp);

            if (runs % 30 == 0)
            {
                if (Host == null)
                {
                    if (!IsMaster) GetSyncMessages(timestamp);
                    CheckOrSendTimeMessage(timestamp);
                }
                UpdateMyIntel(timestamp);
            }

            if (runs % 100 == 0 && Host == null)
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

            runs++;
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
                Context.IGC.SendBroadcastMessage(FleetIntelligenceUtil.IntelPriorityRequestChannelTag, MyTuple.Create(EnemyID, value));
            }

        }

        public Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence> GetFleetIntelligences(TimeSpan timestamp)
        {
            if (Host != null) 
                return Host.GetFleetIntelligences(timestamp);
            if (timestamp == TimeSpan.Zero) 
                return IntelItems;
            GetSyncMessages(timestamp);
            return IntelItems;
        }

        public TimeSpan GetLastUpdatedTime(MyTuple<IntelItemType, long> key)
        {
            if (Host != null)
                return Host.GetLastUpdatedTime(key);
            if (!Timestamps.ContainsKey(key))
                return TimeSpan.MaxValue;
            return Timestamps[key];
        }

        public void ReportFleetIntelligence(IFleetIntelligence item, TimeSpan timestamp)
        {
            if (CanonicalTimeSourceID != 0 && !IsMaster)
            {
                IGCBindings.ForEach(binding => binding.PackAndBroadcastFleetIntelligence(Context, item, CanonicalTimeSourceID));
            }
            MyTuple<IntelItemType, long> intelKey = FleetIntelligenceUtil.GetIntelItemKey(item);
            Timestamps[intelKey] = timestamp;
            if (!IntelItems.ContainsKey(intelKey) || IntelItems[intelKey] != item) IntelItems[intelKey] = item;
        }

        public TimeSpan CanonicalTimeDiff { get
            {
                if (Host != null)
                    return Host.CanonicalTimeDiff;
                return canonicalTimeDiff;
            }
            set
            { 
                canonicalTimeDiff = value;
            }
        } // Add this to timestamp to get canonical time

        TimeSpan canonicalTimeDiff;

        public bool HasMaster
        {
            get
            {
                return CanonicalTimeSourceID != 0;
            }
        }

        public IMyShipController Controller => controller;

        public void ReportCommand(FriendlyShipIntel agent, TaskType taskType, MyTuple<IntelItemType, long> targetKey, TimeSpan timestamp, CommandType commandType = CommandType.Override)
        {
            if (Host != null)
                Host.ReportCommand(agent, taskType, targetKey, timestamp, commandType);
            if (agent.ID == Context.Reference.CubeGrid.EntityId && MyAgent != null)
            {
                MyAgent.AddTask(taskType, targetKey, CommandType.Override, 0, timestamp + CanonicalTimeDiff);
            }
            else
            {
                Context.IGC.SendBroadcastMessage(agent.CommandChannelTag, MyTuple.Create((int)taskType, MyTuple.Create((int)targetKey.Item1, targetKey.Item2), (int)commandType, 0));
            }
        }
        public void AddIntelMutator(IOwnIntelMutator processor)
        {
            intelProcessors.Add(processor);
        }

        const double kOneTick = 16.6666666;
        ExecutionContext Context;
        IMyBroadcastListener SyncListener;
        IMyBroadcastListener ReportListener;
        IMyBroadcastListener PriorityRequestListener;
        IMyBroadcastListener PriorityListener;

        Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence> IntelItems = new Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence>();
        Dictionary<MyTuple<IntelItemType, long>, TimeSpan> Timestamps = new Dictionary<MyTuple<IntelItemType, long>, TimeSpan>();
        List<MyTuple<IntelItemType, long>> KeyScratchpad = new List<MyTuple<IntelItemType, long>>();
        
        TimeSpan kIntelTimeout = TimeSpan.FromSeconds(2);

        long CanonicalTimeSourceID;
        int CanonicalTimeSourceRank;
        IMyBroadcastListener TimeListener;

        long HighestRankID;
        int HighestRank;

        IMyShipController controller;

        HashSet<IOwnIntelMutator> intelProcessors = new HashSet<IOwnIntelMutator>();

        ImmutableDictionary<long, int> EnemyPriorities = null;
        Dictionary<long, int> MasterEnemyPriorities = new Dictionary<long, int>();
        
        List<IIGCIntelBinding> IGCBindings = new List<IIGCIntelBinding>();

        Dictionary<long, int> EnemyPrioritiesOverride = new Dictionary<long, int>();
        HashSet<long> EnemyPrioritiesKeepSet = new HashSet<long>();
        List<long> EnemyPriorityClearScratchpad = new List<long>();

        int runs = 0;

        public IAgentSubsystem MyAgent;

        bool IsMaster = false;
        int Rank = 0;

        float RadiusMulti = 1;

        IntelSubsystem Host;

        public IntelSubsystem(int rank = 0, IntelSubsystem master = null)
        {
            Rank = rank;
            Host = master;
        }

        // [Intel]
        // RadiusMulti = 1
        // Rank = 0
        void ParseConfigs()
        {
            MyIni Parser = new MyIni();
            if (!Parser.TryParse(Context.Reference.CustomData))
                return;

            var flo = Parser.Get("Intel", "RadiusMulti").ToDecimal();
            if (flo != 0) RadiusMulti = (float)flo;

            var num = Parser.Get("Intel", "Rank").ToInt16();
            if (num != 0) Rank = num;
        }

        void GetParts()
        {
            controller = null;
            Context.Terminal.GetBlocksOfType<IMyTerminalBlock>(null, CollectParts);
        }

        bool CollectParts(IMyTerminalBlock block)
        {
            if (!Context.Reference.IsSameConstructAs(block)) return false;

            if (block is IMyShipController && (controller == null || block.CustomName.Contains("[I]")))
                controller = (IMyShipController)block;

            return false;
        }

        void UpdateMyIntel(TimeSpan timestamp)
        {
            if (timestamp == TimeSpan.Zero) return;
            if (controller == null) return;

            FriendlyShipIntel myIntel;
            IMyCubeGrid cubeGrid = Context.Reference.CubeGrid;
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

            if (Host != null)
                Host.ReportFleetIntelligence(myIntel, timestamp);
            else 
                ReportFleetIntelligence(myIntel, timestamp);
        }

        void GetSyncMessages(TimeSpan timestamp)
        {
            while (SyncListener.HasPendingMessage)
            {
                var msg = SyncListener.AcceptMessage();
                if (msg.Source == CanonicalTimeSourceID)
                    IGCBindings.ForEach(binding => binding.ReceiveAndUpdateFleetIntelligenceSyncPackage(Context, msg.Data, IntelItems, ref KeyScratchpad, CanonicalTimeSourceID));
            }

            foreach (var key in KeyScratchpad)
            {
                Timestamps[key] = timestamp;
            }

            KeyScratchpad.Clear();
        }

//         public bool IsIntelTimedOut(TimeSpan timestamp, IntelItemType type, long id)
//         {
//             TimeSpan lastUpdate;
//             return !Timestamps.TryGetValue(MyTuple.Create(type, id), out lastUpdate) ||
//                 (timestamp - lastUpdate).TotalSeconds < 2;
//         }

        void TimeoutIntelItems(TimeSpan timestamp)
        {
            foreach (var kvp in Timestamps)
            {
                if (kvp.Key.Item1 == IntelItemType.Asteroid) continue;
                if ((kvp.Value + kIntelTimeout + TimeSpan.FromSeconds((kvp.Key.Item1 == IntelItemType.Waypoint ? 10 : 0))) < timestamp)
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

        void UpdatePriorities()
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

        void GetTimeMessage(TimeSpan timestamp)
        {
            if (timestamp == TimeSpan.Zero) return;

            MyIGCMessage? msg = null;

            while (TimeListener.HasPendingMessage)
                msg = TimeListener.AcceptMessage();

            if (msg != null)
            {
                var tMsg = (MyIGCMessage)msg;
                if (tMsg.Data is MyTuple<double, int, long>)
                {
                    var unpacked = (MyTuple<double, int, long>)tMsg.Data;

                    if (unpacked.Item2 > CanonicalTimeSourceRank || (unpacked.Item2 == CanonicalTimeSourceRank && unpacked.Item3 > CanonicalTimeSourceID))
                    {
                        CanonicalTimeSourceRank = unpacked.Item2;
                        CanonicalTimeSourceID = unpacked.Item3;
                    }

                    if (CanonicalTimeSourceID == unpacked.Item3)
                    {
                        CanonicalTimeDiff = TimeSpan.FromMilliseconds(unpacked.Item1 + kOneTick) - timestamp;
                    }

                    if (unpacked.Item2 > HighestRank || (unpacked.Item2 == HighestRank && unpacked.Item3 > HighestRankID))
                    {
                        HighestRank = unpacked.Item2;
                        HighestRankID = unpacked.Item3;
                        if (IsMaster) Demote(HighestRankID, HighestRank);
                    }
                }
            }
        }

        void CheckOrSendTimeMessage(TimeSpan timestamp)
        {
            if (timestamp == TimeSpan.Zero) return;

            if (IsMaster)
                IGCBindings.ForEach(binding => binding.PackAndBroadcastFleetIntelligenceSyncPackage(Context, IntelItems, Context.IGC.Me));

            if (Rank > 0)
            {
                Context.IGC.SendBroadcastMessage(FleetIntelligenceUtil.TimeChannelTag, MyTuple.Create(timestamp.TotalMilliseconds, Rank, Context.IGC.Me));
            }

            if (HighestRankID == Context.IGC.Me && !IsMaster && Rank > 0)
            {
                Promote();
            }

            if (HighestRankID != CanonicalTimeSourceID)
            {
                CanonicalTimeSourceRank = 0;
                CanonicalTimeSourceID = 0;
            }

            HighestRank = Rank;
            HighestRankID = Context.IGC.Me;
        }

        void UpdateIntelFromReports(TimeSpan timestamp)
        {
            List<IMyBroadcastListener> listeners = new List<IMyBroadcastListener>();
            Context.IGC.GetBroadcastListeners(listeners);

            while (ReportListener.HasPendingMessage)
            {
                var msg = ReportListener.AcceptMessage();
                foreach ( var binding in IGCBindings)
                {
                    var updateKey = binding.ReceiveAndUpdateFleetIntelligence(Context, msg.Data, IntelItems, Context.IGC.Me);
                    if (updateKey.HasValue)
                    {
                        Timestamps[updateKey.Value] = timestamp;
                        break;
                    }
                }

                if (msg.Data is MyTuple<long, MyTuple<int, long, MyTuple<MyTuple<Vector3D, Vector3D, double>, MyTuple<string, long, float, int>, MyTuple<int, string, int, int, Vector3I>, MyTuple<long, int>>>>)
                {
                    var unpacked = (MyTuple<long, MyTuple<int, long, MyTuple<MyTuple<Vector3D, Vector3D, double>, MyTuple<string, long, float, int>, MyTuple<int, string, int, int, Vector3I>, MyTuple<long, int>>>>)(msg.Data);
                }
            }
        }

        void ReceivePriorityRequests()
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

        void SendPriorities()
        {
            Context.IGC.SendBroadcastMessage(FleetIntelligenceUtil.IntelPriorityChannelTag, MasterEnemyPriorities.ToImmutableDictionary());
        }

        void Promote()
        {
            IsMaster = true;
            CanonicalTimeSourceID = Context.Reference.EntityId;
            CanonicalTimeSourceRank = Rank;
            if (EnemyPriorities != null)
                foreach(var kvp in EnemyPriorities)
                    MasterEnemyPriorities.Add(kvp.Key, kvp.Value);
            CanonicalTimeDiff = TimeSpan.Zero;
        }

        void Demote(long newMasterID, int newMasterRank)
        {
            IsMaster = false;
            CanonicalTimeSourceID = newMasterID;
            EnemyPriorities = null;
            CanonicalTimeSourceRank = newMasterRank;
            MasterEnemyPriorities.Clear();
        }
    }
}
