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
        public IntelSubsystem IntelSubsystem;
        public ScannerNetworkSubsystem ScannerSubsystem;
        public TorpedoSubsystem TorpedoSubsystem;

        public Program()
        {
            subsystemManager = new SubsystemManager(this);
            Runtime.UpdateFrequency = UpdateFrequency.Update1;

            IntelSubsystem = new IntelSubsystem();
            TorpedoSubsystem = new TorpedoSubsystem(IntelSubsystem);
            ScannerSubsystem = new ScannerNetworkSubsystem(IntelSubsystem);
            subsystemManager.AddSubsystem("intel", IntelSubsystem);
            subsystemManager.AddSubsystem("scanner", ScannerSubsystem);
            subsystemManager.AddSubsystem("torpedo", TorpedoSubsystem);

            subsystemManager.DeserializeManager(Storage);

            TorpedoSubsystem.TorpedoTubeGroups["SM"].AutoFire = true;
        }

        MyCommandLine commandLine = new MyCommandLine();

        SubsystemManager subsystemManager;

        public void Save()
        {
            string v = subsystemManager.SerializeManager();
            Storage = v;
        }

        public void Main(string argument, UpdateType updateSource)
        {
            subsystemManager.UpdateTime();
            if (commandLine.TryParse(argument))
            {
                subsystemManager.Command(commandLine.Argument(0), commandLine.Argument(1), commandLine.ArgumentCount > 2 ? commandLine.Argument(2) : null);
            }
            else
            {
                subsystemManager.Update(updateSource);
                var status = subsystemManager.GetStatus();
                if (status != string.Empty) Echo(status);
            }
        }
    }
}
