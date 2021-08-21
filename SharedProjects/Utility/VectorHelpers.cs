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

namespace IngameScript
{
    public static class VectorHelpers
    {
        public static Vector3D VectorProjection(Vector3D vector, Vector3D onto)
        {
            if (Vector3D.IsZero(onto))
                return Vector3D.Zero;

            return vector.Dot(onto) / onto.LengthSquared() * onto;
        }

        public static double VectorAngleBetween(Vector3D a, Vector3D b) //returns radians
        {
            if (Vector3D.IsZero(a) || Vector3D.IsZero(b))
                return 0;
            else
                return Math.Acos(MathHelper.Clamp(a.Dot(b) / Math.Sqrt(a.LengthSquared() * b.LengthSquared()), -1, 1));
        }
        public static double InvLerp(double reference_value, double reference_start, double reference_end)
        {
            return (MathHelper.Clamp(reference_value, reference_start, reference_end) - reference_start) / (reference_end - reference_start);
        }
        public static double Lerp(double lerp, double start, double end)
        {
            return (1 - lerp) * start + lerp * end;
        }

        public static double Respace(double reference_value, double reference_start, double reference_end, double respace_start, double respace_end)
        {
            return Lerp(InvLerp(reference_value, reference_start, reference_end), respace_start, respace_end);
        }
        

    }
}