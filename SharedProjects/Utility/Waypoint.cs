using System;
using System.Collections.Generic;
using System.Text;
using VRage;
using VRageMath;

namespace SharedProjects.Utility
{
    public enum WaypointReferenceMode
    {
        Default,
        Dock,
        Winch
    }

    #region Waypoint
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
}
