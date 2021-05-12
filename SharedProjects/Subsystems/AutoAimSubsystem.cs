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

    public class AutoAimSubsystem : ISubsystem
    {
        #region ISubsystem
        public UpdateFrequency UpdateFrequency => UpdateFrequency.Update1;

        public void Command(TimeSpan timestamp, string command, object argument)
        {
        }

        public void DeserializeSubsystem(string serialized)
        {
        }

        public string GetStatus()
        {
            return string.Empty;
        }

        public string SerializeSubsystem()
        {
            return string.Empty;
        }

        public void Setup(ExecutionContext context, string name)
        {
            Context = context;

            GetParts();
        }

        public void Update(TimeSpan timestamp, UpdateFrequency updateFlags)
        {
            if (TargetKey.Item2 != 0) AimAtTarget(timestamp);
        }
        #endregion

        ExecutionContext Context;

        List<IMyGyro> Gyros = new List<IMyGyro>();
        IMyShipController Controller;

        IIntelProvider IntelProvider;

        MyTuple<IntelItemType, long> TargetKey;

        public Vector3D AttackPoint;

        PID yawPID = new PID(5, 0.2, 2, 0.95, 20);
        PID pitchPID = new PID(5, 0.2, 2, 0.95, 20);
        PID spinPID = new PID(5, 0.2, 2, 0.95, 20);

        public void SetTarget(long targetID)
        {
            TargetKey = MyTuple.Create(IntelItemType.Enemy, targetID);
        }

        public AutoAimSubsystem(IIntelProvider intelProvider)
        {
            IntelProvider = intelProvider;
        }

        void GetParts()
        {
            Context.Terminal.GetBlocksOfType<IMyTerminalBlock>(null, CollectParts);
        }

        void AimAtTarget(TimeSpan localTime)
        {
            var intelItems = IntelProvider.GetFleetIntelligences(localTime);
            if (!intelItems.ContainsKey(TargetKey))
            {
                SetTarget(0);
                ClearGyros();
            }
            else
            {
                // Update Attack Point

                // Compute errors and update PID

                // Set gyros to rorate 
            }

        }

        bool CollectParts(IMyTerminalBlock block)
        {
            if (ProgramReference.CubeGrid.EntityId != block.CubeGrid.EntityId) return false;

            if (block is IMyShipController && ((IMyShipController)block).CanControlShip) Controller = (IMyShipController)block;
            if (block is IMyGyro) Gyros.Add((IMyGyro)block);

            return false;
        }

        void ClearGyros()
        {
            foreach (var gyro in Gyros)
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

        void ApplyGyroOverride(double pitch_speed, double yaw_speed, double roll_speed, List<IMyGyro> gyro_list, IMyTerminalBlock reference)
        {
            if (reference == null) return;
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
