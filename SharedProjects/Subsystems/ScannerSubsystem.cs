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
    public class ScannerSubsystem : ISubsystem
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
        List<IMyCameraBlock> Scanners = new List<IMyCameraBlock>();
        List<EnemyShipIntel> EnemyIntelScratchpad = new List<EnemyShipIntel>();
        IMyMotorStator BaseRotor;
        IIntelProvider IntelProvider;
        string tagPrefix = string.Empty;

        public ScannerSubsystem(IIntelProvider intelProvider, string tag = "")
        {
            IntelProvider = intelProvider;
            if (tag != string.Empty) tagPrefix = $"[{tag}]";
        }

        private void TryScan(TimeSpan localTime)
        {
            EnemyIntelScratchpad.Clear();
            var intelItems = IntelProvider.GetFleetIntelligences(localTime);
            var canonicalTime = localTime + IntelProvider.CanonicalTimeDiff;
            foreach (var kvp in intelItems)
            {
                if (kvp.Key.Item1 != IntelItemType.Enemy) continue;
                EnemyShipIntel enemy = (EnemyShipIntel)kvp.Value;

                if (enemy.LastValidatedCanonicalTime + TimeSpan.FromSeconds(0.5) > canonicalTime) continue;
                if (!EnemyShipIntel.PrioritizeTarget(enemy)) continue;

                Vector3D targetPosition = kvp.Value.GetPositionFromCanonicalTime(canonicalTime);
                var toTarget = targetPosition - Program.Me.WorldMatrix.Translation;
                if (BaseRotor != null && toTarget.Dot(BaseRotor.WorldMatrix.Up) < 0) continue;

                foreach (var camera in Scanners)
                {
                    if (!camera.CanScan(toTarget.Length())) continue;
                    if (!camera.CanScan(targetPosition)) continue;
                    var info = camera.Raycast(targetPosition);
                    if (info.EntityId != kvp.Value.ID) continue;
                    enemy.FromDetectedInfo(info, canonicalTime, true);
                    IntelProvider.ReportFleetIntelligence(enemy, localTime);
                    break;
                }
            }
        }

        void GetParts()
        {
            Scanners.Clear();
            BaseRotor = null;
            Program.GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(null, CollectParts);
        }

        private bool CollectParts(IMyTerminalBlock block)
        {
            if (!Program.Me.IsSameConstructAs(block)) return false;
            if (tagPrefix != string.Empty && !block.CustomName.StartsWith(tagPrefix)) return false;

            if (block is IMyCameraBlock)
            {
                IMyCameraBlock camera = (IMyCameraBlock)block;
                Scanners.Add(camera);
                camera.EnableRaycast = true;
            }
            if (block is IMyMotorStator)
                BaseRotor = (IMyMotorStator)block;


            return false;
        }
    }
}
