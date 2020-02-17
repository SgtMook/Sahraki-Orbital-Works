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
        public bool InterceptControls
        {
            get
            {
                return interceptControls;
            }
            set
            {
                interceptControls = value;
                controller.ControlThrusters = !interceptControls;
                TerminalPropertiesHelper.SetValue(controller, "ControlGyros", !interceptControls);
            }
        }

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
            //updateBuilder.Clear();

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

            panelLeft.Alignment = TextAlignment.RIGHT;
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
        StringBuilder RightHUDBuilder = new StringBuilder();

        IIntelProvider IntelProvider;

        string Tag;
        bool interceptControls = false;

        public SensorSubsystem(IIntelProvider intelProvider, string tag = "")
        {
            IntelProvider = intelProvider;
            UpdateFrequency = UpdateFrequency.Update10;
            Tag = tag;
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

            if (!block.CustomName.StartsWith(Tag)) return false;

            if (block is IMyTextPanel)
            {
                if (block.CustomName.Contains("[SN-SM]"))
                    panelMiddle = (IMyTextPanel)block;
                if (block.CustomName.Contains("[SN-SL]"))
                    panelLeft = (IMyTextPanel)block;
                if (block.CustomName.Contains("[SN-SR]"))
                    panelRight = (IMyTextPanel)block;
            }

            if (block is IMyCameraBlock)
            {
                if (block.CustomName.Contains("[SN-C-S]"))
                    secondaryCameras.Add((IMyCameraBlock)block);
                else if (block.CustomName.Contains("[SN-C-P]"))
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
                if (!primaryCamera.IsActive)
                    InterceptControls = false;
                TriggerInputs(timestamp);
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
            if (CurrentUIMode == UIMode.SelectAgent)
            {
                AgentSelection_CurrentClass = AgentClassAdd(AgentSelection_CurrentClass, -1);
                AgentSelection_CurrentIndex = 0;
            }
            else if (CurrentUIMode == UIMode.SelectTarget)
            {
                TargetSelection_TaskTypesIndex = DeltaSelection(TargetSelection_TaskTypesIndex, TargetSelection_TaskTypes.Count, false);
            }
        }

        void DoS(TimeSpan timestamp)
        {
            if (CurrentUIMode == UIMode.SelectAgent)
            {
                AgentSelection_CurrentIndex = DeltaSelection(AgentSelection_CurrentIndex, AgentSelection_FriendlyAgents.Count, true);
            }
            else if (CurrentUIMode == UIMode.SelectTarget)
            {
                TargetSelection_TargetIndex = DeltaSelection(TargetSelection_TargetIndex, TargetSelection_Targets.Count + TaskTypeToSpecialTargets[TargetSelection_TaskTypes[TargetSelection_TaskTypesIndex]].Count(), true);
            }
            else if (CurrentUIMode == UIMode.SelectWaypoint)
            {
                CursorDist -= 200;
            }
        }

        void DoD(TimeSpan timestamp)
        {
            if (CurrentUIMode == UIMode.SelectAgent)
            {
                AgentSelection_CurrentClass = AgentClassAdd(AgentSelection_CurrentClass, 1);
                AgentSelection_CurrentIndex = 0;
            }
            else if (CurrentUIMode == UIMode.SelectTarget)
            {
                TargetSelection_TaskTypesIndex = DeltaSelection(TargetSelection_TaskTypesIndex, TargetSelection_TaskTypes.Count, true);
            }
        }

        void DoW(TimeSpan timestamp)
        {
            if (CurrentUIMode == UIMode.SelectAgent)
            {
                AgentSelection_CurrentIndex = DeltaSelection(AgentSelection_CurrentIndex, AgentSelection_FriendlyAgents.Count, false);
            }
            else if (CurrentUIMode == UIMode.SelectTarget)
            {
                TargetSelection_TargetIndex = DeltaSelection(TargetSelection_TargetIndex, TargetSelection_Targets.Count + TaskTypeToSpecialTargets[TargetSelection_TaskTypes[TargetSelection_TaskTypesIndex]].Count(), false);
            }
            else if (CurrentUIMode == UIMode.SelectWaypoint)
            {
                CursorDist += 200;
            }
        }

        void DoQ(TimeSpan timestamp)
        {
            if (CurrentUIMode == UIMode.Scan)
            {
                CurrentUIMode = UIMode.SelectAgent;
                AgentSelection_CurrentIndex = 0;
            }
            else if (CurrentUIMode == UIMode.SelectAgent || CurrentUIMode == UIMode.SelectTarget || CurrentUIMode == UIMode.SelectWaypoint)
            {
                CurrentUIMode = UIMode.Scan;
            }
        }

        void DoE(TimeSpan timestamp)
        {

        }

        void DoC(TimeSpan timestamp)
        {
            if (CurrentUIMode == UIMode.SelectTarget)
            {
                CurrentUIMode = UIMode.SelectAgent;
            }
            else
            {
            }
        }

        void DoSpace(TimeSpan timestamp)
        {
            if (CurrentUIMode == UIMode.SelectAgent)
            {
                CurrentUIMode = UIMode.SelectTarget;
            }
            else if(CurrentUIMode == UIMode.SelectTarget)
            {
                if (TargetSelection_TargetIndex < TaskTypeToSpecialTargets[TargetSelection_TaskTypes[TargetSelection_TaskTypesIndex]].Count())
                {
                    // Special handling
                    if (TaskTypeToSpecialTargets[TargetSelection_TaskTypes[TargetSelection_TaskTypesIndex]][TargetSelection_TargetIndex] == "CURSOR")
                    {
                        CurrentUIMode = UIMode.SelectWaypoint;
                    }
                }
                else if (TargetSelection_TargetIndex < TaskTypeToSpecialTargets[TargetSelection_TaskTypes[TargetSelection_TaskTypesIndex]].Count() + TargetSelection_Targets.Count())
                {
                    SendCommand(TargetSelection_Targets[TargetSelection_TargetIndex - TaskTypeToSpecialTargets[TargetSelection_TaskTypes[TargetSelection_TaskTypesIndex]].Count()], timestamp);
                    updateBuilder.AppendLine(((int)TargetSelection_Targets[TargetSelection_TargetIndex - TaskTypeToSpecialTargets[TargetSelection_TaskTypes[TargetSelection_TaskTypesIndex]].Count()].IntelItemType).ToString());
                    CurrentUIMode = UIMode.SelectAgent;
                }
            }
            else if (CurrentUIMode == UIMode.SelectWaypoint)
            {
                Waypoint w = GetWaypoint();
                w.MaxSpeed = 100;
                w.Direction = new Vector3(0, -2, 3);
                ReportIntel(w, timestamp);
                SendCommand(w, timestamp);
            }
            else if (CurrentUIMode == UIMode.Scan)
            {
                DoScan(timestamp);
                if (lastDetectedInfo.Type == MyDetectedEntityType.Asteroid)
                {
                    float radius = (float)(lastDetectedInfo.BoundingBox.Max - lastDetectedInfo.BoundingBox.Center).Length();
                    var astr = new AsteroidIntel();
                    astr.Radius = radius;
                    astr.ID = lastDetectedInfo.EntityId;
                    astr.Position = lastDetectedInfo.BoundingBox.Center;
                    ReportIntel(astr, timestamp);
                }
            }
        }
        #endregion

        #region Raycast
        int Lidar_CameraIndex = 0;
        MyDetectedEntityInfo lastDetectedInfo;
        TimeSpan lastDetectedTimestamp;
        void DoScan(TimeSpan timestamp)
        {
            IMyCameraBlock usingCamera = secondaryCameras[Lidar_CameraIndex];
            Lidar_CameraIndex += 1;
            if (Lidar_CameraIndex == secondaryCameras.Count) Lidar_CameraIndex = 0;
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
        int CursorDist = 1000;
        Waypoint GetWaypoint()
        {
            var w = new Waypoint();

            w.Position = Vector3D.Transform(Vector3D.Forward * CursorDist, primaryCamera.WorldMatrix);

            return w;
        }
        #endregion

        #region Display

        const float kCameraToScreen = 1.06f;
        const int kScreenSize = 512;

        Vector2 kMonospaceConstant = new Vector2(18.68108f, 28.8f);

        const float kMinScale = 0.25f;
        const float kMaxScale = 0.5f;

        const float kMinDist = 1000;
        const float kMaxDist = 10000;

        List<MySprite> SpriteScratchpad = new List<MySprite>();

        UIMode CurrentUIMode = UIMode.SelectAgent;

        readonly Color kFocusedColor = new Color(0.5f, 0.5f, 1f);
        readonly Color kUnfocusedColor = new Color(0.2f, 0.2f, 0.5f, 0.5f);

        enum UIMode
        {
            Debug,
            SelectAgent,
            SelectTarget,
            Scan,
            SelectWaypoint,
        }

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

            var indicator = MySprite.CreateText("><", "Monospace", new Color(scale, scale, scale, 0.5f), scale, TextAlignment.CENTER);
            var v = ((screenPosition * kCameraToScreen) + new Vector2(0.5f, 0.5f)) * kScreenSize;

            v.X = Math.Max(30, Math.Min(kScreenSize - 30, v.X));
            v.Y = Math.Max(30, Math.Min(kScreenSize - 30, v.Y));
            v.Y -= 2 + scale * kMonospaceConstant.Y / 2;
            indicator.Position = v;
            scratchpad.Add(indicator);

            if (!(intel is AsteroidIntel))
            {
                var distSprite = MySprite.CreateText($"{((int)dist).ToString()} m", "Debug", new Color(1, 1, 1, 0.5f), 0.4f, TextAlignment.CENTER);
                v.Y += kMonospaceConstant.Y * kMaxScale + 0.2f;
                distSprite.Position = v;
                scratchpad.Add(distSprite);
            }

            if (intel is FriendlyShipIntel)
            {
                var nameSprite = MySprite.CreateText(intel.DisplayName, "Debug", new Color(1, 1, 1, 0.5f), 0.4f, TextAlignment.CENTER);
                v.Y += kMonospaceConstant.Y * 0.3f;
                nameSprite.Position = v;
                scratchpad.Add(nameSprite);
            }
            //if (intel is AsteroidIntel)
            //{
            //    var sizeSprite = MySprite.CreateText(((int)intel.Radius).ToString(), "Debug", new Color(1, 1, 1, 0.5f), 0.4f, TextAlignment.CENTER);
            //    v.Y += kMonospaceConstant.Y * 0.3f;
            //    sizeSprite.Position = v;
            //    scratchpad.Add(sizeSprite);
            //}
        }

        private void DrawHUD(TimeSpan timestamp)
        {
            UpdateCenterHUD(timestamp);
            UpdateLeftHUD(timestamp);
            UpdateRightHUD(timestamp);
        }

        private void UpdateCenterHUD(TimeSpan timestamp)
        {
            if (panelMiddle == null) return;
            using (var frame = panelMiddle.DrawFrame())
            {
                panelMiddle.ScriptBackgroundColor = new Color(1, 0, 0, 0);

                var crosshairs = new MySprite(SpriteType.TEXTURE, "Cross", size: new Vector2(10f, 10f), color: new Color(1, 1, 1, 0.1f));
                crosshairs.Position = new Vector2(0, -2) + panelMiddle.TextureSize / 2f;
                frame.Add(crosshairs);

                if (CurrentUIMode == UIMode.SelectWaypoint)
                {
                    var distIndicator = MySprite.CreateText(CursorDist.ToString(), "Debug", Color.White, 0.5f);
                    distIndicator.Position = new Vector2(0, 5) + panelMiddle.TextureSize / 2f;
                    frame.Add(distIndicator);
                }

                foreach (IFleetIntelligence intel in IntelProvider.GetFleetIntelligences(timestamp).Values)
                {
                    if (intel.IntelItemType == IntelItemType.Friendly && (CurrentUIMode == UIMode.Scan || AgentSelection_FriendlyAgents.Count == 0 || AgentSelection_FriendlyAgents.Count <= AgentSelection_CurrentIndex || intel != AgentSelection_FriendlyAgents[AgentSelection_CurrentIndex])) continue;
                    if (intel.IntelItemType == IntelItemType.Enemy) continue;
                    FleetIntelItemToSprites(intel, timestamp, ref SpriteScratchpad);
                }

                foreach (var spr in SpriteScratchpad)
                {
                    frame.Add(spr);
                }
                SpriteScratchpad.Clear();
            }
        }

        private void UpdateLeftHUD(TimeSpan timestamp)
        {
            if (panelLeft == null) return;

            LeftHUDBuilder.Clear();

            if (CurrentUIMode == UIMode.Debug)
            {
                DrawDebugLeftUI(timestamp);
            }
            else if (CurrentUIMode == UIMode.SelectAgent || CurrentUIMode == UIMode.SelectTarget || CurrentUIMode == UIMode.SelectWaypoint)
            {
                panelLeft.FontColor = CurrentUIMode == UIMode.SelectAgent ? kFocusedColor : kUnfocusedColor;
                DrawAgentSelectionUI(timestamp);
            }
            else if (CurrentUIMode == UIMode.Scan)
            {
                panelLeft.FontColor = kFocusedColor;
                DrawScanUI(timestamp);
            }

            panelLeft.WriteText(LeftHUDBuilder.ToString());
        }

        private void DrawDebugLeftUI(TimeSpan timestamp)
        {
            LeftHUDBuilder.AppendLine(CursorDist.ToString());
            LeftHUDBuilder.AppendLine();

            foreach (IFleetIntelligence intel in IntelProvider.GetFleetIntelligences(timestamp).Values)
            {
                LeftHUDBuilder.AppendLine(intel.DisplayName);
                LeftHUDBuilder.AppendLine(intel.ID.ToString());
                LeftHUDBuilder.AppendLine(((Vector3I)intel.GetPositionFromCanonicalTime(timestamp + IntelProvider.CanonicalTimeDiff)).ToString());

                if (intel is FriendlyShipIntel && ((FriendlyShipIntel)intel).AgentClass != AgentClass.None)
                {
                    var fsi = (FriendlyShipIntel)intel;
                    LeftHUDBuilder.AppendLine(fsi.AgentClass.ToString());
                    LeftHUDBuilder.AppendLine(fsi.AcceptedTaskTypes.ToString());
                }
            }
        }

        AgentClass AgentSelection_CurrentClass = AgentClass.Drone;
        int AgentSelection_CurrentIndex = 0;
        List<FriendlyShipIntel> AgentSelection_FriendlyAgents = new List<FriendlyShipIntel>();

        Dictionary<AgentClass, string> AgentClassTags = new Dictionary<AgentClass, string>
        {
            { AgentClass.None, "SLF" },
            { AgentClass.Drone, "DRN" },
            { AgentClass.Fighter, "FTR" },
            { AgentClass.Bomber, "BMR" },
            { AgentClass.Miner, "MNR" },
            { AgentClass.Carrier, "CVS" },
        };

        AgentClass AgentClassAdd(AgentClass agentClass, int places = 1)
        {
            return (AgentClass)CustomMod((int)agentClass + places, (int)AgentClass.Last);
        }

        private void DrawAgentSelectionUI(TimeSpan timestamp)
        {
            panelLeft.FontSize = 0.55f;
            panelLeft.TextPadding = 9;

            int kRowLength = 19;
            int kMenuRows = 12;

            LeftHUDBuilder.AppendLine("===== COMMAND =====");
            LeftHUDBuilder.AppendLine();
            LeftHUDBuilder.Append(AgentClassTags[AgentClassAdd(AgentSelection_CurrentClass, -1)]).Append("    [").Append(AgentClassTags[AgentSelection_CurrentClass]).Append("]    ").AppendLine(AgentClassTags[AgentClassAdd(AgentSelection_CurrentClass, +1)]);
            if (CurrentUIMode == UIMode.SelectAgent) LeftHUDBuilder.AppendLine("[<A]           [D>]");
            else LeftHUDBuilder.AppendLine();

            LeftHUDBuilder.AppendLine();

            AgentSelection_FriendlyAgents.Clear();

            foreach (IFleetIntelligence intel in IntelProvider.GetFleetIntelligences(timestamp).Values)
            {
                if (intel is FriendlyShipIntel && ((FriendlyShipIntel)intel).AgentClass == AgentSelection_CurrentClass)
                    AgentSelection_FriendlyAgents.Add((FriendlyShipIntel)intel);
            }
            AgentSelection_FriendlyAgents.Sort(FleetIntelligenceUtil.CompareName);

            for (int i = 0; i < kMenuRows; i++)
            {
                if (i < AgentSelection_FriendlyAgents.Count)
                {
                    var intel = AgentSelection_FriendlyAgents[i];
                    if (i == AgentSelection_CurrentIndex) LeftHUDBuilder.Append(">> ");
                    else LeftHUDBuilder.Append("   ");

                    AppendPaddedLine(kRowLength - 3, intel.DisplayName, LeftHUDBuilder);
                }
                else
                {
                    LeftHUDBuilder.AppendLine();
                }
            }

            LeftHUDBuilder.AppendLine("==== SELECTION ====");

            if (AgentSelection_CurrentIndex < AgentSelection_FriendlyAgents.Count)
            {
                AppendPaddedLine(kRowLength, AgentSelection_FriendlyAgents[AgentSelection_CurrentIndex].DisplayName, LeftHUDBuilder);
                if (CurrentUIMode == UIMode.SelectAgent) AppendPaddedLine(kRowLength, "[SPACE] SELECT TGT", LeftHUDBuilder);
            }
            else
            {
                AppendPaddedLine(kRowLength, "NONE SELECTED", LeftHUDBuilder);
            }
        }

        private void DrawScanUI(TimeSpan timestamp)
        {
            panelLeft.FontSize = 0.55f;
            panelLeft.TextPadding = 9;
            int kRowLength = 19;

            LeftHUDBuilder.AppendLine("====== LIDAR ======");
            LeftHUDBuilder.AppendLine();

            if (secondaryCameras.Count == 0)
            {
                LeftHUDBuilder.AppendLine("=== UNAVAILABLE ===");
                return;
            }

            AppendPaddedLine(kRowLength, "SCANNERS:", LeftHUDBuilder);

            LeftHUDBuilder.AppendLine();

            for (int i = 0; i < 8; i++)
            {
                if (i < secondaryCameras.Count)
                {
                    LeftHUDBuilder.Append(i == Lidar_CameraIndex ? "> " : "  ");
                    LeftHUDBuilder.Append((i + 1).ToString()).Append(": ");

                    if (secondaryCameras[i].IsWorking)
                    {
                        if (secondaryCameras[i].AvailableScanRange >= kScanDistance)
                        {
                            AppendPaddedLine(kRowLength - 5, "READY", LeftHUDBuilder);
                        }
                        else
                        {
                            AppendPaddedLine(kRowLength - 5, "CHARGING", LeftHUDBuilder);
                        }

                        int p = (int)(secondaryCameras[i].AvailableScanRange * 10 / kScanDistance);
                        LeftHUDBuilder.Append('[').Append('=', p).Append(' ', Math.Max(0, 10 - p)).Append(string.Format("] {0,4:0.0}", secondaryCameras[i].AvailableScanRange / 1000)).AppendLine("km");
                    }
                    else
                    {
                        AppendPaddedLine(kRowLength - 5, "UNAVAILABLE", LeftHUDBuilder);
                        LeftHUDBuilder.AppendLine();
                    }
                }
                else
                {
                    LeftHUDBuilder.AppendLine();
                    LeftHUDBuilder.AppendLine();
                }
            }

            LeftHUDBuilder.AppendLine();
            LeftHUDBuilder.AppendLine("===================");

            if (secondaryCameras[Lidar_CameraIndex].IsWorking)
            {
                AppendPaddedLine(kRowLength, "STATUS: AVAILABLE", LeftHUDBuilder);
                int p = (int)(secondaryCameras[Lidar_CameraIndex].AvailableScanRange * 10 / kScanDistance);
                LeftHUDBuilder.Append('[').Append('=', p).Append(' ', Math.Max(0, 10 - p)).Append(string.Format("] {0,4:0.0}", secondaryCameras[Lidar_CameraIndex].AvailableScanRange / 1000)).AppendLine("km");
                AppendPaddedLine(kRowLength, "[SPACE] SCAN", LeftHUDBuilder);
                AppendPaddedLine(kRowLength, lastDetectedInfo.Type.ToString(), LeftHUDBuilder);
            }
            else
            {
                AppendPaddedLine(kRowLength, "STATUS: UNAVAILABLE", LeftHUDBuilder);
                AppendPaddedLine(kRowLength, "[SPACE] CYCLE", LeftHUDBuilder);
            }
        }

        private void UpdateRightHUD(TimeSpan timestamp)
        {
            if (panelRight == null) return;

            RightHUDBuilder.Clear();

            if (CurrentUIMode == UIMode.Debug)
            {
                // Do debug?
            }
            else
            {
                panelRight.FontColor = CurrentUIMode == UIMode.SelectTarget ? kFocusedColor : kUnfocusedColor;
                DrawTargetSelectionUI(timestamp);
            }

            panelRight.WriteText(RightHUDBuilder.ToString());
        }

        Dictionary<TaskType, string> TaskTypeTags = new Dictionary<TaskType, string>
        {
            { TaskType.None, "N/A" },
            { TaskType.Move, "MOV" },
            { TaskType.SmartMove, "SMV" },
            { TaskType.Attack, "ATK" },
            { TaskType.Dock, "DOK" }
        };

        Dictionary<TaskType, IntelItemType> TaskTypeToTargetTypes = new Dictionary<TaskType, IntelItemType>
        {
            { TaskType.None, IntelItemType.NONE},
            { TaskType.Move, IntelItemType.Waypoint},
            { TaskType.SmartMove, IntelItemType.Waypoint },
            { TaskType.Attack, IntelItemType.Enemy | IntelItemType.Waypoint },
            { TaskType.Dock, IntelItemType.Dock }
        };

        Dictionary<TaskType, string[]> TaskTypeToSpecialTargets = new Dictionary<TaskType, string[]>
        {
            { TaskType.None, new string[0]},
            { TaskType.Move, new string[1] { "CURSOR" }},
            { TaskType.SmartMove, new string[1] { "CURSOR" }},
            { TaskType.Attack, new string[2] { "CURSOR", "NEAREST" }},
            { TaskType.Dock, new string[1] { "NEAREST" }}
        };

        List<TaskType> TargetSelection_TaskTypes = new List<TaskType>();
        int TargetSelection_TaskTypesIndex = 0;
        List<IFleetIntelligence> TargetSelection_Targets = new List<IFleetIntelligence>();
        int TargetSelection_TargetIndex;

        private void DrawTargetSelectionUI(TimeSpan timestamp)
        {
            panelRight.FontSize = 0.55f;
            panelRight.TextPadding = 9;

            RightHUDBuilder.AppendLine("=== SELECT TASK ===");

            RightHUDBuilder.AppendLine();

            if (AgentSelection_CurrentIndex >= AgentSelection_FriendlyAgents.Count) return;

            var Agent = AgentSelection_FriendlyAgents[AgentSelection_CurrentIndex];
            TargetSelection_TaskTypes.Clear();

            for (int i = 0; i < 30; i++)
            {
                if (((TaskType)(1 << i) & Agent.AcceptedTaskTypes) != 0)
                    TargetSelection_TaskTypes.Add((TaskType)(1 << i));
            }

            if (TargetSelection_TaskTypes.Count == 0) return;

            if (TargetSelection_TaskTypesIndex >= TargetSelection_TaskTypes.Count) TargetSelection_TaskTypesIndex = 0;

            RightHUDBuilder.Append(TaskTypeTags[TargetSelection_TaskTypes[CustomMod(TargetSelection_TaskTypesIndex - 1, TargetSelection_TaskTypes.Count)]]).
                Append("    [").Append(TaskTypeTags[TargetSelection_TaskTypes[TargetSelection_TaskTypesIndex]]).Append("]    ").
                AppendLine(TaskTypeTags[TargetSelection_TaskTypes[CustomMod(TargetSelection_TaskTypesIndex + 1, TargetSelection_TaskTypes.Count)]]);

            if (CurrentUIMode == UIMode.SelectTarget) RightHUDBuilder.AppendLine("[<A]           [D>]");
            else RightHUDBuilder.AppendLine();
            RightHUDBuilder.AppendLine();

            TargetSelection_Targets.Clear();
            foreach (IFleetIntelligence intel in IntelProvider.GetFleetIntelligences(timestamp).Values)
            {
                if ((intel.IntelItemType & TaskTypeToTargetTypes[TargetSelection_TaskTypes[TargetSelection_TaskTypesIndex]]) != 0)
                    TargetSelection_Targets.Add(intel);
            }

            TargetSelection_Targets.Sort(FleetIntelligenceUtil.CompareName);

            int kMenuRows = 16;
            int kRowLength = 19;
            int specialCount = TaskTypeToSpecialTargets[TargetSelection_TaskTypes[TargetSelection_TaskTypesIndex]].Count();
            for (int i = 0; i < kMenuRows; i++)
            {
                if (i < specialCount)
                {
                    if (i == TargetSelection_TargetIndex) RightHUDBuilder.Append(">> ");
                    else RightHUDBuilder.Append("   ");
                    AppendPaddedLine(kRowLength - 3, TaskTypeToSpecialTargets[TargetSelection_TaskTypes[TargetSelection_TaskTypesIndex]][i], RightHUDBuilder);
                }
                else if (specialCount < i && i < TargetSelection_Targets.Count + specialCount + 1)
                {
                    if (i == TargetSelection_TargetIndex + 1) RightHUDBuilder.Append(">> ");
                    else RightHUDBuilder.Append("   ");
                    var intel = TargetSelection_Targets[i - specialCount - 1];
                    AppendPaddedLine(kRowLength - 3, intel.DisplayName, RightHUDBuilder);
                }
                else
                {
                    RightHUDBuilder.AppendLine();
                }
            }

            RightHUDBuilder.AppendLine();

            RightHUDBuilder.AppendLine("==== SELECTION ====");

            if (TargetSelection_TargetIndex < specialCount)
            {
                AppendPaddedLine(kRowLength, TaskTypeToSpecialTargets[TargetSelection_TaskTypes[TargetSelection_TaskTypesIndex]][TargetSelection_TargetIndex], RightHUDBuilder);
                if (CurrentUIMode == UIMode.SelectTarget)
                {
                    AppendPaddedLine(kRowLength, "[SPACE] SELECT", RightHUDBuilder);
                    AppendPaddedLine(kRowLength, "[C] CANCLE CMD", RightHUDBuilder);
                }
            }
            else if (specialCount <= TargetSelection_TargetIndex && TargetSelection_TargetIndex < TargetSelection_Targets.Count + specialCount)
            {
                AppendPaddedLine(kRowLength, TargetSelection_Targets[TargetSelection_TargetIndex - specialCount].ID.ToString(), RightHUDBuilder);
                if (CurrentUIMode == UIMode.SelectTarget)
                {
                    AppendPaddedLine(kRowLength, "[SPACE] SEND CMD", RightHUDBuilder);
                    AppendPaddedLine(kRowLength, "[C] CANCLE CMD", RightHUDBuilder);
                }
            }
            else
            {
                AppendPaddedLine(kRowLength, "NONE SELECTED", RightHUDBuilder);
            }

        }

        private void AppendPaddedLine(int TotalLength, string text, StringBuilder builder)
        {
            int length = text.Length;
            if (length > TotalLength)
                builder.AppendLine(text.Substring(0, TotalLength));
            else if (length < TotalLength)
                builder.Append(text).Append(' ', TotalLength - length).AppendLine();
            else
                builder.AppendLine(text);
        }

        int CustomMod(int n, int d)
        {
            return (n % d + d) % d;
        }

        int DeltaSelection(int current, int total, bool positive, int min = 0)
        {
            if (total == 0) return 0;
            int newCurrent = current + (positive ? 1 : -1);
            if (newCurrent >= total) newCurrent = 0;
            if (newCurrent <= -1) newCurrent = total - 1;
            return newCurrent;
        }

        #endregion

        #region Intel
        void ReportIntel(IFleetIntelligence intel, TimeSpan timestamp)
        {
            IntelProvider.ReportFleetIntelligence(intel, timestamp);
        }

        void SendCommand(IFleetIntelligence target, TimeSpan timestamp)
        {
            FriendlyShipIntel agent = AgentSelection_FriendlyAgents[AgentSelection_CurrentIndex];
            TaskType taskType = TargetSelection_TaskTypes[TargetSelection_TaskTypesIndex];

            IntelProvider.ReportCommand(agent, taskType, target, timestamp);

            CurrentUIMode = UIMode.SelectAgent;
        }
        #endregion

        #region Debug
        private const int kScanDistance = 25000;
        void GetRaycastDebug()
        {
            foreach (IMyCameraBlock camera in secondaryCameras)
            {
                updateBuilder.AppendLine($"{((int)(camera.AvailableScanRange / 1000)).ToString()} km");
            }

            updateBuilder.AppendLine(Lidar_CameraIndex.ToString());
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
