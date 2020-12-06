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
    public class ScannerNetworkSubsystem : ISubsystem
    {
        #region ISubsystem
        public UpdateFrequency UpdateFrequency => UpdateFrequency.Update1;

        public void Command(TimeSpan timestamp, string command, object argument)
        {
            if (command == "designate") Designate(timestamp);
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

        IMyTerminalBlock ProgramReference;
        public void Setup(MyGridProgram program, string name, IMyTerminalBlock programReference = null)
        {
            ProgramReference = programReference;
            if (ProgramReference == null) ProgramReference = program.Me;
            Program = program;
            if (!WCAPI.Activate(program.Me)) WCAPI = null;
            GetParts();
        }

        public void Update(TimeSpan timestamp, UpdateFrequency updateFlags)
        {
            runs++;
            if (runs % 10 == 0)
            {
                TryScan(timestamp);
                if (Designator != null) UpdateDesignator(timestamp);
                UpdateHardLock1();
            } else if (runs % 10 == 1)
            {
                UpdateHardLock2(timestamp);
            }
        }

        void UpdateHardLock1()
        {
            WCTargetVelocityScratchpad.Clear();
            foreach(var target in WCHardlockTargets.Values)
            {
                if (target.GetPosition() == Vector3D.Zero) continue;
                WCTargetVelocityScratchpad.Add(target.EntityId, target.GetPosition());
            }
        }

        void UpdateHardLock2(TimeSpan timestamp)
        {
            var intelItems = IntelProvider.GetFleetIntelligences(timestamp);
            foreach (var target in WCHardlockTargets.Values)
            {
                var velocity = (target.GetPosition() - WCTargetVelocityScratchpad[target.EntityId]) * 60;
                IFleetIntelligence intel;
                var intelKey = MyTuple.Create(IntelItemType.Enemy, target.EntityId);
                if (!intelItems.TryGetValue(intelKey, out intel))
                {
                    intel = new EnemyShipIntel();
                }

                ((EnemyShipIntel)intel).FromCubeGrid(target, timestamp + IntelProvider.CanonicalTimeDiff, velocity);
                IntelProvider.ReportFleetIntelligence(intel, timestamp);
            }
        }

        #endregion
        MyGridProgram Program;
        IIntelProvider IntelProvider;
        List<EnemyShipIntel> EnemyIntelScratchpad = new List<EnemyShipIntel>();
        string TagPrefix = string.Empty;
        string DesignatorPrefix = string.Empty;

        int runs = 0;

        List<ScannerGroup> ScannerGroups = new List<ScannerGroup>();
        List<IMyCameraBlock> Cameras = new List<IMyCameraBlock>();
        IMyCameraBlock Designator;

        List<IMyLargeTurretBase> Turrets = new List<IMyLargeTurretBase>();
        List<IMyTerminalBlock> WCTurrets = new List<IMyTerminalBlock>();

        Dictionary<IMyEntity, float> GetThreatsScratchpad = new Dictionary<IMyEntity, float>();

        StringBuilder debugBuilder = new StringBuilder();

        WcPbApi WCAPI = new WcPbApi();

        public Dictionary<long, IMyCubeGrid> WCHardlockTargets = new Dictionary<long, IMyCubeGrid>();
        Dictionary<long, Vector3D> WCTargetVelocityScratchpad = new Dictionary<long, Vector3D>();

        int ScanExtent;
        float ScanScatter;

        public ScannerNetworkSubsystem(IIntelProvider intelProvider, string tag = "SE", int scanExtent = 30, float scanScatter = 0.25f)
        {
            IntelProvider = intelProvider;
            if (tag != string.Empty)
            {
                TagPrefix = $"[{tag}";
                DesignatorPrefix = $"[{tag}] <D> T:";
            }

            ScanExtent = scanExtent;
            ScanScatter = scanScatter;
        }

        void GetParts()
        {
            Cameras.Clear();
            Designator = null;
            Program.GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(null, GetTurrets);
            Program.GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(null, GetCameras);
        }

        bool GetTurrets(IMyTerminalBlock block)
        {
            if (!ProgramReference.IsSameConstructAs(block)) return false;
            if (block is IMyLargeTurretBase)
            {
                if (WCAPI != null && WCAPI.HasCoreWeapon(block)) WCTurrets.Add(block);
                else Turrets.Add((IMyLargeTurretBase)block);
            }
            return false;
        }

        bool GetCameras(IMyTerminalBlock block)
        {
            if (!(block is IMyCameraBlock)) return false;
            if (!(block.CustomName.StartsWith(TagPrefix))) return false;

            var camera = (IMyCameraBlock)block;

            if (camera.CustomName.Contains("<D>"))
            {
                Designator = camera;
                Designator.EnableRaycast = true;
                return false;
            }

            if (camera.IsSameConstructAs(ProgramReference))
            {
                camera.EnableRaycast = true;
                Cameras.Add(camera);
            }

            return false;
        }

        void TryScan(TimeSpan localTime)
        {
            // Go through each target
            var intelItems = IntelProvider.GetFleetIntelligences(localTime);
            var canonicalTime = localTime + IntelProvider.CanonicalTimeDiff;
            foreach (var kvp in intelItems)
            {
                if (kvp.Key.Item1 != IntelItemType.Enemy) continue;
                if (WCHardlockTargets.ContainsKey(kvp.Key.Item2)) continue; // Don't scan hardlocked targets
                EnemyShipIntel enemy = (EnemyShipIntel)kvp.Value;

                int priority = IntelProvider.GetPriority(kvp.Key.Item2);
                if (priority < 1) continue;

                if (!EnemyShipIntel.PrioritizeTarget(enemy)) continue;
                if (enemy.LastValidatedCanonicalTime + TimeSpan.FromSeconds(0.1) > canonicalTime) continue;
                if (enemy.LastValidatedCanonicalTime + TimeSpan.FromSeconds(0.2) > canonicalTime && priority < 4) continue;

                Vector3D targetPosition = kvp.Value.GetPositionFromCanonicalTime(canonicalTime);

                TryScanTarget(targetPosition, localTime, enemy);

            }

            var intelDict = IntelProvider.GetFleetIntelligences(localTime);

            foreach (var turret in Turrets)
            {
                if (!turret.HasTarget) continue;
                var target = turret.GetTargetedEntity();
                if (target.IsEmpty()) continue;
                if (target.Type != MyDetectedEntityType.SmallGrid && target.Type != MyDetectedEntityType.LargeGrid) continue;
                if (target.Relationship != MyRelationsBetweenPlayerAndBlock.Enemies) continue;

                if (WCHardlockTargets.ContainsKey(target.EntityId)) continue; // Don't scan hardlocked targets
                var key = MyTuple.Create(IntelItemType.Enemy, target.EntityId);
                var TargetIntel = intelDict.ContainsKey(key) ? (EnemyShipIntel)intelDict[key] : new EnemyShipIntel();
                TargetIntel.ID = target.EntityId;

                if (TargetIntel.LastValidatedCanonicalTime + TimeSpan.FromSeconds(0.5) < canonicalTime)
                {
                    TryScanTarget(target.Position, localTime, TargetIntel);
                }
            }

            debugBuilder.Clear();

            // WC only
            if (WCAPI != null)
            {
                //GetThreatsScratchpad.Clear();
                //WCAPI.GetSortedThreats(ProgramReference.CubeGrid, GetThreatsScratchpad);
                //
                //foreach (var enemy in GetThreatsScratchpad.Keys)
                //{
                //    if (enemy is IMyCubeGrid && !WCHardlockTargets.ContainsKey(enemy.EntityId))
                //    {
                //        var enemyGrid = (IMyCubeGrid)enemy;
                //        WCHardlockTargets.Add(enemyGrid.EntityId, enemyGrid);
                //    }
                //}

                foreach (var turret in WCTurrets)
                {
                    var turretTarget = WCAPI.GetWeaponTarget(turret);
                    if (turretTarget is IMyFunctionalBlock)
                    {
                        var targetBlock = (IMyFunctionalBlock)turretTarget;
                        if (!WCHardlockTargets.ContainsKey(targetBlock.CubeGrid.EntityId))
                            WCHardlockTargets.Add(targetBlock.CubeGrid.EntityId, targetBlock.CubeGrid);

                        if (turretTarget is IMyWarhead)
                        {
                            ((IMyWarhead)turretTarget).Detonate();
                        }
                    }
                }
            }
        }

        public void TryScanTarget(Vector3D targetPosition, TimeSpan localTime, EnemyShipIntel enemy = null)
        {
            var scanned = false;
            var offsetDist = 0d;
            var random = new Random();
            if (enemy == null) enemy = new EnemyShipIntel();
            else offsetDist = enemy.Radius * ScanScatter;
            Vector3D offset;
            int scanCount = 0;

            for (int i = 0; i < ScannerGroups.Count; i++)
            {
                offset = new Vector3D(random.NextDouble() - 0.5, random.NextDouble() - 0.5, random.NextDouble() - 0.5) * offsetDist;
                if (ScannerGroups[i] != null)
                {
                    var result = ScannerGroups[i].TryScan(IntelProvider, targetPosition + offset, enemy, localTime);
                    if (result == TryScanResults.Scanned)
                    {
                        scanned = true;
                        break;
                    }
                }
            }

            if (scanned) return;

            foreach (var camera in Cameras)
            {
                if (!camera.IsWorking) continue;
                offset = new Vector3D(random.NextDouble() - 0.5, random.NextDouble() - 0.5, random.NextDouble() - 0.5) * offsetDist;
                var result = CameraTryScan(IntelProvider, camera, targetPosition + offset, localTime, enemy);
                scanCount++;
                if (result == TryScanResults.Missed)
                {
                    break; // Try again with camera arrays
                }
                else if (result == TryScanResults.Scanned)
                {
                    scanned = true;
                    break;
                }
            }
        }

        public void AddScannerGroup(ScannerGroup group)
        {
            ScannerGroups.Add(group);
            group.Host = this;
        }

        public void RemoveScannerGroup(ScannerGroup group)
        {
            ScannerGroups.Remove(group);
            group.Host = null;
        }

        public enum TryScanResults
        {
            Scanned,
            CannotScan,
            Missed,
        }
        public TryScanResults CameraTryScan(IIntelProvider intelProvider, IMyCameraBlock camera, Vector3D targetPosition, TimeSpan localTime, EnemyShipIntel enemy)
        {
            var cameraToTarget = targetPosition - camera.WorldMatrix.Translation;
            var cameraDist = cameraToTarget.Length();
            cameraToTarget.Normalize();

            if (!camera.CanScan(cameraDist + this.ScanExtent)) return TryScanResults.CannotScan;
            var cameraFinalPosition = cameraToTarget * (cameraDist + this.ScanExtent) + camera.WorldMatrix.Translation;
            if (!camera.CanScan(targetPosition)) return TryScanResults.CannotScan;

            var info = camera.Raycast(cameraFinalPosition);
            if (info.EntityId == 0 || (enemy.ID != 0 && info.EntityId != enemy.ID)) return TryScanResults.Missed;
            enemy.FromDetectedInfo(info, localTime + intelProvider.CanonicalTimeDiff, true);
            intelProvider.ReportFleetIntelligence(enemy, localTime);
            return TryScanResults.Scanned;
        }

        void Designate(TimeSpan localTime)
        {
            if (Designator == null) return;
            var designateInfo = Designator.Raycast(10000);
            if (designateInfo.Type != MyDetectedEntityType.LargeGrid && designateInfo.Type != MyDetectedEntityType.SmallGrid) return;
            if (designateInfo.Relationship != MyRelationsBetweenPlayerAndBlock.Enemies && designateInfo.Relationship != MyRelationsBetweenPlayerAndBlock.Neutral) return;
            var intelDict = IntelProvider.GetFleetIntelligences(localTime);
            var key = MyTuple.Create(IntelItemType.Enemy, designateInfo.EntityId);
            var TargetIntel = intelDict.ContainsKey(key) ? (EnemyShipIntel)intelDict[key] : new EnemyShipIntel();
            TargetIntel.FromDetectedInfo(designateInfo, localTime + IntelProvider.CanonicalTimeDiff, true);
            IntelProvider.ReportFleetIntelligence(TargetIntel, localTime);
        }

        void UpdateDesignator(TimeSpan localTime)
        {
            int enemyCount = 0;
            var intelDict = IntelProvider.GetFleetIntelligences(localTime);
            foreach (var kvp in intelDict)
            {
                if (kvp.Key.Item1 == IntelItemType.Enemy) enemyCount++;
            }

            Designator.CustomName = DesignatorPrefix + enemyCount;
        }
    }

    public class ScannerGroup
    {
        public ScannerNetworkSubsystem Host;
        public ScannerGroup(List<IMyCameraBlock> cameras)
        {
            Cameras = cameras;
            foreach (var camera in Cameras)
            {
                camera.EnableRaycast = true;
            }
        }

        public List<IMyCameraBlock> Cameras = new List<IMyCameraBlock>();

        public ScannerNetworkSubsystem.TryScanResults TryScan(IIntelProvider intelProvider, Vector3D targetPosition, EnemyShipIntel enemy, TimeSpan localTime)
        {
            foreach (var camera in Cameras)
            {
                if (!camera.IsWorking) continue;
                var result = Host.CameraTryScan(intelProvider, camera, targetPosition, localTime, enemy);
                if (result == ScannerNetworkSubsystem.TryScanResults.Scanned) return result;
                if (result == ScannerNetworkSubsystem.TryScanResults.Missed) return ScannerNetworkSubsystem.TryScanResults.CannotScan; // This array cannot scan
            }

            return ScannerNetworkSubsystem.TryScanResults.CannotScan;
        }
    }
}
