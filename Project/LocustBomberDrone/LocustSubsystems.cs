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
    // Gatling cannon based ordinance dispersal system
    public class LocustCombatSystem : ISubsystem
    {
        #region ISubsystem
        public UpdateFrequency UpdateFrequency => UpdateFrequency.Update10;

        public void Command(TimeSpan timestamp, string command, object argument)
        {
            if (command == "deploy") Deploy();
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

        public void Setup(ExecutionContext context, string name)
        {
            Context = context;
            GetParts();
        }

        public void Update(TimeSpan timestamp, UpdateFrequency updateFlags)
        {
            if (deploying > 0)
            {
                deploying--;

                if (deploying <= 0)
                {
                    foreach (var gun in Guns)
                    {
                        TerminalPropertiesHelper.SetValue(gun, "Shoot", false);
                    }
                }
            }
        }
        #endregion
        ExecutionContext Context;

        List<IMySmallGatlingGun> Guns = new List<IMySmallGatlingGun>();

        public EnemyShipIntel TargetIntel;

        int deploying;

        void GetParts()
        {
            Guns.Clear();
            Context.Terminal.GetBlocksOfType<IMyTerminalBlock>(null, CollectParts);
        }

        bool CollectParts(IMyTerminalBlock block)
        {
            if (!Context.Reference.IsSameConstructAs(block)) return false;

            if (block is IMySmallGatlingGun)
                Guns.Add((IMySmallGatlingGun)block);

            return false;
        }

        #region Public accessors

        public void Deploy()
        {
            foreach (var gun in Guns)
            {
                TerminalPropertiesHelper.SetValue(gun, "Shoot", true);
            }
            deploying = 12;
        }

        public const int kEngageRange = 1000;
        #endregion
    }
}
