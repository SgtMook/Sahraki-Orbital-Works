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
    public interface IAutopilot
    {
        void Move(Vector3D targetPosition);
        void Turn(Vector3D targetPosition);
        void Spin(Vector3D targetUp);
        void SetMaxSpeed(float maxSpeed);
        void SetMoveReference(IMyTerminalBlock block);
        void SetWaypoint(Waypoint w);
        bool AtWaypoint(Waypoint w);

        float GetBrakingDistance();

        IMyShipController Controller { get; }

    }

    public class AutopilotSubsystem : ISubsystem, IAutopilot
    {

        StringBuilder statusbuilder = new StringBuilder();
        // StringBuilder debugBuilder = new StringBuilder();

        #region ISubsystem
        public void Command(TimeSpan timestamp, string command, object argument)
        {
            if (command == "move") Move(ParseGPS((string)argument));
            if (command == "turn") Turn(ParseGPS((string)argument) - reference.WorldMatrix.Translation);
            if (command == "spin") Spin(ParseGPS((string)argument) - reference.WorldMatrix.Translation);
            if (command == "setwaypoint") SetWaypoint((Waypoint)argument);
        }

        public void Setup(MyGridProgram program, string name)
        {
            Program = program;

            UpdateFrequency = UpdateFrequency.Update10;

            GetParts();
        }

        public void Update(TimeSpan timestamp, UpdateFrequency updateFlags)
        {
            bool hasPos = targetPosition != Vector3D.Zero;
            if (hasPos) SetThrusterPowers();
            bool hasDir = targetDirection != Vector3D.Zero || targetUp != Vector3D.Zero;
            if (hasDir) SetGyroPowers();

            if (hasPos || hasDir)
                UpdateFrequency = UpdateFrequency.Update1;
            else
            {
                UpdateFrequency = UpdateFrequency.Update10;
                reference = controller;
                maxSpeed = 100;
                IYaw = 0;
                IPitch = 0;
            }
        }

        public string GetStatus()
        {
            statusbuilder.Clear();

            if (controller != null)
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
                //Vector3D desiredVelocity = posError * desiredSpeed;
                //Vector3D currentVelocity = controller.GetShipVelocities().LinearVelocity;
                //
                //Vector3D Error = (desiredVelocity - currentVelocity) * 30 / aMax;
                //statusbuilder.AppendLine($"TVOL: {Error}");
                //statusbuilder.AppendLine($"tD: {targetDirection}");
                //statusbuilder.AppendLine($"refD: {reference.WorldMatrix.Forward}");
                //
                //statusbuilder.AppendLine(reference.WorldMatrix.Up.ToString());
                statusbuilder.AppendLine(IPitch.ToString());
                statusbuilder.AppendLine(IYaw.ToString());
            }
            else
            {
                statusbuilder.AppendLine("ERR");
            }

            return statusbuilder.ToString();
        }

        public UpdateFrequency UpdateFrequency { get; set; }

        public string SerializeSubsystem()
        {
            // StringBuilder builder = new StringBuilder();
            // builder.AppendLine(targetPosition.ToString());
            // builder.AppendLine(targetDirection.ToString());
            // return builder.ToString();
            return string.Empty;
        }

        public void DeserializeSubsystem(string serialized)
        {
            // debugBuilder.Append(serialized);
            // MyStringReader reader = new MyStringReader(serialized);
            // targetPosition = VectorUtilities.StringToVector3(reader.NextLine());
            // targetDirection = VectorUtilities.StringToVector3(reader.NextLine());
        }
        #endregion

        #region IAutopilot
        public void Move(Vector3D targetPosition)
        {
            if (targetPosition != Vector3.One)
                this.targetPosition = targetPosition;
        }
        public void Turn(Vector3D targetDirection)
        {
            if (targetDirection != Vector3.One)
                this.targetDirection = targetDirection;
        }
        public void Spin(Vector3D targetUp)
        {
            if (targetUp != Vector3.One)
                this.targetUp = targetUp;
        }
        public void SetMaxSpeed(float maxSpeed)
        {
            if (maxSpeed != -1f)
                this.maxSpeed = maxSpeed;
        }
        public void SetMoveReference(IMyTerminalBlock block)
        {
            if (block != null && Program.Me.IsSameConstructAs(block))
                reference = block;
            else
                reference = controller;
        }
        public void SetWaypoint(Waypoint w)
        {
            Move(w.Position);
            Turn(w.Direction);
            SetMaxSpeed(w.MaxSpeed);
        }
        public bool AtWaypoint(Waypoint w)
        {
            if (w.Position != Vector3.One && w.Position != Vector3.Zero)
            {
                var speed = (float)controller.GetShipVelocities().LinearVelocity.Length();
                Vector3D posError = (w.Position - reference.WorldMatrix.Translation);
                var distance = (float)posError.Length();
                if (distance > 0.25f || speed > 0.25f)
                    return false;
            }
            if (w.Direction != Vector3.One && w.Direction != Vector3.Zero)
            {
                double yawAngle, pitchAngle;
                GetRotationAngles(w.Direction, reference.WorldMatrix.Forward, reference.WorldMatrix.Left, reference.WorldMatrix.Up, out yawAngle, out pitchAngle);
                if (Math.Abs(yawAngle) > 0.01f || Math.Abs(pitchAngle) > 0.01f)
                    return false;
            }
            if (w.DirectionUp != Vector3.One && w.DirectionUp != Vector3.Zero)
            {
                var projectedTargetUp = targetUp - reference.WorldMatrix.Forward.Dot(targetUp) * reference.WorldMatrix.Forward;
                var spinAngle = -1 * VectorHelpers.VectorAngleBetween(reference.WorldMatrix.Up, projectedTargetUp) * Math.Sign(reference.WorldMatrix.Left.Dot(targetUp));
                if (Math.Abs(spinAngle) > 0.01f)
                    return false;
            }

            return true;
        }

        public float GetBrakingDistance()
        {
            var speed = (float)controller.GetShipVelocities().LinearVelocity.Length();
            var maxThrust = thrusts[0];
            float aMax = 0.8f * maxThrust / controller.CalculateShipMass().PhysicalMass;
            float decelTime = speed / aMax;
            return speed * decelTime - 0.5f * aMax * decelTime * decelTime;
        }

        public IMyShipController Controller => controller;
        #endregion

        MyGridProgram Program;

        IMyRemoteControl controller;
        IMyTerminalBlock reference;

        List<IMyThrust> thrustersList = new List<IMyThrust>();

        List<IMyGyro> gyros = new List<IMyGyro>();
        float[] thrusts = new float[6];
        float maxSpeed = 98;

        Vector3D DTranslate = Vector3.Zero;
        Vector3D ITranslate = Vector3.Zero;
        Vector3D targetPosition = Vector3.Zero;

        double IYaw = 0;
        double IPitch = 0;

        Vector3D targetDirection = Vector3.Zero;
        Vector3D targetUp = Vector3.Zero;

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

        void GetParts()
        {
            controller = null;

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
            Vector3D AutopilotMoveIndicator;

            GetMovementVectors(targetPosition, controller, reference, thrusts[0], maxSpeed, out AutopilotMoveIndicator, ref DTranslate, ref ITranslate);

            if (AutopilotMoveIndicator == Vector3.Zero)
            {
                controller.DampenersOverride = true;
            }
            else
            {
                controller.DampenersOverride = false;
            }

            var gridDirIndicator = Vector3D.TransformNormal(AutopilotMoveIndicator, MatrixD.Transpose(
                controller.CubeGrid.WorldMatrix));

            for (int i = 0; i < thrustersList.Count; i++)
            {
                var f = thrustersList[i].Orientation.Forward;
                float power = (DirectionMap[f] * -1f).Dot(gridDirIndicator);
                thrustersList[i].ThrustOverridePercentage = Math.Max(power, 0) * 1f;
            }
        }

        void SetGyroPowers()
        {
            if (targetDirection == Vector3.Zero && targetUp == Vector3.Zero) return;

            double yawAngle = 0, pitchAngle = 0, spinAngle = 0;
            if (targetDirection != Vector3.Zero) GetRotationAngles(targetDirection, reference.WorldMatrix.Forward, reference.WorldMatrix.Left, reference.WorldMatrix.Up, out yawAngle, out pitchAngle);
            if (targetUp != Vector3.Zero)
            {
                var projectedTargetUp = targetUp - reference.WorldMatrix.Forward.Dot(targetUp) * reference.WorldMatrix.Forward;
                spinAngle = -1 * VectorHelpers.VectorAngleBetween(reference.WorldMatrix.Up, projectedTargetUp) * Math.Sign(reference.WorldMatrix.Left.Dot(targetUp));
            }

            IYaw += yawAngle;
            IPitch += pitchAngle;
            double kI = 0.01;

            ApplyGyroOverride(pitchAngle * 2 + IPitch * kI, yawAngle * 2 + IYaw * kI, spinAngle * 2, gyros, reference);

            if (Math.Abs(yawAngle) < 0.01f && Math.Abs(pitchAngle) < 0.01f && Math.Abs(spinAngle) < 0.01f)
            {
                targetDirection = Vector3.Zero;
                targetUp = Vector3.Zero;
                foreach (var gyro in gyros)
                {
                    gyro.GyroOverride = false;
                }
            }
        }

        Vector3D ParseGPS(string s)
        {
            var split = s.Split(':');
            return new Vector3(float.Parse(split[2]), float.Parse(split[3]), float.Parse(split[4]));
        }

        void GetRotationAngles(Vector3D v_target, Vector3D v_front, Vector3D v_left, Vector3D v_up, out double yaw, out double pitch)
        {
            //Dependencies: VectorProjection() | VectorAngleBetween()
            var projectTargetUp =  VectorHelpers.VectorProjection(v_target, v_up);
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

        void GetMovementVectors(Vector3D target, IMyShipController controller, IMyTerminalBlock reference, float maxThrust, float maxSpeed, out Vector3D AutopilotMoveIndicator, ref Vector3D D, ref Vector3D I)
        {
            var speed = (float)controller.GetShipVelocities().LinearVelocity.Length();

            Vector3D posError = (target - reference.WorldMatrix.Translation);
            var distance = (float)posError.Length();
            posError.Normalize();

            float aMax = 0.8f * maxThrust / controller.CalculateShipMass().PhysicalMass;
            float desiredSpeed = Math.Min((float)Math.Sqrt(2f * aMax * distance), maxSpeed);

            Vector3D desiredVelocity = posError * desiredSpeed;
            Vector3D currentVelocity = controller.GetShipVelocities().LinearVelocity;

            Vector3D adjustVector = currentVelocity - VectorHelpers.VectorProjection(currentVelocity, desiredVelocity);
            if (adjustVector.Length() < currentVelocity.Length() * 0.1) adjustVector = Vector3.Zero;

            Vector3D Error = (desiredVelocity - currentVelocity - adjustVector * 3) * 30 / aMax;

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
            if (distance < 10 && speed < 10) AutopilotMoveIndicator *= 0.5f;
            else if (distance < 1) AutopilotMoveIndicator *= 0.2f;

            if (AutopilotMoveIndicator.Length() > 1) AutopilotMoveIndicator /= AutopilotMoveIndicator.Length();

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
