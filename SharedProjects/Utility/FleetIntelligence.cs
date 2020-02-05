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

    public static class FleetIntelligenceUtil
    {
        public const string IntelReportChannelTag = "[FLTINT-RPT]";
        public const string IntelUpdateChannelTag = "[FLTINT-UPD]";
        public const string IntelSyncChannelTag = "[FLTINT-SYN]";

        public const int kMaxWaypoints = 64;

        public static void PackAndBroadcastFleetIntelligence(IMyIntergridCommunicationSystem IGC, IFleetIntelligence item)
        {
            if (item is Waypoint)
                IGC.SendBroadcastMessage(IntelReportChannelTag, Waypoint.IGCPackGeneric((Waypoint)item));
        }

        /// <summary>
        /// Receives an IGC packed IFleetIntelligence item and puts it into the dictionary provided, updating the existing entry if necessary. Returns the key of the object updated, if available.
        /// </summary>
        public static MyTuple<IntelItemType, long> ReceiveAndUpdateFleetIntelligence(object data, Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence> intelItems)
        {
            if (data is MyTuple<int, long, MyTuple<Vector3, Vector3, float, string, int>>)
            {
                // Waypoint
                var unpacked = (MyTuple<int, long, MyTuple<Vector3, Vector3, float, string, int>>)data;
                var key = MyTuple.Create((IntelItemType)unpacked.Item1, unpacked.Item2);
                if (key.Item1 == IntelItemType.Waypoint)
                {
                    if (intelItems.ContainsKey(key))
                        ((Waypoint)intelItems[key]).IGCUnpackInto(unpacked.Item3);
                    else
                        intelItems.Add(key, Waypoint.IGCUnpack(unpacked.Item3));

                    return key;
                }
            }

            return MyTuple.Create<IntelItemType, long>(IntelItemType.NONE, 0);
        }

        public static void PackAndBroadcastFleetIntelligenceSyncPackage(IMyIntergridCommunicationSystem IGC, Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence> intelItems)
        {
            var WaypointArrayBuilder = ImmutableArray.CreateBuilder<MyTuple<int, long, MyTuple<Vector3, Vector3, float, string, int>>>(kMaxWaypoints); 

            foreach (KeyValuePair<MyTuple<IntelItemType, long>, IFleetIntelligence> kvp in intelItems)
            {
                if (kvp.Key.Item1 == IntelItemType.Waypoint && WaypointArrayBuilder.Count < kMaxWaypoints)
                    WaypointArrayBuilder.Add(Waypoint.IGCPackGeneric((Waypoint)kvp.Value));
            }

            IGC.SendBroadcastMessage(IntelSyncChannelTag, WaypointArrayBuilder.ToImmutable());
        }

        /// <summary>
        /// Receives an array of all of one type of fleet intelligence and puts them into the dictionary provided, updating existing entry if necessary. Adds the keys of each item updated this way to the provided scratchpad.
        /// </summary>
        public static void ReceiveAndUpdateFleetIntelligenceSyncPackage(object data, Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence> intelItems, ref List<MyTuple<IntelItemType, long>> updatedScratchpad)
        {
            // Waypoint
            if (data is ImmutableArray<MyTuple<int, long, MyTuple<Vector3, Vector3, float, string, int>>>)
            {
                foreach (var item in (ImmutableArray<MyTuple<int, long, MyTuple<Vector3, Vector3, float, string, int>>>)data)
                {
                    var updatedKey = ReceiveAndUpdateFleetIntelligence(item, intelItems);
                    updatedScratchpad.Add(updatedKey);
                }
            }
        }
    }

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
}
