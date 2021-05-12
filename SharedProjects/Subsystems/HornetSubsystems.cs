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
            return AlertDist.ToString();
        }

        public string SerializeSubsystem()
        {
            return string.Empty;
        }

        public void Setup(ExecutionContext context, string name)
        {
            Context = context;

            if (!WCAPI.Activate(Context.Program.Me)) 
                WCAPI = null;

            IntelProvider.AddIntelMutator(this);
            GetParts();
            ParseConfigs();
        }

        public void Update(TimeSpan timestamp, UpdateFrequency updateFlags)
        {
            runs++;
            if (WCAPI == null && runs % 12 == 0)
            {
                WCAPI = new WcPbApi();
                if (!WCAPI.Activate(Context.Program.Me))
                    WCAPI = null;
            }


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
            }

            if (fireCounter > 0) fireCounter--;
            if (fireCounter == 0) HoldFire();

            if (engageCounter > 0) engageCounter--;
        }
        #endregion
        ExecutionContext Context;

        List<IMyUserControllableGun> Guns = new List<IMyUserControllableGun>();
        List<IMyLargeTurretBase> Turrets = new List<IMyLargeTurretBase>();

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

        public float OwnSpeedMultiplier = 1f;

        int runs = 0;

        bool UseGuns;

        public HornetCombatSubsystem(IIntelProvider provider, bool useGuns = true)
        {
            IntelProvider = provider;
            UseGuns = useGuns;
        }

        void GetParts()
        {
            Guns.Clear();
            Turrets.Clear();
            Context.Terminal.GetBlocksOfType<IMyTerminalBlock>(null, CollectParts);
        }

        bool CollectParts(IMyTerminalBlock block)
        {
            if (Context.Reference.CubeGrid.EntityId != block.CubeGrid.EntityId) 
                return false;

            if (block is IMyLargeTurretBase)
            {
                IMyLargeTurretBase turret = (IMyLargeTurretBase)block;
                Turrets.Add(turret);
                turret.EnableIdleRotation = false;
                turret.SyncEnableIdleRotation();
            }
            else if (block is IMyUserControllableGun && UseGuns)
                Guns.Add((IMyUserControllableGun)block);

            return false;
        }

        // [Hornet]
        // FireDist = 800
        // EngageDist = 500
        // AlertDist = 1500
        // ProjectileSpeed = 400
        // EngageTheta = 0.1
        // FireTolerance = 0.2
        // OwnSpeedMultiplier = 1
        void ParseConfigs()
        {
            MyIni Parser = new MyIni();
            MyIniParseResult result;
            if (!Parser.TryParse(Context.Reference.CustomData, out result))
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

            OwnSpeedMultiplier = (float)Parser.Get("Hornet", "OwnSpeedMultiplier").ToDecimal(1);
        }

        #region Public accessors
        public void Fire()
        {
            if (fireCounter == -1)
            {
                foreach (var gun in Guns)
                {
                    gun.Enabled = true;
                    if (WCAPI != null && WCAPI.HasCoreWeapon(gun))
                        WCAPI.ToggleWeaponFire(gun, true, true);
                    else
                        TerminalPropertiesHelper.SetValue(gun, "Shoot", true);
                }
            }
            fireCounter = 3;
        }

        public void HoldFire()
        {
            foreach (var gun in Guns)
            {
                if (WCAPI != null && WCAPI.HasCoreWeapon(gun))
                    WCAPI.ToggleWeaponFire(gun, false, true);
                else
                    TerminalPropertiesHelper.SetValue(gun, "Shoot", false);
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
                intel.Radius = (float)Context.Reference.CubeGrid.WorldAABB.Size.Length() * 10;
                intel.AgentStatus |= AgentStatus.Engaged;
            }
        }

        #endregion
    }
}
