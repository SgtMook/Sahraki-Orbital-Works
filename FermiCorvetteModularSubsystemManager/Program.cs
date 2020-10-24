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
        public AutopilotSubsystem AutopilotSubsystem;
        public IntelSubsystem IntelSubsystem;
        public HornetCombatSubsystem CombatSubsystem;
        public LookingGlassNetworkSubsystem LookingGlassNetwork;
        public AgentSubsystem AgentSubsystem;
        public ScannerNetworkSubsystem ScannerSubsystem;
        public TorpedoSubsystem TorpedoSubsystem;
        public CombatLoaderSubsystem CombatLoaderSubsystem;

        public Program()
        {
            subsystemManager = new SubsystemManager(this);
            Runtime.UpdateFrequency = UpdateFrequency.Update1;

            AutopilotSubsystem = new AutopilotSubsystem();
            IntelSubsystem = new IntelSubsystem();
            CombatSubsystem = new HornetCombatSubsystem(IntelSubsystem);
            LookingGlassNetwork = new LookingGlassNetworkSubsystem(IntelSubsystem, "LG", false, false);
            AgentSubsystem = new AgentSubsystem(IntelSubsystem, AgentClass.None);
            TorpedoSubsystem = new TorpedoSubsystem(IntelSubsystem);
            AgentSubsystem.AddTaskGenerator(new HornetAttackTaskGenerator(this, CombatSubsystem, AutopilotSubsystem, AgentSubsystem, null, IntelSubsystem));
            CombatLoaderSubsystem = new CombatLoaderSubsystem("Fermi Cargo", "Combat Supplies");

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

        MyCommandLine commandLine = new MyCommandLine();

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
            subsystemManager.UpdateTime();
            if (commandLine.TryParse(argument))
            {
                subsystemManager.Command(commandLine.Argument(0), commandLine.Argument(1), commandLine.ArgumentCount > 2 ? commandLine.Argument(2) : null);
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

            public void Do4(TimeSpan localTime)
            {
                var pos = Host.ActiveLookingGlass.PrimaryCamera.WorldMatrix.Forward * 10000 + Host.ActiveLookingGlass.PrimaryCamera.WorldMatrix.Translation;
                HostProgram.ScannerSubsystem.TryScanTarget(pos, localTime);
            }

            public void Do7(TimeSpan localTime)
            {
            }

            public void Do6(TimeSpan localTime)
            {
                if (closestEnemyToCursorID != -1)
                {
                    HostProgram.IntelSubsystem.SetPriority(closestEnemyToCursorID, 1);
                    FeedbackOnTarget = true;
                }
            }

            public void Do5(TimeSpan localTime)
            {
                if (HostProgram.TorpedoSubsystem.TorpedoTubeGroups.ContainsKey("SM"))
                {
                    if (FireTorpedoAtCursorTarget("SM", localTime))
                    {
                        FeedbackOnTarget = true;
                        return;
                    }
                }
                FeedbackText = "NOT LOADED";
            }

            public void Do3(TimeSpan localTime)
            {
                if (CAPOn)
                {
                    CAPOn = false;
                    HostProgram.AgentSubsystem.AddTask(TaskType.None, MyTuple.Create(IntelItemType.NONE, (long)0), CommandType.Override, 0, TimeSpan.Zero);
                    HostProgram.AutopilotSubsystem.Clear();
                }
                else
                {
                    CAPOn = true;
                    if (closestEnemyToCursorID != -1)
                    {
                        HostProgram.AgentSubsystem.AddTask(TaskType.Attack, MyTuple.Create(IntelItemType.Enemy, closestEnemyToCursorID), CommandType.Override, 0,
                            localTime + HostProgram.IntelSubsystem.CanonicalTimeDiff);
                    }
                    else
                    {
                        Waypoint waypoint = new Waypoint();
                        waypoint.Position = HostProgram.AutopilotSubsystem.Controller.WorldMatrix.Translation + HostProgram.AutopilotSubsystem.Controller.WorldMatrix.Forward * 1000;
                        waypoint.Name = "Fermi Engaging";

                        HostProgram.IntelSubsystem.ReportFleetIntelligence(waypoint, localTime);

                        HostProgram.AgentSubsystem.AddTask(TaskType.Attack, MyTuple.Create(IntelItemType.Waypoint, waypoint.ID), CommandType.Override, 0,
                            localTime + HostProgram.IntelSubsystem.CanonicalTimeDiff);
                    }
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
                if (HostProgram.AgentSubsystem.TaskQueue.Count < 0 && CAPOn)
                {
                    CAPOn = false;
                    HostProgram.AgentSubsystem.AddTask(TaskType.None, MyTuple.Create(IntelItemType.NONE, (long)0), CommandType.Override, 0, TimeSpan.Zero);
                    HostProgram.AutopilotSubsystem.Clear();
                }
            }

            public LookingGlass_Fermi(Program program)
            {
                HostProgram = program;
            }

            StringBuilder Builder = new StringBuilder();

            List<MySprite> SpriteScratchpad = new List<MySprite>();

            long closestEnemyToCursorID = -1;

            string FeedbackText = string.Empty;
            bool FeedbackOnTarget = false;

            Program HostProgram;

            bool HUDPromptOK = false;
            bool CAPOn = false;

            int LastInventoryUpdate = 0;

            bool FireTorpedoAtCursorTarget(string group, TimeSpan localTime)
            {
                var intelItems = Host.IntelProvider.GetFleetIntelligences(localTime);
                var key = MyTuple.Create(IntelItemType.Enemy, closestEnemyToCursorID);
                var target = (EnemyShipIntel)intelItems.GetValueOrDefault(key, null);

                return HostProgram.TorpedoSubsystem.Fire(localTime, HostProgram.TorpedoSubsystem.TorpedoTubeGroups[group], target, false) != null;
            }

            void DrawActionsUI(TimeSpan timestamp)
            {
                if (!HUDPromptOK)
                {
                    HUDPromptOK = true;
                    Builder.Clear();

                    Builder.AppendLine(" ===== BAR 2 ===== ");
                    Builder.AppendLine();
                    Builder.AppendLine("1 - FIRE WEAPON");
                    Builder.AppendLine("2 - CAMERA");
                    Builder.AppendLine("3 - CAP ON/OFF");
                    Builder.AppendLine("4 - RAYCAST");
                    Builder.AppendLine("5 - FIRE MISSILE");
                    Builder.AppendLine("6 - CONFIRM KILL");
                    Builder.AppendLine("7 - JUMP");
                    Builder.AppendLine("8 - RELOAD AMMO/");
                    Builder.AppendLine("    TORPEDOES");
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

                if (HostProgram.CombatLoaderSubsystem.UpdateNum > LastInventoryUpdate)
                {
                    Builder.Clear();
                    Builder.AppendLine("  ==== TORP ====  ");

                    LastInventoryUpdate = HostProgram.CombatLoaderSubsystem.UpdateNum;

                    int numTorpsReserve = 10000;

                    foreach (var kvp in HostProgram.TorpComponents)
                    {
                        if (!HostProgram.CombatLoaderSubsystem.TotalInventory.ContainsKey(kvp.Key))
                        {
                            numTorpsReserve = 0;
                            break;
                        }
                        numTorpsReserve = Math.Min(numTorpsReserve, HostProgram.CombatLoaderSubsystem.TotalInventory[kvp.Key]/kvp.Value);
                    }

                    int numTorpsLoaded = HostProgram.TorpedoSubsystem.TorpedoTubeGroups["SM"].NumReady;

                    Builder.AppendLine();
                    Builder.Append("[");
                    for (int i = 1; i < 5; i++)
                    {
                        Builder.Append(i <= numTorpsLoaded ? "^" : " ").Append(i < 4 ? '|' : ']');
                    }
                    Builder.Append($"  {numTorpsLoaded}/4  ");
                    Builder.AppendLine();
                    Builder.AppendLine();
                    for (int i = 0; i < 24; i++)
                    {
                        if (i % 4 == 0) Builder.Append(' ');
                        if (i == 12) Builder.AppendLine(" ");
                        Builder.Append(i < numTorpsReserve ? '^' : ' ');
                    }

                    Builder.Append(numTorpsReserve > 24 ? "+ " : "  ");
                    Builder.AppendLine();

                    Builder.AppendLine("  ==== GATS ====  ");

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

            void DrawMiddleHUD(TimeSpan localTime)
            {
                if (Host.ActiveLookingGlass.MiddleHUDs.Count == 0) return;
                SpriteScratchpad.Clear();

                Host.GetDefaultSprites(SpriteScratchpad);

                float closestDistSqr = 200 * 200;
                long newClosestIntelID = -1;

                foreach (IFleetIntelligence intel in Host.IntelProvider.GetFleetIntelligences(localTime).Values)
                {
                    if (intel.IntelItemType == IntelItemType.Friendly)
                    {
                        var fsi = (FriendlyShipIntel)intel;

                        if ((fsi.AgentStatus & AgentStatus.DockedAtHome) != 0) continue;

                        LookingGlass.IntelSpriteOptions options = LookingGlass.IntelSpriteOptions.Small;
                        if (fsi.AgentClass == AgentClass.None) options = LookingGlass.IntelSpriteOptions.ShowName;

                        Host.ActiveLookingGlass.FleetIntelItemToSprites(intel, localTime, Host.ActiveLookingGlass.kFriendlyBlue, ref SpriteScratchpad, options);
                    }
                    else if (intel.IntelItemType == IntelItemType.Enemy)
                    {
                        LookingGlass.IntelSpriteOptions options = LookingGlass.IntelSpriteOptions.None;

                        if (!EnemyShipIntel.PrioritizeTarget((EnemyShipIntel)intel) || Host.IntelProvider.GetPriority(intel.ID) < 2)
                        {
                            options = LookingGlass.IntelSpriteOptions.Small;
                            Host.ActiveLookingGlass.FleetIntelItemToSprites(intel, localTime, Host.ActiveLookingGlass.kEnemyRed, ref SpriteScratchpad, options);
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

                if (CAPOn) FeedbackText = "CAP ONLINE - 3 OFF";
                else FeedbackText = string.Empty;

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
                            prompt.Position = new Vector2(0, -45) + screen.TextureSize / 2f;
                            frame.Add(prompt);
                        }

                        var HUD = MySprite.CreateText(Builder.ToString(), "Monospace", Color.LightBlue, 0.3f);
                        HUD.Position = new Vector2(0, -25) + screen.TextureSize / 2f;
                        frame.Add(HUD);
                    }
                }

                FeedbackOnTarget = false;
            }
        }
    }
}
