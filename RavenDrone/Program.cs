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
using VRage.Library;

using System.Collections.Immutable;

namespace IngameScript
{
    partial class Program : MyGridProgram
    {
        Raven MyRaven;
        IMyRemoteControl RemoteControl;
        public Program()
        {
            Runtime.UpdateFrequency = UpdateFrequency.Update1;
            GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(null, CollectBlocks);

            MyRaven = new Raven(RemoteControl, this);
            MyRaven.Initialize();
        }

        private bool CollectBlocks(IMyTerminalBlock block)
        {
            if (block is IMyRemoteControl) RemoteControl = (IMyRemoteControl)block;
            return false;
        }

        public void Save()
        {
        }

        public void Main(string argument, UpdateType updateSource)
        {
            if (!string.IsNullOrEmpty(argument))
            {
                MyRaven.Drive.SetDest(argument);
            }

            MyRaven.Update();
            Echo(MyRaven.GetStatus());
        }
    }
}
