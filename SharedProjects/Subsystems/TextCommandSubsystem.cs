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
    class EnemyPrioritizer : IComparer<EnemyShipIntel>
    {
        IIntelProvider IntelProvider;
        public int Compare(EnemyShipIntel x, EnemyShipIntel y)
        {
            int priX = IntelProvider.GetPriority(x.ID);
            int priY = IntelProvider.GetPriority(y.ID);
            if (priX.CompareTo(priY) != 0) return priX.CompareTo(priY);
            return x.Radius.CompareTo(y.Radius);
        }

        public EnemyPrioritizer(IIntelProvider intelProvider)
        {
            IntelProvider = intelProvider;
        }
    }

    class FriendlyPrioritizer : IComparer<FriendlyShipIntel>
    {
        IIntelProvider IntelProvider;
        public int Compare(FriendlyShipIntel x, FriendlyShipIntel y)
        {
            return x.ID.CompareTo(y.ID);
        }

        public FriendlyPrioritizer(IIntelProvider intelProvider)
        {
            IntelProvider = intelProvider;
        }
    }

    public class TextCommandSubsystem : ISubsystem
    {
        #region ISubsystem
        public UpdateFrequency UpdateFrequency => UpdateFrequency.Update100;

        public void Command(TimeSpan timestamp, string command, object argument)
        {
            if (command == "attack") Attack(timestamp);
            if (command == "recall") RecallCrafts(timestamp);
            if (command == "autohome") AutoHomeCrafts(timestamp);
            if (command == "toggleCAP") ToggleCAP(timestamp);
        }

        public void DeserializeSubsystem(string serialized)
        {
        }

        public string GetStatus()
        {
            return debugBuilder.ToString();
        }

        public string SerializeSubsystem()
        {
            return string.Empty;
        }

        public void Setup(MyGridProgram program, string name)
        {
            Program = program;
            GetParts();
        }

        public void Update(TimeSpan timestamp, UpdateFrequency updateFlags)
        {
            UpdateAlarms(timestamp);
            UpdateCAP(timestamp);
        }
        #endregion

        public TextCommandSubsystem(IIntelProvider intelProvider)
        {
            IntelProvider = intelProvider;
            Prioritizer = new EnemyPrioritizer(intelProvider);
            FriendlyPrioritizer = new FriendlyPrioritizer(intelProvider);
        }

        MyGridProgram Program;
        IIntelProvider IntelProvider;

        EnemyPrioritizer Prioritizer;
        FriendlyPrioritizer FriendlyPrioritizer;

        List<FriendlyShipIntel> FriendlyShipScratchpad = new List<FriendlyShipIntel>();
        List<DockIntel> DockIntelScratchpad = new List<DockIntel>();
        List<EnemyShipIntel> EnemyShipScratchpad = new List<EnemyShipIntel>();

        StringBuilder debugBuilder = new StringBuilder();

        Vector3D[] PatrolPositions = new Vector3D[6]
        {
            new Vector3D(800, 0, 0),
            new Vector3D(-800, 0, 0),
            new Vector3D(0, 800, 0),
            new Vector3D(0, -800, 0),
            new Vector3D(0, 0, 800),
            new Vector3D(0, 0, -800),
        };
        bool CAP = false;

        bool alarm;

        bool Alarm
        {
            set
            {
                if (alarm != value)
                {
                    alarm = value;
                    foreach (var light in AlarmLights)
                    {
                        light.Color = alarm ? Color.Red : Color.Green;
                    }
                }
            }
        }

        List<IMyInteriorLight> AlarmLights = new List<IMyInteriorLight>();
        IMyShipController Controller;

        private void GetParts()
        {
            AlarmLights.Clear();
            Program.GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(null, CollectParts);
            Alarm = true;
            Alarm = false;
        }

        private bool CollectParts(IMyTerminalBlock block)
        {
            if (!Program.Me.IsSameConstructAs(block)) return false;

            // Exclude types
            if (block is IMyInteriorLight && block.CustomName.Contains("Alarm"))
            {
                IMyInteriorLight light = (IMyInteriorLight)block;
                light.Radius = 20;
                AlarmLights.Add(light);
            }

            if (block is IMyShipController && ((IMyShipController)block).CanControlShip)
            {
                Controller = (IMyShipController)block;
            }

            return false;
        }

        private void UpdateAlarms(TimeSpan localTime)
        {
            var intelItems = IntelProvider.GetFleetIntelligences(localTime);
            bool hasEnemy = false;

            foreach (var kvp in intelItems)
            {
                if (kvp.Key.Item1 == IntelItemType.Enemy && IntelProvider.GetPriority(kvp.Key.Item2) > 1)
                {
                    hasEnemy = true;
                    break;
                }
            }

            Alarm = hasEnemy;
        }

        private void Attack(TimeSpan localTime)
        {
            FriendlyShipScratchpad.Clear();
            EnemyShipScratchpad.Clear();

            var intelItems = IntelProvider.GetFleetIntelligences(localTime);
            foreach (var kvp in intelItems)
            {
                if (kvp.Key.Item1 == IntelItemType.Friendly)
                {
                    var friendly = (FriendlyShipIntel)kvp.Value;
                    if (friendly.AgentClass == AgentClass.Fighter)
                    {
                        FriendlyShipScratchpad.Add(friendly);
                    }
                }
                else if (kvp.Key.Item1 == IntelItemType.Enemy)
                {
                    var enemy = (EnemyShipIntel)kvp.Value;
                    if (EnemyShipIntel.PrioritizeTarget(enemy) && IntelProvider.GetPriority(enemy.ID) > 1)
                        EnemyShipScratchpad.Add(enemy);
                }
            }

            if (EnemyShipScratchpad.Count == 0) return;
            if (FriendlyShipScratchpad.Count == 0) return;

            EnemyShipScratchpad.Sort(Prioritizer);
            EnemyShipScratchpad.Reverse();

            for (int i = 0; i < FriendlyShipScratchpad.Count; i++)
            {
                IntelProvider.ReportCommand(FriendlyShipScratchpad[i], TaskType.Attack, MyTuple.Create(IntelItemType.Enemy, EnemyShipScratchpad[i % EnemyShipScratchpad.Count].ID), localTime);
            }
        }

        private void RecallCrafts(TimeSpan localTime)
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

        private void AutoHomeCrafts(TimeSpan localTime)
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

        private void UpdateCAP(TimeSpan timestamp)
        {
            debugBuilder.Clear();
            debugBuilder.AppendLine(CAP.ToString());
            if (CAP)
            {
                FriendlyShipScratchpad.Clear();

                var intelItems = IntelProvider.GetFleetIntelligences(timestamp);
                foreach (var kvp in intelItems)
                {
                    if (kvp.Key.Item1 == IntelItemType.Friendly)
                    {
                        var friendly = (FriendlyShipIntel)kvp.Value;
                        if (friendly.AgentClass == AgentClass.Fighter)
                        {
                            FriendlyShipScratchpad.Add(friendly);
                        }
                    }
                }

                FriendlyShipScratchpad.Sort(FriendlyPrioritizer);

                debugBuilder.AppendLine(FriendlyShipScratchpad.Count.ToString());

                for (int i = 0; i < PatrolPositions.Count() && i < FriendlyShipScratchpad.Count(); i++)
                {
                    var pos = Program.Me.CubeGrid.WorldMatrix.Translation + PatrolPositions[i];
                    var waypoint = new Waypoint();
                    waypoint.Position = pos;
                    debugBuilder.AppendLine(pos.ToString());
                    waypoint.Velocity = Controller.GetShipVelocities().LinearVelocity;
                    IntelProvider.ReportFleetIntelligence(waypoint, timestamp);
                    IntelProvider.ReportCommand(FriendlyShipScratchpad[i], TaskType.Attack, MyTuple.Create(IntelItemType.Waypoint, waypoint.ID), timestamp);
                }
            }
        }

        private void ToggleCAP(TimeSpan timestamp)
        {
            CAP = !CAP;
        }
    }
}
