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
        public ExecutionContext Context;
        public AutopilotSubsystem AutopilotSubsystem;
        public IntelSubsystem IntelSubsystem;
        public HornetCombatSubsystem CombatSubsystem;
        public LookingGlassNetworkSubsystem LookingGlassNetwork;
        public AgentSubsystem AgentSubsystem;
        public ScannerNetworkSubsystem ScannerSubsystem;
        public CombatLoaderSubsystem CombatLoaderSubsystem;
        public HornetAttackTaskGenerator TaskGenerator;
        public TorpedoSubsystem TorpedoSubsystem;

        public Program()
        {
            Context = new ExecutionContext(this);
            subsystemManager = new SubsystemManager(Context);
            Runtime.UpdateFrequency = UpdateFrequency.Update1;

            AutopilotSubsystem = new AutopilotSubsystem();
            AutopilotSubsystem.Persist = true;
            
            IntelSubsystem = new IntelSubsystem();
            Context.IntelSystem = IntelSubsystem;

            CombatSubsystem = new HornetCombatSubsystem(IntelSubsystem);
            LookingGlassNetwork = new LookingGlassNetworkSubsystem(IntelSubsystem, "LG", false, false);
            AgentSubsystem = new AgentSubsystem(IntelSubsystem, AgentClass.None);
            TaskGenerator = new HornetAttackTaskGenerator(this, CombatSubsystem, AutopilotSubsystem, AgentSubsystem, null, IntelSubsystem);
            AgentSubsystem.AddTaskGenerator(TaskGenerator);
            TaskGenerator.HornetAttackTask.FocusedTarget = true;
            CombatLoaderSubsystem = new CombatLoaderSubsystem("Fermi Cargo", "Combat Supplies");
            TorpedoSubsystem = new TorpedoSubsystem(IntelSubsystem);

            ScannerSubsystem = new ScannerNetworkSubsystem(IntelSubsystem);
            LookingGlassNetwork.AddPlugin("combat", new LookingGlass_Fermi(this));

            subsystemManager.AddSubsystem("autopilot", AutopilotSubsystem);
            subsystemManager.AddSubsystem("intel", IntelSubsystem);
            subsystemManager.AddSubsystem("combat", CombatSubsystem);
            subsystemManager.AddSubsystem("agent", AgentSubsystem);
            subsystemManager.AddSubsystem("scanner", ScannerSubsystem);
            subsystemManager.AddSubsystem("lookingglass", LookingGlassNetwork);
            subsystemManager.AddSubsystem("torpedo", TorpedoSubsystem);
            subsystemManager.AddSubsystem("loader", CombatLoaderSubsystem);

            subsystemManager.DeserializeManager(Storage);
        }

        CommandLine commandLine = new CommandLine();

        SubsystemManager subsystemManager;

        // SubtypeIDs:
        // NATO_25x184mm
        // Construction
        // MetalGrid
        // InteriorPlate
        // SteelPlate
        // Girder
        // SmallTube
        // LargeTube
        // Display
        // BulletproofGlass
        // Superconductor
        // Computer
        // Reactor
        // Thrust
        // GravityGenerator
        // Medical
        // RadioCommunication
        // Detector
        // Motor
        // Explosives
        // SolarCell
        // PowerCell
        // Canvas

        Dictionary<MyItemType, int> TorpComponents = new Dictionary<MyItemType, int>()
        {
            { MyItemType.MakeComponent("SteelPlate"), 87} ,
            { MyItemType.MakeComponent("Construction"), 47} ,
            { MyItemType.MakeComponent("LargeTube"), 5} ,
            { MyItemType.MakeComponent("Motor"), 10} ,
            { MyItemType.MakeComponent("Computer"), 37} ,
            { MyItemType.MakeComponent("MetalGrid"), 4} ,
            { MyItemType.MakeComponent("SmallTube"), 14} ,
            { MyItemType.MakeComponent("InteriorPlate"), 2} ,
            { MyItemType.MakeComponent("Girder"), 1} ,
            { MyItemType.MakeComponent("Explosives"), 2} ,
            { MyItemType.MakeComponent("PowerCell"), 2} ,
        };

        public void Save()
        {
            string v = subsystemManager.SerializeManager();
            Storage = v;
        }

        public void Main(string argument, UpdateType updateSource)
        {
            Context.UpdateTime();
            if (commandLine.TryParse(argument))
            {
                subsystemManager.CommandV2(commandLine);
            }
            else
            {
                subsystemManager.Update(updateSource);
                var status = subsystemManager.GetStatus();
                if (status != string.Empty) Echo(status);
            }
        }

        class LookingGlass_Fermi : ILookingGlassPlugin
        {
            public LookingGlassNetworkSubsystem Host { get; set; }

            public void Do4()
            {
                HostProgram.ScannerSubsystem.LookingGlassRaycast(Host.ActiveLookingGlass.PrimaryCamera, Host.Context.LocalTime);
            }

            public void Do7()
            {
            }

            public void Do6()
            {
                if (closestEnemyToCursorID != -1)
                {
                    HostProgram.IntelSubsystem.SetPriority(closestEnemyToCursorID, 1);
                    FeedbackOnTarget = true;
                }
            }

            public void Do5()
            {
                if (HostProgram.TorpedoSubsystem.TorpedoTubeGroups.ContainsKey("SM"))
                {
                    if (FireTorpedoAtCursorTarget("SM", Host.Context.LocalTime))
                    {
                        FeedbackOnTarget = true;
                        return;
                    }
                }
                FeedbackText = "NOT LOADED";
            }

            public void Do3()
            {
                if (CAPMode == 0)
                {
                    CAPMode = 1;
                    HostProgram.TaskGenerator.HornetAttackTask.Mode = 1;
                }
                else if (CAPMode == 1)
                {
                    HostProgram.TaskGenerator.HornetAttackTask.Mode = 0;
                    CAPMode = 3;
                }
                else
                {
                    CAPMode = 0;
                    HostProgram.AgentSubsystem.AddTask(TaskType.None, MyTuple.Create(IntelItemType.NONE, (long)0), CommandType.Override, 0, TimeSpan.Zero);
                    HostProgram.AutopilotSubsystem.Clear();
                }
            }

            public void Do8()
            {
            }

            public void Setup()
            {
            }

            public void UpdateHUD()
            {
                DrawInfoUI();
                DrawActionsUI();
                DrawMiddleHUD();
            }

            public void UpdateState()
            {
                if (CAPMode != 0)
                {
                    if (HostProgram.AgentSubsystem.TaskQueue.Count == 0)
                    {
                        if (closestEnemyToCursorID != -1)
                        {
                            HostProgram.AgentSubsystem.AddTask(TaskType.Attack, MyTuple.Create(IntelItemType.Enemy, closestEnemyToCursorID), CommandType.Override, 0,
                                Host.Context.CanonicalTime);
                        }
                    }
                }
            }

            public LookingGlass_Fermi(Program program)
            {
                HostProgram = program;
            }


            List<MySprite> SpriteScratchpad = new List<MySprite>();

            long closestEnemyToCursorID = -1;

            string FeedbackText = string.Empty;
            bool FeedbackOnTarget = false;

            Program HostProgram;

            bool HUDPromptOK = false;
            int CAPMode = 0; // 0 - off, 1 - aim, 2 - kite, 3 - standard

            int LastInventoryUpdate = 0;

            bool FireTorpedoAtCursorTarget(string group, TimeSpan localTime)
            {
                var intelItems = Host.IntelProvider.GetFleetIntelligences(localTime);
                var key = MyTuple.Create(IntelItemType.Enemy, closestEnemyToCursorID);
                var target = (EnemyShipIntel)intelItems.GetValueOrDefault(key, null);

                return HostProgram.TorpedoSubsystem.Fire(localTime, HostProgram.TorpedoSubsystem.TorpedoTubeGroups[group], target, false) != null;
            }

            void DrawInfoUI()
            {
                if (HostProgram.CombatLoaderSubsystem.UpdateNum > LastInventoryUpdate)
                {
                    LastInventoryUpdate = HostProgram.CombatLoaderSubsystem.UpdateNum;

                    var Builder = Host.Context.SharedStringBuilder;
                    Builder.Clear();

                    if (HostProgram.TorpedoSubsystem != null)
                    {
                        Builder.AppendLine("== TORPEDO TUBES ==");
                        Builder.AppendLine();

                        foreach (var kvp in HostProgram.TorpedoSubsystem.TorpedoTubeGroups)
                        {
                            int ready = kvp.Value.NumReady;
                            int total = kvp.Value.Children.Count();
                            if (total > 0)
                                // LG [||--    ] AUTO
                                Builder.Append(kvp.Value.Name).Append(" [").Append('|', ready).Append('-', total - ready).Append(' ', Math.Max(0, 8 - total)).Append(kvp.Value.AutoFire ? "] AUTO \n" : "] MANL \n");
                        }

                        Builder.AppendLine();
                    }


                    Builder.AppendLine("  ==== AMMO ====  ");

                    Builder.AppendLine();

                    int numGats = HostProgram.CombatLoaderSubsystem.TotalInventory.GetValueOrDefault(MyItemType.MakeAmmo("NATO_25x184mm"));

                    int percentGats = Math.Min(numGats / 10, 10);

                    Builder.Append('|', percentGats).Append(' ', 10 - percentGats).Append(' ').Append(string.Format("{0:000}", numGats)).AppendLine("  ");

                    Builder.AppendLine();

                    Builder.AppendLine("  ==== ==== ====  ");

                    Builder.AppendLine();

                    Builder.AppendLine(HostProgram.CombatLoaderSubsystem.LoadingInventory ? "  LOADING...    " : "");


                    foreach (var screen in Host.ActiveLookingGlass.LeftHUDs)
                    {
                        screen.FontColor = Host.ActiveLookingGlass.kFocusedColor;
                        screen.WriteText(Builder.ToString());
                    }
                }
            }

            void DrawActionsUI()
            {
                if (!HUDPromptOK)
                {
                    var Builder = Host.Context.SharedStringBuilder;
                    HUDPromptOK = true;
                    Builder.Clear();

                    Builder.AppendLine(" ===== BAR 2 ===== ");
                    Builder.AppendLine();
                    Builder.AppendLine("1 - FIRE WEAPON");
                    Builder.AppendLine("2 - CAMERA");
                    Builder.AppendLine("3 - COMBAT AUTOP.");
                    Builder.AppendLine("4 - RAYCAST");
                    Builder.AppendLine("5 - FIRE MISSILE");
                    Builder.AppendLine("6 - CONFIRM KILL");
                    Builder.AppendLine("7 - JUMP");
                    Builder.AppendLine("8 - RELOAD AMMO/");
                    Builder.AppendLine("    TORPEDOES");
                    Builder.AppendLine();
                    Builder.AppendLine(" ===== BAR 3 ===== ");
                    Builder.AppendLine();
                    Builder.AppendLine("1 - WELDERS ON/OFF");
                    Builder.AppendLine("2 - TURRETS ON/OFF");
                    Builder.AppendLine("3 - PROJECTORS");
                    Builder.AppendLine("    ON/OFF");

                    foreach (var screen in Host.ActiveLookingGlass.RightHUDs)
                    {
                        screen.FontColor = Host.ActiveLookingGlass.kFocusedColor;
                        screen.WriteText(Builder.ToString());
                    }
                }
            }

            void DrawMiddleHUD()
            {
                if (Host.ActiveLookingGlass.MiddleHUDs.Count == 0) return;

                var localTime = Host.Context.LocalTime;

                if (CAPMode == 1) FeedbackText = "CAP ON - AIM MODE - 3 FULL";
                else if (CAPMode == 3) FeedbackText = "CAP ON - FULL MODE - 3 OFF";
                else FeedbackText = string.Empty;

                foreach (var screen in Host.ActiveLookingGlass.MiddleHUDs)
                {
                    SpriteScratchpad.Clear();

                    Host.GetDefaultSprites(SpriteScratchpad);

                    float closestDistSqr = 200 * 200;
                    long newClosestIntelID = -1;

                    foreach (IFleetIntelligence intel in Host.IntelProvider.GetFleetIntelligences(localTime).Values)
                    {
                        if (intel.Type == IntelItemType.Friendly)
                        {
                            var fsi = (FriendlyShipIntel)intel;

                            if ((fsi.AgentStatus & AgentStatus.DockedAtHome) != 0)
                                continue;

                            LookingGlass.IntelSpriteOptions options = LookingGlass.IntelSpriteOptions.Small;
                            if (fsi.AgentClass == AgentClass.None)
                                options = LookingGlass.IntelSpriteOptions.ShowName;

                            Host.ActiveLookingGlass.FleetIntelItemToSprites(screen, intel, localTime, Host.ActiveLookingGlass.kFriendlyBlue, ref SpriteScratchpad, options);
                        }
                        else if (intel.Type == IntelItemType.Enemy)
                        {
                            LookingGlass.IntelSpriteOptions options = LookingGlass.IntelSpriteOptions.None;

                            if (!EnemyShipIntel.PrioritizeTarget((EnemyShipIntel)intel) || Host.IntelProvider.GetPriority(intel.ID) < 2)
                            {
                                options = LookingGlass.IntelSpriteOptions.Small;
                                Host.ActiveLookingGlass.FleetIntelItemToSprites(screen, intel, localTime, Host.ActiveLookingGlass.kEnemyRed, ref SpriteScratchpad, options);
                            }
                            else
                            {
                                if (intel.ID == closestEnemyToCursorID)
                                {
                                    options |= LookingGlass.IntelSpriteOptions.ShowDist | LookingGlass.IntelSpriteOptions.EmphasizeWithBrackets | LookingGlass.IntelSpriteOptions.NoCenter | LookingGlass.IntelSpriteOptions.ShowLastDetected;
                                    if (FeedbackOnTarget)
                                        options |= LookingGlass.IntelSpriteOptions.EmphasizeWithCross;
                                    options |= LookingGlass.IntelSpriteOptions.ShowTruncatedName;
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

                    using (var frame = screen.DrawFrame())
                    {
                        foreach (var spr in SpriteScratchpad)
                        {
                            frame.Add(spr);
                        }

                        if (FeedbackText != string.Empty)
                        {
                            var prompt = MySprite.CreateText(FeedbackText, "Debug", Color.HotPink, 0.9f);
                            prompt.Position = new Vector2(0, -45) + screen.TextureSize / 2f;
                            frame.Add(prompt);
                        }

                    }
                }

                FeedbackOnTarget = false;
            }
        }
    }
}
