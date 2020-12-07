﻿using Sandbox.Game.EntityComponents;
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
        public AutopilotSubsystem AutopilotSubsystem;
        public IntelSubsystem IntelSubsystem;
        public HoneybeeMiningSystem MiningSubsystem;
        public LookingGlassNetworkSubsystem LookingGlassNetwork;
        public AgentSubsystem AgentSubsystem;
        public ScannerNetworkSubsystem ScannerSubsystem;
        public MonitorSubsystem MonitorSubsystem;

        public Program()
        {
            subsystemManager = new SubsystemManager(this);
            Runtime.UpdateFrequency = UpdateFrequency.Update1;

            AutopilotSubsystem = new AutopilotSubsystem();
            IntelSubsystem = new IntelSubsystem();
            MiningSubsystem = new HoneybeeMiningSystem();
            LookingGlassNetwork = new LookingGlassNetworkSubsystem(IntelSubsystem, "LG", false, false);
            AgentSubsystem = new AgentSubsystem(IntelSubsystem, AgentClass.None);
            MonitorSubsystem = new MonitorSubsystem(IntelSubsystem);

            var MiningTaskGenerator = new HoneybeeMiningTaskGenerator(this, MiningSubsystem, AutopilotSubsystem, AgentSubsystem, null, null, null, IntelSubsystem, MonitorSubsystem);
            AgentSubsystem.AddTaskGenerator(MiningTaskGenerator);

            ScannerSubsystem = new ScannerNetworkSubsystem(IntelSubsystem);
            LookingGlassNetwork.AddPlugin("combat", new LookingGlass_Pascal(this));

            subsystemManager.AddSubsystem("autopilot", AutopilotSubsystem);
            subsystemManager.AddSubsystem("intel", IntelSubsystem);
            subsystemManager.AddSubsystem("mining", MiningSubsystem);
            subsystemManager.AddSubsystem("agent", AgentSubsystem);
            subsystemManager.AddSubsystem("scanner", ScannerSubsystem);
            subsystemManager.AddSubsystem("lookingglass", LookingGlassNetwork);
            subsystemManager.AddSubsystem("monitor", MonitorSubsystem);

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

        class LookingGlass_Pascal : ILookingGlassPlugin
        {
            public LookingGlassNetworkSubsystem Host { get; set; }

            public void Do4(TimeSpan localTime)
            {
                HostProgram.MiningSubsystem.Recalling = 2;
            }

            public void Do7(TimeSpan localTime)
            {
            }

            public void Do6(TimeSpan localTime)
            {
                if (closestEnemyToCursorID != -1)
                {
                    HostProgram.IntelSubsystem.SetPriority(closestEnemyToCursorID, 1);
                }
            }

            public void Do5(TimeSpan localTime)
            {
                var pos = Host.ActiveLookingGlass.PrimaryCamera.WorldMatrix.Forward * 10000 + Host.ActiveLookingGlass.PrimaryCamera.WorldMatrix.Translation;
                HostProgram.ScannerSubsystem.TryScanTarget(pos, localTime);
            }

            public void Do3(TimeSpan localTime)
            {
                Host.ActiveLookingGlass.DoScan(localTime);
                if (!Host.ActiveLookingGlass.LastDetectedInfo.IsEmpty() && Host.ActiveLookingGlass.LastDetectedInfo.Type == MyDetectedEntityType.Asteroid)
                {
                    var w = new Waypoint();
                    w.Position = (Vector3D)Host.ActiveLookingGlass.LastDetectedInfo.HitPosition;
                    w.Direction = Host.ActiveLookingGlass.PrimaryCamera.WorldMatrix.Backward;
                    Host.ReportIntel(w, localTime);
                    HostProgram.AgentSubsystem.AddTask(TaskType.Mine, MyTuple.Create(IntelItemType.Waypoint, w.ID), CommandType.Override, 0,
                        localTime + HostProgram.IntelSubsystem.CanonicalTimeDiff);
                }
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

            public LookingGlass_Pascal(Program program)
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
                Builder.AppendLine("1 - ");
                Builder.AppendLine("2 - CAMERA");
                Builder.AppendLine("3 - START MINE");
                Builder.AppendLine("4 - STOP MINE");
                Builder.AppendLine("5 - ");
                Builder.AppendLine("6 - ");
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