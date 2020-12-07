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
    public class AtmoDrive
    {
        const float LateralThrustReserve = 0.25f;

        // Arguments
        public Vector3D AimTarget = Vector3D.Zero;
        public Vector3D Destination = Vector3D.Zero;
        public IMyTerminalBlock MoveRef;

        public StringBuilder StatusBuilder = new StringBuilder();

        // Parts list
        List<IMyGyro> Gyros = new List<IMyGyro>();
        Dictionary<Base6Directions.Direction, List<IMyThrust>> Thrusters = new Dictionary<Base6Directions.Direction, List<IMyThrust>>();
        IMyRemoteControl RemoteControl;

        public void AddComponenet(IMyTerminalBlock block)
        {
            // TODO: Add block to whatever stores
            if (block is IMyGyro) Gyros.Add((IMyGyro)block);
            if (block is IMyThrust)
            {
                var thruster = (IMyThrust)block;
                var relativeDirection = RemoteControl.WorldMatrix.GetClosestDirection(thruster.WorldMatrix.Forward);
                if (!Thrusters.ContainsKey(relativeDirection)) Thrusters[relativeDirection] = new List<IMyThrust>();
                Thrusters[relativeDirection].Add(thruster);
            }
        }

        public void Initialize()
        {
            // Set MaxDownThrust and MaxLateralThrust accordingly
            foreach (var kvp in Thrusters)
            {
                if (kvp.Key == Base6Directions.Direction.Down) foreach (var thruster in kvp.Value) MaxDownThrust += thruster.MaxThrust;
                else if (kvp.Key == Base6Directions.Direction.Forward) foreach (var thruster in kvp.Value) MaxLateralThrust += thruster.MaxThrust;
            }
        }

        float MaxDownThrust;
        float MaxLateralThrust;

        Vector3D gravDir;
        double gravStr;
        float shipMass;

        PID YawPID;
        PID PitchPID;
        PID SpinPID;

        int cycle = 0;

        public AtmoDrive(IMyRemoteControl remoteControl, double TimeStep = 5)
        {
            RemoteControl = remoteControl;
            MoveRef = RemoteControl;

            YawPID = new PID(3, 0.00, 1, 0, TimeStep);
            PitchPID = new PID(3, 0.00, 1, 0, TimeStep);
            SpinPID = new PID(4, 0.001, 8, 0.9, TimeStep);
        }

        // Gets the maximum angle deviation from vertical the drone is allowed to operate in
        double GetMaxAngleConstraint()
        {
            var AntiGravityStr = MaxDownThrust / shipMass;
            var LateralStr = MaxLateralThrust / shipMass;

            var Angle = 2 * Math.Atan((gravStr - Math.Sqrt(gravStr * gravStr + LateralStr * LateralStr * LateralThrustReserve * LateralThrustReserve - LateralStr * LateralStr)) / (LateralStr * (1 + LateralThrustReserve)));

            return Angle;
        }

        public void Update()
        {
            cycle++;
            StatusBuilder.Clear();

            StatusBuilder.AppendLine($"CYCLE {cycle}");

            shipMass = RemoteControl.CalculateShipMass().PhysicalMass;
            gravDir = RemoteControl.GetNaturalGravity();
            gravStr = gravDir.Length();
            gravDir.Normalize();

            if (Destination == Vector3D.Zero)
            {
                RemoteControl.DampenersOverride = true;
            }

            // Rotational Control
            var targetDir = Vector3D.Zero;
            if (AimTarget != Vector3D.Zero)
            {
                targetDir = AimTarget - RemoteControl.WorldMatrix.Translation;
            }
            else
            {
                targetDir = RemoteControl.WorldMatrix.Forward - VectorHelpers.VectorProjection(RemoteControl.WorldMatrix.Forward, gravDir);
            }

            var angleFromVertical = VectorHelpers.VectorAngleBetween(targetDir, gravDir) - Math.PI * 0.5;
            var maxAngleFromVertical = GetMaxAngleConstraint();
            angleFromVertical = Math.Max(Math.Min(angleFromVertical, maxAngleFromVertical), -maxAngleFromVertical);
            var flatAimDir = targetDir - VectorHelpers.VectorProjection(targetDir, gravDir);
            flatAimDir.Normalize();
            var flatCurrentDir = RemoteControl.WorldMatrix.Forward - VectorHelpers.VectorProjection(RemoteControl.WorldMatrix.Forward, gravDir);
            flatCurrentDir.Normalize();
            var downDir = TrigHelpers.FastCos(angleFromVertical) * gravDir + TrigHelpers.FastSin(angleFromVertical) * flatAimDir;

            double yawAngle, pitchAngle, spinAngle;

            MatrixD orientationMatrix = new MatrixD();
            orientationMatrix.Forward = RemoteControl.WorldMatrix.Down;
            orientationMatrix.Left = RemoteControl.WorldMatrix.Left;
            orientationMatrix.Up = RemoteControl.WorldMatrix.Forward;

            spinAngle = - VectorHelpers.VectorAngleBetween(flatAimDir, flatCurrentDir) * Math.Sign(RemoteControl.WorldMatrix.Left.Dot(flatAimDir));

            StatusBuilder.AppendLine(downDir.ToString());

            TrigHelpers.GetRotationAngles(downDir, orientationMatrix.Forward, orientationMatrix.Left, orientationMatrix.Up, out yawAngle, out pitchAngle);

            TrigHelpers.ApplyGyroOverride(PitchPID.Control(pitchAngle), YawPID.Control(yawAngle), SpinPID.Control(spinAngle), Gyros, orientationMatrix);
        }
    }
}