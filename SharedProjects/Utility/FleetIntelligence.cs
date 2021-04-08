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
using System.Collections.Immutable;

namespace IngameScript
{
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
    public class IGCArrayWrapper<T> where T : IEnumerable
    {
        public object data;
        public bool HasValue() { return data is T; }
        public T GetValue() { return (T)data; }
        public IGCArrayWrapper( object _data )
        {
            data = _data;
        }
        public bool TryIGCUnpack(Func<object, Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence>, long, MyTuple<IntelItemType, long>?> unpacker, 
                                    Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence> intelItems, ref List<MyTuple<IntelItemType, long>> updatedScratchpad, long masterID)
        {
            if (!HasValue())
                return false;
            T array = GetValue();
            foreach (var item in array)
            {
                var updatedKey = Waypoint.TryIGCUnpack(item, intelItems, masterID);
                if (updatedKey.HasValue)
                    updatedScratchpad.Add(updatedKey.Value);
            }
            return true;
        }

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
        public static MyTuple<IntelItemType, long>? ReceiveAndUpdateFleetIntelligence(object data, Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence> intelItems, long masterID)
        {
            MyTuple<IntelItemType, long>? updatedIntel;

            updatedIntel = Waypoint.TryIGCUnpack(data, intelItems, masterID);
            if (updatedIntel != null)
                return updatedIntel;

            updatedIntel = FriendlyShipIntel.TryIGCUnpack(data, intelItems, masterID);
            if (updatedIntel != null)
                return updatedIntel;

            updatedIntel = DockIntel.TryIGCUnpack(data, intelItems, masterID);
            if (updatedIntel != null)
                return updatedIntel;

            updatedIntel = AsteroidIntel.TryIGCUnpack(data, intelItems, masterID);
            if (updatedIntel != null)
                return updatedIntel;

            updatedIntel = EnemyShipIntel.TryIGCUnpack(data, intelItems, masterID);
            if (updatedIntel != null)
                return updatedIntel;

            return updatedIntel;
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
        /// 
        public static void ReceiveAndUpdateFleetIntelligenceSyncPackage(object data, Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence> intelItems, ref List<MyTuple<IntelItemType, long>> updatedScratchpad, long masterID)
        {
            // Waypoint
            var waypointWrapper = new IGCArrayWrapper<ImmutableArray<MyTuple<long, MyTuple<int, long, MyTuple<Vector3D, Vector3D, Vector3D, float, string>>>>>(data);
            if (waypointWrapper.TryIGCUnpack(Waypoint.TryIGCUnpack, intelItems, ref updatedScratchpad, masterID))
            {
                return;
            }
            // FriendlyShipIntel
            var friendlyWrapper = new IGCArrayWrapper<ImmutableArray<MyTuple<long, MyTuple<int, long, MyTuple<MyTuple<Vector3D, Vector3D, double>, MyTuple<string, long, float, int>, MyTuple<int, string, int, int, Vector3I>, MyTuple<long, int>>>>>>(data);
            if (waypointWrapper.TryIGCUnpack(FriendlyShipIntel.TryIGCUnpack, intelItems, ref updatedScratchpad, masterID))
            {
                return;
            }
            // DockIntel
            var dockWrapper = new IGCArrayWrapper<ImmutableArray<MyTuple<long, MyTuple<int, long, MyTuple<MyTuple<MatrixD, float, float, Vector3D, double, Vector3D>, MyTuple<long, int, int, string>, MyTuple<long, string>>>>>>(data);
            if (dockWrapper.TryIGCUnpack(DockIntel.TryIGCUnpack, intelItems, ref updatedScratchpad, masterID))
            {
                return;
            }
            // AsteroidIntel
            var asteroidWrapper = new IGCArrayWrapper<ImmutableArray<MyTuple<long, MyTuple<int, long, MyTuple<Vector3D, float, long>>>>>(data);
            if (asteroidWrapper.TryIGCUnpack(AsteroidIntel.TryIGCUnpack, intelItems, ref updatedScratchpad, masterID))
            {
                return;
            }
            // EnemyShipIntel
            var enemyWrapper = new IGCArrayWrapper<ImmutableArray<MyTuple<long, MyTuple<int, long, MyTuple<MyTuple<Vector3D, Vector3D, double, double>, MyTuple<string, long, float, int>>>>>>(data);
            if (enemyWrapper.TryIGCUnpack(EnemyShipIntel.TryIGCUnpack, intelItems, ref updatedScratchpad, masterID))
            {
            }
            return;
        }

        public static MyTuple<IntelItemType, long> GetIntelItemKey(IFleetIntelligence item)
        {
            return MyTuple.Create(item.Type, item.ID);
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
        IntelItemType Type { get; }

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
    public IntelItemType Type => IntelItemType.Waypoint;
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
            (int)w.Type,
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

    public static MyTuple<IntelItemType, long>? TryIGCUnpack(object data, Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence> intelItems, long masterID)
    {
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
        }
        return null;
    }
}

public class FriendlyShipIntel : IFleetIntelligence
    {
        public float Radius { get; set; }

        public long ID { get; set; }

        public string DisplayName { get; set; }

        public IntelItemType Type => IntelItemType.Friendly;

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
                (int)fsi.Type,
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
        public static MyTuple<IntelItemType, long>? TryIGCUnpack(object data, Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence> intelItems, long masterID)
        {
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
            }
            return null;
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
        public IntelItemType Type => IntelItemType.Asteroid;
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
                (int)astr.Type,
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
        public static MyTuple<IntelItemType, long>? TryIGCUnpack(object data, Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence> intelItems, long masterID)
        {
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
            }
            return null;
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

        public IntelItemType Type => IntelItemType.Dock;

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
                (int)di.Type,
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
        public static MyTuple<IntelItemType, long>? TryIGCUnpack(object data, Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence> intelItems, long masterID)
        {
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
                            intelItems.Add(key, IGCUnpack(unpacked.Item2.Item3));

                        return key;
                    }
                }
            }
            return null;
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

        public IntelItemType Type => IntelItemType.Enemy;

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
                (int)esi.Type,
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

        public static MyTuple<IntelItemType, long>? TryIGCUnpack(object data, Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence> intelItems, long masterID)
        {
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
                            intelItems.Add(key, IGCUnpack(unpacked.Item2.Item3));

                        return key;
                    }
                }
            }
            return null;
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
}
