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
        }

        public void DeserializeSubsystem(string serialized)
        {
        }

        public string GetStatus()
        {
            return string.Empty;
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
            TryScan(timestamp);
        }

        #endregion
        MyGridProgram Program;
        IIntelProvider IntelProvider;
        List<EnemyShipIntel> EnemyIntelScratchpad = new List<EnemyShipIntel>();
        string TagPrefix = string.Empty;

        ScannerArray[] ScannerArrays = new ScannerArray[9];
        List<IMyCameraBlock> Cameras = new List<IMyCameraBlock>();

        StringBuilder debugBuilder = new StringBuilder();

        public ScannerNetworkSubsystem(IIntelProvider intelProvider, string tag = "S")
        {
            IntelProvider = intelProvider;
            if (tag != string.Empty) TagPrefix = $"[{tag}";
        }

        void GetParts()
        {
            for (int i = 0; i < ScannerArrays.Length; i++)
                ScannerArrays[i] = null;
            Cameras.Clear();
            Program.GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(null, CollectParts);
        }

        private bool CollectParts(IMyTerminalBlock block)
        {
            if (!Program.Me.IsSameConstructAs(block)) return false;
            if (TagPrefix != string.Empty && !block.CustomName.StartsWith(TagPrefix)) return false;

            var indexTagEnd = block.CustomName.IndexOf(']');
            if (indexTagEnd == -1) return false;

            var numString = block.CustomName.Substring(TagPrefix.Length, indexTagEnd - TagPrefix.Length);

            if (numString == string.Empty)
            {
                // No number - master systems
                if (block is IMyCameraBlock) Cameras.Add((IMyCameraBlock)block);
            }

            int arrayIndex;
            if (!int.TryParse(numString, out arrayIndex)) return false;
            if (ScannerArrays[arrayIndex] == null) ScannerArrays[arrayIndex] = new ScannerArray(arrayIndex, this);
            ScannerArrays[arrayIndex].AddPart(block);

            return false;
        }

        private void TryScan(TimeSpan localTime)
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

                var scanned = false;

                foreach (var camera in Cameras)
                {
                    var result = CameraTryScan(IntelProvider, camera, targetPosition, localTime, enemy);
                    if (result == TryScanResults.Obstructed) break; // Try again with camera arrays
                    else if (result == TryScanResults.Scanned)
                    {
                        scanned = true;
                        break;
                    }
                }

                if (scanned) continue;

                for (int i = 0; i < ScannerArrays.Length; i++)
                {
                    if (ScannerArrays[i] != null && ScannerArrays[i].IsOK())
                    {
                        var result = ScannerArrays[i].TryScan(IntelProvider, Program.Me.WorldMatrix.Translation, targetPosition, enemy, localTime);
                        if (result == TryScanResults.Scanned)
                        {
                            break;
                        }
                    }
                }
            }
        }

        public enum TryScanResults
        {
            Scanned,
            CannotScan,
            Obstructed
        }
        public static TryScanResults CameraTryScan(IIntelProvider intelProvider, IMyCameraBlock camera, Vector3D targetPosition, TimeSpan localTime, EnemyShipIntel enemy)
        {
            var cameraToTarget = targetPosition - camera.WorldMatrix.Translation;
            var cameraDist = cameraToTarget.Length();
            cameraToTarget.Normalize();

            if (!camera.CanScan(cameraDist + 30)) return TryScanResults.CannotScan;
            var cameraFinalPosition = cameraToTarget * (cameraDist + 30) + camera.WorldMatrix.Translation;
            if (!camera.CanScan(targetPosition)) return TryScanResults.CannotScan;

            var info = camera.Raycast(targetPosition);
            if (info.EntityId != enemy.ID) return TryScanResults.Obstructed;
            enemy.FromDetectedInfo(info, localTime + intelProvider.CanonicalTimeDiff, true);
            intelProvider.ReportFleetIntelligence(enemy, localTime);
            return TryScanResults.Scanned;
        }
    }

    public class ScannerArray
    {
        List<IMyCameraBlock> Cameras = new List<IMyCameraBlock>();
        IMyMotorStator BaseRotor;
        string tagPrefix = string.Empty;

        ScannerNetworkSubsystem Host;
        int Index;

        StringBuilder debugBuilder = new StringBuilder();

        public ScannerArray(int index, ScannerNetworkSubsystem host)
        {
            Index = index;
            Host = host;
        }

        public bool IsOK()
        {
            return BaseRotor != null && Cameras.Count > 0;
        }

        public ScannerNetworkSubsystem.TryScanResults TryScan(IIntelProvider intelProvider, Vector3D myPosition, Vector3D targetPosition, EnemyShipIntel enemy, TimeSpan localTime)
        {
            var toTarget = targetPosition - myPosition;
            var dist = toTarget.Length();
            toTarget.Normalize();
            if (VectorHelpers.VectorAngleBetween(toTarget, BaseRotor.WorldMatrix.Up) > 0.55 * Math.PI) return ScannerNetworkSubsystem.TryScanResults.CannotScan;

            foreach (var camera in Cameras)
            {
                var result = ScannerNetworkSubsystem.CameraTryScan(intelProvider, camera, targetPosition, localTime, enemy);
                if (result == ScannerNetworkSubsystem.TryScanResults.Scanned) return result;
                if (result == ScannerNetworkSubsystem.TryScanResults.Obstructed) return ScannerNetworkSubsystem.TryScanResults.CannotScan; // This array cannot scan
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
            if (block is IMyMotorStator && block.CustomName.Contains("Base"))
                BaseRotor = (IMyMotorStator)block;
        }
    }
}
