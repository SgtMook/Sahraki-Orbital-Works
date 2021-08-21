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
        public AtmoDrive AutopilotSubsystem;
        public IntelSubsystem IntelSubsystem;
        public HornetCombatSubsystem CombatSubsystem;
        public LookingGlassNetworkSubsystem LookingGlassNetwork;
        public AgentSubsystem AgentSubsystem;
        public ScannerNetworkSubsystem ScannerSubsystem;
        public CombatLoaderSubsystem CombatLoaderSubsystem;
        public HornetAttackTaskGenerator TaskGenerator;
     //   public DockingSubsystem DockingSubsystem;
        public TorpedoSubsystem TorpedoSubsystem;
        public IMyShipController Controller;

        ExecutionContext Context;

        bool ToolbarOutput = false;
        bool CombatAutopilot = false;

        EnemyShipIntel PriorityTarget = null;
        int LargestTargetDist = 0;

        int runs = 0;

/*        List<IMyShipWelder> Welders = new List<IMyShipWelder>();*/
//        IMyShipConnector Connector;
//        bool lastDocked = false;
//        bool scriptDocked = false;

        Dictionary<MyItemType, int> TorpComponents = new Dictionary<MyItemType, int>();
//         {
//             { MyItemType.MakeComponent("SteelPlate"), 87} ,
//             { MyItemType.MakeComponent("Construction"), 47} ,
//             { MyItemType.MakeComponent("LargeTube"), 5} ,
//             { MyItemType.MakeComponent("Motor"), 10} ,
//             { MyItemType.MakeComponent("Computer"), 37} ,
//             { MyItemType.MakeComponent("MetalGrid"), 4} ,
//             { MyItemType.MakeComponent("SmallTube"), 14} ,
//             { MyItemType.MakeComponent("InteriorPlate"), 2} ,
//             { MyItemType.MakeComponent("Girder"), 1} ,
//             { MyItemType.MakeComponent("Explosives"), 2} ,
//             { MyItemType.MakeComponent("PowerCell"), 2} ,
//         };
        public Program()
        {
            Context = new ExecutionContext(this);
            subsystemManager = new SubsystemManager(Context);
            Runtime.UpdateFrequency = UpdateFrequency.Update1;

            GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(null, CollectBlocks);
            AutopilotSubsystem = new AtmoDrive(Controller, 5, Me);
            AutopilotSubsystem.FullAuto = false;
            IntelSubsystem = new IntelSubsystem();
            CombatSubsystem = new HornetCombatSubsystem(IntelSubsystem, false);
            LookingGlassNetwork = new LookingGlassNetworkSubsystem(IntelSubsystem, "LG", false, false);
            AgentSubsystem = new AgentSubsystem(IntelSubsystem, AgentClass.None);
            TaskGenerator = new HornetAttackTaskGenerator(this, CombatSubsystem, AutopilotSubsystem, AgentSubsystem, null, IntelSubsystem);
            AgentSubsystem.AddTaskGenerator(TaskGenerator);
            TaskGenerator.HornetAttackTask.FocusedTarget = true;
            CombatLoaderSubsystem = new CombatLoaderSubsystem();
            //DockingSubsystem = new DockingSubsystem(IntelSubsystem, CombatLoaderSubsystem);
            TorpedoSubsystem = new TorpedoSubsystem(IntelSubsystem);

            ScannerSubsystem = new ScannerNetworkSubsystem(IntelSubsystem);
            LookingGlassNetwork.AddPlugin("combat", new LookingGlass_Fermi(this));

            subsystemManager.AddSubsystem("autopilot", AutopilotSubsystem);
            subsystemManager.AddSubsystem("intel", IntelSubsystem);
            subsystemManager.AddSubsystem("combat", CombatSubsystem);
            subsystemManager.AddSubsystem("agent", AgentSubsystem);
            subsystemManager.AddSubsystem("scanner", ScannerSubsystem);
            subsystemManager.AddSubsystem("lookingglass", LookingGlassNetwork);
            subsystemManager.AddSubsystem("loader", CombatLoaderSubsystem);
//            subsystemManager.AddSubsystem("docking", DockingSubsystem);
            subsystemManager.AddSubsystem("torpedo", TorpedoSubsystem);

            subsystemManager.DeserializeManager(Storage);

            ParseConfigs();
        }

        private bool CollectBlocks(IMyTerminalBlock block)
        {
            if (Me.CubeGrid.EntityId != block.CubeGrid.EntityId)
                return false;
            if (block is IMyShipController && (Controller == null || block.CustomName.Contains("[I]"))) Controller = (IMyShipController)block;
//            if (block is IMyShipWelder) Welders.Add((IMyShipWelder)block);
//             if (block is IMyShipConnector && block.CustomName.Contains("Docking"))
//             {
//                 Connector = (IMyShipConnector)block;
//                 lastDocked = Connector.Status == MyShipConnectorStatus.Connected;
//             }
            return false;
        }

        void ParseConfigs()
        {
            MyIni Parser = new MyIni();
            MyIniParseResult result;
            if (!Parser.TryParse(Me.CustomData, out result))
                return;

            ToolbarOutput = Parser.Get("SetUp", "ToolbarOutput").ToBoolean(false);
        }

        CommandLine commandLine = new CommandLine();

        SubsystemManager subsystemManager;

        public void Save()
        {
            string v = subsystemManager.SerializeManager();
            Storage = v;
        }

        void UpdateName()
        {
            if (!ToolbarOutput) return;
            var str = "";
            if (!CombatAutopilot)
            {
                str = " CAP OFF";
            }
            else if (PriorityTarget == null)
            {
                str = " TGT: NONE";
            }
            else
            {
                str = $" TGT: {LargestTargetDist}m";
            }
            var s = Me.CustomName.Split('|');
            if (s.Length == 2)
            {
                Me.CustomName = s[0] + "|" + str;
            }
        }

        public void Main(string argument, UpdateType updateSource)
        {
            subsystemManager.UpdateTime();
            if (argument == "combatautopilottoggle")
            {
                CombatAutopilot = !CombatAutopilot;

//                 foreach (var welder in Welders)
//                 {
//                     welder.Enabled = true;
//                 }

                if (!CombatAutopilot)
                {
                    AgentSubsystem.AddTask(TaskType.None, MyTuple.Create(IntelItemType.NONE, (long)0), CommandType.Override, 0, TimeSpan.Zero);
                    AutopilotSubsystem.Clear();
                }

                UpdateName();
            }
            else if (commandLine.TryParse(argument))
            {
                subsystemManager.CommandV2(commandLine);
            }
            else
            {
                runs++;
                /*
                if (Connector != null && runs % 5 == 0)
                {
                    if (lastDocked)
                    {
                        if (Connector.Status == MyShipConnectorStatus.Connectable)
                        {
                            if (scriptDocked)
                            {
                                scriptDocked = false;
                            }
                            else
                            {
                                scriptDocked = true;
                                DockingSubsystem.Dock();
                            }
                        }
                    }
                    else
                    {
                        if (Connector.Status == MyShipConnectorStatus.Connected)
                        {
                            DockingSubsystem.Dock();
                        }
                    }

                    if (scriptDocked && !CombatLoaderSubsystem.LoadingInventory && CombatLoaderSubsystem.QueueReload == 0)
                    {
                        DockingSubsystem.Undock();
                    }

                    lastDocked = Connector.Status == MyShipConnectorStatus.Connected;
                    scriptDocked = scriptDocked && lastDocked;
                }
                */
                if (runs % 30 == 0)
                {
                    if (CombatAutopilot)
                    {
                        var hadTarget = PriorityTarget != null;
                        var intelItems = IntelSubsystem.GetFleetIntelligences(subsystemManager.Timestamp);

                        PriorityTarget = null;
                        float HighestEnemyPriority = 0;

                        foreach (var kvp in intelItems)
                        {
                            if (kvp.Key.Item1 == IntelItemType.Enemy)
                            {
                                var enemy = kvp.Value as EnemyShipIntel;
                                var dist = (int)(enemy.GetPositionFromCanonicalTime(subsystemManager.Timestamp + IntelSubsystem.CanonicalTimeDiff) - Me.GetPosition()).Length();
                                var size = enemy.Radius;
                                if (dist > 2000 || size < 30)
                                    continue;

                                var priority = 2000 - dist;

                                if (priority > HighestEnemyPriority)
                                {
                                    PriorityTarget = enemy;
                                    HighestEnemyPriority = priority;
                                    LargestTargetDist = dist;
                                }
                            }
                        }

                        if (PriorityTarget == null)
                        {
                            if (hadTarget)
                            {
                                AgentSubsystem.AddTask(TaskType.None, MyTuple.Create(IntelItemType.NONE, (long)0), CommandType.Override, 0, TimeSpan.Zero);
                                AutopilotSubsystem.Clear();
                            }
                        }
                        else
                        {
                            AgentSubsystem.AddTask(TaskType.Attack, MyTuple.Create(IntelItemType.Enemy, PriorityTarget.ID), CommandType.Override, 0,
                                subsystemManager.Timestamp + IntelSubsystem.CanonicalTimeDiff);
                        }
                    }
                    else
                    {
                        PriorityTarget = null;
                    }

                    UpdateName();
                }

                subsystemManager.Update(updateSource);

                var status = subsystemManager.GetStatus();
                if (status != string.Empty) Echo(status);
            }
        }
        class LookingGlass_Fermi : ILookingGlassPlugin
        {
            public LookingGlassNetworkSubsystem Host { get; set; }

            public void Do3(TimeSpan localTime)
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

            public void Do4(TimeSpan localTime)
            {
                HostProgram.ScannerSubsystem.LookingGlassRaycast(Host.ActiveLookingGlass.PrimaryCamera, localTime);
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
            public void Do6(TimeSpan localTime)
            {
                if (HostProgram.TorpedoSubsystem.TorpedoTubeGroups.ContainsKey("LG"))
                {
                    if (FireTorpedoAtCursorTarget("LG", localTime))
                    {
                        FeedbackOnTarget = true;
                        return;
                    }
                }
                FeedbackText = "NOT LOADED";
            }

            public void Do7(TimeSpan localTime)
            {
                if (closestEnemyToCursorID != -1)
                {
                    HostProgram.IntelSubsystem.SetPriority(closestEnemyToCursorID, 1);
                    FeedbackOnTarget = true;
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
                DrawInfoUI(localTime);
            }

            public void UpdateState(TimeSpan localTime)
            {
                if (CAPMode != 0)
                {
                    if (HostProgram.AgentSubsystem.TaskQueue.Count == 0)
                    {
                        if (closestEnemyToCursorID != -1)
                        {
                            HostProgram.AgentSubsystem.AddTask(TaskType.Attack, MyTuple.Create(IntelItemType.Enemy, closestEnemyToCursorID), CommandType.Override, 0,
                                localTime + HostProgram.IntelSubsystem.CanonicalTimeDiff);
                        }
                    }
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
            int CAPMode = 0; // 0 - off, 1 - aim, 2 - kite, 3 - standard

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
                    Builder.AppendLine("3 - COMBAT AUTOP.");
                    Builder.AppendLine("4 - RAYCAST");
                    Builder.AppendLine("5 - FIRE MISSILE");
                    Builder.AppendLine("6 - FIRE TORPEDO");
                    Builder.AppendLine("7 - CONFIRM KILL");
                    Builder.AppendLine("8 - JUMP");
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

            void DrawMiddleHUD(TimeSpan localTime)
            {
                if (Host.ActiveLookingGlass.MiddleHUDs.Count == 0) return;
                SpriteScratchpad.Clear();

                Host.GetDefaultSprites(SpriteScratchpad);

                float closestDistSqr = 200 * 200;
                long newClosestIntelID = -1;

                foreach (IFleetIntelligence intel in Host.IntelProvider.GetFleetIntelligences(localTime).Values)
                {
                    if (intel.Type == IntelItemType.Friendly)
                    {
                        var fsi = (FriendlyShipIntel)intel;

                        if ((fsi.AgentStatus & AgentStatus.DockedAtHome) != 0) continue;

                        LookingGlass.IntelSpriteOptions options = LookingGlass.IntelSpriteOptions.Small;
                        if (fsi.AgentClass == AgentClass.None) options = LookingGlass.IntelSpriteOptions.ShowName;

                        Host.ActiveLookingGlass.FleetIntelItemToSprites(intel, localTime, Host.ActiveLookingGlass.kFriendlyBlue, ref SpriteScratchpad, options);
                    }
                    else if (intel.Type == IntelItemType.Enemy)
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

                if (CAPMode == 1) FeedbackText = "CAP ON - AIM MODE - 3 FULL";
                else if (CAPMode == 3) FeedbackText = "CAP ON - FULL MODE - 3 OFF";
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

            void DrawInfoUI(TimeSpan timestamp)
            {
                if (HostProgram.CombatLoaderSubsystem.UpdateNum > LastInventoryUpdate)
                {
                    LastInventoryUpdate = HostProgram.CombatLoaderSubsystem.UpdateNum;

                    Builder.Clear();
                    Builder.AppendLine("  ==== TORP ====  \n");

                    if (HostProgram.TorpedoSubsystem == null)
                    {
                        Builder.AppendLine("- NO TORPEDOS -    ");
                    }
                    else
                    {
                        foreach (var kvp in HostProgram.TorpedoSubsystem.TorpedoTubeGroups)
                        {
                            int ready = kvp.Value.NumReady;
                            int total = kvp.Value.Children.Count();
                            // LG [||--    ] AUTO
                            Builder.Append(kvp.Value.Name).Append(" [").Append('|', ready).Append('-', total - ready).Append(' ', Math.Max(0, 8 - total)).Append(kvp.Value.AutoFire ? "] AUTO \n" : "] MANL \n");
                        }
                    }
// 
//                     int numTorpsReserve = 10000;
// 
//                     foreach (var kvp in HostProgram.TorpComponents)
//                     {
//                         if (!HostProgram.CombatLoaderSubsystem.TotalInventory.ContainsKey(kvp.Key))
//                         {
//                             numTorpsReserve = 0;
//                             break;
//                         }
//                         numTorpsReserve = Math.Min(numTorpsReserve, HostProgram.CombatLoaderSubsystem.TotalInventory[kvp.Key] / kvp.Value);
//                     }
// 
//                     if (HostProgram.TorpedoSubsystem.TorpedoTubeGroups.ContainsKey("SM"))
//                     {
//                         int numTorpsLoaded = HostProgram.TorpedoSubsystem.TorpedoTubeGroups["SM"].NumReady;
// 
//                         Builder.AppendLine();
//                         Builder.Append("[");
//                         for (int i = 1; i < 5; i++)
//                         {
//                             Builder.Append(i <= numTorpsLoaded ? "^" : " ").Append(i < 4 ? '|' : ']');
//                         }
//                         Builder.Append($"  {numTorpsLoaded}/4  ");
//                         Builder.AppendLine();
//                         Builder.AppendLine();
//                         for (int i = 0; i < 24; i++)
//                         {
//                             if (i % 4 == 0) Builder.Append(' ');
//                             if (i == 12) Builder.AppendLine(" ");
//                             Builder.Append(i < numTorpsReserve ? '^' : ' ');
//                         }
// 
//                         Builder.Append(numTorpsReserve > 24 ? "+ " : "  ");
//                         Builder.AppendLine();
//                     }
//                     else
//                     {
//                         Builder.AppendLine("NO TORPEDOES");
//                     }

                    Builder.AppendLine("\n  == 25x184MM ==  \n");
                    
                    var ammoType = MyItemType.MakeAmmo("NATO_25x184mm");
                    int ammo = HostProgram.CombatLoaderSubsystem.TotalInventory.GetValueOrDefault(ammoType);
                    int target = HostProgram.CombatLoaderSubsystem.TotalInventoryRequests.GetValueOrDefault(ammoType, 500);
                    int percentAmmo = Math.Min(ammo*10 / target, 10);

                    Builder.Append('|', percentAmmo).Append(' ', 10 - percentAmmo).Append(' ').Append(string.Format("{0:000}", ammo)).AppendLine("  ");

                    Builder.AppendLine("\n  == ROCKETS ==  \n");

                    ammoType = MyItemType.MakeAmmo("Missile200mm");
                    ammo = HostProgram.CombatLoaderSubsystem.TotalInventory.GetValueOrDefault(ammoType);
                    target = HostProgram.CombatLoaderSubsystem.TotalInventoryRequests.GetValueOrDefault(ammoType, 200);
                    percentAmmo = Math.Min(ammo * 10 / target, 10);

                    Builder.Append('|', percentAmmo).Append(' ', 10 - percentAmmo).Append(' ').Append(string.Format("{0:000}", ammo)).AppendLine("  ");

                    Builder.AppendLine("\n  ==== ==== ====  \n");

                    Builder.AppendLine(HostProgram.CombatLoaderSubsystem.LoadingInventory ? "  LOADING...    " : "");

                    foreach (var screen in Host.ActiveLookingGlass.LeftHUDs)
                    {
                        screen.FontColor = Host.ActiveLookingGlass.kFocusedColor;
                        screen.WriteText(Builder.ToString());
                    }
                }
            }
        }
    }

}
