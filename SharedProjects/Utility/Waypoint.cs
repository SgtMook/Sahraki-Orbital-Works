using System;
using System.Collections.Generic;
using System.Text;
using VRageMath;

namespace SharedProjects.Utility
{
    #region Waypoint
    public class Waypoint
    {
        public Vector3 Position;
        public Vector3 Direction;
        public float MaxSpeed;
        public string Name;
        public string ReferenceMode;
        public static string SerializeWaypoint(Waypoint w)
        {
            return $"{w.Position.ToString()}|{w.Direction.ToString()}|{w.MaxSpeed.ToString()}|{w.Name}|{w.ReferenceMode}";
        }

        public static Waypoint DeserializeWaypoint(string s)
        {
            string[] split = s.Split('|');
            Waypoint w = new Waypoint();
            w.Position = VectorUtilities.StringToVector3(split[0]);
            w.Direction = VectorUtilities.StringToVector3(split[1]);
            w.MaxSpeed = float.Parse(split[2]);
            w.Name = split[3];
            w.ReferenceMode = split[4];
            return w;
        }
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
