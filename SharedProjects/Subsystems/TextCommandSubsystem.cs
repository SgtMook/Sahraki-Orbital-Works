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
    public class TacticalCommandSubsystem : ISubsystem
    {
        #region ISubsystem
        public UpdateFrequency UpdateFrequency => UpdateFrequency.Update100;

        public void Command(TimeSpan timestamp, string command, object argument)
        {
            if (command == "scramble") Attack(timestamp);
            if (command == "recall") RecallCrafts(timestamp);
            if (command == "autohome") AutoHomeCrafts(timestamp);
            if (command == "togglescramble") { AutoScramble = !AutoScramble; };
        }

        public void DeserializeSubsystem(string serialized)
        {
        }

        public string GetStatus()
        {
            debugBuilder.Clear();
            debugBuilder.AppendLine(AutoScramble.ToString());
            debugBuilder.AppendLine(Controller.CustomName);
            return debugBuilder.ToString();
        }

        public string SerializeSubsystem()
        {
            return string.Empty;
        }

        IMyTerminalBlock ProgramReference;
        public void Setup(MyGridProgram program, string name, IMyTerminalBlock programReference = null)
        {
            ProgramReference = programReference;
            if (ProgramReference == null) ProgramReference = program.Me;
            Program = program;
            ParseConfigs();
            GetParts();
        }

        public void Update(TimeSpan timestamp, UpdateFrequency updateFlags)
        {
            UpdateAlarms(timestamp);
        }
        #endregion

        public TacticalCommandSubsystem(IIntelProvider intelProvider)
        {
            IntelProvider = intelProvider;
        }

        MyGridProgram Program;
        IIntelProvider IntelProvider;

        List<FriendlyShipIntel> FriendlyShipScratchpad = new List<FriendlyShipIntel>();
        List<DockIntel> DockIntelScratchpad = new List<DockIntel>();

        StringBuilder debugBuilder = new StringBuilder();

        bool alarm;
        bool AutoScramble;

        int alertDist = 5000;

        bool Alarm
        {
            set
            {
                if (alarm != value)
                {
                    alarm = value;
                    foreach (var light in AlarmLights)
                    {
                        light.Color = alarm ? new Color(255, 120, 120) : new Color(120, 255, 120);
                    }
                }
            }
        }

        List<IMyLightingBlock> AlarmLights = new List<IMyLightingBlock>();
        IMyShipController Controller;

        // [Command]
        // AutoScramble = False
        // AlertDist = 5000
        void ParseConfigs()
        {
            MyIni Parser = new MyIni();
            MyIniParseResult result;
            if (!Parser.TryParse(ProgramReference.CustomData, out result))
                return;

            AutoScramble = Parser.Get("Command", "AutoScramble").ToBoolean(false);
            alertDist = Parser.Get("Command", "AlertDist").ToInt32(alertDist);
        }

        void GetParts()
        {
            AlarmLights.Clear();
            Program.GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(null, CollectParts);
            Alarm = true;
            Alarm = false;
        }

        bool CollectParts(IMyTerminalBlock block)
        {
            if (!ProgramReference.IsSameConstructAs(block)) return false;

            // Exclude types
            if (block is IMyInteriorLight && block.CustomName.Contains("Alarm"))
            {
                IMyInteriorLight light = (IMyInteriorLight)block;
                AlarmLights.Add(light);
            }

            if (block is IMyShipController && ((IMyShipController)block).CanControlShip && (Controller == null || block.CustomName.Contains("[I]")))
            {
                Controller = (IMyShipController)block;
            }

            return false;
        }

        void UpdateAlarms(TimeSpan localTime)
        {
            var intelItems = IntelProvider.GetFleetIntelligences(localTime);
            bool hasEnemy = false;

            foreach (var kvp in intelItems)
            {
                if (kvp.Key.Item1 == IntelItemType.Enemy && IntelProvider.GetPriority(kvp.Key.Item2) > 1 && (kvp.Value.GetPositionFromCanonicalTime(localTime + IntelProvider.CanonicalTimeDiff) - Controller.GetPosition()).Length() < alertDist)
                {
                    hasEnemy = true;
                    break;
                }
            }

            Alarm = hasEnemy;

            if (hasEnemy && AutoScramble)
            {
                Attack(localTime);
            }
        }

        void Attack(TimeSpan localTime)
        {
            FriendlyShipScratchpad.Clear();

            var intelItems = IntelProvider.GetFleetIntelligences(localTime);
            foreach (var kvp in intelItems)
            {
                if (kvp.Key.Item1 == IntelItemType.Friendly)
                {
                    var friendly = (FriendlyShipIntel)kvp.Value;
                    if (friendly.AgentClass == AgentClass.Fighter && (friendly.AgentStatus & AgentStatus.Docked) != 0 && (friendly.GetPositionFromCanonicalTime(localTime + IntelProvider.CanonicalTimeDiff) - Controller.GetPosition()).Length() < 100)
                    {
                        FriendlyShipScratchpad.Add(friendly);
                    }
                }
            }

            if (FriendlyShipScratchpad.Count == 0) return;

            for (int i = 0; i < FriendlyShipScratchpad.Count; i++)
            {
                var targetWaypoint = new Waypoint();

                var gravDir = Controller.GetNaturalGravity();
                targetWaypoint.Position = Controller.GetPosition();
                var angle = 2 * i * Math.PI / FriendlyShipScratchpad.Count;

                if (gravDir != Vector3D.Zero)
                {
                    gravDir.Normalize();
                    var flatForward = Controller.WorldMatrix.Forward - VectorHelpers.VectorProjection(Controller.WorldMatrix.Forward, gravDir);
                    flatForward.Normalize();
                    var flatLeftDir = Vector3D.Cross(flatForward, gravDir);
                    targetWaypoint.Position += (flatForward * TrigHelpers.FastCos(angle) + flatLeftDir * TrigHelpers.FastSin(angle)) * 500;
                    targetWaypoint.Position -= gravDir * 200;
                }
                else
                {
                    targetWaypoint.Position += (Controller.WorldMatrix.Forward * TrigHelpers.FastCos(angle) + Controller.WorldMatrix.Left * TrigHelpers.FastSin(angle)) * 500;
                }

                targetWaypoint.Position += Controller.GetShipVelocities().LinearVelocity * 3;
                IntelProvider.ReportFleetIntelligence(targetWaypoint, localTime);
                IntelProvider.ReportCommand(FriendlyShipScratchpad[i], TaskType.Attack, MyTuple.Create(IntelItemType.Waypoint, targetWaypoint.ID), localTime);
            }
        }

        void RecallCrafts(TimeSpan localTime)
        {
            FriendlyShipScratchpad.Clear();

            var intelItems = IntelProvider.GetFleetIntelligences(localTime);
            foreach (var kvp in intelItems)
            {
                if (kvp.Key.Item1 == IntelItemType.Friendly)
                {
                    var friendly = (FriendlyShipIntel)kvp.Value;
                    if (!string.IsNullOrEmpty(friendly.CommandChannelTag))
                    {
                        FriendlyShipScratchpad.Add(friendly);
                    }
                }
            }

            if (FriendlyShipScratchpad.Count == 0) return;

            for (int i = 0; i < FriendlyShipScratchpad.Count; i++)
            {
                IntelProvider.ReportCommand(FriendlyShipScratchpad[i], TaskType.Dock, MyTuple.Create(IntelItemType.NONE, (long)0), localTime);
            }
        }

        void AutoHomeCrafts(TimeSpan localTime)
        {
            FriendlyShipScratchpad.Clear();
            DockIntelScratchpad.Clear();

            var intelItems = IntelProvider.GetFleetIntelligences(localTime);
            foreach (var kvp in intelItems)
            {
                if (kvp.Key.Item1 == IntelItemType.Friendly)
                {
                    var friendly = (FriendlyShipIntel)kvp.Value;
                    if (friendly.HomeID == -1 && !string.IsNullOrEmpty(friendly.CommandChannelTag))
                    {
                        FriendlyShipScratchpad.Add(friendly);
                    }
                }
                else if (kvp.Key.Item1 == IntelItemType.Dock)
                {
                    var dock = (DockIntel)kvp.Value;
                    if (dock.OwnerID == -1)
                        DockIntelScratchpad.Add(dock);
                }
            }

            if (FriendlyShipScratchpad.Count == 0) return;

            foreach (var craft in FriendlyShipScratchpad)
            {
                DockIntel targetDock = null;
                foreach (var dock in DockIntelScratchpad)
                {
                    if (DockIntel.TagsMatch(craft.HangarTags, dock.Tags))
                    {
                        targetDock = dock;
                        break;
                    }
                }
                
                if (targetDock != null)
                {
                    IntelProvider.ReportCommand(craft, TaskType.SetHome, MyTuple.Create(IntelItemType.Dock, targetDock.ID), localTime);
                    DockIntelScratchpad.Remove(targetDock);
                }
            }
        }
    }
}
