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
    public class LookingGlassNetworkSubsystem : ISubsystem
    {
        #region ISubsystem
        public UpdateFrequency UpdateFrequency { get; set; }
        public void Command(TimeSpan timestamp, string command, object argument)
        {
            if (command == "toggleactive") Active = !Active;
            if (command == "activate")
            {
                AutoActivate = false;
                int index;
                if (!int.TryParse((string)argument, out index)) return;
                if (ActiveLookingGlass != null) ActiveLookingGlass.InterceptControls = false;
                ActiveLookingGlass = LookingGlasses[index];
                ActiveLookingGlass.InterceptControls = true;
            }
            if (command == "deactivate" && ActiveLookingGlass != null)
            {
                ActiveLookingGlass.InterceptControls = false;
                ActiveLookingGlass = null;
            }
            if (command == "activateplugin") ActivatePlugin((string)argument);
            if (command == "cycleplugin") CyclePlugin();
            if (command == "up" || command == "8") DoW(timestamp);
            if (command == "down" || command == "5") DoS(timestamp);
            if (command == "left" || command == "4") DoA(timestamp);
            if (command == "right" || command == "6") DoD(timestamp);
            if (command == "enter" || command == "3") DoSpace(timestamp);
            if (command == "cancel" || command == "7") DoC(timestamp);
        }

        public void CommandV2(TimeSpan timestamp, CommandLine command)
        {

        }

        public void DeserializeSubsystem(string serialized)
        {
        }

        public string GetStatus()
        {
            return Controller.MoveIndicator.ToString();
        }

        public string SerializeSubsystem()
        {
            return string.Empty;
        }

        public void Setup(ExecutionContext context, string name)
        {
            Context = context;

//            ParseConfigs();
            GetParts();
            UpdateFrequency = UpdateFrequency.Update10;
            if (!OverrideThrusters && LookingGlasses.Count == 1)
            {
                ActiveLookingGlass = LookingGlasses[0];
                ActiveLookingGlass.InterceptControls = true;
            }
        }

        public void Update(TimeSpan timestamp, UpdateFrequency updateFlags)
        {
            if ((updateFlags & UpdateFrequency.Update1) != 0 && ActiveLookingGlass != null)
            {
                TriggerInputs(timestamp);
                UpdateSwivels();
            }
            if ((updateFlags & UpdateFrequency.Update10) != 0)
            {
                UpdatePlugins(timestamp);
                UpdateActiveLookingGlass();
                UpdateUpdateFrequency();
            }
        }

        #endregion
        public LookingGlassNetworkSubsystem(IIntelProvider intelProvider, string tag = "LG", bool overrideGyros = true, bool overrideThrusters = true)
        {
            IntelProvider = intelProvider;
            OverrideGyros = overrideGyros;
            OverrideThrusters = overrideThrusters;

            Tag = tag;
            TagPrefix = "[" + tag;
        }

        public IIntelProvider IntelProvider;

        public ExecutionContext Context;

        Dictionary<string, ILookingGlassPlugin> Plugins = new Dictionary<string, ILookingGlassPlugin>();
        List<ILookingGlassPlugin> PluginsList = new List<ILookingGlassPlugin>();
        ILookingGlassPlugin ActivePlugin = null;

        public List<LookingGlass> LookingGlasses = new List<LookingGlass>();
        public LookingGlass ActiveLookingGlass = null;

        LookingGlass[] LookingGlassArray = new LookingGlass[8];

        public IMyShipController Controller;

        bool OverrideGyros;
        bool OverrideThrusters;

        bool Active = true;
        bool AutoActivate = false;

        string Tag;
        string TagPrefix;

        public void AddLookingGlass(LookingGlass lookingGlass)
        {
            LookingGlasses.Add(lookingGlass);
            lookingGlass.Network = this;
        }

//         void ParseConfigs()
//         {
// 
//         }

        void GetParts()
        {
            for (int i = 0; i < LookingGlassArray.Length; i++)
                if (LookingGlassArray[i] != null) LookingGlassArray[i].Clear();
            LookingGlasses.Clear();
            Controller = null;

            //if (OverrideGyros)
            //{
            //    Program.GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(null, FindBases);
            //    Program.GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(null, FindUnassignedBases);
            //    Program.GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(null, FindArms);
            //}
            //else
            //{
                LookingGlassArray[1] = new LookingGlass();
            //}

            Context.Terminal.GetBlocksOfType<IMyTerminalBlock>(null, CollectParts);

            for (int i = 0; i < LookingGlassArray.Length; i++)
            {
                if (LookingGlassArray[i] != null && LookingGlassArray[i].IsOK(OverrideGyros))
                {
                    LookingGlassArray[i].Initialize();
                    AddLookingGlass(LookingGlassArray[i]);
                }
            }
        }

        bool CollectParts(IMyTerminalBlock block)
        {
            if (!Context.Reference.IsSameConstructAs(block)) return false;
            if (block is IMyShipController && ((IMyShipController)block).CanControlShip)
                Controller = (IMyShipController)block;

            //if (OverrideGyros)
            //{
            //    for (int i = 0; i < LookingGlassArray.Length; i++)
            //    {
            //        if (LookingGlassArray[i] != null && LookingGlassArray[i].Pitch != null && block.CubeGrid.EntityId == LookingGlassArray[i].Pitch.TopGrid.EntityId)
            //        {
            //            if (block.CustomName.StartsWith("["))
            //            {
            //                var indexTagEnd = block.CustomName.IndexOf(']');
            //                if (indexTagEnd != -1)
            //                {
            //                    block.CustomName = block.CustomName.Substring(indexTagEnd + 1);
            //                }
            //            }
            //            block.CustomName = $"[{Tag}{i}]" + block.CustomName;
            //            LookingGlassArray[i].AddPart(block);
            //        }
            //    }
            //}
            //else
            //{
                if (!block.CustomName.Contains(TagPrefix)) return false;
                LookingGlassArray[1].AddPart(block);
            //}

            return false;
        }

        //bool FindBases(IMyTerminalBlock block)
        //{
        //    if (!ProgramReference.IsSameConstructAs(block)) return false;
        //    if (block is IMyMotorStator && block.CustomName.StartsWith(TagPrefix) && !block.CustomName.StartsWith($"[{Tag}x]") && block.CubeGrid.EntityId == ProgramReference.CubeGrid.EntityId)
        //    {
        //        var indexTagEnd = block.CustomName.IndexOf(']');
        //        if (indexTagEnd == -1) return false;
        //
        //        var numString = block.CustomName.Substring(TagPrefix.Length, indexTagEnd - TagPrefix.Length);
        //
        //        int index;
        //        if (!int.TryParse(numString, out index)) return false;
        //        if (LookingGlassArray[index] == null) LookingGlassArray[index] = new LookingGlass();
        //        LookingGlassArray[index].AddPart(block);
        //    }
        //    return false;
        //}
        //
        //bool FindUnassignedBases(IMyTerminalBlock block)
        //{
        //    if (!ProgramReference.IsSameConstructAs(block)) return false;
        //    if (block is IMyMotorStator && block.CustomName.StartsWith($"[{Tag}x]") && block.CubeGrid.EntityId == ProgramReference.CubeGrid.EntityId)
        //    {
        //        for (int i = 1; i < LookingGlassArray.Length; i++)
        //        {
        //            if (LookingGlassArray[i] == null)
        //            {
        //                LookingGlassArray[i] = new LookingGlass();
        //                block.CustomName = block.CustomName.Replace($"[{Tag}x]", $"[{Tag}{i}]");
        //                LookingGlassArray[i].AddPart(block);
        //                return false;
        //            }
        //        }
        //    }
        //    return false;
        //}
        //
        //bool FindArms(IMyTerminalBlock block)
        //{
        //    if (!ProgramReference.IsSameConstructAs(block)) return false;
        //    if (block is IMyMotorStator)
        //    {
        //        for (int i = 1; i < LookingGlassArray.Length; i++)
        //        {
        //            if (LookingGlassArray[i] != null && block.CubeGrid.EntityId == LookingGlassArray[i].Yaw.TopGrid.EntityId)
        //            {
        //                if (block.CustomName.StartsWith("["))
        //                {
        //                    var indexTagEnd = block.CustomName.IndexOf(']');
        //                    if (indexTagEnd != -1)
        //                    {
        //                        block.CustomName = block.CustomName.Substring(indexTagEnd + 1);
        //                    }
        //                }
        //                block.CustomName = $"[{Tag}{i}]" + block.CustomName;
        //                LookingGlassArray[i].AddPart(block);
        //            }
        //        }
        //    }
        //    return false;
        //}

        #region plugins
        public void AddPlugin(string name, ILookingGlassPlugin plugin)
        {
            if (Plugins.ContainsKey(name)) return;
            Plugins[name] = plugin;
            plugin.Host = this;
            PluginsList.Add(plugin);

            if (ActivePlugin == null) ActivePlugin = plugin;
        }

        public void ActivatePlugin(string name)
        {
            if (Plugins.ContainsKey(name)) ActivePlugin = Plugins[name];
        }

        void CyclePlugin()
        {
            var index = PluginsList.IndexOf(ActivePlugin);
            index++;
            if (index >= PluginsList.Count) index = 0;
            ActivePlugin = PluginsList[index];
        }
        #endregion

        #region Inputs
        bool lastADown = false;
        bool lastSDown = false;
        bool lastDDown = false;
        bool lastWDown = false;
        bool lastCDown = false;
        bool lastSpaceDown = false;

        void TriggerInputs(TimeSpan timestamp)
        {
            if (Controller == null) return;
            if (!OverrideThrusters) return;
            if (!Active) return;
            var inputVecs = Controller.MoveIndicator;
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
        }

        void DoA(TimeSpan timestamp)
        {
            if (ActivePlugin != null) ActivePlugin.Do4(timestamp);
        }

        void DoS(TimeSpan timestamp)
        {
            if (ActivePlugin != null) ActivePlugin.Do5(timestamp);
        }

        void DoD(TimeSpan timestamp)
        {
            if (ActivePlugin != null) ActivePlugin.Do6(timestamp);
        }

        void DoW(TimeSpan timestamp)
        {
            if (ActivePlugin != null) ActivePlugin.Do8(timestamp);
        }

        void DoC(TimeSpan timestamp)
        {
            if (ActivePlugin != null) ActivePlugin.Do7(timestamp);
        }

        void DoSpace(TimeSpan timestamp)
        {
            if (ActivePlugin != null) ActivePlugin.Do3(timestamp);
        }

        void UpdateSwivels()
        {
            if (OverrideGyros)
            {
                ActiveLookingGlass.Pitch.TargetVelocityRPM = Controller.RotationIndicator[0] * 0.3f;
                ActiveLookingGlass.Yaw.TargetVelocityRPM = Controller.RotationIndicator[1] * 0.3f;
            }
        }
        #endregion

        #region utils
        public void AppendPaddedLine(int TotalLength, string text, StringBuilder builder)
        {
            int length = text.Length;
            if (length > TotalLength)
                builder.AppendLine(text.Substring(0, TotalLength));
            else if (length < TotalLength)
                builder.Append(text).Append(' ', TotalLength - length).AppendLine();
            else
                builder.AppendLine(text);
        }

        public int CustomMod(int n, int d)
        {
            return (n % d + d) % d;
        }

        public int DeltaSelection(int current, int total, bool positive)
        {
            if (total == 0) return 0;
            int newCurrent = current + (positive ? 1 : -1);
            if (newCurrent >= total) newCurrent = 0;
            if (newCurrent <= -1) newCurrent = total - 1;
            return newCurrent;
        }

        #endregion

        #region Intel
        public void ReportIntel(IFleetIntelligence intel, TimeSpan timestamp)
        {
            IntelProvider.ReportFleetIntelligence(intel, timestamp);
        }

        #endregion

        #region updates
        void UpdateUpdateFrequency()
        {
            UpdateFrequency = UpdateFrequency.Update10;
            if (ActiveLookingGlass != null) UpdateFrequency |= UpdateFrequency.Update1;
        }

        void UpdatePlugins(TimeSpan timestamp)
        {
            foreach (var kvp in Plugins)
            {
                if (ActiveLookingGlass != null && kvp.Value == ActivePlugin) kvp.Value.UpdateHUD(timestamp);
                kvp.Value.UpdateState(timestamp);
            }
        }

        void UpdateActiveLookingGlass()
        {
            if (AutoActivate)
            {
                ActiveLookingGlass = null;
            
                foreach (var lg in LookingGlasses)
                {
                    if (lg.PrimaryCamera.IsActive)
                    {
                        ActiveLookingGlass = lg;
                        lg.InterceptControls = true;
                    }
                    else
                    {
                        lg.InterceptControls = false;
                        if (OverrideGyros)
                        {
                            lg.Pitch.TargetVelocityRPM = 0;
                            lg.Yaw.TargetVelocityRPM = 0;
                        }
                    }
                }
            }

            bool interceptingControls = ActiveLookingGlass == null || !Active;
            if (OverrideThrusters) Controller.ControlThrusters = interceptingControls;
            if (OverrideGyros) TerminalPropertiesHelper.SetValue(Controller, "ControlGyros", interceptingControls);
        }
        #endregion

        public void GetDefaultSprites(List<MySprite> scratchpad)
        {
            var crosshairs = new MySprite(SpriteType.TEXTURE, "Cross", size: new Vector2(10f, 10f), color: new Color(1, 1, 1, 0.4f));
            crosshairs.Position = new Vector2(0, -2) + 512 / 2f;
            scratchpad.Add(crosshairs);
        }
    }


    public class LookingGlass : IControlIntercepter
    {
        #region IControlIntercepter
        public bool InterceptControls { get; set; }

        public IMyShipController Controller
        {
            get
            {
                return Network.Controller;
            }
        }
        #endregion

        public List<IMyTextPanel> LeftHUDs = new List<IMyTextPanel>();
        public List<IMyTextPanel> RightHUDs = new List<IMyTextPanel>();
        public List<IMyTextPanel> MiddleHUDs = new List<IMyTextPanel>();
        public IMyCameraBlock PrimaryCamera;
        public List<IMyCameraBlock> SecondaryCameras = new List<IMyCameraBlock>();

        public LookingGlassNetworkSubsystem Network;

        public IMyMotorStator Pitch;
        public IMyMotorStator Yaw;

        public bool IsOK(bool checkrotors)
        {
            if (LeftHUDs.Count == 0) return false;
            if (MiddleHUDs.Count == 0) return false;
            if (RightHUDs.Count == 0) return false;
            if (PrimaryCamera == null) return false;

            if (checkrotors && Pitch == null) return false;
            if (checkrotors && Yaw == null) return false;
            return true;
        }

        public void Initialize()
        {
            foreach (var LeftHUD in LeftHUDs)
            {
                LeftHUD.Alignment = TextAlignment.RIGHT;
                LeftHUD.FontSize = 0.55f;

                if (LeftHUD.BlockDefinition.SubtypeId == "SmallTextPanel") LeftHUD.TextPadding = 2;
                else LeftHUD.TextPadding = 9;
                LeftHUD.Font = "Monospace";
                LeftHUD.ContentType = ContentType.TEXT_AND_IMAGE;
            }

            foreach (var RightHUD in RightHUDs)
            {
                RightHUD.FontSize = 0.55f;
                RightHUD.TextPadding = 9;
                if (RightHUD.BlockDefinition.SubtypeId == "SmallTextPanel") RightHUD.TextPadding = 2;
                else RightHUD.TextPadding = 9;
                RightHUD.Font = "Monospace";
                RightHUD.ContentType = ContentType.TEXT_AND_IMAGE;
            }

            foreach (var MiddleHUD in MiddleHUDs)
            {
                MiddleHUD.ScriptBackgroundColor = new Color(1, 0, 0, 0);
                MiddleHUD.ContentType = ContentType.SCRIPT;
            }
        }

        public void Clear()
        {
            PrimaryCamera = null;
            LeftHUDs.Clear();
            RightHUDs.Clear();
            MiddleHUDs.Clear();
            Pitch = null;
            Yaw = null;
            SecondaryCameras.Clear();
        }


        public void AddPart(IMyTerminalBlock block)
        {
            if (block is IMyTextPanel)
            {
                if (block.CustomName.Contains("[SN-SM]"))
                    MiddleHUDs.Add((IMyTextPanel)block);
                if (block.CustomName.Contains("[SN-SL]"))
                    LeftHUDs.Add((IMyTextPanel)block);
                if (block.CustomName.Contains("[SN-SR]"))
                    RightHUDs.Add((IMyTextPanel)block);
            }

            if (block is IMyCameraBlock)
            {
                if (block.CustomName.Contains("[SN-C-S]"))
                {
                    var camera = (IMyCameraBlock)block;
                    camera.EnableRaycast = true;
                    SecondaryCameras.Add(camera);
                }
                else if (block.CustomName.Contains("[SN-C-P]"))
                    PrimaryCamera = (IMyCameraBlock)block;
            }

            if (block is IMyMotorStator)
            {
                var rotor = (IMyMotorStator)block;
                if (rotor.CustomName.Contains("Yaw"))
                    Yaw = rotor;
                else if (rotor.CustomName.Contains("Pitch"))
                    Pitch = rotor;
            }
        }

        #region Raycast
        public int Lidar_CameraIndex = 0;
        public MyDetectedEntityInfo LastDetectedInfo;

        public bool DoScan(TimeSpan timestamp)
        {
            IMyCameraBlock usingCamera = SecondaryCameras[Lidar_CameraIndex];

            double distance = 10000;
            if (usingCamera.RaycastDistanceLimit >= 0)
            {
                distance = Math.Min(distance, usingCamera.RaycastDistanceLimit);
            }
            LastDetectedInfo = usingCamera.Raycast(distance);

            Lidar_CameraIndex += 1;
            if (Lidar_CameraIndex == SecondaryCameras.Count) Lidar_CameraIndex = 0;

            if (LastDetectedInfo.Type == MyDetectedEntityType.Asteroid)
            {
                float radius = (float)(LastDetectedInfo.BoundingBox.Max - LastDetectedInfo.BoundingBox.Center).Length();
                var astr = new AsteroidIntel();
                astr.Radius = radius;
                astr.ID = LastDetectedInfo.EntityId;
                astr.Position = LastDetectedInfo.BoundingBox.Center;
                Network.ReportIntel(astr, timestamp);
            }
            else if ((LastDetectedInfo.Type == MyDetectedEntityType.LargeGrid || LastDetectedInfo.Type == MyDetectedEntityType.SmallGrid)
                && (LastDetectedInfo.Relationship == MyRelationsBetweenPlayerAndBlock.Enemies || LastDetectedInfo.Relationship == MyRelationsBetweenPlayerAndBlock.Neutral))
            {
                var intelDict = Network.IntelProvider.GetFleetIntelligences(timestamp);
                var key = MyTuple.Create(IntelItemType.Enemy, LastDetectedInfo.EntityId);
                var TargetIntel = intelDict.ContainsKey(key) ? (EnemyShipIntel)intelDict[key] : new EnemyShipIntel();
                TargetIntel.FromDetectedInfo(LastDetectedInfo, timestamp + Network.IntelProvider.CanonicalTimeDiff);
                Network.ReportIntel(TargetIntel, timestamp);
            }

            return true;
        }
        #endregion

        #region Display
        public const float kCameraToScreenSm = 1.06f;
        public const float kCameraToScreenLg = 1.6f;
        public const int kScreenSize = 512;

        public readonly Vector2 kMonospaceConstant = new Vector2(18.68108f, 28.8f);
        public readonly Vector2 kDebugConstant = new Vector2(18.68108f, 28.8f);

        const float kMinScale = 0.25f;
        const float kMaxScale = 0.5f;

        const float kMinDist = 1000;
        const float kMaxDist = 10000;

        [Flags]
        public enum IntelSpriteOptions
        {
            None = 0,
            EmphasizeWithBrackets = 1 << 0,
            EmphasizeWithDashes = 1 << 1,
            ShowDist = 1 << 2,
            ShowName = 1 << 3,
            ShowTruncatedName = 1 << 4,
            Large = 1 << 5,
            Small = 1 << 6,
            Circle = 1 << 7,
            EmphasizeWithCross = 1 << 8,
            NoCenter = 1 << 9,
            ShowLastDetected = 1 << 10,
        }

        static string textPanel = "MyObjectBuilder_TextPanel/";

//         MyDefinitionId L1x1 = MyDefinitionId.Parse("MyObjectBuilder_TextPanel/LargeLCDPanel");
//         MyDefinitionId S1x1 = MyDefinitionId.Parse("MyObjectBuilder_TextPanel/SmallLCDPanel");
        MyDefinitionId Large3x3 = MyDefinitionId.Parse(textPanel + "LargeLCDPanel3x3");
        MyDefinitionId Large5x3 = MyDefinitionId.Parse(textPanel + "LargeLCDPanel5x3");
        MyDefinitionId Large5x5 = MyDefinitionId.Parse(textPanel + "LargeLCDPanel5x5");
        MyDefinitionId LargeTransparentDef = MyDefinitionId.Parse(textPanel + "TransparentLCDLarge");
        MyDefinitionId SmallTransparentDef = MyDefinitionId.Parse(textPanel + "TransparentLCDSmall");

        // The panel does not use up the full block width. The Width output is for this reduced effective width.
        public Vector3D GetPanelSurfacePositionAndWidth(IMyTextPanel panel, out Vector2 Width)
        {

            // These were measured using the carbon fiber texture on blocks. Small grid has 32 hatches, large grid has 48.
            var pos = panel.GetPosition();
            var forward = panel.WorldMatrix.Forward;
            if ( panel.BlockDefinition == LargeTransparentDef)
            {
                
                // 2.50 - 8*(2.5/48)
                Width.X = 2.0833f;
                Width.Y = Width.X;
                // 1.25 - 1*(2.5/48)
                return pos + forward * 1.198;
            }
            else if (panel.BlockDefinition == SmallTransparentDef)
            {
                // .50 - 2*(.5/32) ~ could be 1.5
                // Measurements After Projection
                // -y .23112
                // +y .23112
                // -x .2230
                // Width = 0.46875f; // measured via carbon fiber // .25 - 1*(.5/32)
                Width.X = 0.49f; //0.4460f;
                Width.Y = Width.X; 
                return pos + forward * .234375;
            }
            else if (panel.BlockDefinition == Large3x3)
            {
                Width.X = 7.3958f;
                Width.Y = Width.X;
                return pos + forward * .234375;
            }
            else if (panel.BlockDefinition == Large5x3)
            {
                Width.X = 12.49f;
                Width.Y = 7.3958f;
                return pos + forward * .234375;
            }
            else if (panel.BlockDefinition == Large5x5)
            {
                Width.X = 12.49f;
                Width.Y = Width.X;
                return pos + forward * .234375;
            }

            var size = panel.CubeGrid.GridSizeEnum;
            if (panel.CubeGrid.GridSizeEnum == MyCubeSize.Large )
            {
                // panel.Min & panel.Max may be useful here for general case
                // 2.50 - 2*(2.5/48)
                Width.X = 2.3958f;
                Width.Y = Width.X;
                // 1.25 - 4.3*(2.5/48)
                return pos + forward * 1.026;
            }
            else
            {
                // .50 - 5*(.5/32) ~ 
                Width.X = 0.42188f;
                Width.Y = Width.X;
                // .25 - 5.25*(.5/32)
                return pos + forward * .16797;
            }
        }

        Vector3D GetAdjustedCamera(IMyCameraBlock aCamera, Vector3D aForward)
        {
            // .25f for large grid uncertain
            return PrimaryCamera.GetPosition() + aForward * (aCamera.CubeGrid.GridSizeEnum == MyCubeSize.Large ? .25f : .20f);
        }
        /*
        bool FleetIntelWorldCoordsToPanelCoordsV1(IMyTextPanel panel, IFleetIntelligence intel, TimeSpan localTime, out float distance, out Vector2 outNormalizedScreenPosition, out Vector2 outScreenPosition)
        {
            var worldDirection = intel.GetPositionFromCanonicalTime(localTime + Network.IntelProvider.CanonicalTimeDiff) - PrimaryCamera.WorldMatrix.Translation;
            var camDirection = panel.WorldMatrix.Translation - PrimaryCamera.WorldMatrix.Translation;
            camDirection.Normalize();
            var bodyPosition = Vector3D.TransformNormal(worldDirection, MatrixD.Transpose(MatrixD.CreateWorld(PrimaryCamera.Position, camDirection, panel.WorldMatrix.Up)));
            var screenPosition = new Vector2(-1 * (float)(bodyPosition.X / bodyPosition.Z), (float)(bodyPosition.Y / bodyPosition.Z));

            // It is behind the camera.
            if (bodyPosition.Dot(Vector3D.Forward) > 0)
            {
                distance = (float)bodyPosition.Length();
                outNormalizedScreenPosition = screenPosition * (PrimaryCamera.CubeGrid.GridSizeEnum == MyCubeSize.Small ? kCameraToScreenSm : kCameraToScreenLg);

                outScreenPosition = (outNormalizedScreenPosition + .5f) * kScreenSize;
                outScreenPosition.X = Math.Max(30, Math.Min(kScreenSize - 30, outScreenPosition.X));
                outScreenPosition.Y = Math.Max(30, Math.Min(kScreenSize - 30, outScreenPosition.Y));                
                //              v.Y -= scale * (kMonospaceConstant.Y + 10) / 2;

                return true;
            }

            outScreenPosition = Vector2.PositiveInfinity;
            outNormalizedScreenPosition = outScreenPosition;
            distance = outScreenPosition.X;

            return false;
        }
        bool FleetIntelWorldCoordsToPanelCoordsV2(IMyTextPanel panel, IFleetIntelligence intel, TimeSpan localTime, out float distance, out Vector2 outNormalizedScreenPosition, out Vector2 outScreenPosition)
        {
            outNormalizedScreenPosition = Vector2.PositiveInfinity;
            outScreenPosition = outNormalizedScreenPosition;

            Vector3D camPosition, panelPosition,
            camToTarget, camToPanel,
            targetNormal, forwardNormal,
            intersection, camToPanelTargetPos,
            pointAtPanel;

            Vector2 panelWidth;
            panelPosition = GetPanelSurfacePositionAndWidth(panel, out panelWidth);
            //panelPosition = panel.GetPosition() + panel.WorldMatrix.Forward * .25;

            //camPosition = PrimaryCamera.GetPosition() + PrimaryCamera.WorldMatrix.Backward * .25;
            camPosition = PrimaryCamera.GetPosition() + PrimaryCamera.WorldMatrix.Forward * .25;
            camToTarget = intel.GetPositionFromCanonicalTime(localTime + Network.IntelProvider.CanonicalTimeDiff) - camPosition;
            distance = (float)camToTarget.Length();
            camToPanel = panelPosition - camPosition;
            targetNormal = Vector3D.Normalize(camToTarget);
            forwardNormal = Vector3D.Normalize(camToPanel);
            var panelPlane = new PlaneD(panelPosition, panel.WorldMatrix.Forward);
            if (!TrigHelpers.PlaneIntersectionD(panelPlane, camPosition, targetNormal, out intersection))
                return false;

            camToPanelTargetPos = intersection - camPosition;
            pointAtPanel = Vector3D.ProjectOnPlane(ref camToPanelTargetPos, ref forwardNormal);

            var rot = MatrixD.CreateWorld(Vector3D.Zero, panel.WorldMatrix.Forward, panel.WorldMatrix.Up);
            // if it is "in view", this pointAtPanel value should not exceed +/- panelWidth/2.
            pointAtPanel = Vector3D.Transform(pointAtPanel, MatrixD.Transpose(rot));
//            Network.Context.Log.Debug("P:" + pointAtPanel.ToString());
            var edgeClamp = panelWidth / 2; // panelwidth / 2 * .9
            outNormalizedScreenPosition = new Vector2(MathHelper.Clamp((float)pointAtPanel.X, -edgeClamp.X, edgeClamp.X), -1 * MathHelper.Clamp((float)pointAtPanel.Y, -edgeClamp.Y, edgeClamp.Y));
            //var surface = ((surface * (PrimaryCamera.CubeGrid.GridSizeEnum == MyCubeSize.Small ? kCameraToScreenSm : kCameraToScreenLg)) + new Vector2(0.5f, 0.5f)) * kScreenSize;
            outNormalizedScreenPosition /= panelWidth; // normalize to screen origin space [-.5,+.5], from meters
  //          Network.Context.Log.Debug("O:" + outNormalizedScreenPosition.ToString());
            outScreenPosition = outNormalizedScreenPosition +  0.5f;           // normalize to screen draw space [0,1]
//            Network.Context.Log.Debug("C:" + outScreenPosition.ToString());
            outScreenPosition *= kScreenSize;    // respace to screen draw pixels [0,512]

            return true;
        }
*/
        bool FleetIntelWorldCoordsToPanelCoordsV3(IMyTextPanel panel, IFleetIntelligence intel, TimeSpan localTime, out float distance, out Vector2 outNormalizedScreenPosition, out Vector2 outScreenPosition)
        {
            outNormalizedScreenPosition = Vector2.PositiveInfinity;
            outScreenPosition = outNormalizedScreenPosition;

            Vector3D camPosition, panelPosition,
            targetInPanelSpace, camInPanelSpace,
            panelSpaceTargetVector, pointAtPanel;

//            Network.Context.Draw.RemoveAll();

            Vector2 panelWidth;
            panelPosition = GetPanelSurfacePositionAndWidth(panel, out panelWidth);
//            Network.Context.Draw.AddPoint(panelPosition, Color.Green, 0.002f);

            camPosition = GetAdjustedCamera(PrimaryCamera, PrimaryCamera.WorldMatrix.Forward);
//            Network.Context.Draw.AddPoint(camPosition, Color.Green, 0.002f);
            MatrixD adjustedPanelMatrix = MatrixD.CreateWorld(panelPosition, panel.WorldMatrix.Forward, panel.WorldMatrix.Up);
            var adjustedPanelMatrixInvert = MatrixD.Invert(adjustedPanelMatrix);

            camInPanelSpace = Vector3D.Transform(camPosition, adjustedPanelMatrixInvert);

            targetInPanelSpace = Vector3D.Transform(intel.GetPositionFromCanonicalTime(localTime + Network.IntelProvider.CanonicalTimeDiff), adjustedPanelMatrixInvert);
//            Network.Context.Draw.AddLine(camPosition, intel.GetPositionFromCanonicalTime(localTime + Network.IntelProvider.CanonicalTimeDiff), Color.Red, 0.001f, -1f);

            panelSpaceTargetVector = targetInPanelSpace - camInPanelSpace;
            distance = (float)panelSpaceTargetVector.Length();

            // position is zero since we our test points are already in panel space.
            // targetInPanelSpace
//             if (Vector3D.Forward.Dot(panelSpaceTargetVector) == 0 ) // TODO Epsilon where?
//                 return false;
// 
//             pointAtPanel = Vector3D.ProjectOnPlane(ref panelSpaceTargetVector,ref Vector3D.Forward);
            var panelPlane = new PlaneD(Vector3D.Zero, Vector3.Forward);
            if (!TrigHelpers.PlaneIntersectionD(panelPlane, camInPanelSpace, Vector3D.Normalize(panelSpaceTargetVector), out pointAtPanel))
                return false;
//             Network.Context.Log.Debug("P:" + pointAtPanel.ToString());
//             Network.Context.Draw.AddOBB(new MyOrientedBoundingBoxD(new BoundingBoxD(new Vector3D(-panelWidth / 2f, -panelWidth / 2f, -.002f), new Vector3D(panelWidth / 2f, panelWidth / 2f, .002f)), adjustedPanelMatrix), Color.Blue, thickness: .002f);
//             Network.Context.Draw.AddPoint(Vector3D.Transform(pointAtPanel, adjustedPanelMatrix), Color.Orange, 0.002f);
//            var edgeClamp = panelWidth / 2; // panelwidth / 2 * .9

            var edgeClamp = (panelWidth * .9f) / 2f; // .9f is the clamp area
            outNormalizedScreenPosition = new Vector2(MathHelper.Clamp((float)pointAtPanel.X, -edgeClamp.X, edgeClamp.X), -1 * MathHelper.Clamp((float)pointAtPanel.Y, -edgeClamp.Y, edgeClamp.Y));
            //var surface = ((surface * (PrimaryCamera.CubeGrid.GridSizeEnum == MyCubeSize.Small ? kCameraToScreenSm : kCameraToScreenLg)) + new Vector2(0.5f, 0.5f)) * kScreenSize;
            outNormalizedScreenPosition /= panelWidth; // normalize to screen origin space [-.5,+.5], from meters
                                                       //            Network.Context.Log.Debug("O:" + outNormalizedScreenPosition.ToString());
            outScreenPosition = outNormalizedScreenPosition + 0.5f;// normalize to screen draw space [0,1]
                                                                   //            Network.Context.Log.Debug("C:" + outScreenPosition.ToString());
            outScreenPosition *= kScreenSize;    // respace to screen draw pixels [0,512]

            return true;
        }

//        Vector2 FleetIntelWorldCoordsToPanelCoordsVFailure(IMyTextPanel panel, IFleetIntelligence intel, TimeSpan localTime)
//        {
//            return new Vector2(float.MaxValue, float.MaxValue);
            //            v - new Vector2(0.5f, 0.5f) * kScreenSize;
            //             dot = targetNormal.Dot(forwardNormal);
            //             angle = Math.Acos(dot);

            //             var edgeLength = surfaceDistance / dot;
            //             var target = targetNormal * edgeLength;
            //             Vector3D.ProjectOnPlane(target, target
            //             surfaceDistance.
            //             Vector3D.Fract


            //             {
            //                 var camPosition = PrimaryCamera.GetPosition() + PrimaryCamera.WorldMatrix.Backward * .25;
            //                 var camToTarget = intel.GetPositionFromCanonicalTime(localTime + Network.IntelProvider.CanonicalTimeDiff) - camPosition;
            //                 double[] panelOffset = new double[] { 1.0, 0.20 };
            //                 var panelPosition = panel.GetPosition() + panel.WorldMatrix.Forward * .25;
            //                 var panelPlane = new Plane(panelPosition, panel.WorldMatrix.Forward);
            //                 Vector3 Intersection;
            //                 camToTarget.Normalize();
            //                 TrigHelpers.PlaneIntersection(panelPlane, camPosition, camToTarget, out Intersection);
            //                 Network.Context.Log.Debug("I:" + Intersection.ToString());
            //                 //Network.Context.Log.Debug("P:" + panel.GetPosition());
            //                 Vector3 local = Intersection - panelPosition;
            //                 //            Vector3 local = Vector3.Transform(Intersection, MatrixD.Transpose(panel.WorldMatrix));
            //                 Network.Context.Log.Debug("L:" + local.ToString());
            //                 local *= (256f / panel.CubeGrid.GridSize);
            //                 Network.Context.Log.Debug("S:" + local.ToString());
            // 
            //                 var d = new MySprite(SpriteType.TEXTURE, "Cross", size: new Vector2(30f, 30f), color: kFriendlyBlue);
            //                 d.Position = new Vector2(256f - local.Y, 256f - local.Z);
            //                 scratchpad.Add(d);
            //             }

            //.Transform( Intersection = Intersection - panel.GetPosition() + panel.WorldMatrix.Backward 

            //             var CursorDirection = Intersection - Panels[Vector2I.Zero].GetPosition() + Panels[Vector2I.Zero].WorldMatrix.Backward * 1.25 + Panels[Vector2I.Zero].WorldMatrix.Right * 1.25;
            //             var CursorDist = CursorDirection.Length();
            //             CursorDirection.Normalize();

            // LCD wall configuration:
            // |  |    |
            // |  |    |
            // |  |    |

            // Cursor position, originating in the middle of the LCD wall, with unit in meters
            //         CursorPos = Vector3D.TransformNormal(CursorDirection, MatrixD.Transpose(Panels[Vector2I.Zero].WorldMatrix)) * CursorDist; 
            //         CursorPos2D = new Vector2((float)CursorPos.X, (float)CursorPos.Y); 
            //             if (true)
            //             {
            //                 var targetToCam = intel.GetPositionFromCanonicalTime(localTime + Network.IntelProvider.CanonicalTimeDiff) - PrimaryCamera.WorldMatrix.Translation;
            // 
            // 
            //                 var WallPlane = new Plane(Panels[Vector2I.Zero].GetPosition() - Panels[Vector2I.Zero].WorldMatrix.Backward, Panels[Vector2I.Zero].WorldMatrix.Backward);
            //                 Vector3 Intersection;
            //                 TrigHelpers.PlaneIntersection(WallPlane, Turret.GetPosition() + Turret.WorldMatrix.Up * 0.579 + Turret.WorldMatrix.Backward * 0.068, TurretAimRay, out Intersection);
            //             }
            //             else
            //             {

//        }
        public Vector2 FleetIntelItemToSprites(IMyTextPanel panel, IFleetIntelligence intel, TimeSpan localTime, Color color, ref List<MySprite> scratchpad, IntelSpriteOptions properties = IntelSpriteOptions.None)
        {
            var output = Vector2.PositiveInfinity;
            if (intel.ID == Network.Context.Reference.CubeGrid.EntityId)
                return output;

            float dist;
            Vector2 screenPos;
            if (!FleetIntelWorldCoordsToPanelCoordsV3(panel, intel, localTime, out dist, out output, out screenPos))
                return output;


            //             var worldDirection = intel.GetPositionFromCanonicalTime(localTime + Network.IntelProvider.CanonicalTimeDiff) - PrimaryCamera.WorldMatrix.Translation;
            //             var direction = panel.WorldMatrix.Translation - PrimaryCamera.WorldMatrix.Translation;
            //             direction.Normalize();
            //             var bodyPosition = Vector3D.TransformNormal(worldDirection, MatrixD.Transpose(MatrixD.CreateWorld(PrimaryCamera.Position, direction, panel.WorldMatrix.Up)));
            //             var screenPosition = new Vector2(-1 * (float)(bodyPosition.X / bodyPosition.Z), (float)(bodyPosition.Y / bodyPosition.Z));
            //             if (bodyPosition.Dot(Vector3D.Forward) < 0)
            //                 return new Vector2(float.MaxValue, float.MaxValue);

            //            var v = ((screenPosition * (PrimaryCamera.CubeGrid.GridSizeEnum == MyCubeSize.Small ? kCameraToScreenSm : kCameraToScreenLg)) + new Vector2(0.5f, 0.5f)) * kScreenSize;

            var v = screenPos;



            float scale = kMaxScale;
            if (dist > kMaxDist) scale = kMinScale;
            else if (dist > kMinDist) scale = kMinScale + (kMaxScale - kMinScale) * (kMaxDist - dist) / (kMaxDist - kMinDist);
            if ((properties & IntelSpriteOptions.Large) != 0) scale *= 1.5f;
            else if ((properties & IntelSpriteOptions.Small) != 0) scale *= 0.5f;

            string indicatorText;
            bool circle = (properties & IntelSpriteOptions.Circle) != 0;
            bool brackets = (properties & IntelSpriteOptions.EmphasizeWithBrackets) != 0;
            bool dashes = (properties & IntelSpriteOptions.EmphasizeWithDashes) != 0;
            bool nocenter = (properties & IntelSpriteOptions.NoCenter) != 0;
            if (nocenter)
            {
                if (brackets && dashes) indicatorText = "-[ ]-";
                else if (brackets) indicatorText = "[ ]";
                else if (dashes) indicatorText = "- -";
                else indicatorText = " ";
            }
            else if (circle)
            {
                if (brackets && dashes) indicatorText = "-[O]-";
                else if (brackets) indicatorText = "[O]";
                else if (dashes) indicatorText = "-O-";
                else indicatorText = "O";
            }
            else
            {
                if (brackets && dashes) indicatorText = "-[><]-";
                else if (brackets) indicatorText = "[><]";
                else if (dashes) indicatorText = "-><-";
                else indicatorText = "><";
            }

            bool cross = (properties & IntelSpriteOptions.EmphasizeWithCross) != 0;
            if (cross)
            {
                var c = new MySprite(SpriteType.TEXTURE, "Cross", size: new Vector2(30f, 30f));
                c.Position = v;
                scratchpad.Add(c);                
            }

//             var CenteredScreenPosition = v - new Vector2(0.5f, 0.5f) * kScreenSize;
//             v.X = Math.Max(30, Math.Min(kScreenSize - 30, v.X));
//             v.Y = Math.Max(30, Math.Min(kScreenSize - 30, v.Y));
            
            v.Y -= scale * (kMonospaceConstant.Y + 10) / 2;
            
            var indicator = MySprite.CreateText(indicatorText, "Monospace", color, scale, TextAlignment.CENTER);
            indicator.Position = v;
            scratchpad.Add(indicator);

            v.Y += kMonospaceConstant.Y * scale + 0.2f;

            var builder = Network.Context.StringBuilder;
            builder.Clear();

            if ((properties & IntelSpriteOptions.ShowDist) != 0)
                builder.AppendLine((int)dist + " m");

            if ((properties & IntelSpriteOptions.ShowName) != 0 || (properties & IntelSpriteOptions.ShowTruncatedName) != 0)
            {
                var name = intel.DisplayName != null ? intel.DisplayName : "null";
                if (name.StartsWith("L-") || name.StartsWith("S-"))
                    name = name.Substring(0, Math.Min(name.Length, 8));
                builder.AppendLine(name);
            }
                

            if ((properties & IntelSpriteOptions.ShowLastDetected) != 0 && (intel is EnemyShipIntel))
            {
                var esi = (EnemyShipIntel)intel;
                var timediff = localTime - esi.CurrentCanonicalTime;
                builder.AppendLine((int)timediff.TotalMilliseconds + " ms");
            }

            if (builder.Length > 0)
            {
                var infoSprite = MySprite.CreateText(
                    builder.ToString(),
                    "Debug",
                    new Color(1, 1, 1, 0.5f),
                    (properties & IntelSpriteOptions.Small) != 0 ? 0.3f : 0.4f,
                    TextAlignment.CENTER);
                infoSprite.Position = v;
                scratchpad.Add(infoSprite);
            }

            return output;
        }

        public readonly Color kFriendlyBlue = new Color(140, 140, 255, 100);
        public readonly Color kEnemyRed = new Color(255, 140, 140, 100);
        public readonly Color kWaypointOrange = new Color(255, 210, 180, 100);

        public readonly Color kFocusedColor = new Color(0.9f, 0.9f, 1f);
        public readonly Color kUnfocusedColor = new Color(0.7f, 0.7f, 1f);
        #endregion
    }

    public interface ILookingGlassPlugin
    {
        void Do4(TimeSpan localTime);
        void Do5(TimeSpan localTime);
        void Do6(TimeSpan localTime);
        void Do8(TimeSpan localTime);
        void Do7(TimeSpan localTime);
        void Do3(TimeSpan localTime);

        void UpdateHUD(TimeSpan localTime);
        void UpdateState(TimeSpan localTime);

        void Setup();

        LookingGlassNetworkSubsystem Host { get; set; }
    }

    public class LookingGlassPlugin_Command : ILookingGlassPlugin
    {
        #region ILookingGlassPlugin
        public LookingGlassNetworkSubsystem Host { get; set; }
        public void Do4(TimeSpan localTime)
        {
            if (CurrentUIMode == UIMode.SelectAgent)
            {
                AgentSelection_CurrentClass = AgentClassAdd(AgentSelection_CurrentClass, -1);
                AgentSelection_CurrentIndex = 0;
            }
            else if (CurrentUIMode == UIMode.SelectTarget)
            {
                TargetSelection_TaskTypesIndex = Host.DeltaSelection(TargetSelection_TaskTypesIndex, TargetSelection_TaskTypes.Count, false);
            }
        }

        public void Do5(TimeSpan localTime)
        {
            if (CurrentUIMode == UIMode.SelectAgent)
            {
                AgentSelection_CurrentIndex = Host.DeltaSelection(AgentSelection_CurrentIndex, AgentSelection_FriendlyAgents.Count, true);
            }
            else if (CurrentUIMode == UIMode.SelectTarget)
            {
                TargetSelection_TargetIndex = Host.DeltaSelection(TargetSelection_TargetIndex, TargetSelection_Targets.Count + TaskTypeToSpecialTargets[TargetSelection_TaskTypes[TargetSelection_TaskTypesIndex]].Count(), true);
            }
            else if (CurrentUIMode == UIMode.SelectWaypoint)
            {
                CursorDist -= 200;
                if (CursorDist < 200) CursorDist = 200;
            }
        }

        public void Do6(TimeSpan localTime)
        {
            if (CurrentUIMode == UIMode.SelectAgent)
            {
                AgentSelection_CurrentClass = AgentClassAdd(AgentSelection_CurrentClass, 1);
                AgentSelection_CurrentIndex = 0;
            }
            else if (CurrentUIMode == UIMode.SelectTarget)
            {
                TargetSelection_TaskTypesIndex = Host.DeltaSelection(TargetSelection_TaskTypesIndex, TargetSelection_TaskTypes.Count, true);
            }
        }
        public void Do8(TimeSpan localTime)
        {
            if (CurrentUIMode == UIMode.SelectAgent)
            {
                AgentSelection_CurrentIndex = Host.DeltaSelection(AgentSelection_CurrentIndex, AgentSelection_FriendlyAgents.Count, false);
            }
            else if (CurrentUIMode == UIMode.SelectTarget)
            {
                TargetSelection_TargetIndex = Host.DeltaSelection(TargetSelection_TargetIndex, TargetSelection_Targets.Count + TaskTypeToSpecialTargets[TargetSelection_TaskTypes[TargetSelection_TaskTypesIndex]].Count(), false);
            }
            else if (CurrentUIMode == UIMode.SelectWaypoint)
            {
                CursorDist += 200;
            }
        }

        public void Do7(TimeSpan localTime)
        {
            if (CurrentUIMode == UIMode.SelectTarget)
            {
                CurrentUIMode = UIMode.SelectAgent;
            }
            else if(CurrentUIMode == UIMode.SelectWaypoint || CurrentUIMode == UIMode.Designate)
            {
                CurrentUIMode = UIMode.SelectTarget;
            }
        }

        public void Do3(TimeSpan localTime)
        {
            if (CurrentUIMode == UIMode.SelectAgent)
            {
                if (AgentSelection_CurrentIndex < AgentSelection_FriendlyAgents.Count && AgentSelection_CurrentClass != AgentClass.None)
                    CurrentUIMode = UIMode.SelectTarget;
            }
            else if(CurrentUIMode == UIMode.SelectTarget)
            {
                if (TargetSelection_TargetIndex < TaskTypeToSpecialTargets[TargetSelection_TaskTypes[TargetSelection_TaskTypesIndex]].Count())
                {
                    // Special handling
                    string SpecialCommand = TaskTypeToSpecialTargets[TargetSelection_TaskTypes[TargetSelection_TaskTypesIndex]][TargetSelection_TargetIndex];
                    if (SpecialCommand == "CURSOR")
                    {
                        CurrentUIMode = UIMode.SelectWaypoint;
                    }
                    if (SpecialCommand == "HOME")
                    {
                        SendCommand(MyTuple.Create(IntelItemType.NONE, (long)0), localTime);
                        CurrentUIMode = UIMode.SelectAgent;
                    }
                    if (SpecialCommand == "DESIGNATE")
                    {
                        CurrentUIMode = UIMode.Designate;
                    }
                    if (SpecialCommand == "CLEAR")
                    {
                        SendCommand(MyTuple.Create(IntelItemType.NONE, (long)0), localTime);
                        CurrentUIMode = UIMode.SelectAgent;
                    }
                }
                else if (TargetSelection_TargetIndex < TaskTypeToSpecialTargets[TargetSelection_TaskTypes[TargetSelection_TaskTypesIndex]].Count() + TargetSelection_Targets.Count())
                {
                    SendCommand(TargetSelection_Targets[TargetSelection_TargetIndex - TaskTypeToSpecialTargets[TargetSelection_TaskTypes[TargetSelection_TaskTypesIndex]].Count()], localTime);
                    CurrentUIMode = UIMode.SelectAgent;
                }
            }
            else if (CurrentUIMode == UIMode.SelectWaypoint)
            {
                Waypoint w = GetWaypoint();
                w.MaxSpeed = 100;
                Host.ReportIntel(w, localTime);
                SendCommand(w, localTime);
            }
            else if (CurrentUIMode == UIMode.Designate)
            {
                Host.ActiveLookingGlass.DoScan(localTime);
                if (!Host.ActiveLookingGlass.LastDetectedInfo.IsEmpty() && Host.ActiveLookingGlass.LastDetectedInfo.Type == MyDetectedEntityType.Asteroid)
                {
                    var w = new Waypoint();
                    w.Position = (Vector3D)Host.ActiveLookingGlass.LastDetectedInfo.HitPosition;
                    w.Direction = Host.ActiveLookingGlass.PrimaryCamera.WorldMatrix.Backward;
                    Host.ReportIntel(w, localTime);
                    SendCommand(w, localTime);
                }
                CurrentUIMode = UIMode.SelectAgent;
            }
        }


        public void Setup()
        {
        }

        public void UpdateHUD(TimeSpan localTime)
        {
            DrawMiddleHUD(localTime);
            DrawAgentSelectionUI(localTime);
            DrawTargetSelectionUI(localTime);
        }

        public void UpdateState(TimeSpan localTime)
        {
        }
        #endregion

        StringBuilder Builder = new StringBuilder();

        enum UIMode
        {
            SelectAgent,
            SelectTarget,
            SelectWaypoint,
            Designate,
        }
        UIMode CurrentUIMode = UIMode.SelectAgent;

        List<MySprite> SpriteScratchpad = new List<MySprite>();

        #region SelectAgent
        void DrawAgentSelectionUI(TimeSpan timestamp)
        {
            Builder.Clear();

            int kRowLength = 19;
            int kMenuRows = 16;

            Builder.AppendLine("===== COMMAND =====");
            Builder.AppendLine();
            Builder.Append(AgentClassTags[AgentClassAdd(AgentSelection_CurrentClass, -1)]).Append("    [").Append(AgentClassTags[AgentSelection_CurrentClass]).Append("]    ").AppendLine(AgentClassTags[AgentClassAdd(AgentSelection_CurrentClass, +1)]);
            if (CurrentUIMode == UIMode.SelectAgent) Builder.AppendLine("[<4]           [6>]");
            else Builder.AppendLine();

            Builder.AppendLine();

            AgentSelection_FriendlyAgents.Clear();

            foreach (IFleetIntelligence intel in Host.IntelProvider.GetFleetIntelligences(timestamp).Values)
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
                    if (i == AgentSelection_CurrentIndex) Builder.Append(">> ");
                    else Builder.Append("   ");

                    Host.AppendPaddedLine(kRowLength - 3, intel.DisplayName, Builder);
                }
                else
                {
                    Builder.AppendLine();
                }
            }

            Builder.AppendLine("==== SELECTION ====");

            if (AgentSelection_CurrentIndex < AgentSelection_FriendlyAgents.Count)
            {
                Host.AppendPaddedLine(kRowLength, AgentSelection_FriendlyAgents[AgentSelection_CurrentIndex].DisplayName, Builder);
                if (CurrentUIMode == UIMode.SelectAgent) Host.AppendPaddedLine(kRowLength, "[NUM 0] SELECT TGT", Builder);
            }
            else
            {
                Host.AppendPaddedLine(kRowLength, "NONE SELECTED", Builder);
            }

            foreach (var screen in Host.ActiveLookingGlass.LeftHUDs)
            {
                screen.FontColor = CurrentUIMode == UIMode.SelectAgent ? Host.ActiveLookingGlass.kFocusedColor : Host.ActiveLookingGlass.kUnfocusedColor;
                screen.WriteText(Builder.ToString());
            }
        }

        AgentClass AgentSelection_CurrentClass = AgentClass.Drone;
        int AgentSelection_CurrentIndex = 0;
        List<FriendlyShipIntel> AgentSelection_FriendlyAgents = new List<FriendlyShipIntel>();

        Dictionary<AgentClass, string> AgentClassTags = new Dictionary<AgentClass, string>
        {
            { AgentClass.None, "N/A" },
            { AgentClass.Drone, "DRN" },
            { AgentClass.Fighter, "FTR" },
            { AgentClass.Bomber, "BMR" },
            { AgentClass.Miner, "MNR" },
        };

        AgentClass AgentClassAdd(AgentClass agentClass, int places = 1)
        {
            return (AgentClass)Host.CustomMod((int)agentClass + places, (int)AgentClass.Last);
        }
        #endregion

        #region SelectTarget
        Dictionary<TaskType, string> TaskTypeTags = new Dictionary<TaskType, string>
        {
            { TaskType.None, "N/A" },
            { TaskType.Move, "MOV" },
            { TaskType.SmartMove, "SMV" },
            { TaskType.Attack, "ATK" },
            { TaskType.Picket, "DEF" },
            { TaskType.Dock, "DOK" },
            { TaskType.SetHome, "HOM" },
            { TaskType.Mine, "MNE" }
        };

        Dictionary<TaskType, IntelItemType> TaskTypeToTargetTypes = new Dictionary<TaskType, IntelItemType>
        {
            { TaskType.None, IntelItemType.NONE},
            { TaskType.Move, IntelItemType.Waypoint},
            { TaskType.SmartMove, IntelItemType.Waypoint },
            { TaskType.Attack, IntelItemType.Enemy | IntelItemType.Waypoint },
            { TaskType.Picket, IntelItemType.Waypoint },
            { TaskType.Dock, IntelItemType.NONE },
            { TaskType.SetHome, IntelItemType.NONE },
            { TaskType.Mine, IntelItemType.Waypoint }
        };

        Dictionary<TaskType, string[]> TaskTypeToSpecialTargets = new Dictionary<TaskType, string[]>
        {
            { TaskType.None, new string[0]},
            { TaskType.Move, new string[1] { "CURSOR" }},
            { TaskType.SmartMove, new string[1] { "CURSOR" }},
            { TaskType.Attack, new string[1] { "CURSOR" }},
            { TaskType.Picket, new string[1] { "CURSOR" }},
            { TaskType.Dock, new string[1] { "HOME" }},
            { TaskType.SetHome, new string[1] { "CLEAR" }  },
            { TaskType.Mine, new string[1] { "DESIGNATE" }}
        };

        List<TaskType> TargetSelection_TaskTypes = new List<TaskType>();
        int TargetSelection_TaskTypesIndex = 0;
        List<IFleetIntelligence> TargetSelection_Targets = new List<IFleetIntelligence>();
        int TargetSelection_TargetIndex;

        void DrawTargetSelectionUI(TimeSpan timestamp)
        {
            Builder.Clear();

            foreach (var screen in Host.ActiveLookingGlass.RightHUDs)
            {
                screen.FontColor = CurrentUIMode == UIMode.SelectTarget ? Host.ActiveLookingGlass.kFocusedColor : Host.ActiveLookingGlass.kUnfocusedColor;
            }

            Builder.AppendLine("=== SELECT TASK ===");

            Builder.AppendLine();

            if (AgentSelection_CurrentIndex >= AgentSelection_FriendlyAgents.Count)
            {
                foreach (var screen in Host.ActiveLookingGlass.RightHUDs)
                {
                    screen.WriteText(Builder.ToString());
                }
                CurrentUIMode = UIMode.SelectAgent;
                return;
            }

            var Agent = AgentSelection_FriendlyAgents[AgentSelection_CurrentIndex];
            TargetSelection_TaskTypes.Clear();

            for (int i = 0; i < 30; i++)
            {
                if (((TaskType)(1 << i) & Agent.AcceptedTaskTypes) != 0)
                    TargetSelection_TaskTypes.Add((TaskType)(1 << i));
            }

            if (TargetSelection_TaskTypes.Count == 0)
            {
                foreach (var screen in Host.ActiveLookingGlass.RightHUDs)
                {
                    screen.WriteText(Builder.ToString());
                }
                return;
            }
            if (TargetSelection_TaskTypesIndex >= TargetSelection_TaskTypes.Count) TargetSelection_TaskTypesIndex = 0;

            Builder.Append(TaskTypeTags[TargetSelection_TaskTypes[Host.CustomMod(TargetSelection_TaskTypesIndex - 1, TargetSelection_TaskTypes.Count)]]).
                Append("    [").Append(TaskTypeTags[TargetSelection_TaskTypes[TargetSelection_TaskTypesIndex]]).Append("]    ").
                AppendLine(TaskTypeTags[TargetSelection_TaskTypes[Host.CustomMod(TargetSelection_TaskTypesIndex + 1, TargetSelection_TaskTypes.Count)]]);

            if (CurrentUIMode == UIMode.SelectTarget) Builder.AppendLine("[<4]           [6>]");
            else Builder.AppendLine();
            Builder.AppendLine();

            TargetSelection_Targets.Clear();
            foreach (IFleetIntelligence intel in Host.IntelProvider.GetFleetIntelligences(timestamp).Values)
            {
                if ((intel.Type & TaskTypeToTargetTypes[TargetSelection_TaskTypes[TargetSelection_TaskTypesIndex]]) != 0)
                    TargetSelection_Targets.Add(intel);
                else if ((TargetSelection_TaskTypes[TargetSelection_TaskTypesIndex] == TaskType.Dock || TargetSelection_TaskTypes[TargetSelection_TaskTypesIndex] == TaskType.SetHome)
                    && intel.Type == IntelItemType.Dock && 
                    DockIntel.TagsMatch(Agent.HangarTags, ((DockIntel)intel).Tags)) // Special Handling
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
                    if (i == TargetSelection_TargetIndex) Builder.Append(">> ");
                    else Builder.Append("   ");
                    Host.AppendPaddedLine(kRowLength - 3, TaskTypeToSpecialTargets[TargetSelection_TaskTypes[TargetSelection_TaskTypesIndex]][i], Builder);
                }
                else if (specialCount < i && i < TargetSelection_Targets.Count + specialCount + 1)
                {
                    if (i == TargetSelection_TargetIndex + 1) Builder.Append(">> ");
                    else Builder.Append("   ");
                    var intel = TargetSelection_Targets[i - specialCount - 1];

                    if (intel is DockIntel)
                    {
                        var dockIntel = (DockIntel)intel;
                        Builder.Append(dockIntel.OwnerID != -1 ? ((dockIntel.Status & HangarStatus.Reserved) != 0 ? "[R]" : "[C]") : "[ ]");
                        Host.AppendPaddedLine(kRowLength - 6, intel.DisplayName, Builder);
                    }
                    else
                    {
                        Host.AppendPaddedLine(kRowLength - 3, intel.DisplayName, Builder);
                    }

                }
                else
                {
                    Builder.AppendLine();
                }
            }

            Builder.AppendLine();

            Builder.AppendLine("==== SELECTION ====");

            if (TargetSelection_TargetIndex < specialCount)
            {
                Host.AppendPaddedLine(kRowLength, TaskTypeToSpecialTargets[TargetSelection_TaskTypes[TargetSelection_TaskTypesIndex]][TargetSelection_TargetIndex], Builder);
                if (CurrentUIMode == UIMode.SelectTarget)
                {
                    Host.AppendPaddedLine(kRowLength, "[NUM 3] SELECT", Builder);
                    Host.AppendPaddedLine(kRowLength, "[7] CANCEL CMD", Builder);
                }
            }
            else if (specialCount <= TargetSelection_TargetIndex && TargetSelection_TargetIndex < TargetSelection_Targets.Count + specialCount)
            {
                Host.AppendPaddedLine(kRowLength, TargetSelection_Targets[TargetSelection_TargetIndex - specialCount].ID.ToString(), Builder);
                if (CurrentUIMode == UIMode.SelectTarget)
                {
                    Host.AppendPaddedLine(kRowLength, "[NUM 3] SEND CMD", Builder);
                    Host.AppendPaddedLine(kRowLength, "[7] CANCEL CMD", Builder);
                }
            }
            else
            {
                Host.AppendPaddedLine(kRowLength, "NONE SELECTED", Builder);
            }

            foreach (var screen in Host.ActiveLookingGlass.RightHUDs)
            {
                screen.WriteText(Builder.ToString());
            }
        }
        #endregion

        #region MiddleHUD
        void DrawMiddleHUD(TimeSpan localTime)
        {
            int realTargetIndex = -1;
            if (TargetSelection_TaskTypes.Count > 0)
            {
                if (TargetSelection_TaskTypesIndex >= TargetSelection_TaskTypes.Count) TargetSelection_TaskTypesIndex = 0;
                realTargetIndex = TargetSelection_TargetIndex - TaskTypeToSpecialTargets[TargetSelection_TaskTypes[TargetSelection_TaskTypesIndex]].Count();
            }

            SpriteScratchpad.Clear();

            if (Host.ActiveLookingGlass.MiddleHUDs.Count == 0) return;

            foreach (var screen in Host.ActiveLookingGlass.MiddleHUDs)
            {
                Host.GetDefaultSprites(SpriteScratchpad);
                foreach (IFleetIntelligence intel in Host.IntelProvider.GetFleetIntelligences(localTime).Values)
                {
                    if (intel.Type == IntelItemType.Friendly)
                    {
                        var options = LookingGlass.IntelSpriteOptions.Small;
                        if (AgentSelection_FriendlyAgents.Count > AgentSelection_CurrentIndex && intel == AgentSelection_FriendlyAgents[AgentSelection_CurrentIndex])
                            options = LookingGlass.IntelSpriteOptions.ShowName | LookingGlass.IntelSpriteOptions.ShowDist | LookingGlass.IntelSpriteOptions.EmphasizeWithDashes;

                        Host.ActiveLookingGlass.FleetIntelItemToSprites(screen, intel, localTime, Host.ActiveLookingGlass.kFriendlyBlue, ref SpriteScratchpad, options);
                    }
                    else if (intel.Type == IntelItemType.Enemy)
                    {
                        var options = LookingGlass.IntelSpriteOptions.ShowDist;
                        if (realTargetIndex >= 0 && TargetSelection_Targets.Count > realTargetIndex && intel == TargetSelection_Targets[realTargetIndex])
                            options |= LookingGlass.IntelSpriteOptions.EmphasizeWithDashes;

                        int priority = Host.IntelProvider.GetPriority(intel.ID);

                        if (priority == 0) options |= LookingGlass.IntelSpriteOptions.Circle | LookingGlass.IntelSpriteOptions.Small;
                        else if (priority == 1) options |= LookingGlass.IntelSpriteOptions.Small;
                        else if (priority == 3) options |= LookingGlass.IntelSpriteOptions.Large;
                        else if (priority == 4) options |= LookingGlass.IntelSpriteOptions.Large | LookingGlass.IntelSpriteOptions.EmphasizeWithBrackets;

                        Host.ActiveLookingGlass.FleetIntelItemToSprites(screen, intel, localTime, Host.ActiveLookingGlass.kEnemyRed, ref SpriteScratchpad, options);
                    }
                    else if (intel.Type == IntelItemType.Waypoint)
                    {
                        var options = LookingGlass.IntelSpriteOptions.ShowDist;
                        Host.ActiveLookingGlass.FleetIntelItemToSprites(screen, intel, localTime, Host.ActiveLookingGlass.kWaypointOrange, ref SpriteScratchpad, options);
                    }
                }

                using (var frame = screen.DrawFrame())
                {
            
                    if (CurrentUIMode == UIMode.SelectWaypoint)
                    {
                        var distIndicator = MySprite.CreateText(CursorDist.ToString() + " m", "Debug", Color.White, 0.5f);
                        distIndicator.Position = new Vector2(0, 5) + screen.TextureSize / 2f;
                        frame.Add(distIndicator);

                        var prompt = MySprite.CreateText("[5|W/8|S] +/- DIST", "Debug", Color.White, 0.4f);
                        prompt.Position = new Vector2(0, 20) + screen.TextureSize / 2f;
                        frame.Add(prompt);

                        var prompt2 = MySprite.CreateText("[3|SPACE] CONFIRM", "Debug", Color.White, 0.4f);
                        prompt2.Position = new Vector2(0, 34) + screen.TextureSize / 2f;
                        frame.Add(prompt2);
                    } else if (CurrentUIMode == UIMode.Designate)
                    {
                        var prompt = MySprite.CreateText("[3|SPACE] CONFIRM", "Debug", Color.White, 0.4f);
                        prompt.Position = new Vector2(0, 5) + screen.TextureSize / 2f;
                        frame.Add(prompt);
                    }

                    foreach (var spr in SpriteScratchpad)
                    {
                        frame.Add(spr);
                    }
                }
            }

        }
        #endregion

        #region util

        void SendCommand(IFleetIntelligence target, TimeSpan timestamp)
        {
            SendCommand(MyTuple.Create(target.Type, target.ID), timestamp);
        }
        void SendCommand(MyTuple<IntelItemType, long> targetKey, TimeSpan timestamp)
        {
            FriendlyShipIntel agent = AgentSelection_FriendlyAgents[AgentSelection_CurrentIndex];
            TaskType taskType = TargetSelection_TaskTypes[TargetSelection_TaskTypesIndex];

            Host.IntelProvider.ReportCommand(agent, taskType, targetKey, timestamp);

            CurrentUIMode = UIMode.SelectAgent;
        }
        #endregion

        #region Waypoint Designation
        int CursorDist = 1000;
        Waypoint GetWaypoint()
        {
            var w = new Waypoint();
            w.Position = Vector3D.Transform(Vector3D.Forward * CursorDist, Host.ActiveLookingGlass.PrimaryCamera.WorldMatrix);
        
            return w;
        }
        #endregion
    }

    public class LookingGlassPlugin_Lidar : ILookingGlassPlugin
    {
        #region ILookingGlassPlugin
        public LookingGlassNetworkSubsystem Host { get; set; }
        public void Do4(TimeSpan localTime)
        {
            if (TargetPriority_Selection < TargetPriority_TargetList.Count)
            {
                long iD = TargetPriority_TargetList[TargetPriority_Selection].ID;
                var priority = Host.IntelProvider.GetPriority(iD);
                if (priority <= 0) return;
                Host.IntelProvider.SetPriority(iD, priority - 1);
            }
        }

        public void Do5(TimeSpan localTime)
        {
            TargetPriority_Selection = Host.DeltaSelection(TargetPriority_Selection, TargetPriority_TargetList.Count, true);
        }

        public void Do6(TimeSpan localTime)
        {
            if (TargetPriority_Selection < TargetPriority_TargetList.Count)
            {
                long iD = TargetPriority_TargetList[TargetPriority_Selection].ID;
                var priority = Host.IntelProvider.GetPriority(iD);
                if (priority >= 4) return;
                Host.IntelProvider.SetPriority(iD, priority + 1);
            }
        }
        public void Do8(TimeSpan localTime)
        {
            TargetPriority_Selection = Host.DeltaSelection(TargetPriority_Selection, TargetPriority_TargetList.Count, false);
        }

        public void Do7(TimeSpan localTime)
        {
        }

        public void Do3(TimeSpan localTime)
        {
            Host.ActiveLookingGlass.DoScan(localTime);
        }


        public void Setup()
        {
        }

        public void UpdateHUD(TimeSpan localTime)
        {
            DrawScanUI(localTime);
            DrawTrackingUI(localTime);
            DrawMiddleHUD(localTime);
        }

        public void UpdateState(TimeSpan localTime)
        {
        }
        #endregion

        const int kScanDistance = 25000;

        List<EnemyShipIntel> TargetPriority_TargetList = new List<EnemyShipIntel>();
        int TargetPriority_Selection = 0;

        StringBuilder Builder = new StringBuilder();

        List<MySprite> SpriteScratchpad = new List<MySprite>();

        public LookingGlassPlugin_Lidar()
        {
        }


        void DrawScanUI(TimeSpan timestamp)
        {
            Builder.Clear();
            int kRowLength = 19;
        
            Builder.AppendLine("====== LIDAR ======");
            Builder.AppendLine();
        
            if (Host.ActiveLookingGlass.SecondaryCameras.Count == 0)
            {
                Builder.AppendLine("=== UNAVAILABLE ===");
                return;
            }
        
            Host.AppendPaddedLine(kRowLength, "SCANNERS:", Builder);

            Builder.AppendLine();
        
            for (int i = 0; i < 8; i++)
            {
                if (i < Host.ActiveLookingGlass.SecondaryCameras.Count)
                {
                    Builder.Append(i == Host.ActiveLookingGlass.Lidar_CameraIndex ? "> " : "  ");
                    Builder.Append((i + 1).ToString()).Append(": ");
        
                    if (Host.ActiveLookingGlass.SecondaryCameras[i].IsWorking)
                    {
                        if (Host.ActiveLookingGlass.SecondaryCameras[i].AvailableScanRange >= kScanDistance)
                        {
                            Host.AppendPaddedLine(kRowLength - 5, "READY", Builder);
                        }
                        else
                        {
                            Host.AppendPaddedLine(kRowLength - 5, "CHARGING", Builder);
                        }
        
                        int p = (int)(Host.ActiveLookingGlass.SecondaryCameras[i].AvailableScanRange * 10 / kScanDistance);
                        Builder.Append('[').Append('=', Math.Min(10, p)).Append(' ', Math.Max(0, 10 - p)).Append(string.Format("] {0,4:0.0}", Host.ActiveLookingGlass.SecondaryCameras[i].AvailableScanRange / 1000)).AppendLine("km");
                    }
                    else
                    {
                        Host.AppendPaddedLine(kRowLength - 5, "UNAVAILABLE", Builder);
                        Builder.AppendLine();
                    }
                }
                else
                {
                    Builder.AppendLine();
                    Builder.AppendLine();
                }
            }
        
            Builder.AppendLine();
            Builder.AppendLine("===================");

            Host.AppendPaddedLine(kRowLength, Host.ActiveLookingGlass.LastDetectedInfo.Type.ToString(), Builder);

            foreach (var screen in Host.ActiveLookingGlass.LeftHUDs)
            {
                screen.FontColor = Host.ActiveLookingGlass.kFocusedColor;
                screen.WriteText(Builder.ToString());
            }
        }
        
        void DrawTrackingUI(TimeSpan timestamp)
        {
            Builder.Clear();

            Builder.AppendLine("= TARGET PRIORITY =");
        
            Builder.AppendLine();
        
            var intels = Host.IntelProvider.GetFleetIntelligences(timestamp);
            var canonicalTime = timestamp + Host.IntelProvider.CanonicalTimeDiff;
            TargetPriority_TargetList.Clear();

            foreach (var intel in intels)
                if (intel.Key.Item1 == IntelItemType.Enemy)
                    TargetPriority_TargetList.Add((EnemyShipIntel)intel.Value);

            if (TargetPriority_TargetList.Count <= TargetPriority_Selection) TargetPriority_Selection = 0;
        
            if (TargetPriority_TargetList.Count == 0)
            {
                Builder.AppendLine("NO TARGETS");
                foreach (var screen in Host.ActiveLookingGlass.RightHUDs)
                {
                    screen.WriteText(Builder.ToString());
                    screen.FontColor = Host.ActiveLookingGlass.kFocusedColor;
                }
                return;
            }
        
            Builder.AppendLine("8/5: SELECT");
            Builder.AppendLine("4/6: -/+ PRIORITY");
            Builder.AppendLine();
        
            for (int i = 0; i < 12; i++)
            {
                if (i < TargetPriority_TargetList.Count)
                {
                    Builder.Append(i == TargetPriority_Selection ? "> " : "  ");

                    int priority = Host.IntelProvider.GetPriority(TargetPriority_TargetList[i].ID);

                    if (priority == 0) Builder.Append("-- ");
                    else if (priority == 1) Builder.Append("=- ");
                    else if (priority == 2) Builder.Append("== ");
                    else if (priority == 3) Builder.Append("+= ");
                    else if (priority == 4) Builder.Append("++ ");

                    Host.AppendPaddedLine(14, TargetPriority_TargetList[i].DisplayName, Builder);
                }
                else
                {
                    Builder.AppendLine();
                }
            }
        
            Builder.AppendLine();

            foreach (var screen in Host.ActiveLookingGlass.RightHUDs)
            {
                screen.WriteText(Builder.ToString());
                screen.FontColor = Host.ActiveLookingGlass.kFocusedColor;
            }
        }

        void DrawMiddleHUD(TimeSpan localTime)
        {
            if (Host.ActiveLookingGlass.MiddleHUDs.Count == 0) return;

            foreach (var screen in Host.ActiveLookingGlass.MiddleHUDs)
            {
                SpriteScratchpad.Clear();

                Host.GetDefaultSprites(SpriteScratchpad);

                foreach (IFleetIntelligence intel in Host.IntelProvider.GetFleetIntelligences(localTime).Values)
                {
                    if (intel.Type == IntelItemType.Friendly)
                    {
                        Host.ActiveLookingGlass.FleetIntelItemToSprites(screen, intel, localTime, Host.ActiveLookingGlass.kFriendlyBlue, ref SpriteScratchpad, LookingGlass.IntelSpriteOptions.Small);
                    }
                    else if (intel.Type == IntelItemType.Enemy)
                    {
                        LookingGlass.IntelSpriteOptions options = LookingGlass.IntelSpriteOptions.ShowTruncatedName;

                        if (TargetPriority_TargetList.Count > TargetPriority_Selection && intel == TargetPriority_TargetList[TargetPriority_Selection])
                        {
                            options |= LookingGlass.IntelSpriteOptions.EmphasizeWithDashes;
                            options |= LookingGlass.IntelSpriteOptions.ShowDist;
                        }

                        int priority = Host.IntelProvider.GetPriority(intel.ID);

                        if (priority == 0) options |= LookingGlass.IntelSpriteOptions.Circle | LookingGlass.IntelSpriteOptions.Small;
                        else if (priority == 1) options |= LookingGlass.IntelSpriteOptions.Small;
                        else if (priority == 3) options |= LookingGlass.IntelSpriteOptions.Large;
                        else if (priority == 4) options |= LookingGlass.IntelSpriteOptions.Large | LookingGlass.IntelSpriteOptions.EmphasizeWithBrackets;

                        Host.ActiveLookingGlass.FleetIntelItemToSprites(screen, intel, localTime, priority == 0 ? Color.White : Host.ActiveLookingGlass.kEnemyRed, ref SpriteScratchpad, options);
                    }
                    else if (intel.Type == IntelItemType.Asteroid)
                    {
                        Host.ActiveLookingGlass.FleetIntelItemToSprites(screen, intel, localTime, Color.Green, ref SpriteScratchpad, LookingGlass.IntelSpriteOptions.Large);
                    }
                }

                using (var frame = screen.DrawFrame())
                {
                    foreach (var spr in SpriteScratchpad)
                    {
                        frame.Add(spr);
                    }
                }
            }
        }
    }
    public class LookingGlassPlugin_Combat : ILookingGlassPlugin
    {
        #region ILookingGlassPlugin
        public LookingGlassNetworkSubsystem Host { get; set; }
        public void Do4(TimeSpan localTime)
        {
            if (TorpedoSubsystem != null)
            {
                if (FireTorpedoAtCursorTarget("SM", localTime))
                {
                    FeedbackOnTarget = true;
                    return;
                }
            }
            FeedbackText = "NOT LOADED";
        }

        public void Do5(TimeSpan localTime)
        {
            if (TorpedoSubsystem != null)
            {
                if (FireTorpedoAtCursorTarget("LG", localTime))
                {
                    FeedbackOnTarget = true;
                    return;
                }
            }
            FeedbackText = "NOT LOADED";
        }

        public void Do6(TimeSpan localTime)
        {
            if (closestEnemyToCursorID == -1)
            {
                FeedbackText = "NO TARGET";
            }

            bool launched = false;

            if (HangarSubsystem != null)
            {
                var intelItems = Host.IntelProvider.GetFleetIntelligences(localTime);

                var enemyKey = MyTuple.Create(IntelItemType.Enemy, closestEnemyToCursorID);

                if (!intelItems.ContainsKey(enemyKey)) return;

                foreach (var hangar in HangarSubsystem.SortedHangarsList)
                {
                    if (hangar.OwnerID != -1)
                    {
                        var key = MyTuple.Create(IntelItemType.Friendly, hangar.OwnerID);
                        if (intelItems.ContainsKey(key))
                        {
                            FriendlyShipIntel agent = (FriendlyShipIntel)intelItems[key];

                            if (agent.HydroPowerInv.X > 95 && agent.HydroPowerInv.Y > 20 && agent.HydroPowerInv.Z > 50)
                            {
                                Host.IntelProvider.ReportCommand(agent, TaskType.Attack, enemyKey, localTime);
                                launched = true;
                                FeedbackOnTarget = true;
                            }
                        }
                    }
                }
            }

            if (!launched) FeedbackText = "DRONES UNAVAILABLE";
        }
        public void Do8(TimeSpan localTime)
        {
        }

        public void Do7(TimeSpan localTime)
        {
            var intelItems = Host.IntelProvider.GetFleetIntelligences(localTime);
            var targetKey = MyTuple.Create(IntelItemType.NONE, (long)0);
            foreach (var hangar in HangarSubsystem.SortedHangarsList)
            {
                if (hangar.OwnerID != -1)
                {
                    var key = MyTuple.Create(IntelItemType.Friendly, hangar.OwnerID);
                    if (intelItems.ContainsKey(key))
                    {
                        FriendlyShipIntel agent = (FriendlyShipIntel)intelItems[key];

                        Host.IntelProvider.ReportCommand(agent, TaskType.Dock, targetKey, localTime);
                    }
                }
            }
        }

        public void Do3(TimeSpan localTime)
        {
            if (ScannerSubsystem != null)
            {
                var pos = Host.ActiveLookingGlass.PrimaryCamera.WorldMatrix.Forward * 10000 + Host.ActiveLookingGlass.PrimaryCamera.WorldMatrix.Translation;
                ScannerSubsystem.TryScanTarget(pos, localTime);
            }
        }

        public void Setup()
        {
        }

        public void UpdateHUD(TimeSpan localTime)
        {
            DrawInfoUI(localTime);
            DrawActionsUI(localTime);
            DrawMiddleHUD(localTime);
        }

        public void UpdateState(TimeSpan localTime)
        {
        }
        #endregion

        TorpedoSubsystem TorpedoSubsystem;
        HangarSubsystem HangarSubsystem;
        ScannerNetworkSubsystem ScannerSubsystem;

        StringBuilder Builder = new StringBuilder();

        List<MySprite> SpriteScratchpad = new List<MySprite>();

        long closestEnemyToCursorID = -1;

        string FeedbackText = string.Empty;
        bool FeedbackOnTarget = false;

        public LookingGlassPlugin_Combat(TorpedoSubsystem torpedoSubsystem, HangarSubsystem hangarSubsystem, ScannerNetworkSubsystem scannerSubsystem)
        {
            TorpedoSubsystem = torpedoSubsystem;
            HangarSubsystem = hangarSubsystem;
            ScannerSubsystem = scannerSubsystem;
        }

        bool FireTorpedoAtCursorTarget(string group, TimeSpan localTime)
        {
            var intelItems = Host.IntelProvider.GetFleetIntelligences(localTime);
            var key = MyTuple.Create(IntelItemType.Enemy, closestEnemyToCursorID);
            var target = (EnemyShipIntel)intelItems.GetValueOrDefault(key, null);

            return TorpedoSubsystem.Fire(localTime, TorpedoSubsystem.TorpedoTubeGroups[group], target, false) != null;
        }

        void DrawInfoUI(TimeSpan timestamp)
        {
            Builder.Clear();

            Builder.AppendLine("== TORPEDO TUBES ==");
            Builder.AppendLine();

            if (TorpedoSubsystem == null)
            {
                Builder.AppendLine("- NO TORPEDOS -    ");
            }
            else
            {
                foreach (var kvp in TorpedoSubsystem.TorpedoTubeGroups)
                {
                    int ready = kvp.Value.NumReady;
                    int total = kvp.Value.Children.Count();
                    if ( total > 0 )
                        // LG [||--    ] AUTO
                        Builder.Append(kvp.Value.Name).Append(" [").Append('|', ready).Append('-', total - ready).Append(' ', Math.Max(0, 8 - total)).Append(kvp.Value.AutoFire ? "] AUTO \n" : "] MANL \n");
                }
            }

            Builder.AppendLine();
            Builder.AppendLine("== COMBAT DRONES ==");
            Builder.AppendLine();

            long refillHangerID = -1;

            if (HangarSubsystem == null)
            {
                Builder.AppendLine("- NO DRONES -");
            }
            else
            {
                var intelItems = Host.IntelProvider.GetFleetIntelligences(timestamp);
                Builder.AppendLine("     |H |P |A |ST  |");
                foreach (var hangar in HangarSubsystem.SortedHangarsList)
                {   //     |H |P |A |ST  |
                    // H11:|99|45|82|HOME|
                    // H11:|EMPTY        
                    // H11:|OCCUPIED     

                    Builder.Append('H').Append(hangar.Index.ToString("00")).Append(":");

                    if (hangar.OwnerID == -1)
                    {
                        Builder.AppendLine("|EMPTY         ");
                        refillHangerID = hangar.Connector.EntityId;
                    }
                    else
                    {
                        var key = MyTuple.Create(IntelItemType.Friendly, hangar.OwnerID);
                        if (intelItems.ContainsKey(key))
                        {
                            var fsi = (FriendlyShipIntel)intelItems[key];
                            if (fsi.AgentClass == AgentClass.Fighter)
                            {
                                // HOME/AWAY/ENGE/RTRN
                                Builder.Append('|').Append(fsi.HydroPowerInv.X == 100 ? "99" : fsi.HydroPowerInv.X.ToString("00"));
                                Builder.Append('|').Append(fsi.HydroPowerInv.Y == 100 ? "99" : fsi.HydroPowerInv.Y.ToString("00"));
                                Builder.Append('|').Append(fsi.HydroPowerInv.Z == 100 ? "99" : fsi.HydroPowerInv.Z.ToString("00"));

                                var statusCode = "|AWAY|";
                                if ((fsi.AgentStatus & AgentStatus.DockedAtHome) != 0 && fsi.HydroPowerInv.X < 95) statusCode = "|RFUL|";
                                else if ((fsi.AgentStatus & AgentStatus.DockedAtHome) != 0 && fsi.HydroPowerInv.Y < 20) statusCode = "|RCHG|";
                                else if ((fsi.AgentStatus & AgentStatus.DockedAtHome) != 0 && fsi.HydroPowerInv.Z < 50) statusCode = "|RELD|";
                                else if ((fsi.AgentStatus & AgentStatus.DockedAtHome) != 0) statusCode = "|REDY|";
                                else if ((fsi.AgentStatus & AgentStatus.Engaged) != 0) statusCode = "|ENGE|";
                                else if ((fsi.AgentStatus & AgentStatus.Recalling) != 0) statusCode = "|RTRN|";
                                Builder.AppendLine(statusCode);
                                continue;
                            }
                        }
                        Builder.AppendLine("|OCCUPIED      ");
                    }
                }
            }

            foreach (var screen in Host.ActiveLookingGlass.LeftHUDs)
            {
                screen.WriteText(Builder.ToString());
                screen.FontColor = Host.ActiveLookingGlass.kFocusedColor;
            }
        }

        void CommandNewDroneToDockByID(long id, TimeSpan localTime, long dockID)
        {
            var intelItems = Host.IntelProvider.GetFleetIntelligences(localTime);
            var intelKey = MyTuple.Create(IntelItemType.Friendly, id);
            var hangarKey = MyTuple.Create(IntelItemType.Dock, dockID);
            if (intelItems.ContainsKey(intelKey))
            {
                Host.IntelProvider.ReportCommand((FriendlyShipIntel)intelItems[intelKey], TaskType.SetHome, hangarKey, localTime);
                Host.IntelProvider.ReportCommand((FriendlyShipIntel)intelItems[intelKey], TaskType.Dock, hangarKey, localTime, CommandType.Enqueue);
            }
        }

        void DrawActionsUI(TimeSpan timestamp)
        {
            Builder.Clear();

            Builder.AppendLine("===== CONTROL =====");
            Builder.AppendLine();
            Builder.AppendLine("3 - RAYCAST");
            Builder.AppendLine();
            Builder.AppendLine("4 - FIRE SMALL");
            Builder.AppendLine("5 - FIRE LARGE");
            Builder.AppendLine();
            Builder.AppendLine("6 - DRONES ATTACK");
            Builder.AppendLine("7 - DRONES RECALL");
            Builder.AppendLine("8 - PAIR DRONES");
            Builder.AppendLine();
            Builder.AppendLine("===== CONTROL =====");

            foreach (var screen in Host.ActiveLookingGlass.RightHUDs)
            {
                screen.FontColor = Host.ActiveLookingGlass.kFocusedColor;
                screen.WriteText(Builder.ToString());
            }
        }

        void DrawMiddleHUD(TimeSpan localTime)
        {
            if (Host.ActiveLookingGlass.MiddleHUDs.Count == 0) return;

            foreach (var screen in Host.ActiveLookingGlass.MiddleHUDs)
            {
                SpriteScratchpad.Clear();

                Host.GetDefaultSprites(SpriteScratchpad);

                float closestDistSqr = 100 * 100;
                long newClosestIntelID = -1;

                foreach (IFleetIntelligence intel in Host.IntelProvider.GetFleetIntelligences(localTime).Values)
                {
                    if (intel.Type == IntelItemType.Friendly)
                    {
                        var fsi = (FriendlyShipIntel)intel;

                        if ((fsi.AgentStatus & AgentStatus.DockedAtHome) != 0) continue;

                        LookingGlass.IntelSpriteOptions options = LookingGlass.IntelSpriteOptions.Small;
                        if (fsi.AgentClass == AgentClass.None) options = LookingGlass.IntelSpriteOptions.ShowName;
                        else if (HangarSubsystem != null && fsi.AgentClass == AgentClass.Fighter && HangarSubsystem.HangarsDict.ContainsKey(fsi.HomeID)) options |= LookingGlass.IntelSpriteOptions.EmphasizeWithDashes;

                        Host.ActiveLookingGlass.FleetIntelItemToSprites(screen, intel, localTime, Host.ActiveLookingGlass.kFriendlyBlue, ref SpriteScratchpad, options);
                    }
                    else if (intel.Type == IntelItemType.Enemy)
                    {
                        LookingGlass.IntelSpriteOptions options = LookingGlass.IntelSpriteOptions.ShowTruncatedName;

                        if (intel.Radius < 10)
                        {
                            options = LookingGlass.IntelSpriteOptions.Small;
                            Host.ActiveLookingGlass.FleetIntelItemToSprites(screen, intel, localTime, Host.ActiveLookingGlass.kEnemyRed, ref SpriteScratchpad, options);
                        }
                        else
                        {
                            if (intel.ID == closestEnemyToCursorID)
                            {
                                options = LookingGlass.IntelSpriteOptions.ShowTruncatedName | LookingGlass.IntelSpriteOptions.ShowDist | LookingGlass.IntelSpriteOptions.EmphasizeWithDashes | LookingGlass.IntelSpriteOptions.EmphasizeWithBrackets | LookingGlass.IntelSpriteOptions.NoCenter | LookingGlass.IntelSpriteOptions.ShowLastDetected;
                                if (FeedbackOnTarget) options |= LookingGlass.IntelSpriteOptions.EmphasizeWithCross;
                            }

                            var distToCenterSqr = Host.ActiveLookingGlass.FleetIntelItemToSprites(screen, intel, localTime, Host.ActiveLookingGlass.kEnemyRed, ref SpriteScratchpad, options).LengthSquared();

                            if (distToCenterSqr < closestDistSqr)
                            {
                                closestDistSqr = distToCenterSqr;
                                newClosestIntelID = intel.ID;
                            }
                        }
                    }

                }
                closestEnemyToCursorID = newClosestIntelID;

                Builder.Clear();

                if (TorpedoSubsystem != null)
                {
                    foreach (var kvp in TorpedoSubsystem.TorpedoTubeGroups)
                    {
                        int ready = kvp.Value.NumReady;
                        int total = kvp.Value.Children.Count();
                        Builder.Append("[").Append('|', ready).Append('-', total - ready).Append(']');
                        Builder.AppendLine();
                    }
                }

                using (var frame = screen.DrawFrame())
                {
                    foreach (var spr in SpriteScratchpad)
                    {
                        frame.Add(spr);
                    }

                    if (FeedbackText != string.Empty)
                    {
                        var prompt = MySprite.CreateText(FeedbackText, "Debug", Color.HotPink, 0.9f);
                        prompt.Position = new Vector2(0, -35) + screen.TextureSize / 2f;
                        frame.Add(prompt);
                    }

                    var HUD = MySprite.CreateText(Builder.ToString(), "Monospace", Color.LightBlue, 0.3f);
                    HUD.Position = new Vector2(0, -25) + screen.TextureSize / 2f;
                    frame.Add(HUD);
                }
            }

            FeedbackText = string.Empty;
            FeedbackOnTarget = false;
        }
    }
}

