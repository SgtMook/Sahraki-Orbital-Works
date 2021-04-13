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
    public interface IIGCIntelBinding
    {
        void JIT();
        bool PackAndBroadcastFleetIntelligence(IMyIntergridCommunicationSystem IGC, IFleetIntelligence item, long masterID);
        void PackAndBroadcastFleetIntelligenceSyncPackage(IMyIntergridCommunicationSystem IGC, Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence> intelItems, long masterID);
        MyTuple<IntelItemType, long>? ReceiveAndUpdateFleetIntelligence(object data, Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence> intelItems, long masterID);
        void ReceiveAndUpdateFleetIntelligenceSyncPackage(object data, Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence> intelItems, ref List<MyTuple<IntelItemType, long>> updatedScratchpad, long masterID);
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

        // I is the IFleetIntelligence Intel
        // T is the MyTuple Data including Type & ID eg.      ++++++++++++++++++++++++++++++++++++  
        // Canonical fleet intel packing format is (MasterID, (IntelItemType, IntelItemID, (data)))
        // TODO: Ask Mook if there's a reason for the inteltype & itemid not being part of the 'header'
        public class IGCIntelBinding<INTEL, DATA> : IIGCIntelBinding
            where INTEL : IFleetIntelligence, new()
        {
            public ImmutableArray<MyTuple<long, DATA>>.Builder
            Builder = ImmutableArray.CreateBuilder<MyTuple<long, DATA>>(64);

            public INTEL Proxy = new INTEL();

            public void JIT()
            {
                Proxy.TryIGCUnpack(Proxy.IGCPackGeneric(), null);
            }

            public bool PackAndBroadcastFleetIntelligence(IMyIntergridCommunicationSystem IGC, IFleetIntelligence item, long masterID)
            {
                if (item is INTEL)
                {
                    IGC.SendBroadcastMessage(IntelReportChannelTag, MyTuple.Create(masterID, (DATA)item.IGCPackGeneric()));
                    return true;
                }
                return false;
            }
            public void PackAndBroadcastFleetIntelligenceSyncPackage(IMyIntergridCommunicationSystem IGC, Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence> intelItems, long masterID)
            {
                Builder.Clear();
                foreach (var kvp in intelItems)
                {
                    if (Proxy.Type == kvp.Key.Item1)
                        Builder.Add(MyTuple.Create(masterID, (DATA)kvp.Value.IGCPackGeneric()));
                }
                IGC.SendBroadcastMessage(IntelSyncChannelTag, Builder.ToImmutable());
            }
            // returns true if handled, false otherwise
            public MyTuple<IntelItemType, long>? ReceiveAndUpdateFleetIntelligence(object data, Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence> intelItems, long masterID)
            {
                if ( data is MyTuple<long, DATA> )
                {
                    MyTuple<long, DATA> unpacked = (MyTuple<long, DATA>)data;
                    if (unpacked.Item1 == masterID)
                        return null; 

                    return Proxy.TryIGCUnpack(data, intelItems);
                }
                return null;
            }

            public void ReceiveAndUpdateFleetIntelligenceSyncPackage(object data, Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence> intelItems, ref List<MyTuple<IntelItemType, long>> updatedScratchpad, long masterID)
            {
                // Waypoint
                if ( data is ImmutableArray<MyTuple<long, DATA>>)
                {
                    var array = (ImmutableArray<MyTuple<long, DATA>>)data;
                    foreach (var item in array)
                    {
                        if (item.Item1 == masterID)
                            continue;

                        // MINIFICATION DANGER HERE:
                        // Does not like the data.item2 term that gets fed into TryIGCUnpack
                        object minifyWorkAround = item.Item2;
                        var updatedIntel = Proxy.TryIGCUnpack(minifyWorkAround, intelItems);
                        if (updatedIntel != null)
                            updatedScratchpad.Add(updatedIntel.Value);
                    }
                }
            }
        }


        public static int CompareName(IFleetIntelligence a, IFleetIntelligence b)
        {
            return a.DisplayName.CompareTo(b.DisplayName);
        }

        public static MyTuple<IntelItemType, long> GetIntelItemKey(IFleetIntelligence item)
        {
            return MyTuple.Create(item.Type, item.ID);
        }
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
        object IGCPackGeneric();
        MyTuple<IntelItemType, long>? TryIGCUnpack(object data, Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence> intelItems);
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

        public object IGCPackGeneric()
        {
            return MyTuple.Create
            (
                (int)Type,
                ID,
                MyTuple.Create
                (
                    Position,
                    Direction,
                    DirectionUp,
                    MaxSpeed,
                    Name
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

        public MyTuple<IntelItemType, long>? TryIGCUnpack(object data, Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence> intelItems)
        {
            var unpacked = (MyTuple<int, long, MyTuple<Vector3D, Vector3D, Vector3D, float, string>>)data;
            var key = MyTuple.Create((IntelItemType)unpacked.Item1, unpacked.Item2);
            if (intelItems != null && key.Item1 == IntelItemType.Waypoint)
            {
                if (intelItems.ContainsKey(key))
                    ((Waypoint)intelItems[key]).IGCUnpackInto(unpacked.Item3);
                else
                    intelItems.Add(key, Waypoint.IGCUnpack(unpacked.Item3));

                return key;
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

        public object IGCPackGeneric()
        {
            return MyTuple.Create(
                (int)Type,
                ID,
                MyTuple.Create
                (
                    MyTuple.Create
                    (
                        CurrentPosition,
                        CurrentVelocity,
                        CurrentCanonicalTime.TotalMilliseconds
                    ),
                     MyTuple.Create
                    (
                        DisplayName,
                        ID,
                        Radius,
                        Rank
                    ),
                     MyTuple.Create
                    (
                        (int)AcceptedTaskTypes,
                        CommandChannelTag,
                        (int)AgentClass,
                        (int)AgentStatus,
                        HydroPowerInv
                    ),
                     MyTuple.Create
                    (
                         HomeID,
                         (int)HangarTags
                    )
                )
            );
        }

        // This is designed to be static, but due to language features needs to be called from a proxy object.
        public MyTuple<IntelItemType, long>? TryIGCUnpack(object data, Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence> intelItems)
        {
            var unpacked = (MyTuple<int, long, MyTuple<MyTuple<Vector3D, Vector3D, double>, MyTuple<string, long, float, int>, MyTuple<int, string, int, int, Vector3I>, MyTuple<long, int>>>)data;
            var key = MyTuple.Create((IntelItemType)unpacked.Item1, unpacked.Item2);
            if (intelItems != null && key.Item1 == IntelItemType.Friendly)
            {
                if (intelItems.ContainsKey(key))
                    ((FriendlyShipIntel)intelItems[key]).IGCUnpackInto(unpacked.Item3);
                else
                    intelItems.Add(key, FriendlyShipIntel.IGCUnpack(unpacked.Item3));

                return key;
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

        public object IGCPackGeneric()
        {
            return MyTuple.Create
            (
                (int)Type,
                ID,
                MyTuple.Create
                (
                    Position,
                    Radius,
                    ID
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
        public MyTuple<IntelItemType, long>? TryIGCUnpack(object data, Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence> intelItems)
        {
            var unpacked = (MyTuple<int, long, MyTuple<Vector3D, float, long>>)data;
            var key = MyTuple.Create((IntelItemType)unpacked.Item1, unpacked.Item2);
            if (intelItems != null && key.Item1 == IntelItemType.Asteroid)
            {
                if (intelItems.ContainsKey(key))
                    ((AsteroidIntel)intelItems[key]).IGCUnpackInto(unpacked.Item3);
                else
                    intelItems.Add(key, AsteroidIntel.IGCUnpack(unpacked.Item3));

                return key;
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

        public object IGCPackGeneric()
        {
            return MyTuple.Create
            (
                (int)Type,
                ID,
                MyTuple.Create
                (
                    MyTuple.Create
                    (
                        WorldMatrix,
                        UndockFar,
                        UndockNear,
                        CurrentVelocity,
                        CurrentCanonicalTime.TotalMilliseconds,
                        IndicatorDir
                    ),
                     MyTuple.Create
                    (
                         OwnerID,
                         (int)Status,
                         (int)Tags,
                         HangarChannelTag
                    ),
                     MyTuple.Create
                    (
                        ID,
                        DisplayName
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
        public MyTuple<IntelItemType, long>? TryIGCUnpack(object data, Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence> intelItems)
        {
            var unpacked = (MyTuple<int, long, MyTuple<MyTuple<MatrixD, float, float, Vector3D, double, Vector3D>, MyTuple<long, int, int, string>, MyTuple<long, string>>>)data;
            var key = MyTuple.Create((IntelItemType)unpacked.Item1, unpacked.Item2);
            if (intelItems != null && key.Item1 == IntelItemType.Dock)
            {
                if (intelItems.ContainsKey(key))
                    ((DockIntel)intelItems[key]).IGCUnpackInto(unpacked.Item3);
                else
                    intelItems.Add(key, IGCUnpack(unpacked.Item3));

                return key;
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

        public object IGCPackGeneric()
        {
            return MyTuple.Create
            (
                (int)Type,
                ID,
                MyTuple.Create
                (
                    MyTuple.Create
                    (
                        CurrentPosition,
                        CurrentVelocity,
                        CurrentCanonicalTime.TotalMilliseconds,
                        LastValidatedCanonicalTime.TotalMilliseconds
                    ),
                     MyTuple.Create
                    (
                        DisplayName,
                        ID,
                        Radius,
                        (int)CubeSize
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

        public MyTuple<IntelItemType, long>? TryIGCUnpack(object data, Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence> intelItems)
        {
            // Enemy
            var unpacked = (MyTuple<int, long, MyTuple<MyTuple<Vector3D, Vector3D, double, double>, MyTuple<string, long, float, int>>>)data;
            var key = MyTuple.Create((IntelItemType)unpacked.Item1, unpacked.Item2);
            if (intelItems != null && key.Item1 == IntelItemType.Enemy)
            {
                if (intelItems.ContainsKey(key))
                    ((EnemyShipIntel)intelItems[key]).IGCUnpackInto(unpacked.Item3);
                else
                    intelItems.Add(key, IGCUnpack(unpacked.Item3));

                return key;
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
