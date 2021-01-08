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
using VRage.GameServices;
using System.Diagnostics;
using VRage.Game.VisualScripting;

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
            return Cameras.Count().ToString();
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
            if (!WCAPI.Activate(program.Me)) 
                WCAPI = null;
            GetParts();
            ParseConfigs();
        }

        public void Update(TimeSpan timestamp, UpdateFrequency updateFlags)
        {
            runs++;
            if (runs % 10 == 0)
            {
                TryScan(timestamp);
                if (Designator != null) 
                    UpdateDesignator(timestamp);
            } 
            if (WCAPI == null && runs % 120 == 0)
            {
                WCAPI = new WcPbApi();
                if (!WCAPI.Activate(Program.Me))
                    WCAPI = null;
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

        Dictionary<MyDetectedEntityInfo, float> GetThreatsScratchpad = new Dictionary<MyDetectedEntityInfo, float>();

        StringBuilder debugBuilder = new StringBuilder();

        WcPbApi WCAPI = new WcPbApi();

        double RaycastDistanceMax = 10000;
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

        void ParseConfigs()
        {
            MyIni Parser = new MyIni();
            MyIniParseResult result;
            if (!Parser.TryParse(ProgramReference.CustomData, out result))
                return;

            var val = Parser.Get("Scanner", "RaycastDistMax").ToDouble();
            if (val != 0) RaycastDistanceMax = val;
        }

        bool GetTurrets(IMyTerminalBlock block)
        {
            if (!ProgramReference.IsSameConstructAs(block)) 
                return false;
            
            if (block is IMyLargeTurretBase)
            {
                if (WCAPI != null && WCAPI.HasCoreWeapon(block)) 
                    WCTurrets.Add(block);
                else 
                    Turrets.Add((IMyLargeTurretBase)block);
            }

            return false;
        }

        bool GetCameras(IMyTerminalBlock block)
        {
            if (!(block is IMyCameraBlock)) 
                return false;
            if (!(block.CustomName.Contains(TagPrefix))) 
                return false;

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

            debugBuilder.Clear();

            // WC only...
            if (WCAPI != null)
            {
                GetThreatsScratchpad.Clear();
                WCAPI.GetSortedThreats(ProgramReference, GetThreatsScratchpad);

                foreach (var target in GetThreatsScratchpad.Keys)
                {
                    TryAddEnemyShipIntel(intelItems, localTime, canonicalTime, target, true);
                }
            }
            else
            {
                foreach (var turret in Turrets)
                {
                    if (!turret.HasTarget) 
                        continue;

                    var target = turret.GetTargetedEntity();

                    TryAddEnemyShipIntel(intelItems, localTime, canonicalTime, target);
                }
            }

            foreach (var kvp in intelItems)
            {
                if (kvp.Key.Item1 != IntelItemType.Enemy)
                    continue;
                EnemyShipIntel enemy = (EnemyShipIntel)kvp.Value;

                int priority = IntelProvider.GetPriority(kvp.Key.Item2);
                if (priority < 1)
                    continue;

                if (!EnemyShipIntel.PrioritizeTarget(enemy))
                    continue;
                if (enemy.LastValidatedCanonicalTime + TimeSpan.FromSeconds(0.2) > canonicalTime)
                    continue;
                if (enemy.LastValidatedCanonicalTime + TimeSpan.FromSeconds(0.4) > canonicalTime && priority < 4)
                    continue;

                Vector3D targetPosition = kvp.Value.GetPositionFromCanonicalTime(canonicalTime);

                TryScanTarget(targetPosition, localTime, enemy);
            }
        }
        public void TryAddEnemyShipIntel(Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence> intelDict, TimeSpan localTime, TimeSpan canonicalTime, MyDetectedEntityInfo target, bool validated = false)
        {
            if (target.IsEmpty())
                return;
            if (target.Type != MyDetectedEntityType.SmallGrid && target.Type != MyDetectedEntityType.LargeGrid && target.Type != MyDetectedEntityType.Unknown)
                return;
            if (target.Relationship != MyRelationsBetweenPlayerAndBlock.Enemies)
                return;

            var key = MyTuple.Create(IntelItemType.Enemy, target.EntityId);
            bool gotNewIntel = false;
            IFleetIntelligence TargetIntel;

            if (!intelDict.TryGetValue(key, out TargetIntel))
            {
                gotNewIntel = true;
                TargetIntel = new EnemyShipIntel();
            }
            EnemyShipIntel enemyIntel = (EnemyShipIntel)TargetIntel;

            enemyIntel.ID = target.EntityId;

            if (validated && (target.Type != MyDetectedEntityType.Unknown || !gotNewIntel))
            {
                enemyIntel.FromDetectedInfo(target, canonicalTime, true);
                IntelProvider.ReportFleetIntelligence(enemyIntel, localTime);
            }
            else if (enemyIntel.LastValidatedCanonicalTime + TimeSpan.FromSeconds(0.5) < canonicalTime)
            {
                TryScanTarget(target.Position, localTime, enemyIntel);
            }
        }
        public void LookingGlassRaycast(IMyCameraBlock camera, TimeSpan localTime)
        {
            if (camera == null)
                return;

            double dist = RaycastDistanceMax;
            if (camera.RaycastDistanceLimit >= 0)
                dist = Math.Min(camera.RaycastDistanceLimit, dist);

            // ScanExtent and 10.f are to avoid accidentally overflowing distance limitations later in processing and floating point error respectively.
            dist -= ScanExtent + 10.0;

//            debugBuilder.AppendLine("LGR D=" + dist.ToString() + " L=" + camera.RaycastDistanceLimit.ToString());
            
            var pos = camera.WorldMatrix.Forward * dist + camera.WorldMatrix.Translation;
            TryScanTarget(pos, localTime);
        }
        public void TryScanTarget(Vector3D targetPosition, TimeSpan localTime, EnemyShipIntel enemy = null)
        {
            var scanned = false;
            double offsetDist = 0.0d;
            var random = new Random();
            if (enemy == null) 
                enemy = new EnemyShipIntel();
            else 
                offsetDist = enemy.Radius * ScanScatter;

            Vector3D offset;

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

            if (scanned)
                return;

            int failures = 0;
            foreach (var camera in Cameras)
            {
                if (!camera.IsWorking) 
                    continue;

                offset = new Vector3D(random.NextDouble() - 0.5, random.NextDouble() - 0.5, random.NextDouble() - 0.5) * offsetDist;
                var result = CameraTryScan(IntelProvider, camera, targetPosition + offset, localTime, enemy);

                bool terminate = true;
                switch ( result )
                {
                    case TryScanResults.CannotScan:
                        failures++;
                        terminate = false;
                        break;
                    case TryScanResults.Missed:
//                        debugBuilder.AppendLine("CTS R=" + result.ToString());
                        break;
                    case TryScanResults.Scanned:
                        scanned = true;
//                        debugBuilder.AppendLine("CTS R=" + result.ToString());
                        break;
                }
                if (terminate)
                    break;
            }
//            debugBuilder.AppendLine("CTS F=" + failures.ToString() + " T=" + Cameras.Count);
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

            if (!camera.CanScan(cameraDist + this.ScanExtent)) 
                return TryScanResults.CannotScan;
           
            if (!camera.CanScan(targetPosition)) 
                return TryScanResults.CannotScan;

            cameraToTarget.Normalize();
            var cameraFinalPosition = cameraToTarget * (cameraDist + this.ScanExtent) + camera.WorldMatrix.Translation;
            var info = camera.Raycast(cameraFinalPosition);
            if (info.EntityId == 0 || (enemy.ID != 0 && info.EntityId != enemy.ID)) 
                return TryScanResults.Missed;

            enemy.FromDetectedInfo(info, localTime + intelProvider.CanonicalTimeDiff, true);
            intelProvider.ReportFleetIntelligence(enemy, localTime);
            return TryScanResults.Scanned;
        }

        void Designate(TimeSpan localTime)
        {
            if (Designator == null) return;
            double distance = RaycastDistanceMax;
            if (Designator.RaycastDistanceLimit >= 0)
            {
                distance = Math.Min(distance, Designator.RaycastDistanceLimit);
            }

            var designateInfo = Designator.Raycast(distance);
           
            if (designateInfo.Type != MyDetectedEntityType.LargeGrid && designateInfo.Type != MyDetectedEntityType.SmallGrid) 
                return;

            if (designateInfo.Relationship != MyRelationsBetweenPlayerAndBlock.Enemies && designateInfo.Relationship != MyRelationsBetweenPlayerAndBlock.Neutral) 
                return;

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
