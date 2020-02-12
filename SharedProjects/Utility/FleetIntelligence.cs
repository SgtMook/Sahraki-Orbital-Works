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

    #region Enums
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
    #endregion

    #region Utils
    public static class FleetIntelligenceUtil
    {
        public const string IntelReportChannelTag = "[FLTINT-RPT]";
        public const string IntelUpdateChannelTag = "[FLTINT-UPD]";
        public const string IntelSyncChannelTag = "[FLTINT-SYN]";
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
            else if (item is AsteroidIntel)
                IGC.SendBroadcastMessage(IntelReportChannelTag, MyTuple.Create(masterID, AsteroidIntel.IGCPackGeneric((AsteroidIntel)item)));
            else
                throw new Exception("Bad type when sending fleet intel");
        }

        /// <summary>
        /// Receives an IGC packed IFleetIntelligence item and if it matches master ID puts it into the dictionary provided, updating the existing entry if necessary. Returns the key of the object updated, if available.
        /// </summary>
        public static MyTuple<IntelItemType, long> ReceiveAndUpdateFleetIntelligence(object data, Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence> intelItems, long masterID)
        {
             // Waypoint
            if (data is MyTuple<long, MyTuple<int, long, MyTuple<Vector3, Vector3, float, string, int>>>)
            {
                var unpacked = (MyTuple<long, MyTuple<int, long, MyTuple<Vector3, Vector3, float, string, int>>>)data;
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

            // Friendly
            if (data is MyTuple<long, MyTuple<int, long, MyTuple<MyTuple<Vector3, Vector3, double>, MyTuple<string, long, float>, MyTuple<int, string, int>, MyTuple<long>>>>)
            {
                var unpacked = (MyTuple<long, MyTuple<int, long, MyTuple<MyTuple<Vector3, Vector3, double>, MyTuple<string, long, float>, MyTuple<int, string, int>, MyTuple<long>>>>)data;
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

            // Asteroid
            if (data is MyTuple<long, MyTuple<int, long, MyTuple<Vector3, float, long>>>)
            {
                var unpacked = (MyTuple<long, MyTuple<int, long, MyTuple<Vector3, float, long>>>)data;
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

            throw new Exception($"Bad type when receiving fleet intel: {data.GetType().ToString()}");
        }

        public static void PackAndBroadcastFleetIntelligenceSyncPackage(IMyIntergridCommunicationSystem IGC, Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence> intelItems, long masterID)
        {
            var WaypointArrayBuilder = ImmutableArray.CreateBuilder<MyTuple<long, MyTuple<int, long, MyTuple<Vector3, Vector3, float, string, int>>>>(kMaxIntelPerType); 
            var FriendlyShipIntelArrayBuilder = ImmutableArray.CreateBuilder<MyTuple<long, MyTuple<int, long, MyTuple<MyTuple<Vector3, Vector3, double>, MyTuple<string, long, float>, MyTuple<int, string, int>, MyTuple<long>>>>>(kMaxIntelPerType); 
            var AsteroidIntelArrayBuilder = ImmutableArray.CreateBuilder<MyTuple<long, MyTuple<int, long, MyTuple<Vector3, float, long>>>>(kMaxIntelPerType); 

            foreach (KeyValuePair<MyTuple<IntelItemType, long>, IFleetIntelligence> kvp in intelItems)
            {
                if (kvp.Key.Item1 == IntelItemType.Waypoint)
                    WaypointArrayBuilder.Add(MyTuple.Create(masterID, Waypoint.IGCPackGeneric((Waypoint)kvp.Value)));
                else if (kvp.Key.Item1 == IntelItemType.Friendly)
                    FriendlyShipIntelArrayBuilder.Add(MyTuple.Create(masterID, FriendlyShipIntel.IGCPackGeneric((FriendlyShipIntel)kvp.Value)));
                else if (kvp.Key.Item1 == IntelItemType.Asteroid)
                    AsteroidIntelArrayBuilder.Add(MyTuple.Create(masterID, AsteroidIntel.IGCPackGeneric((AsteroidIntel)kvp.Value)));
                else
                    throw new Exception("Bad type when sending sync package");
            }

            IGC.SendBroadcastMessage(IntelSyncChannelTag, WaypointArrayBuilder.ToImmutable());
            IGC.SendBroadcastMessage(IntelSyncChannelTag, FriendlyShipIntelArrayBuilder.ToImmutable());
            IGC.SendBroadcastMessage(IntelSyncChannelTag, AsteroidIntelArrayBuilder.ToImmutable());
        }

        /// <summary>
        /// Receives an array of all of one type of fleet intelligence and puts them into the dictionary provided, updating existing entry if necessary. Adds the keys of each item updated this way to the provided scratchpad.
        /// </summary>
        public static void ReceiveAndUpdateFleetIntelligenceSyncPackage(object data, Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence> intelItems, ref List<MyTuple<IntelItemType, long>> updatedScratchpad, long masterID)
        {
            // Waypoint
            if (data is ImmutableArray<MyTuple<long, MyTuple<int, long, MyTuple<Vector3, Vector3, float, string, int>>>>)
            {
                foreach (var item in (ImmutableArray<MyTuple<long, MyTuple<int, long, MyTuple<Vector3, Vector3, float, string, int>>>>)data)
                {
                    var updatedKey = ReceiveAndUpdateFleetIntelligence(item, intelItems, masterID);
                    updatedScratchpad.Add(updatedKey);
                }
            }
            // FriendlyShipIntel
            else if (data is ImmutableArray<MyTuple<long, MyTuple<int, long, MyTuple<MyTuple<Vector3, Vector3, double>, MyTuple<string, long, float>, MyTuple<int, string, int>, MyTuple<long>>>>>)
            {
                foreach (var item in (ImmutableArray<MyTuple<long, MyTuple<int, long, MyTuple<MyTuple<Vector3, Vector3, double>, MyTuple<string, long, float>, MyTuple<int, string, int>, MyTuple<long>>>>>)data)
                {
                    var updatedKey = ReceiveAndUpdateFleetIntelligence(item, intelItems, masterID);
                    updatedScratchpad.Add(updatedKey);
                }
            }
            // AsteroidIntel
            else if (data is ImmutableArray<MyTuple<long, MyTuple<int, long, MyTuple<Vector3, float, long>>>>)
            {
                foreach (var item in (ImmutableArray<MyTuple<long, MyTuple<int, long, MyTuple<Vector3, float, long>>>>)data)
                {
                    var updatedKey = ReceiveAndUpdateFleetIntelligence(item, intelItems, masterID);
                    updatedScratchpad.Add(updatedKey);
                }
            }
            else
            {
                throw new Exception("Bad type when receiving sync package");
            }
        }

        public static MyTuple<IntelItemType, long> GetIntelItemKey(IFleetIntelligence item)
        {
            return MyTuple.Create(item.IntelItemType, item.ID);
        }
    }

    public class VectorUtilities
    {
        public static Vector3 StringToVector3(string sVector)
        {
            sVector = sVector.Substring(1, sVector.Length - 2);
            string[] sArray = sVector.Split(' ');
            Vector3 result = new Vector3(
                float.Parse(sArray[0].Substring(2)),
                float.Parse(sArray[1].Substring(2)),
                float.Parse(sArray[2].Substring(2)));
            return result;
        }
    }
    #endregion


    #region Interface
    // Representations of objects in the fleet intelligence system
    public interface IFleetIntelligence
    {
        Vector3 GetPositionFromCanonicalTime(TimeSpan CanonicalTime);
        Vector3 GetVelocity();

        float Radius { get; }

        long ID { get; }
        string DisplayName { get; }
        IntelItemType IntelItemType { get; }

        string Serialize();
        void Deserialize(string s);
    }
    #endregion

    #region Concrete items

    #region Waypoint
    public enum WaypointReferenceMode
    {
        Default,
        Dock,
        Winch
    }

    public class Waypoint : IFleetIntelligence
    {
        public Vector3 Position; // Position of Zero means to stop moving, One means to keep original
        public Vector3 Direction; // Direction of Zero means to stop turning, One means to keep original
        public float MaxSpeed;
        public string Name;
        public WaypointReferenceMode ReferenceMode;

        public static string SerializeWaypoint(Waypoint w)
        {
            return $"{w.Position.ToString()}|{w.Direction.ToString()}|{w.MaxSpeed.ToString()}|{w.Name}|{(int)w.ReferenceMode}";
        }

        public static Waypoint DeserializeWaypoint(string s)
        {
            Waypoint w = new Waypoint();
            w.Deserialize(s);
            return w;
        }


        #region IFleetIntelligence

        public Waypoint()
        {
            Position = Vector3.One;
            Direction = Vector3.One;
            MaxSpeed = -1;
            Name = "Waypoint";
            ReferenceMode = WaypointReferenceMode.Default;
        }

        public float Radius => 50f;
        public string DisplayName => Name;
        public long ID => Position.ToString().GetHashCode();
        public IntelItemType IntelItemType => IntelItemType.Waypoint;
        public Vector3 GetPositionFromCanonicalTime(TimeSpan CanonicalTime)
        {
            return Position;
        }

        public Vector3 GetVelocity()
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
            MaxSpeed = float.Parse(split[2]);
            Name = split[3];
            ReferenceMode = (WaypointReferenceMode)int.Parse(split[4]);
        }

        static public MyTuple<int, long, MyTuple<Vector3, Vector3, float, string, int>> IGCPackGeneric(Waypoint w)
        {
            return MyTuple.Create
            (
                (int)IntelItemType.Waypoint,
                w.ID,
                MyTuple.Create
                (
                    w.Position,
                    w.Direction,
                    w.MaxSpeed,
                    w.Name,
                    (int)w.ReferenceMode
                )
            );
        }
        static public Waypoint IGCUnpack(object data)
        {
            var unpacked = (MyTuple<Vector3, Vector3, float, string, int>)data;
            var w = new Waypoint();
            w.IGCUnpackInto(unpacked);
            return w;
        }

        public void IGCUnpackInto(MyTuple<Vector3, Vector3, float, string, int> unpacked)
        {
            Position = unpacked.Item1;
            Direction = unpacked.Item2;
            MaxSpeed = unpacked.Item3;
            Name = unpacked.Item4;
            ReferenceMode = (WaypointReferenceMode)unpacked.Item5;
        }

        #endregion
    }
    #endregion

    #region Friendly
    public class FriendlyShipIntel : IFleetIntelligence
    {
        #region IFleetIntelligence
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

        public Vector3 GetPositionFromCanonicalTime(TimeSpan CanonicalTime)
        {
            return CurrentPosition + CurrentVelocity * (float)(CanonicalTime - CurrentTime).TotalSeconds;
        }

        public Vector3 GetVelocity()
        {
            return CurrentVelocity;
        }
        #endregion

        public Vector3 CurrentVelocity;
        public Vector3 CurrentPosition;
        public TimeSpan CurrentTime;

        public TaskType AcceptedTaskTypes;
        public string CommandChannelTag;
        public AgentClass AgentClass;

        public long HomeID = 0;

        #region IGC Packing
        static public MyTuple<int, long, MyTuple<MyTuple<Vector3, Vector3, double>, MyTuple<string, long, float>, MyTuple<int, string, int>, MyTuple<long>>> IGCPackGeneric(FriendlyShipIntel fsi)
        {
            return MyTuple.Create
            (
                (int)IntelItemType.Friendly,
                fsi.ID,
                MyTuple.Create
                (
                    MyTuple.Create
                    (
                        fsi.CurrentPosition,
                        fsi.CurrentVelocity,
                        fsi.CurrentTime.TotalMilliseconds
                    ),
                     MyTuple.Create
                    (
                        fsi.DisplayName,
                        fsi.ID,
                        fsi.Radius
                    ),
                     MyTuple.Create
                    (
                        (int)fsi.AcceptedTaskTypes,
                        fsi.CommandChannelTag,
                        (int)fsi.AgentClass
                    ),
                     MyTuple.Create
                    (
                         fsi.HomeID
                    )
                )
            );
        }
        static public FriendlyShipIntel IGCUnpack(object data)
        {
            var unpacked = (MyTuple<MyTuple<Vector3, Vector3, double>, MyTuple<string, long, float>, MyTuple<int, string, int>, MyTuple<long>>)data;
            var fsi = new FriendlyShipIntel();
            fsi.IGCUnpackInto(unpacked);
            return fsi;
        }

        public void IGCUnpackInto(MyTuple<MyTuple<Vector3, Vector3, double>, MyTuple<string, long, float>, MyTuple<int, string, int>, MyTuple<long>> unpacked)
        {
            CurrentPosition = unpacked.Item1.Item1;
            CurrentVelocity = unpacked.Item1.Item2;
            CurrentTime = TimeSpan.FromMilliseconds(unpacked.Item1.Item3);
            DisplayName = unpacked.Item2.Item1;
            ID = unpacked.Item2.Item2;
            Radius = unpacked.Item2.Item3;
            AcceptedTaskTypes = (TaskType)unpacked.Item3.Item1;
            CommandChannelTag = unpacked.Item3.Item2;
            AgentClass = (AgentClass)unpacked.Item3.Item3;
            HomeID = unpacked.Item4.Item1;
        }
        #endregion

    }
    #endregion

    #region Asteroid
    public class AsteroidIntel : IFleetIntelligence
    {
        public Vector3 Position;

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


        #region IFleetIntelligence

        public float Radius { get; set; }
        public string DisplayName => "Asteroid";
        public long ID { get; set; }
        public IntelItemType IntelItemType => IntelItemType.Asteroid;
        public Vector3 GetPositionFromCanonicalTime(TimeSpan CanonicalTime)
        {
            return Position;
        }

        public Vector3 GetVelocity()
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

        static public MyTuple<int, long, MyTuple<Vector3, float, long>> IGCPackGeneric(AsteroidIntel astr)
        {
            return MyTuple.Create
            (
                (int)IntelItemType.Asteroid,
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
            var unpacked = (MyTuple<Vector3, float, long>)data;
            var astr = new AsteroidIntel();
            astr.IGCUnpackInto(unpacked);
            return astr;
        }

        public void IGCUnpackInto(MyTuple<Vector3, float, long> unpacked)
        {
            Position = unpacked.Item1;
            Radius = unpacked.Item2;
            ID = unpacked.Item3;
        }

        #endregion
    }
    #endregion

    #region Dock

    public class DockIntel : IFleetIntelligence
    {
        #region IFleetIntelligence
        public float Radius => 0;

        public long ID { get; set; }

        public string DisplayName { get; set; }

        public IntelItemType IntelItemType => IntelItemType.Dock;

        public void Deserialize(string s)
        {
        }

        public Vector3 GetPositionFromCanonicalTime(TimeSpan CanonicalTime)
        {
            return WorldMatrix.Translation + CurrentVelocity * (float)(CanonicalTime - CurrentTime).TotalSeconds;
        }

        public Vector3 GetVelocity()
        {
            return CurrentVelocity;
        }

        public string Serialize()
        {
            return string.Empty;
        }
        #endregion

        public MatrixD WorldMatrix;
        public float UndockFar = 20;
        public float UndockNear = 1;

        public long OwnerID;

        public Vector3 CurrentVelocity;

        TimeSpan CurrentTime;

    }

    #endregion

    #endregion
}
