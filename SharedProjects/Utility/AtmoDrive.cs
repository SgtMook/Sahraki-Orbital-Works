using Sandbox.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using System.Text;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRageMath;

namespace IngameScript
{
    public class AtmoDrive : ISubsystem, IAutopilot
    {
        float MaxAngleDegrees = 30;
        float MaxAngleTolerance = 1.1f;

        // Arguments
        public Vector3D AimTarget = Vector3D.Zero;
        public Vector3D Destination = Vector3D.Zero;

        public StringBuilder StatusBuilder = new StringBuilder();

        // Parts list
        List<IMyGyro> Gyros = new List<IMyGyro>();
        Dictionary<Base6Directions.Direction, List<IMyThrust>> Thrusters = new Dictionary<Base6Directions.Direction, List<IMyThrust>>();
        Dictionary<Base6Directions.Direction, SmartThrustManager> ThrusterManagers = new Dictionary<Base6Directions.Direction, SmartThrustManager>();

        public bool AddComponent(IMyTerminalBlock block)
        {
            // TODO: Add block to whatever stores
            if (block is IMyGyro) Gyros.Add((IMyGyro)block);
            if (block is IMyThrust)
            {
                var thruster = (IMyThrust)block;
                var relativeDirection = Controller.WorldMatrix.GetClosestDirection(thruster.WorldMatrix.Forward);
                if (!Thrusters.ContainsKey(relativeDirection)) Thrusters[relativeDirection] = new List<IMyThrust>();
                Thrusters[relativeDirection].Add(thruster);
            }

            return false;
        }

        public Vector3D ParseGPS(string s)
        {
            var split = s.Split(':');
            return new Vector3(float.Parse(split[2]), float.Parse(split[3]), float.Parse(split[4]));
        }

        public void SetDest(string s)
        {
            Destination = ParseGPS(s);
        }

        float MaxLiftThrust;
        float MaxDownThrust;
        float MaxLateralThrust;

        Vector3D gravDir;
        double gravStr;
        float shipMass;

        PID YawPID;
        PID PitchPID;
        PID SpinPID;

        float TP = 100;
        float TI = 0.00f;
        float TD = 50;

        PID XPID;
        PID YPID;
        PID ZPID;

        public UpdateFrequency UpdateFrequency => UpdateFrequency.Update1;

        public IMyShipController Controller {get; set;}

        IMyTerminalBlock reference;

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
                    reference = Controller;
            }
        }

        public float CruiseSpeed { get; set; }

        public float CombatSpeed { get; set; }

        float MaxSpeed = 98;

        int runs = 0;

        public void Setup(MyGridProgram program, string name, IMyTerminalBlock reference = null)
        {
            program.GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(null, AddComponent);
            // Set MaxDownThrust and MaxLateralThrust accordingly
            foreach (var kvp in Thrusters)
            {
                if (kvp.Key == Base6Directions.Direction.Down) foreach (var thruster in kvp.Value) MaxLiftThrust += thruster.MaxThrust;
                else if (kvp.Key == Base6Directions.Direction.Forward) foreach (var thruster in kvp.Value) MaxLateralThrust += thruster.MaxThrust;
                else if (kvp.Key == Base6Directions.Direction.Up) foreach (var thruster in kvp.Value) MaxDownThrust += thruster.MaxThrust;

                ThrusterManagers[kvp.Key] = new SmartThrustManager(kvp.Value);
            }
        }

        public void Update(TimeSpan timestamp, UpdateFrequency updateFlags)
        {
            runs++;

            if (runs % 5 == 0)
            {
                StatusBuilder.Clear();

                shipMass = Controller.CalculateShipMass().PhysicalMass;
                gravDir = Controller.GetNaturalGravity();

                double yawAngle, pitchAngle, spinAngle;
                yawAngle = pitchAngle = spinAngle = 0;
                MatrixD orientationMatrix = new MatrixD();

                var flatCurrentDir = Controller.WorldMatrix.Forward - VectorHelpers.VectorProjection(Controller.WorldMatrix.Forward, gravDir);
                flatCurrentDir.Normalize();

                var flatLeftDir = Vector3D.Cross(flatCurrentDir, gravDir);

                if (gravDir != Vector3D.Zero)
                {
                    gravStr = gravDir.Length();
                    gravDir.Normalize();

                    // Rotational Control
                    var targetDir = Vector3D.Zero;
                    if (AimTarget != Vector3D.Zero)
                    {
                        targetDir = AimTarget - Controller.WorldMatrix.Translation;
                    }
                    else
                    {
                        targetDir = Controller.WorldMatrix.Forward - VectorHelpers.VectorProjection(Controller.WorldMatrix.Forward, gravDir);
                    }
                    var angleFromVertical = VectorHelpers.VectorAngleBetween(targetDir, gravDir) - Math.PI * 0.5;
                    var maxAngleFromVertical = GetMaxAngleConstraint();
                    angleFromVertical = Math.Max(Math.Min(angleFromVertical, maxAngleFromVertical), -maxAngleFromVertical);
                    var flatAimDir = targetDir - VectorHelpers.VectorProjection(targetDir, gravDir);
                    flatAimDir.Normalize();

                    var downDir = TrigHelpers.FastCos(angleFromVertical) * gravDir + TrigHelpers.FastSin(angleFromVertical) * flatAimDir;

                    orientationMatrix.Forward = Controller.WorldMatrix.Down;
                    orientationMatrix.Left = Controller.WorldMatrix.Left;
                    orientationMatrix.Up = Controller.WorldMatrix.Forward;

                    spinAngle = -VectorHelpers.VectorAngleBetween(flatAimDir, flatCurrentDir) * Math.Sign(Controller.WorldMatrix.Left.Dot(flatAimDir));
                    TrigHelpers.GetRotationAngles(downDir, orientationMatrix.Forward, orientationMatrix.Left, orientationMatrix.Up, out yawAngle, out pitchAngle);
                }
                else
                {
                    // TODO: Add extragrav rotational control here
                }

                TrigHelpers.ApplyGyroOverride(PitchPID.Control(pitchAngle), YawPID.Control(yawAngle), SpinPID.Control(spinAngle), Gyros, orientationMatrix);

                // Translational Control

                if (Destination == Vector3D.Zero)
                {
                    if (Controller.DampenersOverride == false)
                    {
                        foreach (var kvp in ThrusterManagers)
                            kvp.Value.SetThrust(0);

                        Controller.DampenersOverride = true;
                    }
                }
                else
                {
                    Controller.DampenersOverride = false;

                    // Compute direction of motion
                    var destinationDir = Destination - Reference.GetPosition();
                    var destinationDist = destinationDir.Length();
                    destinationDir.Normalize();

                    // Compute current motion to find desired acceleration
                    var currentVel = Controller.GetShipVelocities().LinearVelocity;

                    if (destinationDist < 0.25 && currentVel.Length() < 0.25)
                    {
                        foreach (var kvp in ThrusterManagers)
                            kvp.Value.SetThrust(0);
                        Controller.DampenersOverride = true;
                        Destination = Vector3D.Zero;
                    }
                    else
                    {
                        var maxSpeed = GetMaxSpeedFromBrakingDistance(destinationDist, GetMaxAccelFromAngleDeviation((float)GetMaxAngleConstraint() * MaxAngleTolerance));
                        var desiredVel = destinationDir * maxSpeed * 0.9;
                        var adjustVector = currentVel - VectorHelpers.VectorProjection(currentVel, desiredVel);
                        var desiredAccel = desiredVel - currentVel - adjustVector * 2;

                        // Transform desired acceleration into remote control frame
                        var gridDesiredAccel = Vector3D.TransformNormal(desiredAccel, MatrixD.Transpose(Controller.WorldMatrix));
                        StatusBuilder.AppendLine(gridDesiredAccel.ToString());

                        double MinScale = 10;

                        if (gridDesiredAccel.LengthSquared() < 0.5)
                            gridDesiredAccel *= 0.1 * gridDesiredAccel.LengthSquared();

                        gridDesiredAccel.X = XPID.Control(gridDesiredAccel.X);
                        gridDesiredAccel.Y = YPID.Control(gridDesiredAccel.Y);
                        gridDesiredAccel.Z = ZPID.Control(gridDesiredAccel.Z);

                        var gridGravDir = Vector3D.TransformNormal(gravDir, MatrixD.Transpose(Controller.WorldMatrix));
                        foreach (var kvp in ThrusterManagers)
                        {
                            var desiredDirectionalThrust = -1 * Base6Directions.GetVector(kvp.Key).Dot(gridDesiredAccel) * shipMass;
                            var gravAssist = Base6Directions.GetVector(kvp.Key).Dot(gridGravDir) * shipMass;
                            if (desiredDirectionalThrust > 0)
                                MinScale = Math.Min((kvp.Value.MaxThrust - gravAssist) / desiredDirectionalThrust, MinScale);
                        }

                        gridDesiredAccel *= MinScale;
                        gridDesiredAccel -= gridGravDir * gravStr;

                        StatusBuilder.AppendLine(gridDesiredAccel.ToString());
                        StatusBuilder.AppendLine(MinScale.ToString());
                        StatusBuilder.AppendLine(destinationDist.ToString());
                        StatusBuilder.AppendLine(maxSpeed.ToString());

                        foreach (var kvp in ThrusterManagers)
                            kvp.Value.SetThrust(-1 * Base6Directions.GetVector(kvp.Key).Dot(gridDesiredAccel + gridGravDir) * shipMass);
                    }
                }
            }
        }

        public void Command(TimeSpan timestamp, string command, object argument)
        {
            if (command == "move") Move(ParseGPS((string)argument));
            if (command == "turn") Turn(ParseGPS((string)argument) - Reference.WorldMatrix.Translation);
            if (command == "spin") Spin(ParseGPS((string)argument) - Reference.WorldMatrix.Translation);
        }

        public string GetStatus()
        {
            return StatusBuilder.ToString();
        }

        public string SerializeSubsystem()
        {
            return "";
        }

        public void DeserializeSubsystem(string serialized)
        {
        }

        void ParseConfigs()
        {
            MyIni Parser = new MyIni();
            MyIniParseResult result;
            if (!Parser.TryParse(Controller.CustomData, out result))
                return;

            var flo = Parser.Get("Autopilot", "TP").ToDecimal();
            if (flo != 0) TP = (float)flo;

            flo = Parser.Get("Autopilot", "TI").ToDecimal();
            if (flo != 0) TI = (float)flo;

            flo = Parser.Get("Autopilot", "TD").ToDecimal();
            if (flo != 0) TD = (float)flo;

            flo = Parser.Get("Autopilot", "MaxCruiseSpeed").ToDecimal();
            if (flo != 0) CruiseSpeed = (float)flo;

            flo = Parser.Get("Autopilot", "MaxCombatSpeed").ToDecimal();
            if (flo != 0) CombatSpeed = (float)flo;
        }

        public AtmoDrive(IMyRemoteControl remoteControl, double TimeStep = 5)
        {
            Controller = remoteControl;
            Reference = Controller;

            YawPID = new PID(5, 0.001, 2, 0.5, TimeStep);
            PitchPID = new PID(5, 0.001, 2, 0.5, TimeStep);
            SpinPID = new PID(5, 0.001, 2, 0.5, TimeStep);

            ParseConfigs();

            XPID = new PID(TP, TI, TD, 0, TimeStep);
            YPID = new PID(TP, TI, TD, 0, TimeStep);
            ZPID = new PID(TP, TI, TD, 0, TimeStep);
        }

        // Gets the maximum angle deviation from vertical the drone is allowed to operate in
        double GetMaxAngleConstraint()
        {
            return MaxAngleDegrees * Math.PI/180;
        }

        double GetMaxAngleDeviationFromAcceleration(float accel)
        {
            var DownAccel = MaxDownThrust / shipMass;
            var LateralAccel = MaxLateralThrust / shipMass;

            var MaxAngle = Math.PI * 0.5;

            var LateralFactor = (LateralAccel - accel) / gravStr;
            if (LateralFactor < 1)
                MaxAngle = TrigHelpers.FastAsin(LateralFactor);

            var DownFactor = (accel - MaxDownThrust) / gravStr;
            if (DownFactor < 1)
                MaxAngle = Math.Min(MaxAngle, Math.Acos(DownFactor));

            return MaxAngle;
        }

        double GetMaxAccelFromAngleDeviation(float angle)
        {
            var DownAccel = MaxDownThrust / shipMass;
            var LateralAccel = MaxLateralThrust / shipMass;
            var LiftAccel = MaxLiftThrust / shipMass;
            return Math.Min(LateralAccel - gravStr * TrigHelpers.FastSin(angle), Math.Min(LiftAccel - gravStr * TrigHelpers.FastCos(angle), DownAccel + gravStr * TrigHelpers.FastCos(angle)));
        }

        double GetMaxSpeedFromBrakingDistance(double distance, double maxAccel)
        {
            return Math.Min(Math.Sqrt(2 * distance * maxAccel), distance * distance + 0.5);
        }

        public void Move(Vector3D targetPosition)
        {
            if (targetPosition != Vector3.One)
                Destination = targetPosition;
        }

        public void Turn(Vector3D targetDirection)
        {
            if (targetDirection != Vector3.One)
                AimTarget = targetDirection + Reference.Position;
        }

        public void Spin(Vector3D targetUp)
        {
            // TODO: Implement?
        }

        public void Drift(Vector3D targetDrift)
        {
            // TODO: Implement?
        }

        public void SetMaxSpeed(float maxSpeed)
        {
            MaxSpeed = maxSpeed;
        }

        public bool AtWaypoint(Waypoint w)
        {
            StatusBuilder.AppendLine((Controller == null).ToString());
            StatusBuilder.AppendLine((w == null).ToString());
            StatusBuilder.AppendLine((Reference == null).ToString());
            if (w.Position != Vector3.One && w.Position != Vector3.Zero)
            {
                var speed = (float)(Controller.GetShipVelocities().LinearVelocity - w.Velocity).Length();
                Vector3D posError = Destination - Reference.WorldMatrix.Translation;
                var distance = (float)posError.Length();
                if (distance > 0.25f || speed > 0.25f)
                    return false;
            }
            if (w.Direction != Vector3.One && w.Direction != Vector3.Zero)
            {
                if (VectorHelpers.VectorAngleBetween(Reference.WorldMatrix.Forward, AimTarget - Reference.GetPosition()) > 0.03f)
                    return false;
            }

            return true;
        }

        public void Clear()
        {
            Reference = Controller;
            AimTarget = Vector3D.Zero;
            Destination = Vector3D.Zero;
        }

        public float GetBrakingDistance()
        {
            var speed = (float)Controller.GetShipVelocities().LinearVelocity.Length();
            float aMax = (float)GetMaxAccelFromAngleDeviation((float)GetMaxAngleConstraint() * MaxAngleTolerance);
            float decelTime = speed / aMax;
            return speed * decelTime - 0.5f * aMax * decelTime * decelTime;
        }

        public float GetMaxSpeedFromBrakingDistance(float distance)
        {
            return (float)GetMaxSpeedFromBrakingDistance(distance, GetMaxAccelFromAngleDeviation((float)GetMaxAngleConstraint() * MaxAngleTolerance));
        }
    }
}