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
    // This is a Raven class attack drone
    public class Raven
    {
        int runs = 0;

        IMyRemoteControl Controller;

        List<IMyLargeTurretBase> Turrets = new List<IMyLargeTurretBase>();

        MyGridProgram Program;
        AtmoDrive Drive;

        public Raven(IMyRemoteControl reference, MyGridProgram program)
        {
            Program = program;
            Controller = reference;
        }

        public void Initialize()
        {
            Drive = new AtmoDrive(Controller);
            Program.GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(null, CollectBlock);
            Drive.Initialize();
        }

        private bool CollectBlock(IMyTerminalBlock block)
        {
            Drive.AddComponenet(block);
            if (block is IMyLargeTurretBase) Turrets.Add((IMyLargeTurretBase)block);
            return false;
        }

        public void Update()
        {
            runs++;
            if (runs % 5 == 0)
            {
                Drive.AimTarget = Vector3D.Zero;
                // TODO: Add WeaponCore targeting here
                foreach (var turret in Turrets)
                {
                    if (turret.HasTarget)
                    {
                        var target = turret.GetTargetedEntity();
                        Drive.AimTarget = target.Position;
                        break;
                    }
                }
                Drive.Update();
            }
        }

        public string GetStatus()
        {
            return Drive.StatusBuilder.ToString();
        }
    }
}
