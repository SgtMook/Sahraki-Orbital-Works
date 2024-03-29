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

namespace IngameScript
{
    public static class TrigHelpers
    {
        public static double FastAsin(double x)
        {
            return x * (1 + x * x * (0.1666666 + 0.075 * x * x));
        }

        public static double FastCos(double x)
        {
            if (x > Math.PI || x < -Math.PI) return Math.Cos(x);
            return 1 + x * x * (-0.5 + x * x * (0.04166666 + x * x * (-0.0013888888 + x * x * 0.0000248015)));
        }

        public static double FastSin(double x)
        {
            if (x > Math.PI || x < -Math.PI) return Math.Sin(x);
            return x * (1 + x * x * (-0.1666666 + x * x * (0.00833333 + x * x * (-0.00019841269841 + x * x * 0.00000275573))));
        }

        public static double fastTan(double x)
        {
            if (x > Math.PI || x < -Math.PI) return Math.Tan(x);
            return FastSin(x) / FastCos(x);
        }

        public static void GetRotationAngles(Vector3D v_target, Vector3D v_front, Vector3D v_left, Vector3D v_up, out double yaw, out double pitch)
        {
            //Dependencies: VectorProjection() | VectorAngleBetween()
            var projectTargetUp = VectorHelpers.VectorProjection(v_target, v_up);
            var projTargetFrontLeft = v_target - projectTargetUp;

            yaw = VectorHelpers.VectorAngleBetween(v_front, projTargetFrontLeft);

            if (Vector3D.IsZero(projTargetFrontLeft) && !Vector3D.IsZero(projectTargetUp)) //check for straight up case
                pitch = MathHelper.PiOver2;
            else
                pitch = VectorHelpers.VectorAngleBetween(v_target, projTargetFrontLeft); //pitch should not exceed 90 degrees by nature of this definition

            //---Check if yaw angle is left or right  
            //multiplied by -1 to convert from right hand rule to left hand rule
            yaw = -1 * Math.Sign(v_left.Dot(v_target)) * yaw;

            //---Check if pitch angle is up or down    
            pitch = Math.Sign(v_up.Dot(v_target)) * pitch;

            //---Check if target vector is pointing opposite the front vector
            if (Math.Abs(yaw) <= 1E-6 && v_target.Dot(v_front) < 0)
            {
                yaw = Math.PI;
            }
        }

        public static void ApplyGyroOverride(double pitch_speed, double yaw_speed, double roll_speed, List<IMyGyro> gyro_list, MatrixD reference)
        {
            if (reference == null) return;
            var rotationVec = new Vector3D(-pitch_speed, yaw_speed, roll_speed); //because keen does some weird stuff with signs
            var shipMatrix = reference;
            var relativeRotationVec = Vector3D.TransformNormal(rotationVec, shipMatrix);

            foreach (var thisGyro in gyro_list)
            {
                var gyroMatrix = thisGyro.WorldMatrix;
                var transformedRotationVec = Vector3D.TransformNormal(relativeRotationVec, Matrix.Transpose(gyroMatrix));

                thisGyro.Pitch = (float)transformedRotationVec.X;
                thisGyro.Yaw = (float)transformedRotationVec.Y;
                thisGyro.Roll = (float)transformedRotationVec.Z;
                thisGyro.GyroOverride = true;
            }
        }

        public static float DistanceToPlane(Plane plane, Vector3 point)
        {
            return plane.Normal.Dot(point) + plane.D;
        }
        public static double DistanceToPlaneD(PlaneD plane, Vector3D point)
        {
            return plane.Normal.Dot(point) + plane.D;
        }

        public static Vector3 GetRandomPerpendicularNormalToDirection(Random random, Vector3 direction)
        {
            direction.Normalize();
            var perp = Vector3D.CalculatePerpendicularVector(direction);
            perp.Normalize();
            var coperp = perp.Cross(direction);

            var theta = random.NextDouble() * Math.PI * 2;

            return perp * Math.Sin(theta) + coperp * Math.Cos(theta);

            // Original Trickshot code
//             var perp = new Vector3D(1, 1, -(rangeVector.X + rangeVector.Y) / rangeVector.Z);
//             var coperp = perp.Cross(rangeVector);
//             perp.Normalize();
//             coperp.Normalize();
// 
//             var rand = new Random();
//             var theta = rand.NextDouble() * Math.PI * 2;
// 
//             //         var dist = -rangeVector;
//             //         dist.Normalize();
//             //         dist *= 1200;
// 
//             TrickshotOffset = perp * Math.Sin(theta) + coperp * Math.Cos(theta);
 
        }

        public static bool PlaneIntersection(Plane plane, Vector3 rayPos, Vector3 rayNorm, out Vector3 result)
        {
            result = Vector3.Zero;
            var a = plane.Normal.Dot(rayNorm);
            if (a == 0)
                return false; // parallel

            result = rayPos - rayNorm * (DistanceToPlane(plane, rayPos) / a);
            return true;
        }

        public static bool PlaneIntersectionD(PlaneD plane, Vector3D rayPos, Vector3D rayNorm, out Vector3D result)
        {
            result = Vector3D.Zero;
            var a = plane.Normal.Dot(rayNorm);
            if (a == 0)
                return false; // parallel

            result = rayPos - rayNorm * (DistanceToPlaneD(plane, rayPos) / a);
            return true;
        }
    }
}
