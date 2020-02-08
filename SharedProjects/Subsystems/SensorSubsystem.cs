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
    public class SensorSubsystem : ISubsystem, IControlIntercepter
    {
        #region IControlIntercepter
        public bool InterceptControls { get; set; }

        public IMyShipController Controller
        {
            get
            {
                return controller;
            }
        }
        #endregion

        #region ISubsystem
        public UpdateFrequency UpdateFrequency { get; set; }

        public void Command(TimeSpan timestamp, string command, object argument)
        {
            if (command == "togglecontrol") InterceptControls = !InterceptControls;
        }

        public void DeserializeSubsystem(string serialized)
        {
        }

        public string GetStatus()
        {
            updateBuilder.Clear();

            return updateBuilder.ToString();
        }

        public string SerializeSubsystem()
        {
            return string.Empty;
        }

        public void Setup(MyGridProgram program, string name)
        {
            Program = program;
            GetParts();
        }

        public void Update(TimeSpan timestamp, UpdateFrequency updateFlags)
        {
            if ((updateFlags & UpdateFrequency.Update1) != 0)
            {
                UpdateInputs(timestamp);
            }
            if ((updateFlags & UpdateFrequency.Update10) != 0)
            {
                UpdateRaytracing();
                DrawHUD(timestamp);
            }

            UpdateFrequency = UpdateFrequency.Update10;
            if (InterceptControls) UpdateFrequency |= UpdateFrequency.Update1;
        }

        #endregion

        IMyShipController controller;

        MyGridProgram Program;

        IMyTextPanel panelLeft;
        IMyTextPanel panelRight;
        IMyTextPanel panelMiddle;

        List<IMyCameraBlock> secondaryCameras = new List<IMyCameraBlock>();
        IMyCameraBlock primaryCamera;

        StringBuilder updateBuilder = new StringBuilder();
        StringBuilder LeftHUDBuilder = new StringBuilder();

        IIntelProvider IntelProvider;

        public SensorSubsystem(IIntelProvider intelProvider)
        {
            IntelProvider = intelProvider;
            UpdateFrequency = UpdateFrequency.Update10;
            InterceptControls = false;
        }

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

        private void UpdateInputs(TimeSpan timestamp)
        {
            if (InterceptControls)
            {
                if (!primaryCamera.IsActive) InterceptControls = false;

                controller.ControlThrusters = false;
                TerminalPropertiesHelper.SetValue(controller, "ControlGyros", false);
                // Take inputs
                TriggerInputs(timestamp);
            }
            else
            {
                controller.ControlThrusters = true;
                TerminalPropertiesHelper.SetValue(controller, "ControlGyros", true);
            }
        }

        private void TriggerInputs(TimeSpan timestamp)
        {
            if (controller == null) return;
            var inputVecs = controller.MoveIndicator;
            var inputRoll = controller.RollIndicator;
            if (!lastADown && inputVecs.X < 0) DoA(timestamp);
            lastADown = inputVecs.X < 0;
            if (!lastSDown && inputVecs.Z > 0) DoS(timestamp);
            lastSDown = inputVecs.Z > 0;
            if (!lastDDown && inputVecs.X > 0) DoD(timestamp);
            lastDDown = inputVecs.X > 0;
            if (!lastWDown && inputVecs.Z < 0) DoW(timestamp);
            lastWDown = inputVecs.Z < 0;
            if (!lastCDown && inputVecs.Y < 0) DoC(timestamp);
            lastCDown = inputVecs.Y < 0;
            if (!lastSpaceDown && inputVecs.Y > 0) DoSpace(timestamp);
            lastSpaceDown = inputVecs.Y > 0;
            if (!lastQDown && inputRoll < 0) DoQ(timestamp);
            lastQDown = inputRoll < 0;
            if (!lastEDown && inputRoll > 0) DoE(timestamp);
            lastEDown = inputRoll > 0;
        }

        void DoA(TimeSpan timestamp)
        {

        }

        void DoS(TimeSpan timestamp)
        {
            distMeters -= 500;
        }

        void DoD(TimeSpan timestamp)
        {

        }

        void DoW(TimeSpan timestamp)
        {
            distMeters += 500;
        }

        void DoQ(TimeSpan timestamp)
        {

        }

        void DoE(TimeSpan timestamp)
        {

        }

        void DoC(TimeSpan timestamp)
        {

        }

        void DoSpace(TimeSpan timestamp)
        {
            Waypoint w = GetWaypoint();
            ReportWaypoint(w, timestamp);
        }
        #endregion

        #region Raycast
        int cameraIndex = 0;
        MyDetectedEntityInfo lastDetectedInfo;
        TimeSpan lastDetectedTimestamp;
        void DoScan(TimeSpan timestamp)
        {
            IMyCameraBlock usingCamera = secondaryCameras[cameraIndex];
            cameraIndex += 1;
            if (cameraIndex == secondaryCameras.Count) cameraIndex = 0;
            lastDetectedInfo = usingCamera.Raycast(usingCamera.AvailableScanRange);
            lastDetectedTimestamp = timestamp;
        }

        private void UpdateRaytracing()
        {
            foreach (IMyCameraBlock camera in secondaryCameras)
            {
                camera.EnableRaycast = camera.AvailableScanRange < kScanDistance;
            }
        }
        #endregion

        #region Waypoint Designation
        int distMeters = 1000;
        Waypoint GetWaypoint()
        {
            var w = new Waypoint();

            w.Position = Vector3D.Transform(Vector3D.Forward * distMeters, primaryCamera.WorldMatrix);

            return w;
        }
        #endregion

        #region Display

        const float kCameraToScreen = 1.06f;
        const int kScreenSize = 512;

        Vector2 kMonospaceConstant = new Vector2(18.68108f, 28.8f);

        const float kMinScale = 0.5f;
        const float kMaxScale = 1.5f;

        const float kMinDist = 1000;
        const float kMaxDist = 10000;

        List<MySprite> SpriteScratchpad = new List<MySprite>();

        void FleetIntelItemToSprites(IFleetIntelligence intel, TimeSpan timestamp, ref List<MySprite> scratchpad)
        {
            var worldDirection = intel.GetPositionFromCanonicalTime(timestamp + IntelProvider.CanonicalTimeDiff) - primaryCamera.WorldMatrix.Translation;
            var bodyPosition = Vector3D.TransformNormal(worldDirection, MatrixD.Transpose(primaryCamera.WorldMatrix));
            var screenPosition = new Vector2(-1 * (float)(bodyPosition.X / bodyPosition.Z), (float)(bodyPosition.Y / bodyPosition.Z));

            if (bodyPosition.Dot(Vector3D.Forward) < 0) return;

            float dist = (float)bodyPosition.Length();
            float scale = kMaxScale;

            if (dist > kMaxDist) scale = kMinScale;
            else if (dist > kMinDist)
            {
                scale = kMinScale + (kMaxScale - kMinScale) * (kMaxDist - dist) / (kMaxDist - kMinDist);
            }

            var indicator = MySprite.CreateText("x", "Monospace", new Color(scale, scale, scale, 0.5f), scale, TextAlignment.CENTER);
            var v = ((screenPosition * kCameraToScreen) + new Vector2(0.5f, 0.5f)) * kScreenSize;

            v.X = Math.Max(30, Math.Min(kScreenSize - 30, v.X));
            v.Y = Math.Max(30, Math.Min(kScreenSize - 30, v.Y));
            v.Y -= 5 + scale * kMonospaceConstant.Y / 2;
            indicator.Position = v;
            scratchpad.Add(indicator);

            var distSprite = MySprite.CreateText($"{((int)dist).ToString()} m", "Monospace", new Color(1, 1, 1, 0.5f), 0.4f, TextAlignment.CENTER);
            v.Y += kMonospaceConstant.Y * scale;
            distSprite.Position = v;
            scratchpad.Add(distSprite);

            if (intel is FriendlyShipIntel)
            {
                var nameSprite = MySprite.CreateText(intel.DisplayName, "Monospace", new Color(1, 1, 1, 0.5f), 0.4f, TextAlignment.CENTER);
                v.Y += kMonospaceConstant.Y * 0.4f;
                nameSprite.Position = v;
                scratchpad.Add(nameSprite);
            }
        }

        private void DrawHUD(TimeSpan timestamp)
        {
            DrawCenterHUD(timestamp);
            UpdateLeftHUD(timestamp);
        }

        private void UpdateLeftHUD(TimeSpan timestamp)
        {
            if (panelLeft == null) return;
            LeftHUDBuilder.Clear();

            LeftHUDBuilder.AppendLine($"{distMeters.ToString()} m");
            LeftHUDBuilder.AppendLine($"{(Vector3I)Program.Me.CubeGrid.GetPosition()}");

            foreach (IFleetIntelligence intel in IntelProvider.GetFleetIntelligences().Values)
            {
                LeftHUDBuilder.AppendLine(((Vector3I)intel.GetPositionFromCanonicalTime(timestamp + IntelProvider.CanonicalTimeDiff)).ToString());
            }

            panelLeft.Alignment = TextAlignment.RIGHT;
            panelLeft.WriteText(LeftHUDBuilder.ToString());
        }

        private void DrawCenterHUD(TimeSpan timestamp)
        {
            if (panelMiddle == null) return;
            using (var frame = panelMiddle.DrawFrame())
            {
                var crosshairs = new MySprite(SpriteType.TEXTURE, "Cross", size: new Vector2(10f, 10f), color: new Color(1, 1, 1, 0.1f));
                crosshairs.Position = new Vector2(0, -2) + panelMiddle.TextureSize / 2f;
                panelMiddle.ScriptBackgroundColor = new Color(1, 0, 0, 0);
                frame.Add(crosshairs);

                foreach (IFleetIntelligence intel in IntelProvider.GetFleetIntelligences().Values)
                {
                    FleetIntelItemToSprites(intel, timestamp, ref SpriteScratchpad);
                }

                foreach (var spr in SpriteScratchpad)
                {
                    frame.Add(spr);
                }
                SpriteScratchpad.Clear();
            }
        }
        #endregion

        #region Intel
        void ReportWaypoint(Waypoint w, TimeSpan timestamp)
        {
            IntelProvider.ReportFleetIntelligence(w, timestamp);
        }
        #endregion

        #region Debug
        private const int kScanDistance = 5000000;
        void GetRaycastDebug()
        {
            foreach (IMyCameraBlock camera in secondaryCameras)
            {
                updateBuilder.AppendLine($"{((int)(camera.AvailableScanRange / 1000)).ToString()} km");
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
        }
        #endregion
    }
}
