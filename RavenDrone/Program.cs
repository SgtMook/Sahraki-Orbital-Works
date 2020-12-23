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
        }

        private bool CollectBlocks(IMyTerminalBlock block)
        {
            if (Me.CubeGrid.EntityId != block.CubeGrid.EntityId)
                return false;
            if (block is IMyRemoteControl) RemoteControl = (IMyRemoteControl)block;
            return false;
        }

        public void Save()
        {
        }

        public void Main(string argument, UpdateType updateSource)
        {
            Echo(MyRaven.GetStatus());
            if (!string.IsNullOrEmpty(argument))
            {
                MyRaven.Drive.SetDest(argument);
            }
            try
            {
                MyRaven.Update(updateSource);
            }
            catch (Exception e)
            {
                Me.GetSurface(0).WriteText(e.StackTrace);
                Me.GetSurface(0).WriteText("\n", true);
                Me.GetSurface(0).WriteText(e.Message, true);
                Me.GetSurface(0).WriteText("\n", true);
                Me.GetSurface(0).WriteText(MyRaven.GetStatus(), true);
            }
        }
    }
}
