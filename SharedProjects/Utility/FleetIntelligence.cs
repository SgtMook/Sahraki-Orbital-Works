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

namespace SharedProjects.Utility
{
    public enum IntelItemType
    {
        Waypoint,
        Asteroid,
        EnemyLarge,
        EnemySmall,
        FriendlyLarge,
        FriendlySmall,
    }

    public static class FleetIntelligenceConfig
    {
        public static string IntelReportChannelTag = "[FLTINT-RPT]";
        public static string IntelUpdateChannelTag = "[FLTINT-UPD]";
        public static string IntelSyncChannelTag = "[FLTINT-SYN]";

        public static IFleetIntelligence IGCUnpackGeneric(object data)
        {
            if (data is MyTuple<int, MyTuple<Vector3, Vector3, float, string, int>>)
            {
                // Waypoint
                var unpacked = (MyTuple<int, MyTuple<Vector3, Vector3, float, string, int>>)data;
                if ((IntelItemType)unpacked.Item1 == IntelItemType.Waypoint)
                    return Waypoint.IGCUnpack(unpacked.Item2);
            }

            return null;
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
