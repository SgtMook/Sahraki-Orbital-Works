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
            if (command == "up") DoW(timestamp);
            if (command == "down") DoS(timestamp);
            if (command == "left") DoA(timestamp);
            if (command == "right") DoD(timestamp);
            if (command == "enter") DoSpace(timestamp);
            if (command == "cancel") DoC(timestamp);
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

        public void Setup(MyGridProgram program, string name)
        {
            Program = program;
            GetParts();
            UpdateFrequency = UpdateFrequency.Update10;
            AddDefaultPlugins();
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

        public MyGridProgram Program;

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

        void GetParts()
        {
            for (int i = 0; i < LookingGlassArray.Length; i++)
                if (LookingGlassArray[i] != null) LookingGlassArray[i].Clear();
            LookingGlasses.Clear();
            Controller = null;

            if (OverrideGyros)
            {
                Program.GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(null, FindBases);
                Program.GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(null, FindUnassignedBases);
                Program.GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(null, FindArms);
            }
            else
            {
                LookingGlassArray[1] = new LookingGlass();
            }

            Program.GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(null, CollectParts);

            for (int i = 0; i < LookingGlassArray.Length; i++)
            {
                if (LookingGlassArray[i] != null && LookingGlassArray[i].IsOK(OverrideGyros))
                {
                    LookingGlassArray[i].Initialize();
                    AddLookingGlass(LookingGlassArray[i]);
                }
            }
        }

        private bool CollectParts(IMyTerminalBlock block)
        {
            if (!Program.Me.IsSameConstructAs(block)) return false;
            if (block is IMyShipController && ((IMyShipController)block).CanControlShip)
                Controller = (IMyShipController)block;

            if (OverrideGyros)
            {
                for (int i = 0; i < LookingGlassArray.Length; i++)
                {
                    if (LookingGlassArray[i] != null && LookingGlassArray[i].Pitch != null && block.CubeGrid.EntityId == LookingGlassArray[i].Pitch.TopGrid.EntityId)
                    {
                        if (block.CustomName.StartsWith("["))
                        {
                            var indexTagEnd = block.CustomName.IndexOf(']');
                            if (indexTagEnd != -1)
                            {
                                block.CustomName = block.CustomName.Substring(indexTagEnd + 1);
                            }
                        }
                        block.CustomName = $"[{Tag}{i}]" + block.CustomName;
                        LookingGlassArray[i].AddPart(block);
                    }
                }
            }
            else
            {
                if (!block.CustomName.StartsWith(TagPrefix)) return false;
                LookingGlassArray[1].AddPart(block);
            }

            return false;
        }

        private bool FindBases(IMyTerminalBlock block)
        {
            if (!Program.Me.IsSameConstructAs(block)) return false;
            if (block is IMyMotorStator && block.CustomName.StartsWith(TagPrefix) && !block.CustomName.StartsWith($"[{Tag}x]") && block.CubeGrid.EntityId == Program.Me.CubeGrid.EntityId)
            {
                var indexTagEnd = block.CustomName.IndexOf(']');
                if (indexTagEnd == -1) return false;

                var numString = block.CustomName.Substring(TagPrefix.Length, indexTagEnd - TagPrefix.Length);

                int index;
                if (!int.TryParse(numString, out index)) return false;
                if (LookingGlassArray[index] == null) LookingGlassArray[index] = new LookingGlass();
                LookingGlassArray[index].AddPart(block);
            }
            return false;
        }

        private bool FindUnassignedBases(IMyTerminalBlock block)
        {
            if (!Program.Me.IsSameConstructAs(block)) return false;
            if (block is IMyMotorStator && block.CustomName.StartsWith($"[{Tag}x]") && block.CubeGrid.EntityId == Program.Me.CubeGrid.EntityId)
            {
                for (int i = 1; i < LookingGlassArray.Length; i++)
                {
                    if (LookingGlassArray[i] == null)
                    {
                        LookingGlassArray[i] = new LookingGlass();
                        block.CustomName = block.CustomName.Replace($"[{Tag}x]", $"[{Tag}{i}]");
                        LookingGlassArray[i].AddPart(block);
                        return false;
                    }
                }
            }
            return false;
        }

        private bool FindArms(IMyTerminalBlock block)
        {
            if (!Program.Me.IsSameConstructAs(block)) return false;
            if (block is IMyMotorStator)
            {
                for (int i = 1; i < LookingGlassArray.Length; i++)
                {
                    if (LookingGlassArray[i] != null && block.CubeGrid.EntityId == LookingGlassArray[i].Yaw.TopGrid.EntityId)
                    {
                        if (block.CustomName.StartsWith("["))
                        {
                            var indexTagEnd = block.CustomName.IndexOf(']');
                            if (indexTagEnd != -1)
                            {
                                block.CustomName = block.CustomName.Substring(indexTagEnd + 1);
                            }
                        }
                        block.CustomName = $"[{Tag}{i}]" + block.CustomName;
                        LookingGlassArray[i].AddPart(block);
                    }
                }
            }
            return false;
        }

        #region plugins
        public void AddPlugin(string name, ILookingGlassPlugin plugin)
        {
            if (Plugins.ContainsKey(name)) return;
            Plugins[name] = plugin;
            plugin.Host = this;
            PluginsList.Add(plugin);

            if (ActivePlugin == null) ActivePlugin = plugin;
        }

        void AddDefaultPlugins()
        {
            AddPlugin("command", new LookingGlassPlugin_Command());
            AddPlugin("lidar", new LookingGlassPlugin_Lidar());
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

        private void TriggerInputs(TimeSpan timestamp)
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
            if (ActivePlugin != null) ActivePlugin.DoA(timestamp);
        }

        void DoS(TimeSpan timestamp)
        {
            if (ActivePlugin != null) ActivePlugin.DoS(timestamp);
        }

        void DoD(TimeSpan timestamp)
        {
            if (ActivePlugin != null) ActivePlugin.DoD(timestamp);
        }

        void DoW(TimeSpan timestamp)
        {
            if (ActivePlugin != null) ActivePlugin.DoW(timestamp);
        }

        void DoC(TimeSpan timestamp)
        {
            if (ActivePlugin != null) ActivePlugin.DoC(timestamp);
        }

        void DoSpace(TimeSpan timestamp)
        {
            if (ActivePlugin != null) ActivePlugin.DoSpace(timestamp);
        }

        private void UpdateSwivels()
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
        private void UpdateUpdateFrequency()
        {
            UpdateFrequency = UpdateFrequency.Update10;
            if (ActiveLookingGlass != null) UpdateFrequency |= UpdateFrequency.Update1;
        }

        private void UpdatePlugins(TimeSpan timestamp)
        {
            foreach (var kvp in Plugins)
            {
                if (ActiveLookingGlass != null && kvp.Value == ActivePlugin) kvp.Value.UpdateHUD(timestamp);
                kvp.Value.UpdateState(timestamp);
            }
        }

        private void UpdateActiveLookingGlass()
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
                LeftHUD.TextPadding = 9;
                LeftHUD.Font = "Monospace";
                LeftHUD.ContentType = ContentType.TEXT_AND_IMAGE;
            }

            foreach (var RightHUD in RightHUDs)
            {
                RightHUD.FontSize = 0.55f;
                RightHUD.TextPadding = 9;
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

            LastDetectedInfo = usingCamera.Raycast(10000);

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
                TargetIntel.FromDetectedInfo(LastDetectedInfo, timestamp + Network.IntelProvider.CanonicalTimeDiff, true);
                Network.ReportIntel(TargetIntel, timestamp);
            }

            return true;
        }
        #endregion

        #region Display
        public const float kCameraToScreen = 1.06f;
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
        }
        public Vector2 FleetIntelItemToSprites(IFleetIntelligence intel, TimeSpan localTime, Color color, ref List<MySprite> scratchpad, IntelSpriteOptions properties = IntelSpriteOptions.None)
        {
            if (intel.ID == Network.Program.Me.CubeGrid.EntityId) return new Vector2(float.MaxValue, float.MaxValue);

            var worldDirection = intel.GetPositionFromCanonicalTime(localTime + Network.IntelProvider.CanonicalTimeDiff) - PrimaryCamera.WorldMatrix.Translation;
            var bodyPosition = Vector3D.TransformNormal(worldDirection, MatrixD.Transpose(PrimaryCamera.WorldMatrix));
            var screenPosition = new Vector2(-1 * (float)(bodyPosition.X / bodyPosition.Z), (float)(bodyPosition.Y / bodyPosition.Z));

            if (bodyPosition.Dot(Vector3D.Forward) < 0) return new Vector2(float.MaxValue, float.MaxValue);

            float dist = (float)bodyPosition.Length();
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

            var indicator = MySprite.CreateText(indicatorText, "Monospace", color, scale, TextAlignment.CENTER);
            var v = ((screenPosition * kCameraToScreen) + new Vector2(0.5f, 0.5f)) * kScreenSize;

            if (cross)
            {
                var c = new MySprite(SpriteType.TEXTURE, "Cross", size: new Vector2(30f, 30f));
                c.Position = v;
                scratchpad.Add(c);
            }

            var CenteredScreenPosition = v - new Vector2(0.5f, 0.5f) * kScreenSize;
            v.X = Math.Max(30, Math.Min(kScreenSize - 30, v.X));
            v.Y = Math.Max(30, Math.Min(kScreenSize - 30, v.Y));
            v.Y -= scale * (kMonospaceConstant.Y + 10) / 2;
            indicator.Position = v;
            scratchpad.Add(indicator);

            v.Y += kMonospaceConstant.Y * scale + 0.2f;

            if ((properties & IntelSpriteOptions.ShowDist) != 0)
            {
                float textSize= 0.4f;
                if ((properties & IntelSpriteOptions.Small) != 0) textSize = 0.3f;
                var distSprite = MySprite.CreateText($"{((int)dist).ToString()} m", "Debug", new Color(1, 1, 1, 0.5f), textSize, TextAlignment.CENTER);
                distSprite.Position = v;
                scratchpad.Add(distSprite);
                v.Y += kDebugConstant.Y * textSize + 0.1f;
            }

            if ((properties & IntelSpriteOptions.ShowName) != 0 || (properties & IntelSpriteOptions.ShowTruncatedName) != 0)
            {
                float textSize = 0.4f;
                if ((properties & IntelSpriteOptions.Small) != 0) textSize = 0.3f;
                var name = intel.DisplayName;
                if ((properties & IntelSpriteOptions.ShowTruncatedName) != 0) name = name.Substring(0, 8);
                var nameSprite = MySprite.CreateText(name, "Debug", new Color(1, 1, 1, 0.5f), textSize, TextAlignment.CENTER);
                nameSprite.Position = v;
                scratchpad.Add(nameSprite);
                v.Y += kDebugConstant.Y * textSize + 0.1f;
            }

            return CenteredScreenPosition;
        }

        public readonly Color kFriendlyBlue = new Color(140, 140, 255, 100);
        public readonly Color kEnemyRed = new Color(255, 140, 140, 100);
        public readonly Color kWaypointOrange = new Color(255, 210, 180, 100);

        public readonly Color kFocusedColor = new Color(0.5f, 0.5f, 1f);
        public readonly Color kUnfocusedColor = new Color(0.2f, 0.2f, 0.5f, 0.5f);
        #endregion
    }

    public interface ILookingGlassPlugin
    {
        void DoA(TimeSpan localTime);
        void DoS(TimeSpan localTime);
        void DoD(TimeSpan localTime);
        void DoW(TimeSpan localTime);
        void DoC(TimeSpan localTime);
        void DoSpace(TimeSpan localTime);

        void UpdateHUD(TimeSpan localTime);
        void UpdateState(TimeSpan localTime);

        void Setup();

        LookingGlassNetworkSubsystem Host { get; set; }
    }

    public class LookingGlassPlugin_Command : ILookingGlassPlugin
    {
        #region ILookingGlassPlugin
        public LookingGlassNetworkSubsystem Host { get; set; }
        public void DoA(TimeSpan localTime)
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

        public void DoS(TimeSpan localTime)
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

        public void DoD(TimeSpan localTime)
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
        public void DoW(TimeSpan localTime)
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

        public void DoC(TimeSpan localTime)
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

        public void DoSpace(TimeSpan localTime)
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
        private void DrawAgentSelectionUI(TimeSpan timestamp)
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

        private void DrawTargetSelectionUI(TimeSpan timestamp)
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
                if ((intel.IntelItemType & TaskTypeToTargetTypes[TargetSelection_TaskTypes[TargetSelection_TaskTypesIndex]]) != 0)
                    TargetSelection_Targets.Add(intel);
                else if ((TargetSelection_TaskTypes[TargetSelection_TaskTypesIndex] == TaskType.Dock || TargetSelection_TaskTypes[TargetSelection_TaskTypesIndex] == TaskType.SetHome)
                    && intel.IntelItemType == IntelItemType.Dock && 
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
                    Host.AppendPaddedLine(kRowLength, "[NUM 0] SELECT", Builder);
                    Host.AppendPaddedLine(kRowLength, "[7] CANCLE CMD", Builder);
                }
            }
            else if (specialCount <= TargetSelection_TargetIndex && TargetSelection_TargetIndex < TargetSelection_Targets.Count + specialCount)
            {
                Host.AppendPaddedLine(kRowLength, TargetSelection_Targets[TargetSelection_TargetIndex - specialCount].ID.ToString(), Builder);
                if (CurrentUIMode == UIMode.SelectTarget)
                {
                    Host.AppendPaddedLine(kRowLength, "[NUM 0] SEND CMD", Builder);
                    Host.AppendPaddedLine(kRowLength, "[7] CANCLE CMD", Builder);
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

            Host.GetDefaultSprites(SpriteScratchpad);
            foreach (IFleetIntelligence intel in Host.IntelProvider.GetFleetIntelligences(localTime).Values)
            {
                if (intel.IntelItemType == IntelItemType.Friendly)
                {
                    var options = LookingGlass.IntelSpriteOptions.Small;
                    if (AgentSelection_FriendlyAgents.Count > AgentSelection_CurrentIndex && intel == AgentSelection_FriendlyAgents[AgentSelection_CurrentIndex])
                        options = LookingGlass.IntelSpriteOptions.ShowName | LookingGlass.IntelSpriteOptions.ShowDist | LookingGlass.IntelSpriteOptions.EmphasizeWithDashes;

                    Host.ActiveLookingGlass.FleetIntelItemToSprites(intel, localTime, Host.ActiveLookingGlass.kFriendlyBlue, ref SpriteScratchpad, options);
                }
                else if (intel.IntelItemType == IntelItemType.Enemy)
                {
                    var options = LookingGlass.IntelSpriteOptions.ShowDist;
                    if (realTargetIndex >= 0 && TargetSelection_Targets.Count > realTargetIndex && intel == TargetSelection_Targets[realTargetIndex])
                        options |= LookingGlass.IntelSpriteOptions.EmphasizeWithDashes;

                    int priority = Host.IntelProvider.GetPriority(intel.ID);

                    if (priority == 0) options |= LookingGlass.IntelSpriteOptions.Circle | LookingGlass.IntelSpriteOptions.Small;
                    else if (priority == 1) options |= LookingGlass.IntelSpriteOptions.Small;
                    else if (priority == 3) options |= LookingGlass.IntelSpriteOptions.Large;
                    else if (priority == 4) options |= LookingGlass.IntelSpriteOptions.Large | LookingGlass.IntelSpriteOptions.EmphasizeWithBrackets;

                    Host.ActiveLookingGlass.FleetIntelItemToSprites(intel, localTime, Host.ActiveLookingGlass.kEnemyRed, ref SpriteScratchpad, options);
                }
                else if (intel.IntelItemType == IntelItemType.Waypoint)
                {
                    var options = LookingGlass.IntelSpriteOptions.ShowDist;
                    Host.ActiveLookingGlass.FleetIntelItemToSprites(intel, localTime, Host.ActiveLookingGlass.kWaypointOrange, ref SpriteScratchpad, options);
                }
            }

            if (Host.ActiveLookingGlass.MiddleHUDs.Count == 0) return;

            foreach (var screen in Host.ActiveLookingGlass.MiddleHUDs)
            {
                using (var frame = screen.DrawFrame())
                {
            
                    if (CurrentUIMode == UIMode.SelectWaypoint)
                    {
                        var distIndicator = MySprite.CreateText(CursorDist.ToString() + " m", "Debug", Color.White, 0.5f);
                        distIndicator.Position = new Vector2(0, 5) + screen.TextureSize / 2f;
                        frame.Add(distIndicator);

                        var prompt = MySprite.CreateText("[W/S] +/- DIST", "Debug", Color.White, 0.4f);
                        prompt.Position = new Vector2(0, 20) + screen.TextureSize / 2f;
                        frame.Add(prompt);

                        var prompt2 = MySprite.CreateText("[SPACE] CONFIRM", "Debug", Color.White, 0.4f);
                        prompt2.Position = new Vector2(0, 34) + screen.TextureSize / 2f;
                        frame.Add(prompt2);
                    } else if (CurrentUIMode == UIMode.Designate)
                    {
                        var prompt = MySprite.CreateText("[SPACE] CONFIRM", "Debug", Color.White, 0.4f);
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
            SendCommand(MyTuple.Create(target.IntelItemType, target.ID), timestamp);
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
        public void DoA(TimeSpan localTime)
        {
            if (TargetPriority_Selection < TargetPriority_TargetList.Count)
            {
                long iD = TargetPriority_TargetList[TargetPriority_Selection].ID;
                var priority = Host.IntelProvider.GetPriority(iD);
                if (priority <= 0) return;
                Host.IntelProvider.SetPriority(iD, priority - 1);
            }
        }

        public void DoS(TimeSpan localTime)
        {
            TargetPriority_Selection = Host.DeltaSelection(TargetPriority_Selection, TargetPriority_TargetList.Count, true);
        }

        public void DoD(TimeSpan localTime)
        {
            if (TargetPriority_Selection < TargetPriority_TargetList.Count)
            {
                long iD = TargetPriority_TargetList[TargetPriority_Selection].ID;
                var priority = Host.IntelProvider.GetPriority(iD);
                if (priority >= 4) return;
                Host.IntelProvider.SetPriority(iD, priority + 1);
            }
        }
        public void DoW(TimeSpan localTime)
        {
            TargetPriority_Selection = Host.DeltaSelection(TargetPriority_Selection, TargetPriority_TargetList.Count, false);
        }

        public void DoC(TimeSpan localTime)
        {
        }

        public void DoSpace(TimeSpan localTime)
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

        private const int kScanDistance = 25000;

        List<EnemyShipIntel> TargetPriority_TargetList = new List<EnemyShipIntel>();
        int TargetPriority_Selection = 0;

        StringBuilder Builder = new StringBuilder();

        List<MySprite> SpriteScratchpad = new List<MySprite>();

        public LookingGlassPlugin_Lidar()
        {
        }


        private void DrawScanUI(TimeSpan timestamp)
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
        
        private void DrawTrackingUI(TimeSpan timestamp)
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
            SpriteScratchpad.Clear();

            Host.GetDefaultSprites(SpriteScratchpad);

            foreach (IFleetIntelligence intel in Host.IntelProvider.GetFleetIntelligences(localTime).Values)
            {
                if (intel.IntelItemType == IntelItemType.Friendly)
                {
                    Host.ActiveLookingGlass.FleetIntelItemToSprites(intel, localTime, Host.ActiveLookingGlass.kFriendlyBlue, ref SpriteScratchpad, LookingGlass.IntelSpriteOptions.Small);
                }
                else if (intel.IntelItemType == IntelItemType.Enemy)
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

                    Host.ActiveLookingGlass.FleetIntelItemToSprites(intel, localTime, priority == 0 ? Color.White : Host.ActiveLookingGlass.kEnemyRed, ref SpriteScratchpad, options);
                }
                else if (intel.IntelItemType == IntelItemType.Asteroid)
                {
                    Host.ActiveLookingGlass.FleetIntelItemToSprites(intel, localTime, Color.Green, ref SpriteScratchpad, LookingGlass.IntelSpriteOptions.Large);
                }
            }


            if (Host.ActiveLookingGlass.MiddleHUDs.Count == 0) return;

            foreach (var screen in Host.ActiveLookingGlass.MiddleHUDs)
            {
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
        public void DoA(TimeSpan localTime)
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

        public void DoS(TimeSpan localTime)
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

        public void DoD(TimeSpan localTime)
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

                            if ((agent.AgentStatus & AgentStatus.DockedAtHome) != 0 && agent.HydroPowerInv.X > 95 && agent.HydroPowerInv.Y > 20 && agent.HydroPowerInv.Z > 50)
                            {
                                Host.IntelProvider.ReportCommand(agent, TaskType.Attack, enemyKey, localTime);
                                launched = true;
                                FeedbackOnTarget = true;
                            }
                        }
                    }
                }
            }

            if (!launched) FeedbackText = "NO DRONE";
        }
        public void DoW(TimeSpan localTime)
        {
        }

        public void DoC(TimeSpan localTime)
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

        public void DoSpace(TimeSpan localTime)
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

        private bool FireTorpedoAtCursorTarget(string group, TimeSpan localTime)
        {
            var intelItems = Host.IntelProvider.GetFleetIntelligences(localTime);
            var key = MyTuple.Create(IntelItemType.Enemy, closestEnemyToCursorID);
            var target = (EnemyShipIntel)intelItems.GetValueOrDefault(key, null);

            return TorpedoSubsystem.Fire(localTime, TorpedoSubsystem.TorpedoTubeGroups[group], target) != null;
        }

        private void DrawInfoUI(TimeSpan timestamp)
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
                    // LG [||--    ] AUTO
                    Builder.Append(kvp.Value.Name).Append(" [").Append('|', ready).Append('-', total - ready).Append(' ', 8 - total).Append(kvp.Value.AutoFire ? "] AUTO \n" : "] MANL \n");
                }
            }

            Builder.AppendLine();
            Builder.AppendLine("== COMBAT DRONES ==");
            Builder.AppendLine();

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
                                if ((fsi.AgentStatus & AgentStatus.DockedAtHome) != 0) statusCode = "|HOME|";
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

        private void DrawActionsUI(TimeSpan timestamp)
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
            SpriteScratchpad.Clear();

            Host.GetDefaultSprites(SpriteScratchpad);

            float closestDistSqr = 100 * 100;
            long newClosestIntelID = -1;

            foreach (IFleetIntelligence intel in Host.IntelProvider.GetFleetIntelligences(localTime).Values)
            {
                if (intel.IntelItemType == IntelItemType.Friendly)
                {
                    var fsi = (FriendlyShipIntel)intel;

                    if ((fsi.AgentStatus & AgentStatus.DockedAtHome) != 0) continue;

                    LookingGlass.IntelSpriteOptions options = LookingGlass.IntelSpriteOptions.Small;
                    if (fsi.AgentClass == AgentClass.None) options = LookingGlass.IntelSpriteOptions.ShowName;
                    else if (HangarSubsystem != null && fsi.AgentClass == AgentClass.Fighter && HangarSubsystem.HangarsDict.ContainsKey(fsi.HomeID)) options |= LookingGlass.IntelSpriteOptions.EmphasizeWithDashes;

                    Host.ActiveLookingGlass.FleetIntelItemToSprites(intel, localTime, Host.ActiveLookingGlass.kFriendlyBlue, ref SpriteScratchpad, options);
                }
                else if (intel.IntelItemType == IntelItemType.Enemy)
                {
                    LookingGlass.IntelSpriteOptions options = LookingGlass.IntelSpriteOptions.ShowTruncatedName;

                    if (intel.Radius < 10) continue;

                    if (intel.ID == closestEnemyToCursorID)
                    {
                        options = LookingGlass.IntelSpriteOptions.ShowTruncatedName | LookingGlass.IntelSpriteOptions.ShowDist | LookingGlass.IntelSpriteOptions.EmphasizeWithDashes | LookingGlass.IntelSpriteOptions.EmphasizeWithBrackets | LookingGlass.IntelSpriteOptions.NoCenter;
                        if (FeedbackOnTarget) options |= LookingGlass.IntelSpriteOptions.EmphasizeWithCross;
                    }

                    var distToCenterSqr = Host.ActiveLookingGlass.FleetIntelItemToSprites(intel, localTime, Host.ActiveLookingGlass.kEnemyRed, ref SpriteScratchpad, options).LengthSquared();

                    if (distToCenterSqr < closestDistSqr)
                    {
                        closestDistSqr = distToCenterSqr;
                        newClosestIntelID = intel.ID;
                    }
                }

            }
            closestEnemyToCursorID = newClosestIntelID;

            Builder.Clear();

            foreach (var kvp in TorpedoSubsystem.TorpedoTubeGroups)
            {
                int ready = kvp.Value.NumReady;
                int total = kvp.Value.Children.Count();
                Builder.Append("[").Append('|', ready).Append('-', total - ready).Append(']');
                Builder.AppendLine();
            }

            foreach (var screen in Host.ActiveLookingGlass.MiddleHUDs)
            {
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

