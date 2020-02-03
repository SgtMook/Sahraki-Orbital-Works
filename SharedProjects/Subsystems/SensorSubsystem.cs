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

namespace SharedProjects.Subsystems
{
    public class SensorSubsystem : ISubsystem
    {

        #region ISubsystem
        public int UpdateFrequency { get; private set; }

        public void Command(string command, object argument)
        {
        }

        public void DeserializeSubsystem(string serialized)
        {
        }

        public string GetStatus()
        {
            updateBuilder.Clear();

            foreach (IMyCameraBlock camera in secondaryCameras)
            {
                updateBuilder.AppendLine($"{((int)(camera.AvailableScanRange/1000)).ToString()} km");
            }

            updateBuilder.AppendLine(cameraIndex.ToString());
            updateBuilder.AppendLine();
            updateBuilder.AppendLine("Last detected: ");
            if (lastDetectedInfo.IsEmpty())
            {
                updateBuilder.AppendLine("None");
            }
            else
            {
                var distance = ((Vector3D)lastDetectedInfo.HitPosition - controller.WorldMatrix.Translation).Length();
                updateBuilder.AppendLine($"{lastDetectedInfo.Name}");
                updateBuilder.AppendLine($"Range: {(int)distance} m");
                updateBuilder.AppendLine();

                // Get direction
                var worldDirection = lastDetectedInfo.Position - primaryCamera.WorldMatrix.Translation;
                var bodyPosition = Vector3D.TransformNormal(worldDirection, MatrixD.Transpose(primaryCamera.WorldMatrix));
                updateBuilder.AppendLine($"{(int)bodyPosition.X} {(int)bodyPosition.Y} {(int)bodyPosition.Z}");
                updateBuilder.AppendLine($"{Vector3D.Forward}");
            }

            updateBuilder.AppendLine();
            updateBuilder.AppendLine(panelMiddle.TextureSize.ToString());


            return updateBuilder.ToString();
        }

        public string SerializeSubsystem()
        {
            return string.Empty;
        }

        public void Setup(MyGridProgram program, SubsystemManager manager)
        {
            Program = program;
            GetParts();
            UpdateFrequency = 1;
        }

        private const int kScanDistance = 5000000;

        public void Update(TimeSpan timestamp)
        {
            Timestamp = timestamp;
            if (primaryCamera.IsActive)
            {
                // Move swivel
                pitch.TargetVelocityRPM = controller.RotationIndicator[0]*0.3f;
                yaw.TargetVelocityRPM = controller.RotationIndicator[1]*0.3f;

                // Take inputs
                TriggerInputs();

                // Draw HUD
                using (var frame = panelMiddle.DrawFrame())
                {
                    var crosshairs = new MySprite(SpriteType.TEXTURE, "Cross", size: new Vector2(10f, 10f), color: new Color(1, 1, 1, 0.1f));
                    crosshairs.Position = new Vector2(0, -2) + panelMiddle.TextureSize/2f;
                    panelMiddle.ScriptBackgroundColor = new Color(1, 0, 0, 0);
                    frame.Add(crosshairs);

                    if (!lastDetectedInfo.IsEmpty())
                    {
                        frame.Add(DetectedInfoToSprite(lastDetectedInfo, Timestamp - lastDetectedTimestamp));
                    }
                }

                panelLeft.Alignment = TextAlignment.RIGHT;

                foreach (IMyCameraBlock camera in secondaryCameras)
                {
                    camera.EnableRaycast = camera.AvailableScanRange < kScanDistance;
                }

                panelLeft.WriteText(GetStatus());
            }
        }

        #endregion

        IMyShipController controller;

        IMyMotorStator yaw;
        IMyMotorStator pitch;

        MyGridProgram Program;

        IMyTextPanel panelLeft;
        IMyTextPanel panelRight;
        IMyTextPanel panelMiddle;

        List<IMyCameraBlock> secondaryCameras = new List<IMyCameraBlock>();
        IMyCameraBlock primaryCamera;

        StringBuilder updateBuilder = new StringBuilder();

        TimeSpan Timestamp;
        void GetParts()
        {
            secondaryCameras.Clear();
            Program.GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(null, CollectParts);
        }

        private bool CollectParts(IMyTerminalBlock block)
        {
            if (!Program.Me.IsSameConstructAs(block)) return false;

            if (block is IMyShipController)
                controller = (IMyShipController)block;

            if (block is IMyMotorStator)
            {
                var rotor = (IMyMotorStator)block;
                if (rotor.CustomName.StartsWith("[SN-Y]"))
                    yaw = rotor;
                else if (rotor.CustomName.StartsWith("[SN-P]"))
                    pitch = rotor;
            }

            if (block is IMyTextPanel)
            {
                if (block.CustomName.StartsWith("[SN-SM]"))
                    panelMiddle = (IMyTextPanel)block;
                if (block.CustomName.StartsWith("[SN-SL]"))
                    panelLeft = (IMyTextPanel)block;
                if (block.CustomName.StartsWith("[SN-SR]"))
                    panelRight = (IMyTextPanel)block;
            }

            if (block is IMyCameraBlock)
            {
                if (block.CustomName.StartsWith("[SN-C-S]"))
                    secondaryCameras.Add((IMyCameraBlock)block);
                else if (block.CustomName.StartsWith("[SN-C-P]"))
                    primaryCamera = (IMyCameraBlock)block;
            }

            return false;
        }

        #region Inputs
        bool lastADown = false;
        bool lastSDown = false;
        bool lastDDown = false;
        bool lastWDown = false;
        bool lastQDown = false;
        bool lastEDown = false;
        bool lastCDown = false;
        bool lastSpaceDown = false;

        private void TriggerInputs()
        {
            if (controller == null) return;
            var inputVecs = controller.MoveIndicator;
            var inputRoll = controller.RollIndicator;
            if (!lastADown && inputVecs.X < 0) DoA();
            lastADown = inputVecs.X < 0;
            if (!lastSDown && inputVecs.Z > 0) DoS();
            lastSDown = inputVecs.Z > 0;
            if (!lastDDown && inputVecs.X > 0) DoD();
            lastDDown = inputVecs.X > 0;
            if (!lastWDown && inputVecs.Z < 0) DoW();
            lastWDown = inputVecs.Z < 0;
            if (!lastCDown && inputVecs.Y < 0) DoC();
            lastCDown = inputVecs.Y < 0;
            if (!lastSpaceDown && inputVecs.Y > 0) DoSpace();
            lastSpaceDown = inputVecs.Y > 0;
            if (!lastQDown && inputRoll < 0) DoQ();
            lastQDown = inputRoll < 0;
            if (!lastEDown && inputRoll > 0) DoE();
            lastEDown = inputRoll > 0;
        }

        void DoA()
        {

        }

        void DoS()
        {

        }

        void DoD()
        {

        }

        void DoW()
        {

        }

        void DoQ()
        {

        }

        void DoE()
        {

        }

        void DoC()
        {

        }

        void DoSpace()
        {
            DoScan();
        }
        #endregion

        #region Raycast
        int cameraIndex = 0;
        MyDetectedEntityInfo lastDetectedInfo;
        TimeSpan lastDetectedTimestamp;
        void DoScan()
        {
            IMyCameraBlock usingCamera = secondaryCameras[cameraIndex];
            cameraIndex += 1;
            if (cameraIndex == secondaryCameras.Count) cameraIndex = 0;
            lastDetectedInfo = usingCamera.Raycast(usingCamera.AvailableScanRange);
            lastDetectedTimestamp = Timestamp;
        }
        #endregion

        #region Display

        const float kCameraToScreen = 1.06f;
        const int kScreenSize = 512;

        MySprite DetectedInfoToSprite(MyDetectedEntityInfo info, TimeSpan deltaTime)
        {
            var worldDirection = info.Position + (info.Velocity * (float)deltaTime.TotalSeconds) - primaryCamera.WorldMatrix.Translation;
            var bodyPosition = Vector3D.TransformNormal(worldDirection, MatrixD.Transpose(primaryCamera.WorldMatrix));
            var screenPosition = new Vector2(-1 * (float)(bodyPosition.X / bodyPosition.Z), (float)(bodyPosition.Y/bodyPosition.Z));

            //var sprite = new MySprite(SpriteType.TEXTURE, "Danger", size: new Vector2(50f, 50f), color: new Color(1, 1, 1, 0.1f));
            var sprite = MySprite.CreateText("x", "Debug", new Color(1, 1, 1, 0.1f), 1, TextAlignment.CENTER);
            var v = ((screenPosition * kCameraToScreen) + new Vector2(0.5f, 0.5f)) * kScreenSize;

            v.X = Math.Max(30, Math.Min(kScreenSize - 30, v.X));
            v.Y = Math.Max(30, Math.Min(kScreenSize - 30, v.Y));
            v.Y -= 20;

            sprite.Position = v;
            return sprite;
        }
        #endregion
    }
}
