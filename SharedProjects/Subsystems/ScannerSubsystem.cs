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
        public UpdateFrequency UpdateFrequency => UpdateFrequency.Update10;

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
            GetParts();
        }

        public void Update(TimeSpan timestamp, UpdateFrequency updateFlags)
        {
            TryScan(timestamp);
            if (Designator != null) UpdateDesignator(timestamp);
        }

        #endregion
        MyGridProgram Program;
        IIntelProvider IntelProvider;
        List<EnemyShipIntel> EnemyIntelScratchpad = new List<EnemyShipIntel>();
        string TagPrefix = string.Empty;
        string DesignatorPrefix = string.Empty;

        List<ScannerArray> ScannerArrays = new List<ScannerArray>();
        List<IMyCameraBlock> Cameras = new List<IMyCameraBlock>();
        IMyCameraBlock Designator;

        List<IMyLargeTurretBase> Turrets = new List<IMyLargeTurretBase>();

        StringBuilder debugBuilder = new StringBuilder();

        public ScannerNetworkSubsystem(IIntelProvider intelProvider, string tag = "SE")
        {
            IntelProvider = intelProvider;
            if (tag != string.Empty)
            {
                TagPrefix = $"[{tag}";
                DesignatorPrefix = $"[{tag}] <D> T:";
            }
        }

        void GetParts()
        {
            ScannerArrays.Clear();
            Cameras.Clear();
            Designator = null;
            Program.GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(null, GetTurrets);
            Program.GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(null, GetBases);
            Program.GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(null, GetArms);
            Program.GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(null, GetCameras);

            debugBuilder.AppendLine($"CAMERAS: {Cameras.Count}");
            debugBuilder.AppendLine($"ARRAYS: {ScannerArrays.Count}");
        }

        bool GetTurrets(IMyTerminalBlock block)
        {
            if (!ProgramReference.IsSameConstructAs(block)) return false;
            if (block is IMyLargeTurretBase) Turrets.Add((IMyLargeTurretBase)block);
            return false;
        }

        bool GetBases(IMyTerminalBlock block)
        {
            if (ProgramReference.CubeGrid.EntityId != block.CubeGrid.EntityId) return false;
            if (!(block is IMyMotorStator)) return false;
            if (!block.CustomName.StartsWith(TagPrefix)) return false;
            if (!block.CustomName.Contains("Base")) return false;

            ScannerArray scanner = new ScannerArray(this);
            scanner.AddPart(block);
            ScannerArrays.Add(scanner);
            return false;
        }

        bool GetArms(IMyTerminalBlock block)
        {
            if (!(block is IMyMotorStator)) return false;
            foreach (var array in ScannerArrays)
            {
                if (block.CubeGrid.EntityId == array.Base.TopGrid.EntityId)
                {
                    array.AddPart(block);
                    break;
                }
            }

            return false;
        }

        bool GetCameras(IMyTerminalBlock block)
        {
            if (!(block is IMyCameraBlock)) return false;
            if (!block.CustomName.StartsWith(TagPrefix)) return false;

            var camera = (IMyCameraBlock)block;

            if (camera.CustomName.Contains("<D>"))
            {
                Designator = camera;
                Designator.EnableRaycast = true;
                return false;
            }

            foreach (var array in ScannerArrays)
            {
                if (block.CubeGrid.EntityId == array.Base.TopGrid.EntityId || block.CubeGrid.EntityId == array.Arm.TopGrid.EntityId)
                {
                    array.AddPart(block);
                    return false;
                }
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
                EnemyShipIntel enemy = (EnemyShipIntel)kvp.Value;

                int priority = IntelProvider.GetPriority(kvp.Key.Item2);
                if (priority < 1) continue;

                if (!EnemyShipIntel.PrioritizeTarget(enemy)) continue;
                if (enemy.LastValidatedCanonicalTime + TimeSpan.FromSeconds(0.1) > canonicalTime) continue;
                if (enemy.LastValidatedCanonicalTime + TimeSpan.FromSeconds(0.2) > canonicalTime && priority < 4) continue;

                Vector3D targetPosition = kvp.Value.GetPositionFromCanonicalTime(canonicalTime);

                TryScanTarget(targetPosition, localTime, enemy);

            }

            foreach (var turret in Turrets)
            {
                if (!turret.HasTarget) continue;
                var target = turret.GetTargetedEntity();
                if (target.IsEmpty()) continue;
                if (target.Type != MyDetectedEntityType.SmallGrid && target.Type != MyDetectedEntityType.LargeGrid) continue;
                if (target.Relationship != MyRelationsBetweenPlayerAndBlock.Enemies) continue;

                var intelDict = IntelProvider.GetFleetIntelligences(localTime);
                var key = MyTuple.Create(IntelItemType.Enemy, target.EntityId);
                var TargetIntel = intelDict.ContainsKey(key) ? (EnemyShipIntel)intelDict[key] : new EnemyShipIntel();
                TargetIntel.ID = target.EntityId;

                if (TargetIntel.LastValidatedCanonicalTime + TimeSpan.FromSeconds(0.5) < canonicalTime)
                {
                    TryScanTarget(target.Position, localTime, TargetIntel);
                }
            }

        }

        public void TryScanTarget(Vector3D targetPosition, TimeSpan localTime, EnemyShipIntel enemy = null)
        {
            var scanned = false;
            var offsetDist = 0d;
            var random = new Random();
            if (enemy == null) enemy = new EnemyShipIntel();
            else offsetDist = enemy.Radius * 0.25;
            Vector3D offset;
            int scanCount = 0;

            foreach (var camera in Cameras)
            {
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

            if (scanned) return;

            for (int i = 0; i < ScannerArrays.Count; i++)
            {
                offset = new Vector3D(random.NextDouble() - 0.5, random.NextDouble() - 0.5, random.NextDouble() - 0.5) * offsetDist;
                if (ScannerArrays[i] != null && ScannerArrays[i].IsOK())
                {
                    var result = ScannerArrays[i].TryScan(IntelProvider, ProgramReference.WorldMatrix.Translation, targetPosition + offset, enemy, localTime);
                    if (result == TryScanResults.Scanned)
                    {
                        break;
                    }
                }
            }
        }

        public enum TryScanResults
        {
            Scanned,
            CannotScan,
            Missed,
        }
        public static TryScanResults CameraTryScan(IIntelProvider intelProvider, IMyCameraBlock camera, Vector3D targetPosition, TimeSpan localTime, EnemyShipIntel enemy)
        {
            var cameraToTarget = targetPosition - camera.WorldMatrix.Translation;
            var cameraDist = cameraToTarget.Length();
            cameraToTarget.Normalize();

            if (!camera.CanScan(cameraDist + 30)) return TryScanResults.CannotScan;
            var cameraFinalPosition = cameraToTarget * (cameraDist + 30) + camera.WorldMatrix.Translation;
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

    public class ScannerArray
    {
        List<IMyCameraBlock> Cameras = new List<IMyCameraBlock>();
        public IMyMotorStator Base;
        public IMyMotorStator Arm;
        string tagPrefix = string.Empty;

        ScannerNetworkSubsystem Host;

        StringBuilder debugBuilder = new StringBuilder();

        public ScannerArray(ScannerNetworkSubsystem host)
        {
            Host = host;
        }

        public bool IsOK()
        {
            return Base != null && Cameras.Count > 0;
        }

        public ScannerNetworkSubsystem.TryScanResults TryScan(IIntelProvider intelProvider, Vector3D myPosition, Vector3D targetPosition, EnemyShipIntel enemy, TimeSpan localTime)
        {
            var toTarget = targetPosition - myPosition;
            var dist = toTarget.Length();
            toTarget.Normalize();
            if (VectorHelpers.VectorAngleBetween(toTarget, Base.WorldMatrix.Up) > 0.55 * Math.PI) return ScannerNetworkSubsystem.TryScanResults.CannotScan;

            foreach (var camera in Cameras)
            {
                var result = ScannerNetworkSubsystem.CameraTryScan(intelProvider, camera, targetPosition, localTime, enemy);
                if (result == ScannerNetworkSubsystem.TryScanResults.Scanned) return result;
                if (result == ScannerNetworkSubsystem.TryScanResults.Missed) return ScannerNetworkSubsystem.TryScanResults.CannotScan; // This array cannot scan
            }

            return ScannerNetworkSubsystem.TryScanResults.CannotScan;
        }

        public void AddPart(IMyTerminalBlock block)
        {
            if (block is IMyCameraBlock)
            {
                IMyCameraBlock camera = (IMyCameraBlock)block;
                Cameras.Add(camera);
                camera.EnableRaycast = true;
            }
            if (block is IMyMotorStator)
            {
                if (block.CustomName.Contains("Base")) Base = (IMyMotorStator)block;
                else Arm = (IMyMotorStator)block;
            }
        }
    }
}
