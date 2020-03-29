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
            if (command == "cyclemode") CycleMode(timestamp);
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
            UpdatePassiveCommand(timestamp);
        }
        #endregion

        public TextCommandSubsystem(IIntelProvider intelProvider)
        {
            IntelProvider = intelProvider;
            Prioritizer = new EnemyPrioritizer(intelProvider);
            FriendlyPrioritizer = new FriendlyPrioritizer(intelProvider);
            PatrolSpeed = (float)(2 * Math.PI * PatrolRange / PatrolSeconds);
        }

        MyGridProgram Program;
        IIntelProvider IntelProvider;

        EnemyPrioritizer Prioritizer;
        FriendlyPrioritizer FriendlyPrioritizer;

        List<FriendlyShipIntel> FriendlyShipScratchpad = new List<FriendlyShipIntel>();
        List<DockIntel> DockIntelScratchpad = new List<DockIntel>();
        List<EnemyShipIntel> EnemyShipScratchpad = new List<EnemyShipIntel>();

        StringBuilder debugBuilder = new StringBuilder();

        enum DroneMode
        {
            None,
            Recall,
            Escort,
            Patrol,
            Attack,
        }

        DroneMode PassiveMode = DroneMode.None;

        Vector3D[] EscortPositions = new Vector3D[6]
        {
            new Vector3D(200, 0, 0),
            new Vector3D(-200, 0, 0),
            new Vector3D(0, 200, 0),
            new Vector3D(0, -200, 0),
            new Vector3D(0, 0, 200),
            new Vector3D(0, 0, -200),
        };

        int PatrolSeconds = 120;
        int PatrolRange = 800;
        float PatrolSpeed;

        MatrixD PatrolXMatrix = MatrixD.Identity;
        MatrixD PatrolYMatrix = MatrixD.Identity;
        MatrixD PatrolZMatrix = MatrixD.Identity;

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

        private void UpdatePassiveCommand(TimeSpan timestamp)
        {
            if (PassiveMode != DroneMode.None)
            {
                FriendlyShipScratchpad.Clear();
                DockIntelScratchpad.Clear();
                EnemyShipScratchpad.Clear();

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
                    else if (kvp.Key.Item1 == IntelItemType.Dock)
                    {
                        var dock = (DockIntel)kvp.Value;
                        if (dock.OwnerID == -1)
                            DockIntelScratchpad.Add(dock);
                    }
                    else if (kvp.Key.Item1 == IntelItemType.Enemy)
                    {
                        var enemy = (EnemyShipIntel)kvp.Value;
                        if (EnemyShipIntel.PrioritizeTarget(enemy) && IntelProvider.GetPriority(enemy.ID) > 1)
                            EnemyShipScratchpad.Add(enemy);
                    }
                }
                FriendlyShipScratchpad.Sort(FriendlyPrioritizer);
                EnemyShipScratchpad.Sort(Prioritizer);
                EnemyShipScratchpad.Reverse();

                int enemyIndex = 0;

                if (PassiveMode == DroneMode.Patrol) UpdatePatrolMatrices(timestamp.TotalSeconds % PatrolSeconds);

                for (int i = 0;i < FriendlyShipScratchpad.Count(); i++)
                {
                    // Set home first
                    if (FriendlyShipScratchpad[i].HomeID == -1)
                    {
                        for (int j = 0; j < DockIntelScratchpad.Count; j++)
                        {
                            if (DockIntel.TagsMatch(FriendlyShipScratchpad[i].HangarTags, DockIntelScratchpad[j].Tags))
                            {
                                IntelProvider.ReportCommand(FriendlyShipScratchpad[i], TaskType.SetHome, MyTuple.Create(IntelItemType.Dock, DockIntelScratchpad[j].ID), timestamp);
                                DockIntelScratchpad.Remove(DockIntelScratchpad[j]);
                                break;
                            }
                        }
                        continue;
                    }

                    // If low OR if we are on recall mode
                    if (VitalsLow(FriendlyShipScratchpad[i]) || PassiveMode == DroneMode.Recall)
                    {
                        // If not recalling or docked
                        if ((FriendlyShipScratchpad[i].AgentStatus & (AgentStatus.Recalling | AgentStatus.DockedAtHome)) == 0) IntelProvider.ReportCommand(FriendlyShipScratchpad[i], TaskType.Dock, MyTuple.Create(IntelItemType.NONE, (long)0), timestamp);
                        continue;
                    }

                    // If docked
                    if ((FriendlyShipScratchpad[i].AgentStatus & AgentStatus.DockedAtHome) != 0 && (!VitalsHigh(FriendlyShipScratchpad[i]) || PassiveMode == DroneMode.Recall))
                    {
                        continue;
                    }

                    if ((PassiveMode == DroneMode.Escort || (PassiveMode == DroneMode.Attack && EnemyShipScratchpad.Count == 0)) && EscortPositions.Length > i)
                    {
                        var pos = Program.Me.CubeGrid.WorldMatrix.Translation + EscortPositions[i];
                        var waypoint = new Waypoint();
                        waypoint.Position = pos;
                        waypoint.Velocity = Controller.GetShipVelocities().LinearVelocity;
                        IntelProvider.ReportFleetIntelligence(waypoint, timestamp);
                        IntelProvider.ReportCommand(FriendlyShipScratchpad[i], TaskType.Attack, MyTuple.Create(IntelItemType.Waypoint, waypoint.ID), timestamp);
                    }
                    else if ((PassiveMode == DroneMode.Patrol) && EscortPositions.Length > i)
                    {
                        var pos = Program.Me.CubeGrid.WorldMatrix.Translation + PatrolOffset(i);
                        var waypoint = new Waypoint();
                        waypoint.Position = pos;
                        waypoint.Velocity = Controller.GetShipVelocities().LinearVelocity;
                        waypoint.MaxSpeed = (float)(Controller.GetShipSpeed() + PatrolSpeed);
                        IntelProvider.ReportFleetIntelligence(waypoint, timestamp);
                        IntelProvider.ReportCommand(FriendlyShipScratchpad[i], TaskType.Attack, MyTuple.Create(IntelItemType.Waypoint, waypoint.ID), timestamp);
                    }
                    else if (PassiveMode == DroneMode.Attack)
                    {
                        IntelProvider.ReportCommand(FriendlyShipScratchpad[i], TaskType.Attack, MyTuple.Create(IntelItemType.Enemy, EnemyShipScratchpad[enemyIndex].ID), timestamp);
                        enemyIndex++;
                        if (enemyIndex >= EnemyShipScratchpad.Count) enemyIndex = 0;
                    }
                }
            }
        }

        private void CycleMode(TimeSpan timestamp)
        {
            if (PassiveMode == DroneMode.None) PassiveMode = DroneMode.Recall;
            else if (PassiveMode == DroneMode.Recall) PassiveMode = DroneMode.Escort;
            else if (PassiveMode == DroneMode.Escort) PassiveMode = DroneMode.Patrol;
            else if (PassiveMode == DroneMode.Patrol) PassiveMode = DroneMode.Attack;
            else if (PassiveMode == DroneMode.Attack) PassiveMode = DroneMode.None;
        }

        private bool VitalsLow(FriendlyShipIntel ship)
        {
            return ship.HydroPowerInv.X < 25 || ship.HydroPowerInv.Y < 15 || ship.HydroPowerInv.Z < 8;
        }

        private bool VitalsHigh(FriendlyShipIntel ship)
        {
            return ship.HydroPowerInv.X > 95 && ship.HydroPowerInv.Y > 20 && ship.HydroPowerInv.Z > 50;
        }

        private void UpdatePatrolMatrices(double timeMod)
        {
            double theta = Math.PI * 2 * timeMod / PatrolSeconds;

            PatrolXMatrix.M22 = (float)Math.Cos(theta);
            PatrolXMatrix.M23 = (float)Math.Sin(theta);
            PatrolXMatrix.M32 = -(float)Math.Sin(theta);
            PatrolXMatrix.M33 = (float)Math.Cos(theta);

            PatrolYMatrix.M11 = (float)Math.Cos(theta);
            PatrolYMatrix.M13 = (float)Math.Sin(theta);
            PatrolYMatrix.M31 = -(float)Math.Sin(theta);
            PatrolYMatrix.M33 = (float)Math.Cos(theta);

            PatrolZMatrix.M11 = (float)Math.Cos(theta);
            PatrolZMatrix.M12 = (float)Math.Sin(theta);
            PatrolZMatrix.M21 = -(float)Math.Sin(theta);
            PatrolZMatrix.M22 = (float)Math.Cos(theta);
        }

        private Vector3D PatrolOffset(int index)
        {
            if (index == 0) return Vector3D.Transform(new Vector3D(PatrolRange, 0, 0), PatrolYMatrix);
            if (index == 1) return Vector3D.Transform(new Vector3D(0, PatrolRange, 0), PatrolZMatrix);
            if (index == 2) return Vector3D.Transform(new Vector3D(0, 0, PatrolRange), PatrolXMatrix);
            if (index == 3) return Vector3D.Transform(new Vector3D(-PatrolRange, 0, 0), PatrolYMatrix);
            if (index == 4) return Vector3D.Transform(new Vector3D(0, -PatrolRange, 0), PatrolZMatrix);
            return Vector3D.Transform(new Vector3D(0, 0, -PatrolRange), PatrolXMatrix);
        }
    }
}
