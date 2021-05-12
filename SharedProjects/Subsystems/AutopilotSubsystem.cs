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
        void Drift(Vector3D targetDrift);
        void SetMaxSpeed(float maxSpeed);
        bool AtWaypoint(Waypoint w);
        void Clear();

        float GetBrakingDistance();
        float GetMaxSpeedFromBrakingDistance(float maxSpeed);

        IMyShipController Controller { get; }
        IMyTerminalBlock Reference { get; set; }

        float CruiseSpeed { get; }
        float CombatSpeed { get; }

    }

    public class AutopilotSubsystem : ISubsystem, IAutopilot
    {
        StringBuilder statusbuilder = new StringBuilder();
        // StringBuilder debugBuilder = new StringBuilder();
        int run = 0;
        int kRunEveryXUpdates = 5;
        float kInverseTimeStep;
        public bool Persist = false;

        #region ISubsystem
        public void Command(TimeSpan timestamp, string command, object argument)
        {
            if (command == "move") Move(ParseGPS((string)argument));
            if (command == "turn") Turn(ParseGPS((string)argument) - reference.WorldMatrix.Translation);
            if (command == "spin") Spin(ParseGPS((string)argument) - reference.WorldMatrix.Translation);
        }

        public void Setup(ExecutionContext context, string name)
        {
            Context = context;

            UpdateFrequency = UpdateFrequency.Update10;

            kInverseTimeStep = 1 / kRunEveryXUpdates;

            GetParts();

            ParseConfigs();

            currentMaxSpeed = MaxCruiseSpeed;

            // Force JIT Compilation
            Vector3D AutopilotMoveIndicator;
            GetMovementVectors(targetPosition, controller, reference, thrusts[0], currentMaxSpeed, out AutopilotMoveIndicator, ref DTranslate, ref ITranslate);
            SetThrusterPowers();
            SetGyroPowers(true);
            ApplyGyroOverride(0, 0, 0, gyros, reference);
            ClearGyros();
            Clear();
        }

        public void Update(TimeSpan timestamp, UpdateFrequency updateFlags)
        {
            run++;
            if (run == kRunEveryXUpdates)
            {
                bool hasPos = targetPosition != Vector3D.Zero || targetDrift != Vector3D.Zero;
                if (hasPos || tActive)
                {
                    tActive = hasPos;
                    if (targetDrift != Vector3D.Zero && targetPosition != Vector3D.Zero)
                        targetPosition += targetDrift * kRunEveryXUpdates / 60;
                    SetThrusterPowers();
                }
                bool hasDir = targetDirection != Vector3D.Zero || targetUp != Vector3D.Zero;
                if (hasDir || rActive)
                {
                    rActive = hasDir;
                    SetGyroPowers();
                }

                if (hasPos || hasDir)
                    UpdateFrequency = UpdateFrequency.Update1;
                else if (UpdateFrequency == UpdateFrequency.Update1)
                {
                    UpdateFrequency = UpdateFrequency.Update10;
                    Clear();
                }
                run = 0;
            }
        }

        public string GetStatus()
        {
            statusbuilder.Clear();

            statusbuilder.AppendLine(RP.ToString());

            return statusbuilder.ToString();
        }

        public UpdateFrequency UpdateFrequency { get; set; }

        public string SerializeSubsystem()
        {
            return string.Empty;
        }

        public void DeserializeSubsystem(string serialized)
        {
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
        public void Drift(Vector3D targetDrift)
        {
            if (targetDrift != Vector3.One)
                this.targetDrift = targetDrift;
        }
        public void SetMaxSpeed(float maxSpeed)
        {
            if (maxSpeed != -1f)
                this.currentMaxSpeed = maxSpeed;
        }
        public bool AtWaypoint(Waypoint w)
        {
            if (Persist) return false;
            if (w.Position != Vector3.One && w.Position != Vector3.Zero)
            {
                var speed = (float)(controller.GetShipVelocities().LinearVelocity - w.Velocity).Length();
                Vector3D posError = (targetPosition - reference.WorldMatrix.Translation);
                var distance = (float)posError.Length();
                if (distance > 0.25f || speed > 0.25f)
                    return false;
            }
            if (w.Direction != Vector3.One && w.Direction != Vector3.Zero)
            {
                double yawAngle, pitchAngle;
                GetRotationAngles(w.Direction, reference.WorldMatrix.Forward, reference.WorldMatrix.Left, reference.WorldMatrix.Up, out yawAngle, out pitchAngle);
                if (Math.Abs(yawAngle) > 0.03f || Math.Abs(pitchAngle) > 0.03f)
                    return false;
            }
            if (w.DirectionUp != Vector3.One && w.DirectionUp != Vector3.Zero)
            {
                var projectedTargetUp = targetUp - reference.WorldMatrix.Forward.Dot(targetUp) * reference.WorldMatrix.Forward;
                var spinAngle = -1 * VectorHelpers.VectorAngleBetween(reference.WorldMatrix.Up, projectedTargetUp) * Math.Sign(reference.WorldMatrix.Left.Dot(targetUp));
                if (Math.Abs(spinAngle) > 0.03f)
                    return false;
            }

            return true;
        }

        public void Clear()
        {
            targetDirection = Vector3D.Zero;
            targetUp = Vector3D.Zero;
            targetPosition = Vector3D.Zero;
            targetDrift = Vector3D.Zero;
            reference = controller;
            currentMaxSpeed = MaxCruiseSpeed;
            IYaw = 0;
            IPitch = 0;
            ITranslate = Vector3D.Zero;
            DTranslate = Vector3D.Zero;
        }

        public float GetBrakingDistance()
        {
            var speed = (float)controller.GetShipVelocities().LinearVelocity.Length();
            var maxThrust = thrusts[0];
            float aMax = 0.8f * maxThrust / controller.CalculateShipMass().PhysicalMass;
            float decelTime = speed / aMax;
            return speed * decelTime - 0.5f * aMax * decelTime * decelTime;
        }

        public float GetMaxSpeedFromBrakingDistance(float distance)
        {
            var speed = (float)controller.GetShipVelocities().LinearVelocity.Length();
            var maxThrust = thrusts[0];
            float aMax = 0.8f * maxThrust / controller.CalculateShipMass().PhysicalMass;

            return (float)Math.Sqrt(2 * distance * aMax);
        }

        public IMyShipController Controller => controller;

        public IMyTerminalBlock Reference
        {
            get
            {
                return reference;
            }
            set
            {
                if (value != null)
                    reference = value;
                else
                    reference = controller;
            }
        }

        public float CruiseSpeed => MaxCruiseSpeed;
        public float CombatSpeed => MaxCombatSpeed;
        #endregion

        ExecutionContext Context;

        IMyShipController controller;
        IMyTerminalBlock reference;

        ThrusterManager thrusterManager = new ThrusterManager();
        List<IMyThrust> thrustersList = new List<IMyThrust>();

        List<IMyGyro> gyros = new List<IMyGyro>();
        float[] thrusts = new float[6];
        float currentMaxSpeed;

        Vector3D DTranslate = Vector3.Zero;
        Vector3D ITranslate = Vector3.Zero;
        Vector3D targetPosition = Vector3.Zero;
        Vector3D targetDrift = Vector3.Zero;

        double IYaw = 0;
        double IPitch = 0;
        double DYaw = 0;
        double DPitch = 0;

        Vector3D targetDirection = Vector3.Zero;
        Vector3D targetUp = Vector3.Zero;

        bool tActive = true;
        bool rActive = true;

        // Translation PID - FAR
        float TP = 2;
        float TI = 0.08f;
        float TD = 3;
        
        // Translation PID - NEAR
        float TP2 = 2f;
        float TI2 = 0.08f;
        float TD2 = 2;

        // Rotation PID Values
        float RP = 5;
        float RI = 0.2f;
        float RD = 2;

        float MaxCruiseSpeed = 98;
        float MaxCombatSpeed = 98;


        // Helpers

        void GetParts()
        {
            controller = null;

            thrustersList.Clear();
            for (int i = 0; i < thrusts.Length; i++)
                thrusts[i] = 0;

            thrusterManager.Clear();

            Context.Terminal.GetBlocksOfType<IMyTerminalBlock>(null, CollectParts);

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

        bool CollectParts(IMyTerminalBlock block)
        {
            if (Context.Reference.CubeGrid.EntityId != block.CubeGrid.EntityId) return false;

            if (block is IMyShipController && ((IMyShipController)block).CanControlShip)
                controller = (IMyShipController)block;

            if (block is IMyThrust)
            {
                IMyThrust thruster = (IMyThrust)block;
                thrustersList.Add(thruster);
                thruster.ThrustOverride = 0;
                thrusterManager.AddThruster(thruster);
            }

            if (block is IMyGyro)
            {
                IMyGyro gyro = (IMyGyro)block;
                gyros.Add(gyro);
                gyro.Pitch = 0;
                gyro.Yaw = 0;
                gyro.Roll = 0;
                gyro.GyroOverride = false;
            }

            return false;
        }

        // [Autopilot]
        // TP = 1
        // TI = 0.15
        // TD = 5
        // TP2 = 0.2
        // TI2 = 0.08
        // TD2 = 2
        // RP = 5
        // RI = 0.2
        // RD = 2
        // MaxCruiseSpeed = 98
        // MaxCombatSpeed = 98
        void ParseConfigs()
        {
            MyIni Parser = new MyIni();
            MyIniParseResult result;
            if (!Parser.TryParse(Context.Reference.CustomData, out result))
                return;

            var flo = Parser.Get("Autopilot", "TP").ToDecimal();
            if (flo != 0) TP = (float)flo;

            flo = Parser.Get("Autopilot", "TI").ToDecimal();
            if (flo != 0) TI = (float)flo;

            flo = Parser.Get("Autopilot", "TD").ToDecimal();
            if (flo != 0) TD = (float)flo;

            flo = Parser.Get("Autopilot", "TP2").ToDecimal();
            if (flo != 0) TP2 = (float)flo;

            flo = Parser.Get("Autopilot", "TI2").ToDecimal();
            if (flo != 0) TI2 = (float)flo;

            flo = Parser.Get("Autopilot", "TD2").ToDecimal();
            if (flo != 0) TD2 = (float)flo;

            flo = Parser.Get("Autopilot", "RP").ToDecimal();
            if (flo != 0) RP = (float)flo;

            flo = Parser.Get("Autopilot", "RI").ToDecimal();
            if (flo != 0) RI = (float)flo;

            flo = Parser.Get("Autopilot", "RD").ToDecimal();
            if (flo != 0) RD = (float)flo;
            
            flo = Parser.Get("Autopilot", "MaxCruiseSpeed").ToDecimal();
            if (flo != 0) MaxCruiseSpeed = (float)flo;

            flo = Parser.Get("Autopilot", "MaxCombatSpeed").ToDecimal();
            if (flo != 0) MaxCombatSpeed = (float)flo;
        }

        void SetThrusterPowers()
        {
            if (reference == null) return;
            Vector3D AutopilotMoveIndicator = Vector3.Zero;
            if (targetPosition != Vector3D.Zero || targetDrift != Vector3D.Zero) 
                GetMovementVectors(targetPosition, controller, reference, thrusts[0], currentMaxSpeed, out AutopilotMoveIndicator, ref DTranslate, ref ITranslate);
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

            thrusterManager.SmartSetThrust(gridDirIndicator);
        }

        void SetGyroPowers(bool fake = false)
        {
            if (fake) return;
            if (reference == null) return;
            double yawAngle = 0, pitchAngle = 0, spinAngle = 0;
            if (targetDirection != Vector3.Zero || targetUp != Vector3.Zero)
            {
                if (targetDirection != Vector3.Zero) 
                    GetRotationAngles(targetDirection, reference.WorldMatrix.Forward, reference.WorldMatrix.Left, reference.WorldMatrix.Up, out yawAngle, out pitchAngle);
                if (targetUp != Vector3.Zero)
                {
                    var projectedTargetUp = targetUp - reference.WorldMatrix.Forward.Dot(targetUp) * reference.WorldMatrix.Forward;
                    spinAngle = -1 * VectorHelpers.VectorAngleBetween(reference.WorldMatrix.Up, projectedTargetUp) * Math.Sign(reference.WorldMatrix.Left.Dot(targetUp));
                }

                if (DYaw == 0) 
                    DYaw = yawAngle;
                if (DPitch == 0) 
                    DPitch = pitchAngle;

                IYaw += yawAngle * kRunEveryXUpdates;
                IPitch += pitchAngle * kRunEveryXUpdates;

                // no point in trying to spin over 180 deg in a direction.
                IYaw = MathHelper.Clamp(IYaw, -1.0, 1.0);
                IPitch = MathHelper.Clamp(IPitch, -1.0, 1.0);

                ApplyGyroOverride(pitchAngle * RP + (pitchAngle - DPitch) * RD * kInverseTimeStep + IPitch * RI, yawAngle * RP + (yawAngle - DYaw) * RD * kInverseTimeStep + IYaw * RI, spinAngle * RP, gyros, reference);
            }


            if (!Persist && Math.Abs(yawAngle) < 0.01f && Math.Abs(pitchAngle) < 0.01f && Math.Abs(spinAngle) < 0.01f)
            {
                targetDirection = Vector3.Zero;
                targetUp = Vector3.Zero;
            }

            if (targetDirection == Vector3.Zero && targetUp == Vector3.Zero) ClearGyros();
        }

        Vector3D ParseGPS(string s)
        {
            var split = s.Split(':');
            return new Vector3(float.Parse(split[2]), float.Parse(split[3]), float.Parse(split[4]));
        }

        void ClearGyros()
        {
            foreach (var gyro in gyros)
            {
                gyro.Pitch = 0;
                gyro.Yaw = 0;
                gyro.Roll = 0;
                gyro.GyroOverride = false;
            }
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

        void ApplyGyroOverride(double pitch_speed, double yaw_speed, double roll_speed, List<IMyGyro> gyro_list, IMyTerminalBlock reference)
        {
            if (reference == null) 
                return;
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

        void GetMovementVectors(Vector3D target, IMyShipController controller, IMyTerminalBlock reference, float maxThrust, float maxSpeed, out Vector3D AutopilotMoveIndicator, ref Vector3D D, ref Vector3D I)
        {
            if (controller == null)
            {
                AutopilotMoveIndicator = Vector3D.Zero;
                return;
            }
            var speed = (float)(controller.GetShipVelocities().LinearVelocity - targetDrift).Length();
            Vector3D currentVelocity = controller.GetShipVelocities().LinearVelocity;
            float aMax = 0.8f * maxThrust / controller.CalculateShipMass().PhysicalMass;
            Vector3D desiredVelocity = targetDrift;
            Vector3D posError = target - reference.WorldMatrix.Translation;
            var distance = (float)posError.Length();
            posError.Normalize();

            if (target != Vector3D.Zero)
            {
                float desiredSpeed = Math.Min((float)Math.Sqrt(2f * aMax * distance) * 0.01f * (100 - (float)targetDrift.Length()), maxSpeed);
                desiredSpeed = Math.Min(distance * distance * 2f, desiredSpeed);
                desiredVelocity += posError * desiredSpeed;
            }

            Vector3D adjustVector = currentVelocity - targetDrift - VectorHelpers.VectorProjection(currentVelocity - targetDrift, desiredVelocity - targetDrift);
            if (adjustVector.Length() < (currentVelocity - targetDrift).Length() * 0.1) 
                adjustVector = Vector3.Zero;

            Vector3D Error = (desiredVelocity - currentVelocity - adjustVector * 1) * 60 / (aMax * kRunEveryXUpdates);

            float kP = TP;
            float kI = TI;
            float kD = TD;

            if (Error.LengthSquared() > 1) 
                Error.Normalize();
            else
            {
                kP = TP2;
                kI = TI2;
                kD = TD2;
            }

            if (D == Vector3.Zero) 
                D = Error;

            AutopilotMoveIndicator = kP * Error + kD * (Error - D) * kInverseTimeStep + kI * I;

            // decrease speed when close and slow
            if (distance < 10 && speed < 10) 
                AutopilotMoveIndicator *= 0.5f;

            if (AutopilotMoveIndicator.Length() > 1) 
                AutopilotMoveIndicator /= AutopilotMoveIndicator.Length();

            I += Error * kRunEveryXUpdates;
            if (I.Length() > 5) 
                I *= 5 / I.Length();
            D = Error;

            // if close enough, stop.
            if (targetDrift == Vector3D.Zero && distance < 0.25f && speed < 0.25f)
            {
                targetPosition = Vector3.Zero;
                AutopilotMoveIndicator = Vector3.Zero;
                I = Vector3.Zero;
                D = Vector3.Zero;
            }
        }
    }
}
