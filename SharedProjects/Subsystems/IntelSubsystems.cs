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
    [Flags]
    public enum IntelItemType
    {
        NONE = 0,
        Waypoint = 1,
        Asteroid = 2,
        Friendly = 4,
        Dock = 8,
        Enemy = 16,
    }

    public static class FleetIntelligenceUtil
    {
        public const string IntelReportChannelTag = "[FLTINT-RPT]";
        public const string IntelUpdateChannelTag = "[FLTINT-UPD]";
        public const string IntelSyncChannelTag = "[FLTINT-SYN]";
        public const string IntelPriorityChannelTag = "[FLTINT-PRI]";
        public const string IntelPriorityRequestChannelTag = "[FLTINT-PRI-REQ]";
        public const string TimeChannelTag = "[FLTTM]";

        public const int kMaxIntelPerType = 64;

        public static int CompareName(IFleetIntelligence a, IFleetIntelligence b)
        {
            return a.DisplayName.CompareTo(b.DisplayName);
        }

        // Canonical fleet intel packing format is (MasterID, (IntelItemType, IntelItemID, (data)))

        public static void PackAndBroadcastFleetIntelligence(IMyIntergridCommunicationSystem IGC, IFleetIntelligence item, long masterID)
        {
            if (item is Waypoint)
                IGC.SendBroadcastMessage(IntelReportChannelTag, MyTuple.Create(masterID, Waypoint.IGCPackGeneric((Waypoint)item)));
            else if (item is FriendlyShipIntel)
                IGC.SendBroadcastMessage(IntelReportChannelTag, MyTuple.Create(masterID, FriendlyShipIntel.IGCPackGeneric((FriendlyShipIntel)item)));
            else if (item is DockIntel)
                IGC.SendBroadcastMessage(IntelReportChannelTag, MyTuple.Create(masterID, DockIntel.IGCPackGeneric((DockIntel)item)));
            else if (item is AsteroidIntel)
                IGC.SendBroadcastMessage(IntelReportChannelTag, MyTuple.Create(masterID, AsteroidIntel.IGCPackGeneric((AsteroidIntel)item)));
            else if (item is EnemyShipIntel)
                IGC.SendBroadcastMessage(IntelReportChannelTag, MyTuple.Create(masterID, EnemyShipIntel.IGCPackGeneric((EnemyShipIntel)item)));
        }

        /// <summary>
        /// Receives an IGC packed IFleetIntelligence item and if it matches master ID puts it into the dictionary provided, updating the existing entry if necessary. Returns the key of the object updated, if available.
        /// </summary>
        public static MyTuple<IntelItemType, long> ReceiveAndUpdateFleetIntelligence(object data, Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence> intelItems, long masterID)
        {
            // Waypoint
            if (data is MyTuple<long, MyTuple<int, long, MyTuple<Vector3D, Vector3D, Vector3D, float, string>>>)
            {
                var unpacked = (MyTuple<long, MyTuple<int, long, MyTuple<Vector3D, Vector3D, Vector3D, float, string>>>)data;
                if (masterID == unpacked.Item1)
                {
                    var key = MyTuple.Create((IntelItemType)unpacked.Item2.Item1, unpacked.Item2.Item2);
                    if (key.Item1 == IntelItemType.Waypoint)
                    {
                        if (intelItems.ContainsKey(key))
                            ((Waypoint)intelItems[key]).IGCUnpackInto(unpacked.Item2.Item3);
                        else
                            intelItems.Add(key, Waypoint.IGCUnpack(unpacked.Item2.Item3));

                        return key;
                    }
                }
                else
                {
                    return MyTuple.Create(IntelItemType.NONE, (long)0);
                }
            }

            // Friendly
            if (data is MyTuple<long, MyTuple<int, long, MyTuple<MyTuple<Vector3D, Vector3D, double>, MyTuple<string, long, float, int>, MyTuple<int, string, int, int, Vector3I>, MyTuple<long, int>>>>)
            {
                var unpacked = (MyTuple<long, MyTuple<int, long, MyTuple<MyTuple<Vector3D, Vector3D, double>, MyTuple<string, long, float, int>, MyTuple<int, string, int, int, Vector3I>, MyTuple<long, int>>>>)data;
                if (masterID == unpacked.Item1)
                {
                    var key = MyTuple.Create((IntelItemType)unpacked.Item2.Item1, unpacked.Item2.Item2);
                    if (key.Item1 == IntelItemType.Friendly)
                    {
                        if (intelItems.ContainsKey(key))
                            ((FriendlyShipIntel)intelItems[key]).IGCUnpackInto(unpacked.Item2.Item3);
                        else
                            intelItems.Add(key, FriendlyShipIntel.IGCUnpack(unpacked.Item2.Item3));

                        return key;
                    }
                }
                else
                {
                    return MyTuple.Create(IntelItemType.NONE, (long)0);
                }
            }
            // Dock
            if (data is MyTuple<long, MyTuple<int, long, MyTuple<MyTuple<MatrixD, float, float, Vector3D, double, Vector3D>, MyTuple<long, int, int, string>, MyTuple<long, string>>>>)
            {
                var unpacked = (MyTuple<long, MyTuple<int, long, MyTuple<MyTuple<MatrixD, float, float, Vector3D, double, Vector3D>, MyTuple<long, int, int, string>, MyTuple<long, string>>>>)data;
                if (masterID == unpacked.Item1)
                {
                    var key = MyTuple.Create((IntelItemType)unpacked.Item2.Item1, unpacked.Item2.Item2);
                    if (key.Item1 == IntelItemType.Dock)
                    {
                        if (intelItems.ContainsKey(key))
                            ((DockIntel)intelItems[key]).IGCUnpackInto(unpacked.Item2.Item3);
                        else
                            intelItems.Add(key, DockIntel.IGCUnpack(unpacked.Item2.Item3));

                        return key;
                    }
                }
                else
                {
                    return MyTuple.Create(IntelItemType.NONE, (long)0);
                }
            }
            // Asteroid
            if (data is MyTuple<long, MyTuple<int, long, MyTuple<Vector3D, float, long>>>)
            {
                var unpacked = (MyTuple<long, MyTuple<int, long, MyTuple<Vector3D, float, long>>>)data;
                if (masterID == unpacked.Item1)
                {
                    var key = MyTuple.Create((IntelItemType)unpacked.Item2.Item1, unpacked.Item2.Item2);
                    if (key.Item1 == IntelItemType.Asteroid)
                    {
                        if (intelItems.ContainsKey(key))
                            ((AsteroidIntel)intelItems[key]).IGCUnpackInto(unpacked.Item2.Item3);
                        else
                            intelItems.Add(key, AsteroidIntel.IGCUnpack(unpacked.Item2.Item3));

                        return key;
                    }
                }
                else
                {
                    return MyTuple.Create(IntelItemType.NONE, (long)0);
                }
            }

            // Enemy
            if (data is MyTuple<long, MyTuple<int, long, MyTuple<MyTuple<Vector3D, Vector3D, double, double>, MyTuple<string, long, float, int>>>>)
            {
                var unpacked = (MyTuple<long, MyTuple<int, long, MyTuple<MyTuple<Vector3D, Vector3D, double, double>, MyTuple<string, long, float, int>>>>)data;
                if (masterID == unpacked.Item1)
                {
                    var key = MyTuple.Create((IntelItemType)unpacked.Item2.Item1, unpacked.Item2.Item2);
                    if (key.Item1 == IntelItemType.Enemy)
                    {
                        if (intelItems.ContainsKey(key))
                            ((EnemyShipIntel)intelItems[key]).IGCUnpackInto(unpacked.Item2.Item3);
                        else
                            intelItems.Add(key, EnemyShipIntel.IGCUnpack(unpacked.Item2.Item3));

                        return key;
                    }
                }
                else
                {
                    return MyTuple.Create(IntelItemType.NONE, (long)0);
                }
            }

            return MyTuple.Create(IntelItemType.NONE, (long)0);
        }

        public static void PackAndBroadcastFleetIntelligenceSyncPackage(IMyIntergridCommunicationSystem IGC, Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence> intelItems, long masterID, IGCSyncPacker packer)
        {
            packer.WaypointArrayBuilder.Clear();
            packer.FriendlyShipIntelArrayBuilder.Clear();
            packer.DockIntelArrayBuilder.Clear();
            packer.AsteroidIntelArrayBuilder.Clear();
            packer.EnemyShipIntelArrayBuilder.Clear();

            foreach (KeyValuePair<MyTuple<IntelItemType, long>, IFleetIntelligence> kvp in intelItems)
            {
                if (kvp.Key.Item1 == IntelItemType.Waypoint)
                    packer.WaypointArrayBuilder.Add(MyTuple.Create(masterID, Waypoint.IGCPackGeneric((Waypoint)kvp.Value)));
                else if (kvp.Key.Item1 == IntelItemType.Friendly)
                    packer.FriendlyShipIntelArrayBuilder.Add(MyTuple.Create(masterID, FriendlyShipIntel.IGCPackGeneric((FriendlyShipIntel)kvp.Value)));
                else if (kvp.Key.Item1 == IntelItemType.Dock)
                    packer.DockIntelArrayBuilder.Add(MyTuple.Create(masterID, DockIntel.IGCPackGeneric((DockIntel)kvp.Value)));
                else if (kvp.Key.Item1 == IntelItemType.Asteroid)
                    packer.AsteroidIntelArrayBuilder.Add(MyTuple.Create(masterID, AsteroidIntel.IGCPackGeneric((AsteroidIntel)kvp.Value)));
                else if (kvp.Key.Item1 == IntelItemType.Enemy)
                    packer.EnemyShipIntelArrayBuilder.Add(MyTuple.Create(masterID, EnemyShipIntel.IGCPackGeneric((EnemyShipIntel)kvp.Value)));
            }

            IGC.SendBroadcastMessage(IntelSyncChannelTag, packer.WaypointArrayBuilder.ToImmutable());
            IGC.SendBroadcastMessage(IntelSyncChannelTag, packer.FriendlyShipIntelArrayBuilder.ToImmutable());
            IGC.SendBroadcastMessage(IntelSyncChannelTag, packer.DockIntelArrayBuilder.ToImmutable());
            IGC.SendBroadcastMessage(IntelSyncChannelTag, packer.AsteroidIntelArrayBuilder.ToImmutable());
            IGC.SendBroadcastMessage(IntelSyncChannelTag, packer.EnemyShipIntelArrayBuilder.ToImmutable());
        }

        /// <summary>
        /// Receives an array of all of one type of fleet intelligence and puts them into the dictionary provided, updating existing entry if necessary. Adds the keys of each item updated this way to the provided scratchpad.
        /// </summary>
        public static void ReceiveAndUpdateFleetIntelligenceSyncPackage(object data, Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence> intelItems, ref List<MyTuple<IntelItemType, long>> updatedScratchpad, long masterID)
        {
            // Waypoint
            if (data is ImmutableArray<MyTuple<long, MyTuple<int, long, MyTuple<Vector3D, Vector3D, Vector3D, float, string>>>>)
            {
                foreach (var item in (ImmutableArray<MyTuple<long, MyTuple<int, long, MyTuple<Vector3D, Vector3D, Vector3D, float, string>>>>)data)
                {
                    var updatedKey = ReceiveAndUpdateFleetIntelligence(item, intelItems, masterID);
                    if (updatedKey.Item1 != IntelItemType.NONE) updatedScratchpad.Add(updatedKey);
                }
            }
            // FriendlyShipIntel
            else if (data is ImmutableArray<MyTuple<long, MyTuple<int, long, MyTuple<MyTuple<Vector3D, Vector3D, double>, MyTuple<string, long, float, int>, MyTuple<int, string, int, int, Vector3I>, MyTuple<long, int>>>>>)
            {
                foreach (var item in (ImmutableArray<MyTuple<long, MyTuple<int, long, MyTuple<MyTuple<Vector3D, Vector3D, double>, MyTuple<string, long, float, int>, MyTuple<int, string, int, int, Vector3I>, MyTuple<long, int>>>>>)data)
                {
                    var updatedKey = ReceiveAndUpdateFleetIntelligence(item, intelItems, masterID);
                    if (updatedKey.Item1 != IntelItemType.NONE) updatedScratchpad.Add(updatedKey);
                }
            }
            // DockIntel
            else if (data is ImmutableArray<MyTuple<long, MyTuple<int, long, MyTuple<MyTuple<MatrixD, float, float, Vector3D, double, Vector3D>, MyTuple<long, int, int, string>, MyTuple<long, string>>>>>)
            {
                foreach (var item in (ImmutableArray<MyTuple<long, MyTuple<int, long, MyTuple<MyTuple<MatrixD, float, float, Vector3D, double, Vector3D>, MyTuple<long, int, int, string>, MyTuple<long, string>>>>>)data)
                {
                    var updatedKey = ReceiveAndUpdateFleetIntelligence(item, intelItems, masterID);
                    if (updatedKey.Item1 != IntelItemType.NONE) updatedScratchpad.Add(updatedKey);
                }
            }
            // AsteroidIntel
            else if (data is ImmutableArray<MyTuple<long, MyTuple<int, long, MyTuple<Vector3D, float, long>>>>)
            {
                foreach (var item in (ImmutableArray<MyTuple<long, MyTuple<int, long, MyTuple<Vector3D, float, long>>>>)data)
                {
                    var updatedKey = ReceiveAndUpdateFleetIntelligence(item, intelItems, masterID);
                    if (updatedKey.Item1 != IntelItemType.NONE) updatedScratchpad.Add(updatedKey);
                }
            }
            // EnemyShipIntel
            else if (data is ImmutableArray<MyTuple<long, MyTuple<int, long, MyTuple<MyTuple<Vector3D, Vector3D, double, double>, MyTuple<string, long, float, int>>>>>)
            {
                foreach (var item in (ImmutableArray<MyTuple<long, MyTuple<int, long, MyTuple<MyTuple<Vector3D, Vector3D, double, double>, MyTuple<string, long, float, int>>>>>)data)
                {
                    var updatedKey = ReceiveAndUpdateFleetIntelligence(item, intelItems, masterID);
                    if (updatedKey.Item1 != IntelItemType.NONE) updatedScratchpad.Add(updatedKey);
                }
            }
        }

        public static MyTuple<IntelItemType, long> GetIntelItemKey(IFleetIntelligence item)
        {
            return MyTuple.Create(item.IntelItemType, item.ID);
        }
    }

    public class IGCSyncPacker
    {
        public ImmutableArray<MyTuple<long, MyTuple<int, long, MyTuple<Vector3D, Vector3D, Vector3D, float, string>>>>.Builder WaypointArrayBuilder =
            ImmutableArray.CreateBuilder<MyTuple<long, MyTuple<int, long, MyTuple<Vector3D, Vector3D, Vector3D, float, string>>>>(64);
        public ImmutableArray<MyTuple<long, MyTuple<int, long, MyTuple<MyTuple<Vector3D, Vector3D, double>, MyTuple<string, long, float, int>, MyTuple<int, string, int, int, Vector3I>, MyTuple<long, int>>>>>.Builder FriendlyShipIntelArrayBuilder =
            ImmutableArray.CreateBuilder<MyTuple<long, MyTuple<int, long, MyTuple<MyTuple<Vector3D, Vector3D, double>, MyTuple<string, long, float, int>, MyTuple<int, string, int, int, Vector3I>, MyTuple<long, int>>>>>(64);
        public ImmutableArray<MyTuple<long, MyTuple<int, long, MyTuple<MyTuple<MatrixD, float, float, Vector3D, double, Vector3D>, MyTuple<long, int, int, string>, MyTuple<long, string>>>>>.Builder
            DockIntelArrayBuilder = ImmutableArray.CreateBuilder<MyTuple<long, MyTuple<int, long, MyTuple<MyTuple<MatrixD, float, float, Vector3D, double, Vector3D>, MyTuple<long, int, int, string>, MyTuple<long, string>>>>>(64);
        public ImmutableArray<MyTuple<long, MyTuple<int, long, MyTuple<Vector3D, float, long>>>>.Builder
            AsteroidIntelArrayBuilder = ImmutableArray.CreateBuilder<MyTuple<long, MyTuple<int, long, MyTuple<Vector3D, float, long>>>>(64);
        public ImmutableArray<MyTuple<long, MyTuple<int, long, MyTuple<MyTuple<Vector3D, Vector3D, double, double>, MyTuple<string, long, float, int>>>>>.Builder
            EnemyShipIntelArrayBuilder = ImmutableArray.CreateBuilder<MyTuple<long, MyTuple<int, long, MyTuple<MyTuple<Vector3D, Vector3D, double, double>, MyTuple<string, long, float, int>>>>>(64);
    }

    public class VectorUtilities
    {
        public static Vector3D StringToVector3(string sVector)
        {
            sVector = sVector.Substring(1, sVector.Length - 2);
            string[] sArray = sVector.Split(' ');
            Vector3D result = new Vector3(
                float.Parse(sArray[0].Substring(2)),
                float.Parse(sArray[1].Substring(2)),
                float.Parse(sArray[2].Substring(2)));
            return result;
        }
    }

    // Representations of objects in the fleet intelligence system
    public interface IFleetIntelligence
    {
        Vector3D GetPositionFromCanonicalTime(TimeSpan CanonicalTime);
        Vector3D GetVelocity();

        float Radius { get; }

        long ID { get; }
        string DisplayName { get; }
        IntelItemType IntelItemType { get; }

        string Serialize();
        void Deserialize(string s);
    }

    public class Waypoint : IFleetIntelligence
    {
        public Vector3D Position; // Position of Zero means to stop moving, One means to keep original
        public Vector3D Direction; // Direction of Zero means to stop turning, One means to keep original
        public Vector3D DirectionUp; // Direction of Zero means to stop turning, One means to keep original
        public Vector3D Velocity;
        public float MaxSpeed;
        public string Name;

        public static string SerializeWaypoint(Waypoint w)
        {
            return $"{w.Position.ToString()}|{w.Direction.ToString()}|{w.MaxSpeed.ToString()}|{w.Name}";
        }

        public static Waypoint DeserializeWaypoint(string s)
        {
            Waypoint w = new Waypoint();
            w.Deserialize(s);
            return w;
        }
        public Waypoint()
        {
            Position = Vector3.One;
            Direction = Vector3.One;
            MaxSpeed = -1;
            Name = "Waypoint";
        }

        public float Radius => 50f;
        public string DisplayName => Name;
        public long ID => Position.ToString().GetHashCode();
        public IntelItemType IntelItemType => IntelItemType.Waypoint;
        public Vector3D GetPositionFromCanonicalTime(TimeSpan CanonicalTime)
        {
            return Position;
        }

        public Vector3D GetVelocity()
        {
            return Vector3.Zero;
        }

        public string Serialize()
        {
            return SerializeWaypoint(this);
        }

        public void Deserialize(string s)
        {
            string[] split = s.Split('|');
            Position = VectorUtilities.StringToVector3(split[0]);
            Direction = VectorUtilities.StringToVector3(split[1]);
            DirectionUp = VectorUtilities.StringToVector3(split[2]);
            MaxSpeed = float.Parse(split[3]);
            Name = split[4];
        }

        static public MyTuple<int, long, MyTuple<Vector3D, Vector3D, Vector3D, float, string>> IGCPackGeneric(Waypoint w)
        {
            return MyTuple.Create
            (
                (int)w.IntelItemType,
                w.ID,
                MyTuple.Create
                (
                    w.Position,
                    w.Direction,
                    w.DirectionUp,
                    w.MaxSpeed,
                    w.Name
                )
            );
        }
        static public Waypoint IGCUnpack(object data)
        {
            var unpacked = (MyTuple<Vector3D, Vector3D, Vector3D, float, string>)data;
            var w = new Waypoint();
            w.IGCUnpackInto(unpacked);
            return w;
        }

        public void IGCUnpackInto(MyTuple<Vector3D, Vector3D, Vector3D, float, string> unpacked)
        {
            Position = unpacked.Item1;
            Direction = unpacked.Item2;
            DirectionUp = unpacked.Item3;
            MaxSpeed = unpacked.Item4;
            Name = unpacked.Item5;
        }
    }

    public class FriendlyShipIntel : IFleetIntelligence
    {
        public float Radius { get; set; }

        public long ID { get; set; }

        public string DisplayName { get; set; }

        public IntelItemType IntelItemType => IntelItemType.Friendly;

        public void Deserialize(string s)
        {
            // TODO: Implement
        }
        public string Serialize()
        {
            return string.Empty;
        }

        public Vector3D GetPositionFromCanonicalTime(TimeSpan CanonicalTime)
        {
            return CurrentPosition + CurrentVelocity * (CanonicalTime - CurrentCanonicalTime).TotalSeconds;
        }

        public Vector3D GetVelocity()
        {
            return CurrentVelocity;
        }

        public Vector3D CurrentVelocity;
        public Vector3D CurrentPosition;
        public TimeSpan CurrentCanonicalTime;

        public TaskType AcceptedTaskTypes;
        public string CommandChannelTag;
        public AgentClass AgentClass;
        public AgentStatus AgentStatus;
        public Vector3I HydroPowerInv = Vector3I.Zero;

        public long HomeID = 0;
        public HangarTags HangarTags = HangarTags.None;

        public int Rank = 0;

        static public MyTuple<int, long, MyTuple<MyTuple<Vector3D, Vector3D, double>, MyTuple<string, long, float, int>, MyTuple<int, string, int, int, Vector3I>, MyTuple<long, int>>> IGCPackGeneric(FriendlyShipIntel fsi)
        {
            return MyTuple.Create
            (
                (int)fsi.IntelItemType,
                fsi.ID,
                MyTuple.Create
                (
                    MyTuple.Create
                    (
                        fsi.CurrentPosition,
                        fsi.CurrentVelocity,
                        fsi.CurrentCanonicalTime.TotalMilliseconds
                    ),
                     MyTuple.Create
                    (
                        fsi.DisplayName,
                        fsi.ID,
                        fsi.Radius,
                        fsi.Rank
                    ),
                     MyTuple.Create
                    (
                        (int)fsi.AcceptedTaskTypes,
                        fsi.CommandChannelTag,
                        (int)fsi.AgentClass,
                        (int)fsi.AgentStatus,
                        fsi.HydroPowerInv
                    ),
                     MyTuple.Create
                    (
                         fsi.HomeID,
                         (int)fsi.HangarTags
                    )
                )
            );
        }
        static public FriendlyShipIntel IGCUnpack(object data)
        {
            var unpacked = (MyTuple<MyTuple<Vector3D, Vector3D, double>, MyTuple<string, long, float, int>, MyTuple<int, string, int, int, Vector3I>, MyTuple<long, int>>)data;
            var fsi = new FriendlyShipIntel();
            fsi.IGCUnpackInto(unpacked);
            return fsi;
        }

        public void IGCUnpackInto(MyTuple<MyTuple<Vector3D, Vector3D, double>, MyTuple<string, long, float, int>, MyTuple<int, string, int, int, Vector3I>, MyTuple<long, int>> unpacked)
        {
            CurrentPosition = unpacked.Item1.Item1;
            CurrentVelocity = unpacked.Item1.Item2;
            CurrentCanonicalTime = TimeSpan.FromMilliseconds(unpacked.Item1.Item3);

            DisplayName = unpacked.Item2.Item1;
            ID = unpacked.Item2.Item2;
            Radius = unpacked.Item2.Item3;
            Rank = unpacked.Item2.Item4;

            AcceptedTaskTypes = (TaskType)unpacked.Item3.Item1;
            CommandChannelTag = unpacked.Item3.Item2;
            AgentClass = (AgentClass)unpacked.Item3.Item3;
            AgentStatus = (AgentStatus)unpacked.Item3.Item4;
            HydroPowerInv = unpacked.Item3.Item5;

            HomeID = unpacked.Item4.Item1;
            HangarTags = (HangarTags)unpacked.Item4.Item2;
        }
    }

    public class AsteroidIntel : IFleetIntelligence
    {
        public Vector3D Position;

        public static string SerializeAsteroid(AsteroidIntel astr)
        {
            return $"{astr.Position.ToString()}|{astr.Radius}|{astr.ID}";
        }

        public static AsteroidIntel DeserializeAsteroid(string s)
        {
            AsteroidIntel astr = new AsteroidIntel();
            astr.Deserialize(s);
            return astr;
        }


        public float Radius { get; set; }
        public string DisplayName => "Asteroid";
        public long ID { get; set; }
        public IntelItemType IntelItemType => IntelItemType.Asteroid;
        public Vector3D GetPositionFromCanonicalTime(TimeSpan CanonicalTime)
        {
            return Position;
        }

        public Vector3D GetVelocity()
        {
            return Vector3.Zero;
        }

        public string Serialize()
        {
            return SerializeAsteroid(this);
        }

        public void Deserialize(string s)
        {
            string[] split = s.Split('|');
            Position = VectorUtilities.StringToVector3(split[0]);
            Radius = float.Parse(split[1]);
            ID = long.Parse(split[2]);
        }

        static public MyTuple<int, long, MyTuple<Vector3D, float, long>> IGCPackGeneric(AsteroidIntel astr)
        {
            return MyTuple.Create
            (
                (int)astr.IntelItemType,
                astr.ID,
                MyTuple.Create
                (
                    astr.Position,
                    astr.Radius,
                    astr.ID
                )
            );
        }
        static public AsteroidIntel IGCUnpack(object data)
        {
            var unpacked = (MyTuple<Vector3D, float, long>)data;
            var astr = new AsteroidIntel();
            astr.IGCUnpackInto(unpacked);
            return astr;
        }

        public void IGCUnpackInto(MyTuple<Vector3D, float, long> unpacked)
        {
            Position = unpacked.Item1;
            Radius = unpacked.Item2;
            ID = unpacked.Item3;
        }
    }

    public enum HangarStatus
    {
        None = 0,
        Available = 1 << 0,
        Reserved = 1 << 1,
        ReadyToDock = 1 << 2,
        Docking = 1 << 3,
        Launching = 1 << 4,
    }

    [Flags]
    public enum HangarTags
    {
        None = 0,
        A = 1 << 0,
        B = 1 << 1,
        C = 1 << 2,
        D = 1 << 3,
        E = 1 << 4,
        F = 1 << 5,
        G = 1 << 6,
        H = 1 << 7,
        I = 1 << 8,
        J = 1 << 9,
        K = 1 << 10,
    }

    public class DockIntel : IFleetIntelligence
    {
        public float Radius => 0;

        public long ID { get; set; }

        public string DisplayName { get; set; }

        public IntelItemType IntelItemType => IntelItemType.Dock;

        public void Deserialize(string s)
        {
        }

        public Vector3D GetPositionFromCanonicalTime(TimeSpan CanonicalTime)
        {
            return WorldMatrix.Translation + CurrentVelocity * (CanonicalTime - CurrentCanonicalTime).TotalSeconds;
        }

        public Vector3D GetVelocity()
        {
            return CurrentVelocity;
        }

        public string Serialize()
        {
            return string.Empty;
        }

        public MatrixD WorldMatrix;
        public float UndockFar = 20;
        public float UndockNear = 1.5f;
        public Vector3D IndicatorDir;

        public Vector3D CurrentVelocity;
        public TimeSpan CurrentCanonicalTime;

        public long OwnerID;
        public HangarStatus Status;
        public HangarTags Tags;
        public string HangarChannelTag;

        static public MyTuple<int, long, MyTuple<MyTuple<MatrixD, float, float, Vector3D, double, Vector3D>, MyTuple<long, int, int, string>, MyTuple<long, string>>> IGCPackGeneric(DockIntel di)
        {
            return MyTuple.Create
            (
                (int)di.IntelItemType,
                di.ID,
                MyTuple.Create
                (
                    MyTuple.Create
                    (
                        di.WorldMatrix,
                        di.UndockFar,
                        di.UndockNear,
                        di.CurrentVelocity,
                        di.CurrentCanonicalTime.TotalMilliseconds,
                        di.IndicatorDir
                    ),
                     MyTuple.Create
                    (
                         di.OwnerID,
                         (int)di.Status,
                         (int)di.Tags,
                         di.HangarChannelTag
                    ),
                     MyTuple.Create
                    (
                        di.ID,
                        di.DisplayName
                    )
                )
            );
        }
        static public DockIntel IGCUnpack(object data)
        {
            var unpacked = (MyTuple<MyTuple<MatrixD, float, float, Vector3D, double, Vector3D>, MyTuple<long, int, int, string>, MyTuple<long, string>>)data;
            var di = new DockIntel();
            di.IGCUnpackInto(unpacked);
            return di;
        }

        public void IGCUnpackInto(MyTuple<MyTuple<MatrixD, float, float, Vector3D, double, Vector3D>, MyTuple<long, int, int, string>, MyTuple<long, string>> unpacked)
        {
            WorldMatrix = unpacked.Item1.Item1;
            UndockFar = unpacked.Item1.Item2;
            UndockNear = unpacked.Item1.Item3;
            CurrentVelocity = unpacked.Item1.Item4;
            CurrentCanonicalTime = TimeSpan.FromMilliseconds(unpacked.Item1.Item5);
            IndicatorDir = unpacked.Item1.Item6;
            OwnerID = unpacked.Item2.Item1;
            Status = (HangarStatus)unpacked.Item2.Item2;
            Tags = (HangarTags)unpacked.Item2.Item3;
            HangarChannelTag = unpacked.Item2.Item4;
            ID = unpacked.Item3.Item1;
            DisplayName = unpacked.Item3.Item2;
        }

        static public bool TagsMatch(HangarTags x, HangarTags y)
        {
            return (x == 0 || y == 0 || (x & y) != 0);
        }
    }

    public class EnemyShipIntel : IFleetIntelligence
    {
        public float Radius { get; set; }

        public long ID { get; set; }

        public string DisplayName { get; set; }

        public IntelItemType IntelItemType => IntelItemType.Enemy;

        public void Deserialize(string s)
        {
        }

        public Vector3D GetPositionFromCanonicalTime(TimeSpan CanonicalTime)
        {
            return CurrentPosition + CurrentVelocity * (CanonicalTime - CurrentCanonicalTime).TotalSeconds;
        }

        public Vector3D GetVelocity()
        {
            return CurrentVelocity;
        }

        public string Serialize()
        {
            return string.Empty;
        }

        public Vector3D CurrentVelocity;
        public Vector3D CurrentPosition;
        public TimeSpan CurrentCanonicalTime;
        public MyCubeSize CubeSize;
        public TimeSpan LastValidatedCanonicalTime;
        static public MyTuple<int, long, MyTuple<MyTuple<Vector3D, Vector3D, double, double>, MyTuple<string, long, float, int>>> IGCPackGeneric(EnemyShipIntel esi)
        {
            return MyTuple.Create
            (
                (int)esi.IntelItemType,
                esi.ID,
                MyTuple.Create
                (
                    MyTuple.Create
                    (
                        esi.CurrentPosition,
                        esi.CurrentVelocity,
                        esi.CurrentCanonicalTime.TotalMilliseconds,
                        esi.LastValidatedCanonicalTime.TotalMilliseconds
                    ),
                     MyTuple.Create
                    (
                        esi.DisplayName,
                        esi.ID,
                        esi.Radius,
                        (int)esi.CubeSize
                    )
                )
            );
        }
        static public EnemyShipIntel IGCUnpack(object data)
        {
            var unpacked = (MyTuple<MyTuple<Vector3D, Vector3D, double, double>, MyTuple<string, long, float, int>>)data;
            var esi = new EnemyShipIntel();
            esi.IGCUnpackInto(unpacked);
            return esi;
        }

        public void IGCUnpackInto(MyTuple<MyTuple<Vector3D, Vector3D, double, double>, MyTuple<string, long, float, int>> unpacked)
        {
            CurrentPosition = unpacked.Item1.Item1;
            CurrentVelocity = unpacked.Item1.Item2;
            CurrentCanonicalTime = TimeSpan.FromMilliseconds(unpacked.Item1.Item3);
            LastValidatedCanonicalTime = TimeSpan.FromMilliseconds(unpacked.Item1.Item4);
            DisplayName = unpacked.Item2.Item1;
            ID = unpacked.Item2.Item2;
            Radius = unpacked.Item2.Item3;
            CubeSize = (MyCubeSize)unpacked.Item2.Item4;
        }

        static public bool PrioritizeTarget(EnemyShipIntel target)
        {
            if (target.CubeSize == MyCubeSize.Small && target.Radius < 4) return false;
            if (target.CubeSize == MyCubeSize.Large && target.Radius < 8) return false;
            return true;
        }

        public void FromDetectedInfo(MyDetectedEntityInfo info, TimeSpan canonicalTime, bool updateSize = false)
        {
            if (info.Type != MyDetectedEntityType.SmallGrid && info.Type != MyDetectedEntityType.LargeGrid && info.Type != MyDetectedEntityType.Unknown) return;
            if (info.Relationship != MyRelationsBetweenPlayerAndBlock.Enemies && info.Relationship != MyRelationsBetweenPlayerAndBlock.Neutral) return;

            if (ID != info.EntityId)
            {
                if (info.Type != MyDetectedEntityType.Unknown) // Only update with unknown intel if ID matches
                {
                    CubeSize = info.Type == MyDetectedEntityType.SmallGrid ? MyCubeSize.Small : MyCubeSize.Large;
                    ID = info.EntityId;
                }
                else return;
            }

            if (DisplayName == null || DisplayName.StartsWith("S-") || DisplayName.StartsWith("L-"))
            {
                if (updateSize || DisplayName == null)
                {
                    if (updateSize) LastValidatedCanonicalTime = canonicalTime;
                    Radius = (float)info.BoundingBox.Size.Length() * 0.5f;
                    DisplayName = (info.Type == MyDetectedEntityType.SmallGrid ? "S-" : "L-") + ((int)Radius).ToString() + " " + info.EntityId.ToString();
                }
            }

            CurrentPosition = info.BoundingBox.Center;
            CurrentVelocity = info.Velocity;
            CurrentCanonicalTime = canonicalTime;
        }

        public void FromCubeGrid(IMyCubeGrid grid, TimeSpan canonicalTime, Vector3D velocity)
        {
            if (ID != grid.EntityId)
            {
                CubeSize = grid.GridSizeEnum;
                ID = grid.EntityId;
            }

            DisplayName = (grid.GridSizeEnum == MyCubeSize.Small ? "S-" : "L-") + ((int)Radius).ToString() + " " + grid.EntityId.ToString();

            Radius = (float)grid.WorldAABB.Size.Length() * 0.5f;
            CurrentPosition = grid.WorldAABB.Center;
            CurrentVelocity = velocity;
            CurrentCanonicalTime = canonicalTime;
            LastValidatedCanonicalTime = canonicalTime;
        }
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

        public string GetStatus()
        {
            debugBuilder.Clear();
            debugBuilder.AppendLine(IntelItems.Count().ToString());
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
        IMyTerminalBlock ProgramReference;
        public void Setup(MyGridProgram program, string name, IMyTerminalBlock programReference = null)
        {
            ProgramReference = programReference;
            if (ProgramReference == null) ProgramReference = program.Me;
            Program = program;

            if (Host == null)
            {
                // JIT initialization
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
                UpdateMyIntel(TimeSpan.Zero);

                // Set up listeners
                ReportListener = program.IGC.RegisterBroadcastListener(FleetIntelligenceUtil.IntelReportChannelTag);
                PriorityRequestListener = program.IGC.RegisterBroadcastListener(FleetIntelligenceUtil.IntelPriorityRequestChannelTag);
                SyncListener = program.IGC.RegisterBroadcastListener(FleetIntelligenceUtil.IntelSyncChannelTag);
                TimeListener = program.IGC.RegisterBroadcastListener(FleetIntelligenceUtil.TimeChannelTag);
                PriorityListener = program.IGC.RegisterBroadcastListener(FleetIntelligenceUtil.IntelPriorityChannelTag);
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
                Program.IGC.SendBroadcastMessage(FleetIntelligenceUtil.IntelPriorityRequestChannelTag, MyTuple.Create(EnemyID, value));
            }

        }

        public Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence> GetFleetIntelligences(TimeSpan timestamp)
        {
            if (Host != null) return Host.GetFleetIntelligences(timestamp);
            if (timestamp == TimeSpan.Zero) return IntelItems;
            GetSyncMessages(timestamp);
            return IntelItems;
        }

        public TimeSpan GetLastUpdatedTime(MyTuple<IntelItemType, long> key)
        {
            if (Host != null) return Host.GetLastUpdatedTime(key);
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

        public TimeSpan CanonicalTimeDiff { get
            {
                if (Host != null) return Host.CanonicalTimeDiff;
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
            if (Host != null) Host.ReportCommand(agent, taskType, targetKey, timestamp, commandType);
            if (agent.ID == ProgramReference.CubeGrid.EntityId && MyAgent != null)
            {
                MyAgent.AddTask(taskType, targetKey, CommandType.Override, 0, timestamp + CanonicalTimeDiff);
            }
            else
            {
                Program.IGC.SendBroadcastMessage(agent.CommandChannelTag, MyTuple.Create((int)taskType, MyTuple.Create((int)targetKey.Item1, targetKey.Item2), (int)commandType, 0));
            }
        }
        public void AddIntelMutator(IOwnIntelMutator processor)
        {
            intelProcessors.Add(processor);
        }

        const double kOneTick = 16.6666666;
        MyGridProgram Program;
        IMyBroadcastListener SyncListener;
        IMyBroadcastListener ReportListener;
        IMyBroadcastListener PriorityRequestListener;
        IMyBroadcastListener PriorityListener;

        Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence> IntelItems = new Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence>();
        Dictionary<MyTuple<IntelItemType, long>, TimeSpan> Timestamps = new Dictionary<MyTuple<IntelItemType, long>, TimeSpan>();
        List<MyTuple<IntelItemType, long>> KeyScratchpad = new List<MyTuple<IntelItemType, long>>();

        StringBuilder debugBuilder = new StringBuilder();

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
        IGCSyncPacker IGCSyncPacker = new IGCSyncPacker();

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
            MyIniParseResult result;
            if (!Parser.TryParse(ProgramReference.CustomData, out result))
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

        bool CollectParts(IMyTerminalBlock block)
        {
            if (!ProgramReference.IsSameConstructAs(block)) return false;

            if (block is IMyShipController && (controller == null || block.CustomName.Contains("[I]")))
                controller = (IMyShipController)block;

            return false;
        }

        void UpdateMyIntel(TimeSpan timestamp)
        {
            if (timestamp == TimeSpan.Zero) return;
            if (controller == null) return;

            FriendlyShipIntel myIntel;
            IMyCubeGrid cubeGrid = ProgramReference.CubeGrid;
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

            if (Host != null) Host.ReportFleetIntelligence(myIntel, timestamp);
            else ReportFleetIntelligence(myIntel, timestamp);
        }

        void GetSyncMessages(TimeSpan timestamp)
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

            if (IsMaster) FleetIntelligenceUtil.PackAndBroadcastFleetIntelligenceSyncPackage(Program.IGC, IntelItems, Program.IGC.Me, IGCSyncPacker);

            if (Rank > 0)
            {
                Program.IGC.SendBroadcastMessage(FleetIntelligenceUtil.TimeChannelTag, MyTuple.Create(timestamp.TotalMilliseconds, Rank, Program.IGC.Me));
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

        void UpdateIntelFromReports(TimeSpan timestamp)
        {
            List<IMyBroadcastListener> listeners = new List<IMyBroadcastListener>();
            Program.IGC.GetBroadcastListeners(listeners);

            while (ReportListener.HasPendingMessage)
            {
                var msg = ReportListener.AcceptMessage();
                var updateKey = FleetIntelligenceUtil.ReceiveAndUpdateFleetIntelligence(msg.Data, IntelItems, Program.IGC.Me);
                if (updateKey.Item1 != IntelItemType.NONE)
                {
                    Timestamps[updateKey] = timestamp;
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
            Program.IGC.SendBroadcastMessage(FleetIntelligenceUtil.IntelPriorityChannelTag, MasterEnemyPriorities.ToImmutableDictionary());
        }

        void Promote()
        {
            IsMaster = true;
            CanonicalTimeSourceID = ProgramReference.EntityId;
            CanonicalTimeSourceRank = Rank;
            if (EnemyPriorities != null) foreach(var kvp in EnemyPriorities) MasterEnemyPriorities.Add(kvp.Key, kvp.Value);
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
