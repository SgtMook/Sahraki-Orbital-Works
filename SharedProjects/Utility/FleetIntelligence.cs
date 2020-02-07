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

namespace SharedProjects.Utility
{
    #region Enums
    public enum IntelItemType
    {
        NONE,
        Waypoint,
        Asteroid,
        EnemyLarge,
        EnemySmall,
        FriendlyLarge,
        FriendlySmall,
    }
    #endregion

    #region Utils
    public static class FleetIntelligenceUtil
    {
        public const string IntelReportChannelTag = "[FLTINT-RPT]";
        public const string IntelUpdateChannelTag = "[FLTINT-UPD]";
        public const string IntelSyncChannelTag = "[FLTINT-SYN]";
        public const string TimeChannelTag = "[FLTTM]";

        public const int kMaxWaypoints = 64;

        // Canonical fleet intel packing format is (MasterID, (IntelItemType, IntelItemID, (data)))

        public static void PackAndBroadcastFleetIntelligence(IMyIntergridCommunicationSystem IGC, IFleetIntelligence item, long masterID)
        {
            if (item is Waypoint)
                IGC.SendBroadcastMessage(IntelReportChannelTag, MyTuple.Create(masterID, Waypoint.IGCPackGeneric((Waypoint)item)));
        }

        /// <summary>
        /// Receives an IGC packed IFleetIntelligence item and if it matches master ID puts it into the dictionary provided, updating the existing entry if necessary. Returns the key of the object updated, if available.
        /// </summary>
        public static MyTuple<IntelItemType, long> ReceiveAndUpdateFleetIntelligence(object data, Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence> intelItems, long masterID)
        {
            if (data is MyTuple<long, MyTuple<int, long, MyTuple<Vector3, Vector3, float, string, int>>>)
            {
                // Waypoint
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

            return MyTuple.Create<IntelItemType, long>(IntelItemType.NONE, 0);
        }

        public static void PackAndBroadcastFleetIntelligenceSyncPackage(IMyIntergridCommunicationSystem IGC, Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence> intelItems, long masterID)
        {
            var WaypointArrayBuilder = ImmutableArray.CreateBuilder<MyTuple<long, MyTuple<int, long, MyTuple<Vector3, Vector3, float, string, int>>>>(kMaxWaypoints); 

            foreach (KeyValuePair<MyTuple<IntelItemType, long>, IFleetIntelligence> kvp in intelItems)
            {
                if (kvp.Key.Item1 == IntelItemType.Waypoint && WaypointArrayBuilder.Count < kMaxWaypoints)
                    WaypointArrayBuilder.Add(MyTuple.Create(masterID, Waypoint.IGCPackGeneric((Waypoint)kvp.Value)));
            }

            IGC.SendBroadcastMessage(IntelSyncChannelTag, WaypointArrayBuilder.ToImmutable());
        }

        /// <summary>
        /// Receives an array of all of one type of fleet intelligence and puts them into the dictionary provided, updating existing entry if necessary. Adds the keys of each item updated this way to the provided scratchpad.
        /// </summary>
        public static void ReceiveAndUpdateFleetIntelligenceSyncPackage(object data, Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence> intelItems, ref List<MyTuple<IntelItemType, long>> updatedScratchpad, long masterID)
        {
            // Waypoint
            if (data is ImmutableArray<MyTuple<int, long, MyTuple<Vector3, Vector3, float, string, int>>>)
            {
                foreach (var item in (ImmutableArray<MyTuple<int, long, MyTuple<Vector3, Vector3, float, string, int>>>)data)
                {
                    var updatedKey = ReceiveAndUpdateFleetIntelligence(item, intelItems, masterID);
                    updatedScratchpad.Add(updatedKey);
                }
            }
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
        Vector3 GetPosition(TimeSpan time);
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
        public Vector3 GetPosition(TimeSpan time)
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
    public class FriendlyLarge : IFleetIntelligence
    {
        #region IFleetIntelligence
        public float Radius { get; set; }

        public long ID { get; set; }

        public string DisplayName { get; set; }

        public IntelItemType IntelItemType => IntelItemType.FriendlyLarge;

        public void Deserialize(string s)
        {
            // TODO: Implement
        }
        public string Serialize()
        {
            return string.Empty;
        }

        public Vector3 GetPosition(TimeSpan time)
        {
            return Vector3.Zero;
        }

        public Vector3 GetVelocity()
        {
            return Vector3.Zero;
        }
        #endregion

    }
    #endregion

    #endregion
}
