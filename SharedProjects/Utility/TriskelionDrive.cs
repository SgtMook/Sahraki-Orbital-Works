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
        public List<HoverEngineDriver> HoverEngines = new List<HoverEngineDriver>();
        List<IMyGyro> Gyros = new List<IMyGyro>();
        Dictionary<Vector3D, float> DirForces = new Dictionary<Vector3D, float>();
        List<Vector3D> Dirs = new List<Vector3D>();
        float MaxForce;

        public IMyShipController Controller;

        const float MinThrustRatio = 0.8660254f; // sqrt(3)/2 at opposite one drive
        const float sqrt3inv = 0.57735026919f;
        const float VerticalForceRatio = 0.577287712086f;
        const float HorizontalForceRatio = 0.816540811886f;
        const float VertiHoriForceRatio = 0.733232f;

        public string Status { get; private set; }
        public float MaxAccel { get { return (float)Controller.GetNaturalGravity().Length() * VertiHoriForceRatio; } }

        public bool Arrived { get; internal set; }


        public double GravAngle;

        public float DesiredAltitude;
        double DesiredVerticalVel = 0;

        public int SpeedLimit = 0;

        public float GetBrakingDistance()
        {
            var speed = (float)Controller.GetShipVelocities().LinearVelocity.Length();
            float aMax = MinThrustRatio * MaxAccel;
            float decelTime = speed / aMax;
            return speed * decelTime - 0.5f * aMax * decelTime * decelTime;
        }

        public float GetMaxSpeedFromBrakingDistance(float distance)
        {
            var speed = (float)Controller.GetShipVelocities().LinearVelocity.Length();
            float aMax = MinThrustRatio * MaxAccel;
            return (float)Math.Sqrt(2 * distance * aMax);
        }

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
            YawPID = new PID(1, 0.00, 1, 0, TimeStep);
            PitchPID = new PID(1, 0.00, 1, 0, TimeStep);
            SpinPID = new PID(3, 0.05, 1, RC, TimeStep);

            MaxForce = HoverEngines[0].MaxThrust;
            Turn(MatrixD.Zero, Vector3D.Zero);
            Drive(Vector3D.Zero);
        }

        public void Turn(MatrixD Reference, Vector3D target)
        {
            if (Reference == MatrixD.Zero) return;
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
            GravAngle = VectorHelpers.VectorAngleBetween(gravDir, orientationMatrix.Forward);

            if (target != Vector3D.Zero && GravAngle < Math.PI * 0.1)
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
            TrigHelpers.GetRotationAngles(gravDir, orientationMatrix.Forward, orientationMatrix.Left, orientationMatrix.Up, out yawAngle, out pitchAngle);
            TrigHelpers.ApplyGyroOverride(PitchPID.Control(pitchAngle), YawPID.Control(yawAngle), SpinPID.Control(spinAngle), Gyros, orientationMatrix);

        }

        public void Drive(Vector3D Destination)
        {
            if (Destination == Vector3D.Zero) return;

            if (GravAngle > Math.PI * 0.1)
            {
                foreach (var engine in HoverEngines)
                {
                    engine.Block.Enabled = true;
                    engine.OverridePercentage = 0;
                    engine.AltitudeMin = DesiredAltitude / VertiHoriForceRatio;
                }
                return;
            }

            var gravDir = Controller.GetNaturalGravity();
            var gravStr = gravDir.Length();
            gravDir.Normalize();
            var destDir = Destination - Controller.WorldMatrix.Translation;
            destDir -= VectorHelpers.VectorProjection(destDir, gravDir);
            var dist = destDir.Length();
            Arrived = dist < 20;
            destDir.Normalize();

            var maxSpeed = SpeedLimit == 0 ? Math.Min(100, GetMaxSpeedFromBrakingDistance((float)dist)) : SpeedLimit;
            var currentVel = Controller.GetShipVelocities().LinearVelocity;
            var horiVel = currentVel - VectorHelpers.VectorProjection(Controller.GetShipVelocities().LinearVelocity, gravDir);
            var vertiVel = currentVel.Dot(-gravDir);
            var accelDir = maxSpeed * destDir - horiVel;

            accelDir.Normalize();
            var mass = Controller.CalculateShipMass().PhysicalMass;
            var gravForce = gravStr * mass;

            DirForces.Clear();
            Dirs.Clear();

            Dirs.Add(Controller.WorldMatrix.Forward);
            Dirs.Add(Controller.WorldMatrix.Down);
            Dirs.Add(Controller.WorldMatrix.Left);

            float alpha = 0;
            double altitude;
            Controller.TryGetPlanetElevation(MyPlanetElevation.Surface, out altitude);
            DesiredVerticalVel = DesiredAltitude - altitude;
            var altAdjust = DesiredVerticalVel - vertiVel;

            Vector3D PrimaryDriveDir = Vector3D.Zero;
            Vector3D SecondaryDriveDir = Vector3D.Zero;
            Vector3D OpposDriveDir = Vector3D.Zero;
            foreach (var dir in Dirs)
            {
                var engineDir = dir;
                engineDir -= VectorHelpers.VectorProjection(engineDir, gravDir);
                engineDir.Normalize();

                var engineAngleDiff = VectorHelpers.VectorAngleBetween(engineDir, accelDir);
                if (engineAngleDiff < 0.3333333333333 * Math.PI)
                {
                    DirForces[dir] = 0;
                    OpposDriveDir = dir;
                }
                else if (engineAngleDiff > 2 * 0.3333333333333 * Math.PI)
                {
                    DirForces[dir] = 1f;
                    PrimaryDriveDir = dir;
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
            var secondaryDriveForce = primaryDriveForce * alpha;

            var totalUpForce = primaryDriveForce + secondaryDriveForce;
            var multiplier = (gravForce + altAdjust * mass) / totalUpForce;

            Status = $"{altAdjust}, {dist}";

            if (PrimaryDriveDir != Vector3D.Zero)
            {
                DirForces[PrimaryDriveDir] = Math.Max(0.01f, (float)multiplier);
            }
            if (SecondaryDriveDir != Vector3D.Zero)
            {
                DirForces[SecondaryDriveDir] = (float)Math.Max(0.01f, alpha * (float)multiplier);
            }

            var altMin = 1;
            if (altitude > 7) altMin = 6;
            if (altitude > 40) altMin = 10;

            foreach (var engine in HoverEngines)
            {
                engine.AltitudeMin = altMin;
                engine.Block.Enabled = OpposDriveDir != engine.Block.WorldMatrix.Forward;
                engine.OverridePercentage = DirForces[engine.Block.WorldMatrix.Forward];
            }
        }

        public void Flush()
        {
            foreach (var engine in HoverEngines)
            {
                engine.Block.Enabled = true;
                engine.OverridePercentage = 0;
                engine.AltitudeMin = 10;
            }
            Arrived = false;
            return;
        }
    }
}