﻿using Sandbox.Game.EntityComponents;
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
        public Program()
        {
            hornet = new Hornet(Me, this);
            Runtime.UpdateFrequency = UpdateFrequency.Update1;
        }
        Hornet hornet;

        public void Main(string argument, UpdateType updateSource)
        {
            hornet.Update(updateSource);
            Echo(hornet.SubsystemManager.GetStatus());
        }
    }
}
