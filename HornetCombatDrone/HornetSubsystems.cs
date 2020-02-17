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
    // Configuration: 4 forward cannons, 2 turrets
    public class HornetCombatSubsystem : ISubsystem
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
            TargetIntel = null;

            foreach (var turret in Turrets)
            {
                if (!turret.HasTarget) continue;
                var target = turret.GetTargetedEntity();
                if (target.IsEmpty()) continue;
                if (target.Type != MyDetectedEntityType.SmallGrid && target.Type != MyDetectedEntityType.LargeGrid) continue;
                if (target.Relationship != MyRelationsBetweenPlayerAndBlock.Enemies) continue;

                var intelDict = IntelProvider.GetFleetIntelligences(timestamp);
                var key = MyTuple.Create(IntelItemType.Enemy, target.EntityId);
                TargetIntel = intelDict.ContainsKey(key) ? (EnemyShipIntel)intelDict[key] : new EnemyShipIntel();
                TargetIntel.FromDetectedInfo(target, timestamp + IntelProvider.CanonicalTimeDiff);
                IntelProvider.ReportFleetIntelligence(TargetIntel, timestamp);

                break;
            }

            if (fireCounter > 0) fireCounter--;
            if (fireCounter == 0) HoldFire();
        }
        #endregion
        MyGridProgram Program;

        List<IMySmallGatlingGun> Guns = new List<IMySmallGatlingGun>();
        List<IMyLargeGatlingTurret> Turrets = new List<IMyLargeGatlingTurret>();
        IMyRadioAntenna Antenna;

        StringBuilder updateBuilder = new StringBuilder();

        IIntelProvider IntelProvider;

        public EnemyShipIntel TargetIntel;

        int fireCounter;

        public HornetCombatSubsystem(IIntelProvider provider)
        {
            IntelProvider = provider;
        }

        void GetParts()
        {
            Guns.Clear();
            Turrets.Clear();
            Antenna = null;
            Program.GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(null, CollectParts);
        }

        private bool CollectParts(IMyTerminalBlock block)
        {
            if (!Program.Me.IsSameConstructAs(block)) return false;

            if (block is IMyRadioAntenna)
                Antenna = (IMyRadioAntenna)block;

            if (block is IMySmallGatlingGun)
                Guns.Add((IMySmallGatlingGun)block);

            if (block is IMyLargeGatlingTurret)
            {
                IMyLargeGatlingTurret turret = (IMyLargeGatlingTurret)block;
                Turrets.Add(turret);
                turret.EnableIdleRotation = false;
                turret.SyncEnableIdleRotation();
            }

            return false;
        }

        #region Public accessors
        public void Fire()
        {
            if (fireCounter == -1)
            {
                foreach (var gun in Guns)
                {
                    TerminalPropertiesHelper.SetValue(gun, "Shoot", true);
                }
            }
            fireCounter = 6;
        }

        public void HoldFire()
        {
            foreach (var gun in Guns)
            {
                TerminalPropertiesHelper.SetValue(gun, "Shoot", false);
            }
            fireCounter = -1;
        }

        public const int kEngageRange = 500;
        #endregion
    }
}
