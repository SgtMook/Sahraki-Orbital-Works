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



namespace IngameScript
{
    partial class Program : MyGridProgram
    {
        public Program()
        {
            subsystemManager = new SubsystemManager(this);
            Runtime.UpdateFrequency = UpdateFrequency.Update1;

            // Add subsystems
            IntelMasterSubsystem intelSubsystem = new IntelMasterSubsystem();
            subsystemManager.AddSubsystem("intel", intelSubsystem);

            // Looking Glass Setup
            LookingGlassNetworkSubsystem lookingGlassNetwork = new LookingGlassNetworkSubsystem(intelSubsystem);

            lookingGlassNetwork.AddPlugin("command", new LookingGlassPlugin_Command());
            lookingGlassNetwork.AddPlugin("combat", new LookingGlassPlugin_Combat());

            LookingGlass lookingGlass1 = new LookingGlass(this, "[S1]");
            LookingGlass lookingGlass2 = new LookingGlass(this, "[S2]");

            lookingGlassNetwork.AddLookingGlass(lookingGlass1);
            lookingGlassNetwork.AddLookingGlass(lookingGlass2);

            subsystemManager.AddSubsystem("lookingglass", lookingGlassNetwork);

            subsystemManager.AddSubsystem("sensorswivel1", new SwivelSubsystem("[SN1]", lookingGlass1));
            subsystemManager.AddSubsystem("sensorswivel2", new SwivelSubsystem("[SN2]", lookingGlass2));

            subsystemManager.AddSubsystem("hangar", new HangarSubsystem(intelSubsystem));

            subsystemManager.AddSubsystem("scanner", new ScannerSubsystem(intelSubsystem, "SCN"));
            subsystemManager.AddSubsystem("scanner2", new ScannerSubsystem(intelSubsystem, "SCN2"));

            subsystemManager.DeserializeManager(Storage);
        }

        MyCommandLine commandLine = new MyCommandLine();

        SubsystemManager subsystemManager;

        public void Save()
        {
            string v = subsystemManager.SerializeManager();
            Me.CustomData = v;
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
                Echo(subsystemManager.GetStatus());
            }
        }
    }
}
