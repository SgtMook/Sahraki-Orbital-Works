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
using VRage.Library;

using System.Collections.Immutable;

namespace IngameScript
{
    partial class Program : MyGridProgram
    {
        public IntelSubsystem IntelProvider = new IntelSubsystem();
        public ScannerNetworkSubsystem SensorSubsystem;
        public HummingbirdCommandSubsystem HummingbirdCommandSubsystem;
        public Program()
        {
            subsystemManager = new SubsystemManager(this);
            Runtime.UpdateFrequency = UpdateFrequency.Update1;

            SensorSubsystem = new ScannerNetworkSubsystem(IntelProvider, "SE", 100, 0);
            HummingbirdCommandSubsystem = new HummingbirdCommandSubsystem(IntelProvider, SensorSubsystem);

            subsystemManager.AddSubsystem("intel", IntelProvider);
            subsystemManager.AddSubsystem("sensor", SensorSubsystem);
            subsystemManager.AddSubsystem("hummingbird", HummingbirdCommandSubsystem);

            subsystemManager.DeserializeManager(Storage);
        }

        MyCommandLine commandLine = new MyCommandLine();

        SubsystemManager subsystemManager;

        public void Save()
        {
            string v = subsystemManager.SerializeManager();
            Storage = v;
        }

        public void Main(string argument, UpdateType updateSource)
        {
            subsystemManager.UpdateTime();
            if (commandLine.TryParse(argument))
            {
                subsystemManager.Command(commandLine.Argument(0), commandLine.Argument(1), commandLine.ArgumentCount > 2 ? commandLine.Argument(2) : null);
            }
            else
            {
                subsystemManager.Update(updateSource);
                Echo(subsystemManager.GetStatus());
            }
        }

        class LookingGlass_Hummingbird : ILookingGlassPlugin
        {
            public LookingGlassNetworkSubsystem Host { get; set; }

            public void Do4(TimeSpan localTime)
            {
            }

            public void Do7(TimeSpan localTime)
            {
            }

            public void Do6(TimeSpan localTime)
            {

            }

            public void Do5(TimeSpan localTime)
            {
            }

            public void Do3(TimeSpan localTime)
            {
            }

            public void Do8(TimeSpan localTime)
            {
            }

            public void Setup()
            {
            }

            public void UpdateHUD(TimeSpan localTime)
            {
                DrawActionsUI(localTime);
                DrawMiddleHUD(localTime);
            }

            public void UpdateState(TimeSpan localTime)
            {
            }

            public LookingGlass_Hummingbird(Program program)
            {
                HostProgram = program;
            }

            StringBuilder Builder = new StringBuilder();

            List<MySprite> SpriteScratchpad = new List<MySprite>();

            long closestEnemyToCursorID = -1;

            string FeedbackText = string.Empty;
            bool FeedbackOnTarget = false;

            Program HostProgram;

            void DrawActionsUI(TimeSpan timestamp)
            {
                Builder.Clear();

                Builder.AppendLine("===== CONTROL =====");
                Builder.AppendLine();
                Builder.AppendLine("1 - LOCK/UNLOCK");
                Builder.AppendLine("2 - CAMERA");
                Builder.AppendLine("3 - DESIGNATE TARGET");
                Builder.AppendLine("4 - ATTACK TARGET");
                Builder.AppendLine("5 - RAYCAST");
                Builder.AppendLine();
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
                    if (intel.Type == IntelItemType.Friendly)
                    {
                        var fsi = (FriendlyShipIntel)intel;

                        if ((fsi.AgentStatus & AgentStatus.DockedAtHome) != 0) continue;

                        LookingGlass.IntelSpriteOptions options = LookingGlass.IntelSpriteOptions.Small;
                        if (fsi.AgentClass == AgentClass.None) options = LookingGlass.IntelSpriteOptions.ShowName;

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

                            var distToCenterSqr = Host.ActiveLookingGlass.FleetIntelItemToSprites(intel, localTime, Host.ActiveLookingGlass.kEnemyRed, ref SpriteScratchpad, options).LengthSquared();

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
}
