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

        public void CommandV2(TimeSpan timestamp, CommandLine command)
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
            HoldFire();
        }

        public void Update(TimeSpan timestamp, UpdateFrequency updateFlags)
        {
            runs++;

            if (WCAPI == null && runs % 12 == 0)
            {
                WCAPI = new WcPbApi();
                if (WCAPI.Activate(Context.Program.Me))
                    GetParts();
                else
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

        RoundRobin<IMyFunctionalBlock> WCGuns = new RoundRobin<IMyFunctionalBlock>();
        RoundRobin<IMyUserControllableGun> Guns = new RoundRobin<IMyUserControllableGun>();
        List<IMyLargeTurretBase> Turrets = new List<IMyLargeTurretBase>();

        StringBuilder updateBuilder = new StringBuilder();

        IIntelProvider IntelProvider;

        public EnemyShipIntel TargetIntel;

        int fireCounter;
        TimeSpan NextSalvoFire;

        int engageCounter;

        public int FireDist = 800;
        public int FireSalvoMS = 0;
        public int EngageDist = 500;
        public int AlertDist = 1500;

        public int ProjectileSpeed = 400;

        public double EngageTheta = 0.1;
        public double FireTolerance = 0.2;
        public double OwnSpeedMultiplier = 1;

        int runs = 0;

        bool UseGuns;

        public HornetCombatSubsystem(IIntelProvider provider, bool useGuns = true)
        {
            IntelProvider = provider;
            UseGuns = useGuns;
        }

        void GetParts()
        {
            Guns.Items.Clear();
            WCGuns.Items.Clear();
            Turrets.Clear();
            Context.Terminal.GetBlocksOfType<IMyTerminalBlock>(null, CollectParts);
        }

        bool CollectParts(IMyTerminalBlock block)
        {
            if (!UseGuns || 
                Context.Reference.CubeGrid.EntityId != block.CubeGrid.EntityId ||
                block.CustomName.Contains("[X]"))
                return false;

            bool isWCGun = WCAPI != null && WCAPI.HasCoreWeapon(block);
            var turret = block as IMyLargeTurretBase;
            var gun = block as IMyUserControllableGun;

            if (isWCGun)
                WCGuns.Items.Add(block as IMyFunctionalBlock);
            else if (turret != null)
            {
                Turrets.Add(turret);
                turret.EnableIdleRotation = false;
                turret.SyncEnableIdleRotation();
            }
            else if (gun != null)
                Guns.Items.Add(gun);

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
            var hornetSection = "Hornet";
            MyIni Parser = new MyIni();
            MyIniParseResult result;
            if (!Parser.TryParse(Context.Reference.CustomData, out result))
                return;

            FireDist = Parser.Get(hornetSection, "FireDist").ToInt32(FireDist);
            FireSalvoMS = Parser.Get(hornetSection, "FireSalvoMS").ToInt32(FireSalvoMS);
            EngageDist = Parser.Get(hornetSection, "EngageDist").ToInt32(EngageDist);
            AlertDist = Parser.Get(hornetSection, "AlertDist").ToInt32(AlertDist);
            ProjectileSpeed = Parser.Get(hornetSection, "ProjectileSpeed").ToInt32(ProjectileSpeed);

            EngageTheta = Parser.Get(hornetSection, "EngageTheta").ToDouble(EngageTheta);
            FireTolerance = Parser.Get(hornetSection, "FireTolerance").ToDouble(FireTolerance);
            OwnSpeedMultiplier = Parser.Get(hornetSection, "OwnSpeedMultiplier").ToDouble(OwnSpeedMultiplier);
        }

        #region Public accessors
        public void Fire()
        {
            if (fireCounter == -1)
            {
                if (FireSalvoMS == 0)
                {
                    Guns.Items.ForEach(gun => { gun.Enabled = true; TerminalPropertiesHelper.SetValue(gun, "Shoot", true); });
                    if ( WCAPI != null )
                        WCGuns.Items.ForEach(gun => { gun.Enabled = true; WCAPI.ToggleWeaponFire(gun, true, true);});
                }
                else
                {
                    NextSalvoFire = Context.CurrentTime;
                }
            }
            fireCounter = 3;

            if (fireCounter > 0 && 
                Context.CurrentTime >= NextSalvoFire)
            {
                NextSalvoFire += ScriptTime.FromMilliseconds(FireSalvoMS);
                var gun = Guns.GetAndAdvance();
                if (gun != null)
                {
                    gun.Enabled = true;
                    TerminalPropertiesHelper.SetValue(gun, "Shoot", true);
                }
                if (WCAPI != null)
                {
                    var wcGun = WCGuns.GetAndAdvance();
                    if (wcGun != null)
                    {
                        gun.Enabled = true;
                        WCAPI.ToggleWeaponFire(gun, true, true);
                    }
                } 
            }
        }

        public void HoldFire()
        {
            Guns.Items.ForEach(gun => TerminalPropertiesHelper.SetValue(gun, "Shoot", false));
            if (WCAPI != null)
                WCGuns.Items.ForEach(gun => WCAPI.ToggleWeaponFire(gun, false, true));
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
