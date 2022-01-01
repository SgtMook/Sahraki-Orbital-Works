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
        ExecutionContext Context;
        public Program()
        {
            Context = new ExecutionContext(this);
            subsystemManager = new SubsystemManager(Context);
            Runtime.UpdateFrequency = UpdateFrequency.Update1;

            // Add subsystems
            // Intel system setup
            IIntelProvider intelSubsystem;
            intelSubsystem = new IntelSubsystem();
            Context.IntelSystem = intelSubsystem;

            subsystemManager.AddSubsystem("intel", (ISubsystem)intelSubsystem);

            //// Looking Glass Setup
            //LookingGlassNetworkSubsystem lookingGlassNetwork = new LookingGlassNetworkSubsystem(intelSubsystem);
            //subsystemManager.AddSubsystem("lookingglass", lookingGlassNetwork);
            //
            //// Hangar system setup
            //HangarSubsystem hangarSubsystem = new HangarSubsystem(intelSubsystem);
            //subsystemManager.AddSubsystem("hangar", hangarSubsystem);
            //
            //// Seeing-Eye scanner setup
            //subsystemManager.AddSubsystem("scanner", new ScannerNetworkSubsystem(intelSubsystem, "SE"));
            //
            //// Inventory system setup
            //InventoryManagerSubsystem inventorySubsystem = new InventoryManagerSubsystem();
            //inventorySubsystem.RegisterRequester(hangarSubsystem);
            //subsystemManager.AddSubsystem("inventory", inventorySubsystem);
            //
            //// Command system setup
            //TextCommandSubsystem textCommandSubsystem = new TextCommandSubsystem(intelSubsystem);
            //subsystemManager.AddSubsystem("command", textCommandSubsystem);

            subsystemManager.DeserializeManager(Storage);
        }

        CommandLine commandLine = new CommandLine();

        SubsystemManager subsystemManager;

        public void Save()
        {
            string v = subsystemManager.SerializeManager();
            Me.CustomData = v;
            Storage = v;
        }

        public void Main(string argument, UpdateType updateSource)
        {
            Context.UpdateTime();
            if (commandLine.TryParse(argument))
            {
                subsystemManager.CommandV2(commandLine);
            }
            else
            {
                subsystemManager.Update(updateSource);
                Echo(subsystemManager.GetStatus());
            }
        }
    }
}
