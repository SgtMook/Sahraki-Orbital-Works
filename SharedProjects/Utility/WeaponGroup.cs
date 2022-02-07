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
    public class WeaponGroup
    {
        public RoundRobin<IMyFunctionalBlock> Weapons = new RoundRobin<IMyFunctionalBlock>();

        public int salvoTicks = 0; // 0 or lower means no salvoing
        public int salvoTickCounter = 0;

        public int fireTicks = 0;

        public WcPbApi WCAPI = null;

        public void Add(IMyFunctionalBlock Weapon)
        {
            Weapons.Items.Add(Weapon);
        }

        public void OpenFire()
        {
            fireTicks = 20;
        }

        public void HoldFire()
        {
            fireTicks = -1;
        }

        public bool Active
        {
            get
            {
                if (Weapons.Items.Count == 0) return false;
                var anyWeaponOn = false;

                foreach (var weapon in Weapons.Items)
                {
                    if (weapon.Enabled)
                    {
                        anyWeaponOn = true;
                        break;
                    }
                }

                if (!anyWeaponOn) return false;

                return true;
            }
        }

        public IMyTerminalBlock Reference => Weapons.Items.Count > 0 ? Weapons.Items[0] : null;

        public void Update(int ticks = 1)
        {
            salvoTickCounter -= ticks;
            fireTicks -= ticks;

            //statusBuilder.Clear();
            if (Weapons.Items.Count == 0) return;

            //statusBuilder.AppendLine("TGTS: " + targets.Count.ToString());
            while (Weapons.Items[0].Closed)
            {
                Weapons.Items.RemoveAtFast(0);
                if (Weapons.Items.Count == 0) return;
            }

            if (fireTicks > 0)
            {
                if (salvoTicks <= 0)
                {
                    foreach (var weapon in Weapons.Items)
                    {
                        if (WCAPI != null)
                            Weapons.Items.ForEach(gun => { WCAPI.ToggleWeaponFire(gun, true, true); });
                        else
                            Weapons.Items.ForEach(gun => { TerminalPropertiesHelper.SetValue(gun, "Shoot", true); });
                    }
                }
                else
                {
                    if (salvoTickCounter < 0)
                    {
                        if (WCAPI != null)
                        {
                            var gun = Weapons.GetAndAdvance();
                            WCAPI.ToggleWeaponFire(gun, true, true);
                        }
                        else
                        {
                            var gun = Weapons.GetAndAdvance();
                            TerminalPropertiesHelper.SetValue(gun, "Shoot", true);
                        }

                        salvoTickCounter = salvoTicks;
                    }
                }
            }
            else
            {
                foreach (var weapon in Weapons.Items)
                {
                    if (WCAPI != null)
                        Weapons.Items.ForEach(gun => { WCAPI.ToggleWeaponFire(gun, false, true); });
                    else
                        Weapons.Items.ForEach(gun => { TerminalPropertiesHelper.SetValue(gun, "Shoot", false); });
                }
            }
        }
    }
}