using Sandbox.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using System.Text;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRageMath;
using System.Linq;

namespace IngameScript
{
    public class HeliDrive
    {
        public string mode;
        public float maxFlightPitch;
        public float maxFlightRoll;

        public float maxLandingPitch;
        public float maxLandingRoll;

        public IMyShipController controller;
        public HeliGyroController gyroController;
        public HeliThrusterController thrustController;

        public bool enableLateralOverride;
        public bool autoStop;

        public float pitch;
        public float roll;

        int runs = 0;

        // Interface variables
        public Vector3 MoveIndicators = Vector3D.Zero;
        public Vector3 RotationIndicators = Vector3D.Zero;


        public void Setup(IMyShipController shipController)
        {
            controller = shipController;
            gyroController = new HeliGyroController(controller);
            thrustController = new HeliThrusterController(controller);

            maxFlightPitch = 60;
            maxFlightRoll = 60;
        }

        public void SwitchToMode(string mode)
        {
            if (!IsValidMode(mode)) return;
            switch (mode)
            {
                case "flight":
                    gyroController.SetEnabled(true);
                    thrustController.SetEnabled(true);
                    gyroController.SetOverride(true);
                    break;
                case "landing":
                    gyroController.SetEnabled(true);
                    thrustController.SetEnabled(true);
                    gyroController.SetOverride(true);
                    controller.DampenersOverride = true;
                    break;
                case "manual":
                    gyroController.SetEnabled(true);
                    thrustController.SetEnabled(true);
                    gyroController.SetOverride(true);
                    break;
                case "shutdown":
                    gyroController.SetEnabled(false);
                    thrustController.SetEnabled(false);
                    break;
                case "standby":
                    gyroController.SetEnabled(true);
                    thrustController.SetEnabled(true);
                    gyroController.SetOverride(false);
                    thrustController.SetYAxisThrust(0);
                    break;
            }
            this.mode = mode;
            enableLateralOverride = false;
            autoStop = true;
        }

        bool IsValidMode(string mode)
        {
            return mode == "flight" || mode == "landing" || mode == "manual" || mode == "shutdown" || mode == "standby";
        }

        public bool IsEqual(float value1, float value2, float epsilon = 0.0001f)
        {
            return Math.Abs(gyroController.NotNaN(value1 - value2)) <= epsilon;
        }

        public void Drive()
        {
            var dampeningRotation = gyroController.CalculatePitchRollToAchiveVelocity(Vector3.Zero);

            switch (mode)
            {
                case "flight":
                    {
                        pitch = MoveIndicators.Z * maxFlightPitch * (float)Math.PI / 180;
                        roll = MoveIndicators.X * maxFlightRoll * (float)Math.PI / 180;
                        dampeningRotation = Vector2.Min(dampeningRotation, new Vector2(maxFlightRoll, maxFlightPitch) * (float)Math.PI / 180);

                        if ((autoStop || enableLateralOverride) && IsEqual(0, roll)) roll = gyroController.MinAbs(dampeningRotation.X, maxFlightRoll * (float)Math.PI / 180);
                        if (autoStop && IsEqual(0, pitch)) pitch = gyroController.MinAbs(dampeningRotation.Y, maxFlightPitch * (float)Math.PI / 180);

                        gyroController.SetAngularVelocity(gyroController.CalculateVelocityToAlign(pitch, roll) + RotationIndicators);
                        thrustController.SetYAxisThrust(MoveIndicators.Y != 0 ? 0 : thrustController.CalculateThrustToHover());
                        break;
                    }
                case "landing":
                    {
                        pitch = MoveIndicators.Z * maxLandingPitch * (float)Math.PI / 180;
                        roll = MoveIndicators.X * maxLandingRoll * (float)Math.PI / 180;
                        dampeningRotation = Vector2.Min(dampeningRotation, new Vector2(maxLandingRoll, maxLandingPitch) * (float)Math.PI / 180);

                        if ((autoStop || enableLateralOverride) && IsEqual(0, roll)) roll = gyroController.MinAbs(dampeningRotation.X, maxLandingRoll);
                        if (autoStop && IsEqual(0, pitch)) pitch = gyroController.MinAbs(dampeningRotation.Y, maxLandingPitch);

                        gyroController.SetAngularVelocity(gyroController.CalculateVelocityToAlign(pitch, roll) + RotationIndicators);
                        thrustController.SetYAxisThrust(MoveIndicators.Y != 0 ? 0 : thrustController.CalculateThrustToHover());
                        break;
                    }
                case "manual":
                    gyroController.SetAngularVelocity(RotationIndicators); thrustController.SetYAxisThrust(MoveIndicators.Y != 0 ? 0 : thrustController.CalculateThrustToHover());
                    break;
                case "shutdown":
                    break;
                case "standby":
                    break;
            }
        }
    }


    //The GyroController module is based on Flight Assist's GyroController and HoverModule, sharing code in places.
    public class HeliGyroController
    {
        public float MinAbs(float value1, float value2)
        {
            return Math.Min(Math.Abs(value1), Math.Abs(value2)) * (value1 < 0 ? -1 : 1);
        }

        public float NotNaN(float value)
        {
            return float.IsNaN(value) ? 0 : value;
        }

        const float dampeningFactor = 25.0f;

        private IMyShipController controller;
        private List<IMyGyro> gyroscopes;

        public HeliGyroController(IMyShipController controller)
        {
            this.controller = controller;
            this.gyroscopes = new List<IMyGyro>();
        }

        public void Update(IMyShipController controller, List<IMyGyro> gyroscopes)
        {
            SetController(controller);
            AddGyroscopes(gyroscopes);
        }

        public void AddGyroscopes(List<IMyGyro> gyroscopes)
        {
            this.gyroscopes.AddList(gyroscopes);
            this.gyroscopes = this.gyroscopes.Distinct().ToList();
        }

        public void SetController(IMyShipController controller)
        {
            this.controller = controller;
        }

        public void SetEnabled(bool setEnabled)
        {
            foreach (var gyroscope in gyroscopes)
            {
                gyroscope.Enabled = setEnabled;
            }
        }

        public void SetOverride(bool setOverride)
        {
            foreach (var gyroscope in gyroscopes)
            {
                gyroscope.GyroOverride = setOverride;
            }
        }

        public Vector2 CalculatePitchRollToAchiveVelocity(Vector3 targetVelocity)
        {
            Vector3 diffrence = Vector3.Normalize(controller.GetShipVelocities().LinearVelocity - targetVelocity);
            Vector3 gravity = -Vector3.Normalize(controller.GetNaturalGravity());
            float velocity = (float)controller.GetShipSpeed();
            float proportionalModifier = (float)Math.Pow(Math.Abs(diffrence.Length()), 2);

            float pitch = NotNaN(Vector3.Dot(diffrence, Vector3.Cross(gravity, controller.WorldMatrix.Right)) * velocity) * proportionalModifier / dampeningFactor;
            float roll = NotNaN(Vector3.Dot(diffrence, Vector3.Cross(gravity, controller.WorldMatrix.Forward)) * velocity) * proportionalModifier / dampeningFactor;

            pitch = MinAbs(pitch, 90.0f * (float)Math.PI / 180);
            roll = MinAbs(roll, 90.0f * (float)Math.PI / 180);

            return new Vector2(roll, pitch);
        }

        public Vector3 CalculateVelocityToAlign(float offsetPitch = 0.0f, float offsetRoll = 0.0f)
        {
            var gravity = -Vector3.Normalize(Vector3.TransformNormal(controller.GetNaturalGravity(), Matrix.Transpose(controller.WorldMatrix)));
            var target = Vector3.Normalize(Vector3.Transform(gravity, Matrix.CreateFromAxisAngle(Vector3.Right, offsetPitch) * Matrix.CreateFromAxisAngle(Vector3.Forward, offsetRoll)));

            var pitch = Vector3.Dot(Vector3.Forward, target);
            var roll = Vector3.Dot(Vector3.Right, target);

            return new Vector3(pitch, 0, roll);
        }

        public void SetAngularVelocity(Vector3 velocity)
        {
            var cockpitLocalVelocity = Vector3.TransformNormal(velocity, controller.WorldMatrix);
            foreach (var gyro in gyroscopes)
            {
                var gyroLocalVelocity = Vector3.TransformNormal(cockpitLocalVelocity, Matrix.Transpose(gyro.WorldMatrix));

                gyro.Pitch = gyroLocalVelocity.X;
                gyro.Yaw = gyroLocalVelocity.Y;
                gyro.Roll = gyroLocalVelocity.Z;
            }
        }
    }
    public class HeliThrusterController
    {
        public IMyShipController controller;
        public List<IMyThrust> allThrusters;
        public List<IMyThrust> upThrusters, downThrusters, leftThrusters, rightThrusters, forwardThrusters, backwardThrusters;

        public HeliThrusterController(IMyShipController controller)
        {
            upThrusters = new List<IMyThrust>();
            downThrusters = new List<IMyThrust>();
            leftThrusters = new List<IMyThrust>();
            rightThrusters = new List<IMyThrust>();
            forwardThrusters = new List<IMyThrust>();
            backwardThrusters = new List<IMyThrust>();

            Update(controller, new List<IMyThrust>());
        }

        public void Update(IMyShipController controller, List<IMyThrust> thrusters)
        {
            this.controller = controller;
            this.allThrusters = thrusters.Distinct().ToList();

            foreach (var thruster in thrusters)
            {
                if (thruster.GridThrustDirection.Z < 0) forwardThrusters.Add(thruster);
                if (thruster.GridThrustDirection.Z > 0) backwardThrusters.Add(thruster);
                if (thruster.GridThrustDirection.Y < 0) upThrusters.Add(thruster);
                if (thruster.GridThrustDirection.Y > 0) downThrusters.Add(thruster);
                if (thruster.GridThrustDirection.X < 0) leftThrusters.Add(thruster);
                if (thruster.GridThrustDirection.X > 0) rightThrusters.Add(thruster);

                thruster.ThrustOverride = 0;
            }

            forwardThrusters = forwardThrusters.Distinct().ToList();
            backwardThrusters = backwardThrusters.Distinct().ToList();
            upThrusters = upThrusters.Distinct().ToList();
            downThrusters = downThrusters.Distinct().ToList();
            leftThrusters = leftThrusters.Distinct().ToList();
            rightThrusters = rightThrusters.Distinct().ToList();
        }

        public void SetEnabled(bool enabled)
        {
            foreach (var thruster in allThrusters)
            {
                thruster.Enabled = enabled;
            }
        }

        public float SetZAxisThrust(float thrust)
        {
            return setAxisThrust(thrust, ref forwardThrusters, ref backwardThrusters);
        }

        public float SetYAxisThrust(float thrust)
        {
            return setAxisThrust(thrust, ref upThrusters, ref downThrusters);
        }

        public float SetXAxisThrust(float thrust)
        {
            return setAxisThrust(thrust, ref leftThrusters, ref rightThrusters);
        }

        public float CalculateMaxEffectiveForwardThrust()
        {
            return calculateMaxAxisThrust(ref forwardThrusters);
        }

        public float CalculateMaxEffectiveBackwardThrust()
        {
            return calculateMaxAxisThrust(ref backwardThrusters);
        }

        public float CalculateMaxEffectiveLeftThrust()
        {
            return calculateMaxAxisThrust(ref leftThrusters);
        }

        public float CalculateMaxEffectiveRightThrust()
        {
            return calculateMaxAxisThrust(ref rightThrusters);
        }

        public float CalculateMaxEffectiveUpThrust()
        {
            return calculateMaxAxisThrust(ref upThrusters);
        }

        public float CalculateMaxEffectiveDownThrust()
        {
            return calculateMaxAxisThrust(ref downThrusters);
        }

        public float CalculateThrustToHover()
        {
            var gravityDir = controller.GetNaturalGravity();
            var weight = controller.CalculateShipMass().TotalMass * gravityDir.Length();
            var velocity = controller.GetShipVelocities().LinearVelocity;

            gravityDir.Normalize();
            var gravityMatrix = Matrix.Invert(Matrix.CreateFromDir(gravityDir));
            velocity = Vector3D.Transform(velocity, gravityMatrix);


            if (Vector3.Transform(controller.WorldMatrix.GetOrientation().Down, gravityMatrix).Z < 0)
                return (float)(weight + weight * -velocity.Z);
            else
                return -(float)(weight + weight * -velocity.Z);
        }

        private float calculateMaxAxisThrust(ref List<IMyThrust> thrusters)
        {
            float thrust = 0;
            foreach (var thruster in thrusters)
            {
                thrust += thruster.MaxEffectiveThrust;
            }
            return thrust;
        }

        private float calculateEffectiveThustRatio(IMyThrust thruster)
        {
            return thruster.MaxThrust / thruster.MaxEffectiveThrust;
        }

        private float setAxisThrust(float thrust, ref List<IMyThrust> thrustersPos, ref List<IMyThrust> thrustersNeg)
        {
            List<IMyThrust> thrusters, backThrusters;

            if (thrust >= 0)
            {
                thrusters = thrustersPos;
                backThrusters = thrustersNeg;
            }
            else
            {
                thrusters = thrustersNeg;
                backThrusters = thrustersPos;
            }

            thrust = Math.Abs(thrust);

            foreach (var thruster in backThrusters)
            {
                thruster.ThrustOverride = 0.0f;
            }

            foreach (var thruster in thrusters)
            {
                //TODO: replace with smart thruster thrust allocation code.
                var localThrust = (thrust / thrusters.Count) * calculateEffectiveThustRatio(thruster);
                thruster.ThrustOverride = (float.IsNaN(localThrust) || float.IsInfinity(localThrust)) ? 0 : localThrust;
            }
            return 0.0f;
        }
    }
}
