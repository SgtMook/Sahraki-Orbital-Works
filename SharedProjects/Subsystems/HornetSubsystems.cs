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
    // Configuration: Forward cannons and turrets
    // Condor compatible
    public class HornetCombatSubsystem : ISubsystem, IOwnIntelMutator
    {
        WcPbApi WCAPI = new WcPbApi();
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

        IMyTerminalBlock ProgramReference;
        public void Setup(MyGridProgram program, string name, IMyTerminalBlock programReference = null)
        {
            ProgramReference = programReference;
            if (ProgramReference == null) ProgramReference = program.Me;
            Program = program;
            if (!WCAPI.Activate(program.Me)) WCAPI = null;
            IntelProvider.AddIntelMutator(this);
            GetParts();
            ParseConfigs();
        }

        public void Update(TimeSpan timestamp, UpdateFrequency updateFlags)
        {
            TargetIntel = null;
            var canonicalTime = timestamp + IntelProvider.CanonicalTimeDiff;
            foreach (var turret in Turrets)
            {
                if (!turret.HasTarget) continue;
                var target = turret.GetTargetedEntity();
                if (target.IsEmpty()) continue;
                if (target.Type != MyDetectedEntityType.SmallGrid && target.Type != MyDetectedEntityType.LargeGrid) continue;
                if (target.Relationship != MyRelationsBetweenPlayerAndBlock.Enemies) continue;
            
                var intelDict = IntelProvider.GetFleetIntelligences(timestamp);
                var key = MyTuple.Create(IntelItemType.Enemy, target.EntityId);

                if (intelDict.ContainsKey(key))
                {
                    turret.ResetTargetingToDefault();
                    continue;
                }

                TargetIntel = intelDict.ContainsKey(key) ? (EnemyShipIntel)intelDict[key] : new EnemyShipIntel();
            
                if (TargetIntel.LastValidatedCanonicalTime + TimeSpan.FromSeconds(0.1) < canonicalTime)
                {
                    foreach (var camera in Scanners)
                    {
                        if (camera.CanScan(target.Position))
                        {
                            var validatedTarget = camera.Raycast(target.Position);
                            if (validatedTarget.EntityId != target.EntityId) break;
                            TargetIntel.FromDetectedInfo(validatedTarget, timestamp + IntelProvider.CanonicalTimeDiff, true);
                            IntelProvider.ReportFleetIntelligence(TargetIntel, timestamp);
                            break;
                        }
                    }
                }
            }

            if (fireCounter > 0) fireCounter--;
            if (fireCounter == 0) HoldFire();

            if (engageCounter > 0) engageCounter--;
        }
        #endregion
        MyGridProgram Program;

        List<IMySmallGatlingGun> Guns = new List<IMySmallGatlingGun>();
        List<IMySmallMissileLauncher> Launchers = new List<IMySmallMissileLauncher>();
        List<IMyLargeTurretBase> Turrets = new List<IMyLargeTurretBase>();
        List<IMyCameraBlock> Scanners = new List<IMyCameraBlock>();
        IMyRadioAntenna Antenna;

        StringBuilder updateBuilder = new StringBuilder();

        IIntelProvider IntelProvider;

        public EnemyShipIntel TargetIntel;

        int fireCounter;

        int engageCounter;

        public int FireDist = 800;
        public int EngageDist = 500;
        public int AlertDist = 1500;

        public int ProjectileSpeed = 400;

        public float EngageTheta = 0.1f;

        public float FireTolerance = 0.2f;

        public HornetCombatSubsystem(IIntelProvider provider)
        {
            IntelProvider = provider;
        }

        void GetParts()
        {
            Guns.Clear();
            Turrets.Clear();
            Scanners.Clear();
            Launchers.Clear();
            Antenna = null;
            Program.GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(null, CollectParts);
        }

        bool CollectParts(IMyTerminalBlock block)
        {
            if (block is IMySmallGatlingGun && block.IsSameConstructAs(ProgramReference))
                Guns.Add((IMySmallGatlingGun)block);

            if (ProgramReference.CubeGrid.EntityId != block.CubeGrid.EntityId) return false;

            if (block is IMyRadioAntenna)
                Antenna = (IMyRadioAntenna)block;

            if (block is IMySmallMissileLauncher)
                Launchers.Add((IMySmallMissileLauncher)block);

            if (block is IMyLargeTurretBase)
            {
                IMyLargeTurretBase turret = (IMyLargeTurretBase)block;
                Turrets.Add(turret);
                turret.EnableIdleRotation = false;
                turret.SyncEnableIdleRotation();
            }

            if (block is IMyCameraBlock)
            {
                IMyCameraBlock camera = (IMyCameraBlock)block;
                Scanners.Add(camera);
                camera.EnableRaycast = true;
            }

            return false;
        }

        // [Hornet]
        // FireDist = 800
        // EngageDist = 500
        // AlertDist = 1500
        // ProjectileSpeed = 400
        // EngageTheta = 0.1
        // FireTolerance = 0.2
        void ParseConfigs()
        {
            MyIni Parser = new MyIni();
            MyIniParseResult result;
            if (!Parser.TryParse(ProgramReference.CustomData, out result))
                return;

            var val = Parser.Get("Hornet", "FireDist").ToInt16();
            if (val != 0) FireDist = val;
            val = Parser.Get("Hornet", "EngageDist").ToInt16();
            if (val != 0) EngageDist = val;
            val = Parser.Get("Hornet", "AlertDist").ToInt16();
            if (val != 0) AlertDist = val;

            val = Parser.Get("Hornet", "ProjectileSpeed").ToInt16();
            if (val != 0) ProjectileSpeed = val;

            var flo = Parser.Get("Hornet", "EngageTheta").ToDecimal();
            if (flo != 0) EngageTheta = (float)flo;
            
            flo = Parser.Get("Hornet", "FireTolerance").ToDecimal();
            if (flo != 0) FireTolerance = (float)flo;
        }

        #region Public accessors
        public void Fire()
        {
            if (fireCounter == -1)
            {
                foreach (var gun in Guns)
                {
                    gun.Enabled = true;
                    TerminalPropertiesHelper.SetValue(gun, "Shoot", true);
                    if (WCAPI != null) WCAPI.ToggleWeaponFire(gun, true, true);
                }
                foreach (var launcher in Launchers)
                {
                    launcher.Enabled = true;
                    TerminalPropertiesHelper.SetValue(launcher, "Shoot", true);
                    if (WCAPI != null) WCAPI.ToggleWeaponFire(launcher, true, true);
                }
            }
            fireCounter = 3;
        }

        public void HoldFire()
        {
            foreach (var gun in Guns)
            {
                TerminalPropertiesHelper.SetValue(gun, "Shoot", false);
                if (WCAPI != null) WCAPI.ToggleWeaponFire(gun, false, true);
            }
            foreach (var launcher in Launchers)
            {
                TerminalPropertiesHelper.SetValue(launcher, "Shoot", false);
                if (WCAPI != null) WCAPI.ToggleWeaponFire(launcher, false, true);
            }
            fireCounter = -1;
        }

        public void MarkEngaged()
        {
            engageCounter = 6;
        }
        #endregion
        public const int kEngageRange = 500;

        #region IOwnIntelMutator
        public void ProcessIntel(FriendlyShipIntel intel)
        {
            if (engageCounter > 0)
            {
                intel.Radius = (float)ProgramReference.CubeGrid.WorldAABB.Size.Length() * 10;
                intel.AgentStatus |= AgentStatus.Engaged;
            }
        }

        #endregion
    }
}
