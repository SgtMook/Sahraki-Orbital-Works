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
        public AgentSubsystem AgentSubsystem;
        public ScannerNetworkSubsystem ScannerSubsystem;
        public CombatLoaderSubsystem CombatLoaderSubsystem;
        public HornetAttackTaskGenerator TaskGenerator;
        public DockingSubsystem DockingSubsystem;
        public TorpedoSubsystem TorpedoSubsystem;
        public IMyShipController Controller;

        IMyCockpit Cockpit;

        bool ToolbarOutput = false;
        bool CombatAutopilot = false;

        EnemyShipIntel PriorityTarget = null;
        int LargestTargetDist = 0;

        int runs = 0;

        List<IMyShipWelder> Welders = new List<IMyShipWelder>();
        IMyShipConnector Connector;
        bool lastDocked = false;
        bool scriptDocked = false;

        public Program()
        {
            subsystemManager = new SubsystemManager(this);
            Runtime.UpdateFrequency = UpdateFrequency.Update1;

            GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(null, CollectBlocks);
            AutopilotSubsystem = new AtmoDrive(Controller, 5, Me);
            AutopilotSubsystem.FullAuto = false;
            IntelSubsystem = new IntelSubsystem();
            CombatSubsystem = new HornetCombatSubsystem(IntelSubsystem, false);
            AgentSubsystem = new AgentSubsystem(IntelSubsystem, AgentClass.None);
            TaskGenerator = new HornetAttackTaskGenerator(this, CombatSubsystem, AutopilotSubsystem, AgentSubsystem, null, IntelSubsystem);
            AgentSubsystem.AddTaskGenerator(TaskGenerator);
            TaskGenerator.HornetAttackTask.FocusedTarget = true;
            CombatLoaderSubsystem = new CombatLoaderSubsystem();
            DockingSubsystem = new DockingSubsystem(IntelSubsystem, CombatLoaderSubsystem);
            TorpedoSubsystem = new TorpedoSubsystem(IntelSubsystem);

            ScannerSubsystem = new ScannerNetworkSubsystem(IntelSubsystem);

            subsystemManager.AddSubsystem("autopilot", AutopilotSubsystem);
            subsystemManager.AddSubsystem("intel", IntelSubsystem);
            subsystemManager.AddSubsystem("combat", CombatSubsystem);
            subsystemManager.AddSubsystem("agent", AgentSubsystem);
            subsystemManager.AddSubsystem("scanner", ScannerSubsystem);
            subsystemManager.AddSubsystem("loader", CombatLoaderSubsystem);
            subsystemManager.AddSubsystem("docking", DockingSubsystem);
            subsystemManager.AddSubsystem("torpedo", TorpedoSubsystem);

            subsystemManager.DeserializeManager(Storage);

            ParseConfigs();
        }

        private bool CollectBlocks(IMyTerminalBlock block)
        {
            if (Me.CubeGrid.EntityId != block.CubeGrid.EntityId)
                return false;
            if (block is IMyShipController && (Controller == null || block.CustomName.Contains("[I]"))) Controller = (IMyShipController)block;
            if (block is IMyCockpit) Cockpit = (IMyCockpit)block;
            if (block is IMyShipWelder) Welders.Add((IMyShipWelder)block);
            if (block is IMyShipConnector && block.CustomName.Contains("Docking"))
            {
                Connector = (IMyShipConnector)block;
                lastDocked = Connector.Status == MyShipConnectorStatus.Connected;
            }
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

        MyCommandLine commandLine = new MyCommandLine();

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

                foreach (var welder in Welders)
                {
                    welder.Enabled = true;
                }

                if (!CombatAutopilot)
                {
                    AgentSubsystem.AddTask(TaskType.None, MyTuple.Create(IntelItemType.NONE, (long)0), CommandType.Override, 0, TimeSpan.Zero);
                    AutopilotSubsystem.Clear();
                }

                UpdateName();
            }
            else if (argument == "releaseremote 1")
            {
                IGC.SendBroadcastMessage("[PDCFORGE]", "1");
            }
            else if (argument == "releaseremote 2")
            {
                IGC.SendBroadcastMessage("[PDCFORGE]", "2");
            }
            else if (argument == "releaseremote 3")
            {
                IGC.SendBroadcastMessage("[PDCFORGE]", "3");
            }
            else if (commandLine.TryParse(argument))
            {
                subsystemManager.Command(commandLine.Argument(0), commandLine.Argument(1), commandLine.ArgumentCount > 2 ? commandLine.Argument(2) : null);
            }
            else
            {
                runs++;

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

                if (runs % 30 == 0)
                {
                    if (!Cockpit.IsUnderControl)
                    {
                        CombatAutopilot = false;
                    }

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
    }
}
