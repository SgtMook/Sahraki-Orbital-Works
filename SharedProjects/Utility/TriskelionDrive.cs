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
    // CONTROLLER BOTTOM LEFT CORNER FACES DOWN
    public class TriskelionDrive
    {
        List<HoverEngineDriver> HoverEngines = new List<HoverEngineDriver>();
        List<IMyGyro> Gyros = new List<IMyGyro>();
        Dictionary<Vector3D, float> DirForces = new Dictionary<Vector3D, float>();
        List<Vector3D> Dirs = new List<Vector3D>();
        float MaxForce;

        public IMyShipController Controller;

        const float MinThrustRatio = 0.8660254f; // sqrt(3)/2 at opposite one drive
        const float sqrt3inv = 0.57735026919f;
        const float VerticalForceRatio = 0.577287712086f;
        const float HorizontalForceRatio = 0.816540811886f;
        const float MaxPullMultiplier = 0.0f;

        public string Status { get; private set; }

        PID YawPID;
        PID PitchPID;
        PID SpinPID;

        float RC = 0.96f;

        public void AddEngine(HoverEngineDriver engine)
        {
            HoverEngines.Add(engine);
            engine.Block.Enabled = true;
            engine.Block.ThrustOverridePercentage = 0;
        }

        public void AddGyro(IMyGyro gyro)
        {
            Gyros.Add(gyro);
        }

        public void SetUp(double TimeStep)
        {
            YawPID = new PID(4, 0.01, 4, RC, TimeStep);
            PitchPID = new PID(4, 0.01, 4, RC, TimeStep);
            SpinPID = new PID(3, 0.05, 1, RC, TimeStep);

            MaxForce = HoverEngines[0].MaxThrust;
        }

        public void Turn(MatrixD Reference, Vector3D target)
        {
            MatrixD orientationMatrix = MatrixD.Identity;
            orientationMatrix.Translation = Reference.Translation;
            Vector3D orientationForward = Controller.WorldMatrix.Forward + Controller.WorldMatrix.Down + Controller.WorldMatrix.Left;
            orientationForward.Normalize();
            orientationMatrix.Forward = orientationForward;
            Vector3D orientationUp = Reference.Forward;
            orientationUp -= VectorHelpers.VectorProjection(orientationUp, orientationForward);
            orientationUp.Normalize();
            orientationMatrix.Up = orientationUp;
            Vector3D OrientationRight = orientationForward.Cross(orientationUp);
            orientationMatrix.Right = OrientationRight;
            var gravDir = Controller.GetNaturalGravity();
            gravDir.Normalize();

            double yawAngle, pitchAngle, spinAngle;

            if (target != Vector3D.Zero)
            {
                Vector3D TargetDir = target - orientationMatrix.Translation;
                TargetDir.Normalize();
                var projectedTargetUp = TargetDir - VectorHelpers.VectorProjection(TargetDir, orientationForward);
                spinAngle = -1 * VectorHelpers.VectorAngleBetween(orientationMatrix.Up, projectedTargetUp) * Math.Sign(orientationMatrix.Left.Dot(TargetDir));
            }
            else
            {
                spinAngle = 0;
                SpinPID.Reset();
            }
            GetRotationAngles(gravDir, orientationMatrix.Forward, orientationMatrix.Left, orientationMatrix.Up, out yawAngle, out pitchAngle);
            ApplyGyroOverride(PitchPID.Control(pitchAngle), YawPID.Control(yawAngle), spinAngle == 0 ? 0.75 : SpinPID.Control(spinAngle), Gyros, orientationMatrix);
        }

        void GetRotationAngles(Vector3D v_target, Vector3D v_front, Vector3D v_left, Vector3D v_up, out double yaw, out double pitch)
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

        void ApplyGyroOverride(double pitch_speed, double yaw_speed, double roll_speed, List<IMyGyro> gyro_list, MatrixD reference)
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

        public void Drive(Vector3D normalizedProjectedDirection)
        {
            Status = string.Empty;

            if (normalizedProjectedDirection == Vector3D.Zero)
            {
                foreach (var engine in HoverEngines)
                {
                    engine.Block.Enabled = true;
                    engine.OverridePercentage = 0;
                    continue;
                }
                return;
            }

            DirForces.Clear();
            Dirs.Clear();

            Dirs.Add(Controller.WorldMatrix.Forward);
            Dirs.Add(Controller.WorldMatrix.Down);
            Dirs.Add(Controller.WorldMatrix.Left);

            var gravDir = Controller.GetNaturalGravity();
            var gravStr = gravDir.Length();
            gravDir.Normalize();
            var gravForce = gravStr * Controller.CalculateShipMass().PhysicalMass;

            float alpha = 0;

            Vector3D PrimaryDriveDir = Vector3D.Zero;
            Vector3D SecondaryDriveDir = Vector3D.Zero;
            Vector3D OpposDriveDir = Vector3D.Zero;
            foreach (var dir in Dirs)
            {
                var engineDir = -dir;
                engineDir -= VectorHelpers.VectorProjection(engineDir, gravDir);
                engineDir.Normalize();

                var engineAngleDiff = VectorHelpers.VectorAngleBetween(engineDir, normalizedProjectedDirection);
                if (engineAngleDiff < 0.3333333333333 * Math.PI)
                {
                    DirForces[dir] = 1;
                    PrimaryDriveDir = dir;
                }
                else if (engineAngleDiff > 2 * 0.3333333333333 * Math.PI)
                {
                    DirForces[dir] = 0f;
                    OpposDriveDir = dir;
                }
                else
                {
                    var aPrime = 1 / (TrigHelpers.fastTan(engineAngleDiff - Math.PI * 0.333333333) * sqrt3inv + 1);
                    DirForces[dir] = (float)(1 - aPrime) * 2;
                    SecondaryDriveDir = dir;
                    alpha = DirForces[dir];
                }
            }

            var primaryDriveForce = MaxForce * VerticalForceRatio;
            var secondaryDriveForce = (primaryDriveForce * (1 - 2 * alpha) + gravForce * (alpha - 1)) / (alpha - 2);

            var totalUpForce = primaryDriveForce + secondaryDriveForce;

            // var maxPullForce = primaryDriveForce * MaxPullMultiplier;
            // float adjustPrimary = 0;
            // float adjustSecondary = 0;
            // float overdrive = 0;

            //if (totalUpForce > maxPullForce + gravForce)
            //{
            //    overdrive = (float)(totalUpForce - maxPullForce - gravForce);
            //    adjustSecondary = (float)-((alpha * (primaryDriveForce - overdrive + maxPullForce) - secondaryDriveForce - maxPullForce) / ((alpha + 1) * overdrive));
            //    adjustPrimary = 1 - adjustSecondary;
            //}
            //
            //Status += string.Format("OD: {0:0.00}, TUp: {1:0.0}, MaxP: {2:0.0}, GravF: {3:0.0}", overdrive, totalUpForce, maxPullForce, gravForce) + "\n";
            //Status += string.Format("Pd: {0:0.00}, Sd: {1:0.0}, A: {2:0.0}", primaryDriveForce, secondaryDriveForce, alpha) + "\n";
            //Status += string.Format("Dn: {0:0.00}, Nu: {1:0.0}, OSec: {2:0.0}", (alpha + 1) * overdrive, alpha * (primaryDriveForce - overdrive + maxPullForce) - secondaryDriveForce - maxPullForce, adjustSecondary) + "\n";

            //if (PrimaryDriveDir != Vector3D.Zero)
            //{
            //    DirForces[PrimaryDriveDir] = Math.Max(0.0001f, 1 - adjustPrimary * overdrive / MaxForce);
            //}
            //if (SecondaryDriveDir != Vector3D.Zero)
            //{
            //    DirForces[SecondaryDriveDir] = (float)Math.Max(0.00001f, secondaryDriveForce / (MaxForce * VerticalForceRatio) - adjustSecondary * overdrive / MaxForce);
            //}

            var multiplier = gravForce / totalUpForce;

            if (PrimaryDriveDir != Vector3D.Zero)
            {
                DirForces[PrimaryDriveDir] = Math.Max(0.0001f, (float)multiplier);
            }
            if (SecondaryDriveDir != Vector3D.Zero)
            {
                DirForces[SecondaryDriveDir] = (float)Math.Max(0.00001f, secondaryDriveForce * (float)multiplier / (MaxForce * VerticalForceRatio));
            }

            foreach (var engine in HoverEngines)
            {
                engine.Block.Enabled = OpposDriveDir != engine.Block.WorldMatrix.Forward;
                engine.OverridePercentage = DirForces[engine.Block.WorldMatrix.Forward];
            }

            Status += string.Format("Mult: {0:0.00}, P: {1:0.0}, S: {2:0.0}", multiplier, PrimaryDriveDir == Vector3D.Zero ? 0 : DirForces[PrimaryDriveDir], SecondaryDriveDir == Vector3D.Zero ? 0 : DirForces[SecondaryDriveDir]) + "\n";
        }
    }
}
