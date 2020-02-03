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

using SharedProjects.Utility;

namespace SharedProjects.Subsystems
{
    public class AutopilotSubsystem : ISubsystem
    {

        StringBuilder statusbuilder = new StringBuilder();
        // StringBuilder debugBuilder = new StringBuilder();

        #region Subsystem 
        public void Command(string command, object argument)
        {
            if (command == "move") targetPosition = ParseGPS((string)argument);
            if (command == "turn") targetDirection = ParseGPS((string)argument) - reference.WorldMatrix.Translation;
            if (command == "setwaypoint") SetWaypoint((Waypoint)argument);
        }

        public void Setup(MyGridProgram program, SubsystemManager manager)
        {
            Program = program;

            GetParts();
        }

        public void Update(TimeSpan timestamp)
        {
            if (targetPosition != Vector3.Zero) SetThrusterPowers();
            if (targetDirection != Vector3.Zero) SetGyroPowers();
        }

        public string GetStatus()
        {
            statusbuilder.Clear();

            if (controller != null && connector != null)
            {
                statusbuilder.AppendLine("AOK");

                // Debug
                //statusbuilder.AppendLine($"TPOS: {targetPosition.ToString()}");
                //statusbuilder.AppendLine($"TDIR: {targetDirection.ToString()}");
                
                //var speed = (float)controller.GetShipVelocities().LinearVelocity.Length();
                //
                //Vector3D posError = (targetPosition - reference.WorldMatrix.Translation);
                //var distance = (float)posError.Length();
                //posError.Normalize();
                //
                //float aMax = 0.8f * thrusts[0] / controller.CalculateShipMass().PhysicalMass;
                //float desiredSpeed = Math.Min((float)Math.Sqrt(2f * aMax * distance), maxSpeed);
                //
                //Vector3 desiredVelocity = posError * desiredSpeed;
                //Vector3 currentVelocity = controller.GetShipVelocities().LinearVelocity;
                //
                //Vector3 Error = (desiredVelocity - currentVelocity) * 30 / aMax;
                //statusbuilder.AppendLine($"TVOL: {Error}");
            }
            else
            {
                statusbuilder.AppendLine("ERR");
            }

            return statusbuilder.ToString();
        }

        public int UpdateFrequency
        {
            get
            {
                return 1;
            }
        }

        public string SerializeSubsystem()
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine(targetPosition.ToString());
            builder.AppendLine(targetDirection.ToString());
            return builder.ToString();
        }

        public void DeserializeSubsystem(string serialized)
        {
            // debugBuilder.Append(serialized);
            MyStringReader reader = new MyStringReader(serialized);
            targetPosition = VectorUtilities.StringToVector3(reader.NextLine());
            targetDirection = VectorUtilities.StringToVector3(reader.NextLine());
        }
        #endregion

        MyGridProgram Program;

        IMyRemoteControl controller;
        IMyShipConnector connector;
        IMyTerminalBlock reference;

        List<IMyThrust> thrustersList = new List<IMyThrust>();

        List<IMyGyro> gyros = new List<IMyGyro>();
        float[] thrusts = new float[6];
        float maxSpeed = 98;

        Vector3 D = Vector3.Zero;
        Vector3 I = Vector3.Zero;
        Vector3 targetDirection = Vector3.Zero;
        Vector3 targetPosition = Vector3.Zero;

        Dictionary<Base6Directions.Direction, Vector3I> DirectionMap = new Dictionary<Base6Directions.Direction, Vector3I>()
        {
            { Base6Directions.Direction.Up, Vector3I.Up },
            { Base6Directions.Direction.Down, Vector3I.Down },
            { Base6Directions.Direction.Left, Vector3I.Left },
            { Base6Directions.Direction.Right, Vector3I.Right },
            { Base6Directions.Direction.Forward, Vector3I.Forward },
            { Base6Directions.Direction.Backward, Vector3I.Backward },
        };

        // Helpers

        void SetWaypoint(Waypoint w)
        {
            if (w.Position != Vector3.One)
                targetPosition = w.Position;
            if (w.Direction != Vector3.One)
                targetDirection = w.Direction;
            if (w.MaxSpeed != -1f)
                maxSpeed = w.MaxSpeed;

            if (w.ReferenceMode == "Dock")
                reference = connector;
            else
                reference = controller;
        }

        void GetParts()
        {
            controller = null;
            connector = null;

            thrustersList.Clear();
            for (int i = 0; i < thrusts.Length; i++)
                thrusts[i] = 0;

            Program.GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(null, CollectParts);

            if (controller != null)
            {
                controller.IsMainCockpit = true;
                controller.DampenersOverride = true;
                reference = controller;
            }
            foreach (var thruster in thrustersList)
            {
                var f = thruster.Orientation.Forward;
                thrusts[(int)f] += thruster.MaxEffectiveThrust;
            }
        }

        private bool CollectParts(IMyTerminalBlock block)
        {
            if (!Program.Me.IsSameConstructAs(block)) return false;

            if (block is IMyRemoteControl)
                controller = (IMyRemoteControl)block;

            if (block is IMyShipConnector)
                connector = (IMyShipConnector)block;

            if (block is IMyThrust)
            {
                IMyThrust thruster = (IMyThrust)block;
                thrustersList.Add(thruster);
                thruster.ThrustOverride = 0;
            }

            if (block is IMyGyro)
            {
                IMyGyro gyro = (IMyGyro)block;
                gyros.Add(gyro);
                gyro.GyroOverride = false;
            }

            return false;
        }

        void SetThrusterPowers()
        {
            Vector3 AutopilotMoveIndicator;

            GetMovementVectors(targetPosition, controller, reference, thrusts[0], maxSpeed, out AutopilotMoveIndicator, ref D, ref I);

            if (AutopilotMoveIndicator == Vector3.Zero)
            {
                controller.DampenersOverride = true;
            }
            else
            {
                controller.DampenersOverride = false;
            }

            var gridDirIndicator = Vector3.TransformNormal(AutopilotMoveIndicator, MatrixD.Transpose(
                Program.Me.CubeGrid.WorldMatrix));

            for (int i = 0; i < thrustersList.Count; i++)
            {
                var f = thrustersList[i].Orientation.Forward;
                float power = (DirectionMap[f] * -1f).Dot(ref gridDirIndicator);
                thrustersList[i].ThrustOverridePercentage = Math.Max(power, 0) * 1f;
            }
        }

        void SetGyroPowers()
        {
            if (targetDirection == Vector3.Zero) return;

            double yawAngle, pitchAngle;
            GetRotationAngles(targetDirection, reference.WorldMatrix.Forward, reference.WorldMatrix.Left, reference.WorldMatrix.Up, out yawAngle, out pitchAngle);
            ApplyGyroOverride(pitchAngle * 2, yawAngle * 2, 0, gyros, reference);

            if (yawAngle < 0.01f && pitchAngle < 0.01f)
            {
                targetDirection = Vector3.Zero;
                foreach (var gyro in gyros)
                {
                    gyro.GyroOverride = false;
                }
            }
        }

        Vector3 ParseGPS(string s)
        {
            var split = s.Split(':');
            return new Vector3(float.Parse(split[2]), float.Parse(split[3]), float.Parse(split[4]));
        }

        void GetRotationAngles(Vector3D v_target, Vector3D v_front, Vector3D v_left, Vector3D v_up, out double yaw, out double pitch)
        {
            //Dependencies: VectorProjection() | VectorAngleBetween()
            var projectTargetUp = VectorProjection(v_target, v_up);
            var projTargetFrontLeft = v_target - projectTargetUp;

            yaw = VectorAngleBetween(v_front, projTargetFrontLeft);

            if (Vector3D.IsZero(projTargetFrontLeft) && !Vector3D.IsZero(projectTargetUp)) //check for straight up case
                pitch = MathHelper.PiOver2;
            else
                pitch = VectorAngleBetween(v_target, projTargetFrontLeft); //pitch should not exceed 90 degrees by nature of this definition

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

        void GetMovementVectors(Vector3 target, IMyShipController controller, IMyTerminalBlock reference, float maxThrust, float maxSpeed, out Vector3 AutopilotMoveIndicator, ref Vector3 D, ref Vector3 I)
        {
            var speed = (float)controller.GetShipVelocities().LinearVelocity.Length();

            Vector3D posError = (target - reference.WorldMatrix.Translation);
            var distance = (float)posError.Length();
            posError.Normalize();

            float aMax = 0.8f * maxThrust / controller.CalculateShipMass().PhysicalMass;
            float desiredSpeed = Math.Min((float)Math.Sqrt(2f * aMax * distance), maxSpeed);

            Vector3 desiredVelocity = posError * desiredSpeed;
            Vector3 currentVelocity = controller.GetShipVelocities().LinearVelocity;

            Vector3 Error = (desiredVelocity - currentVelocity) * 30 / aMax;


            float kP = 1f;
            float kD = 2.5f;
            float kI = 0.2f;

            if (Error.Length() > 1) Error.Normalize();
            else
            {
                kD = 1;
                kI = 0.1f;
                kP = 0.2f;
            }

            if (D == Vector3.Zero) D = Error;


            AutopilotMoveIndicator = kP * Error + kD * (Error - D) + kI * I;
            if (distance < 10) AutopilotMoveIndicator *= 0.5f;
            else if (distance < 1) AutopilotMoveIndicator *= 0.2f;

            if (AutopilotMoveIndicator.Length() > 1) AutopilotMoveIndicator /= AutopilotMoveIndicator.Length();

            // echoBuilder.AppendLine(speed.ToString());
            // echoBuilder.AppendLine(desiredSpeed.ToString());
            // echoBuilder.AppendLine(desiredVelocity.ToString());
            // echoBuilder.AppendLine(Error.ToString());
            // echoBuilder.AppendLine((Error - D).ToString());
            // echoBuilder.AppendLine(I.ToString());
            // echoBuilder.AppendLine(AutopilotMoveIndicator.ToString());
            // echoBuilder.AppendLine(AutopilotMoveIndicator.Length().ToString());

            I += Error;
            if (I.Length() > 5) I *= 5 / I.Length();
            D = Error;

            if (distance < 0.25f && speed < 0.25f)
            {
                targetPosition = Vector3.Zero;
                AutopilotMoveIndicator = Vector3.Zero;
                I = Vector3.Zero;
                D = Vector3.Zero;
            }
        }

        Vector3D VectorProjection(Vector3D a, Vector3D b)
        {
            if (Vector3D.IsZero(b))
                return Vector3D.Zero;

            return a.Dot(b) / b.LengthSquared() * b;
        }

        double VectorAngleBetween(Vector3D a, Vector3D b) //returns radians
        {
            if (Vector3D.IsZero(a) || Vector3D.IsZero(b))
                return 0;
            else
                return Math.Acos(MathHelper.Clamp(a.Dot(b) / Math.Sqrt(a.LengthSquared() * b.LengthSquared()), -1, 1));
        }

        void ApplyGyroOverride(double pitch_speed, double yaw_speed, double roll_speed, List<IMyGyro> gyro_list, IMyTerminalBlock reference)
        {
            var rotationVec = new Vector3D(-pitch_speed, yaw_speed, roll_speed); //because keen does some weird stuff with signs
            var shipMatrix = reference.WorldMatrix;
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
    }
}
