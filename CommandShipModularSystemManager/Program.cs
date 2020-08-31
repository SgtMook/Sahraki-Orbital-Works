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

            ParseConfigs();

            // Add subsystems
            // Intel system setup
            IIntelProvider intelSubsystem;
            intelSubsystem = new IntelSlaveSubsystem(1);
            
            subsystemManager.AddSubsystem("intel", (ISubsystem)intelSubsystem);
            LookingGlassNetworkSubsystem lookingGlassNetwork = null;

            // Looking Glass Setup
            if (LookingGlass)
            {
                lookingGlassNetwork = new LookingGlassNetworkSubsystem(intelSubsystem, "LG", !FixedLookingGlass, ThrusterLookingGlass);
                subsystemManager.AddSubsystem("lookingglass", lookingGlassNetwork);
            }
            
            // Hangar system setup
            HangarSubsystem hangarSubsystem = new HangarSubsystem(intelSubsystem);
            subsystemManager.AddSubsystem("hangar", hangarSubsystem);

            ScannerNetworkSubsystem scannerSubsystem = null;
            
            // Seeing-Eye scanner setup
            if (Scanner)
            {
                scannerSubsystem = new ScannerNetworkSubsystem(intelSubsystem, "SE");
                subsystemManager.AddSubsystem("scanner", scannerSubsystem);
            }
            
            DroneForgeSubsystem forgeSubsystem = null;
            
            // Drone Forge setup
            if (Forge)
            {
                forgeSubsystem = new DroneForgeSubsystem(intelSubsystem);
                subsystemManager.AddSubsystem("forge", forgeSubsystem);
            }
            
            // Inventory system setup
            if (Inventory)
            {
                InventoryManagerSubsystem inventorySubsystem = new InventoryManagerSubsystem();
                inventorySubsystem.RegisterRequester(hangarSubsystem);
                if (Forge) inventorySubsystem.RegisterRequester(forgeSubsystem);
                subsystemManager.AddSubsystem("inventory", inventorySubsystem);
            }
            TorpedoSubsystem torpedoSubsystem = null;
            // Torpedo system setup
            if (Torpedos)
            {
                torpedoSubsystem = new TorpedoSubsystem(intelSubsystem);
                subsystemManager.AddSubsystem("torpedo", torpedoSubsystem);
            }

            if (lookingGlassNetwork != null)
            {
                lookingGlassNetwork.AddPlugin("combat", new LookingGlassPlugin_Combat(torpedoSubsystem, hangarSubsystem, scannerSubsystem, forgeSubsystem));
                lookingGlassNetwork.ActivatePlugin(DefaultLookingGlassPlugin);
            }

            //
            // Command system setup
            TacticalCommandSubsystem tacticalSubsystem = new TacticalCommandSubsystem(intelSubsystem);
            subsystemManager.AddSubsystem("command", tacticalSubsystem);

            // Black ops
            // ECMInterfaceSubsystem ECM = new ECMInterfaceSubsystem(intelSubsystem);
            // subsystemManager.AddSubsystem("ECM", ECM);

            subsystemManager.DeserializeManager(Storage);
        }

        bool LookingGlass = true;
        bool FixedLookingGlass = false;
        bool ThrusterLookingGlass = false;
        bool Scanner = true;
        bool Inventory = true;
        bool Forge = true;
        bool Torpedos = false;
        string DefaultLookingGlassPlugin = "command";
        // [Setup]
        // IsMaster = true
        // LookingGlass = true
        // FixedLookingGlass = false
        // ThrusterLookingGlass = false
        // Scanner = true
        // Inventory = true
        // Forge = true
        // Torpedos = true
        // DefaultLookingGlassPlugin = command
        void ParseConfigs()
        {
            MyIni Parser = new MyIni();
            MyIniParseResult result;
            if (!Parser.TryParse(Me.CustomData, out result))
                return;

            LookingGlass = Parser.Get("Setup", "LookingGlass").ToBoolean();
            FixedLookingGlass = Parser.Get("Setup", "FixedLookingGlass").ToBoolean();
            ThrusterLookingGlass = Parser.Get("Setup", "ThrusterLookingGlass").ToBoolean();
            DefaultLookingGlassPlugin = Parser.Get("Setup", "DefaultLookingGlassPlugin").ToString("command");
            Scanner = Parser.Get("Setup", "Scanner").ToBoolean();
            Inventory = Parser.Get("Setup", "Inventory").ToBoolean();
            Forge = Parser.Get("Setup", "Forge").ToBoolean();
            Torpedos = Parser.Get("Setup", "Torpedos").ToBoolean();
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
                Echo(subsystemManager.GetStatus());
            }
        }
    }
}
