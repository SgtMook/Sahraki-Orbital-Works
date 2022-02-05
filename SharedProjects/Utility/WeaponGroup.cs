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
    /*
    class WeaponGroup
    {
        public int Range = 800;
        public int ProjectileSpeed = 400;
        public int ChargeUpMS = 0;
        public int ReloadMS = 200;
        public double OwnSpeedMod = 1; // tweaking this value helps set up projectiles delay

        RoundRobin<IMyFunctionalBlock> WCGuns = new RoundRobin<IMyFunctionalBlock>();
        RoundRobin<IMyUserControllableGun> Guns = new RoundRobin<IMyUserControllableGun>();

        void BuildWeaponGroups(MyIni parser)
        {
            string section = "WeaponGroup";
            for (int i = 0; i < 6; i++ )
            {
                if (parser.ContainsSection(section + "_" + i))
                {

                }
            }

            void ParseTubeGroupConfig(MyIni parser, string groupName, TorpedoTubeGroup group)
        {
            string section = "WeaponGroup";
            if (parser.ContainsSection(section + "_" + groupName))
            {
                section = section + "_" + groupName;
            }
            group.GuidanceStartSeconds = (float)parser.Get(section, "GuidanceStartSeconds").ToDouble(2.0);

            group.CruiseDistSqMin = parser.Get(section, "CruiseDistMin").ToDouble(1000);
            group.CruiseDistSqMin *= group.CruiseDistSqMin;

            group.PlungeDist = parser.Get(section, "PlungeDist").ToInt16(1000);
            group.HitOffset = parser.Get(section, "HitOffset").ToDouble(0.0);
            group.ReloadCooldownMS = parser.Get(section, "ReloadCooldownMS").ToInt32(group.ReloadCooldownMS);

            group.AutoFire = parser.Get(section, "AutoFire").ToBoolean();
            group.AutoFireRange = parser.Get(section, "AutoFireRange").ToInt16(15000);
            group.AutoFireTubeMS = parser.Get(section, "AutoFireTubeMS").ToInt16(500);
            group.AutoFireTargetMS = parser.Get(section, "AutoFireTargetMS").ToInt16(2000);
            group.AutoFireRadius = parser.Get(section, "AutoFireRadius").ToInt16(30);
            group.AutoFireSizeMask = parser.Get(section, "AutoFireSizeMask").ToInt16(1);

            group.Trickshot = parser.Get(section, "Trickshot").ToBoolean(false);
            group.TrickshotDistance = parser.Get(section, "TrickshotDistance").ToInt32(1200);
            group.TrickshotTerminalDistanceSq = parser.Get(section, "TrickshotTerminalDistance").ToInt32(1000);
            group.TrickshotTerminalDistanceSq *= group.TrickshotTerminalDistanceSq;

            group.Evasion = parser.Get(section, "Evasion").ToBoolean();
            group.EvasionDistSqStart = parser.Get(section, "EvasionDistStart").ToInt32(2000);
            group.EvasionDistSqStart *= group.EvasionDistSqStart;
            group.EvasionDistSqEnd = parser.Get(section, "EvasionDistEnd").ToInt32(500);
            group.EvasionDistSqEnd *= group.EvasionDistSqEnd;
            group.EvasionOffsetMagnitude = parser.Get(section, "EvasionOffsetMagnitude").ToDouble(group.EvasionOffsetMagnitude);
            group.EvasionAdjTimeMin = parser.Get(section, "EvasionAdjTimeMin").ToInt32(group.EvasionAdjTimeMin);
            group.EvasionAdjTimeMax = parser.Get(section, "EvasionAdjTimeMax").ToInt32(group.EvasionAdjTimeMax);

            string partsSection = "Torpedo_Parts";
            if (parser.ContainsSection(partsSection + "_" + groupName))
            {
                partsSection = partsSection + "_" + groupName;
            }

            List<MyIniKey> partKeys = new List<MyIniKey>();
            parser.GetKeys(partsSection, partKeys);
            foreach (var key in partKeys)
            {
                var count = parser.Get(key).ToInt32();
                if (count == 0)
                    continue;
                var type = MyItemType.Parse(key.Name);
                group.TorpedoParts[type] = count;
            }
        }

        bool CollectParts(ExecutionContext Context, IMyTerminalBlock block)
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

        public void Fire()
        {
            if (fireCounter == -1)
            {
                if (FireSalvoMS == 0)
                {
                    Guns.Items.ForEach(gun => { gun.Enabled = true; TerminalPropertiesHelper.SetValue(gun, "Shoot", true); });
                    if (WCAPI != null)
                        WCGuns.Items.ForEach(gun => { gun.Enabled = true; WCAPI.ToggleWeaponFire(gun, true, true); });
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
                        wcGun.Enabled = true;
                        WCAPI.ToggleWeaponFire(wcGun, true, true);
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
    }*/
}
