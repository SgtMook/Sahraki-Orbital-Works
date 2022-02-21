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

            ParseConfigs();

            // Add subsystems
            // Intel system setup
            IntelSubsystem intelSubsystem = new IntelSubsystem(1);
            Context.IntelSystem = intelSubsystem;

            subsystemManager.AddSubsystem("intel", intelSubsystem);
            LookingGlassNetworkSubsystem lookingGlassNetwork = null;

            // Looking Glass Setup
            if (LookingGlass)
            {
                lookingGlassNetwork = new LookingGlassNetworkSubsystem(intelSubsystem, "LG", !FixedLookingGlass, ThrusterLookingGlass);
                subsystemManager.AddSubsystem("lookingglass", lookingGlassNetwork);
                lookingGlassNetwork.AddPlugin("command", new LookingGlassPlugin_Command());
                lookingGlassNetwork.AddPlugin("lidar", new LookingGlassPlugin_Lidar());
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

            TorpedoSubsystem torpedoSubsystem = null;
            // Torpedo system setup
            if (Torpedos)
            {
                torpedoSubsystem = new TorpedoSubsystem(intelSubsystem);
                subsystemManager.AddSubsystem("torpedo", torpedoSubsystem);
            }

            if (lookingGlassNetwork != null)
            {
                lookingGlassNetwork.AddPlugin("combat", new LookingGlassPlugin_Combat(torpedoSubsystem, hangarSubsystem, scannerSubsystem));
                lookingGlassNetwork.ActivatePlugin(DefaultLookingGlassPlugin);
            }

            subsystemManager.AddSubsystem("turret", new TurretSubsystem(intelSubsystem));

            //
            // Command system setup
            TacticalCommandSubsystem tacticalSubsystem = new TacticalCommandSubsystem(intelSubsystem);
            subsystemManager.AddSubsystem("command", tacticalSubsystem);

            // Black ops
            // ECMInterfaceSubsystem ECM = new ECMInterfaceSubsystem(intelSubsystem);
            // subsystemManager.AddSubsystem("ECM", ECM);

            subsystemManager.AddSubsystem("loader", new CombatLoaderSubsystem(CombatLoaderCargo, CombatCargoStore));

            subsystemManager.DeserializeManager(Storage);
        }

        bool LookingGlass = true;
        bool FixedLookingGlass = false;
        bool ThrusterLookingGlass = false;
        bool Scanner = true;
        bool Inventory = true;
        bool Torpedos = false;
        string DefaultLookingGlassPlugin = "command";
        string CombatLoaderCargo;
        string CombatCargoStore;
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
            if (!Parser.TryParse(Me.CustomData))
                return;

            LookingGlass = Parser.Get("Setup", "LookingGlass").ToBoolean();
            FixedLookingGlass = Parser.Get("Setup", "FixedLookingGlass").ToBoolean();
            ThrusterLookingGlass = Parser.Get("Setup", "ThrusterLookingGlass").ToBoolean();
            DefaultLookingGlassPlugin = Parser.Get("Setup", "DefaultLookingGlassPlugin").ToString("command");
            CombatLoaderCargo = Parser.Get("Setup", "CombatLoaderCargo").ToString("Cargo");
            CombatCargoStore = Parser.Get("Setup", "CombatCargoStore").ToString("Store");
            Scanner = Parser.Get("Setup", "Scanner").ToBoolean();
            Inventory = Parser.Get("Setup", "Inventory").ToBoolean();
            Torpedos = Parser.Get("Setup", "Torpedos").ToBoolean();
        }

        CommandLine commandLine = new CommandLine();

        SubsystemManager subsystemManager;

        public void Save()
        {
            string v = subsystemManager.SerializeManager();
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
                try
                {
                    subsystemManager.Update(updateSource);
                    var status = subsystemManager.GetStatus();
                    if (status != string.Empty) Echo(status);
                }
                catch (Exception e)
                {
                    Me.GetSurface(0).WriteText(e.StackTrace);
                    Me.GetSurface(0).WriteText("\n", true);
                    Me.GetSurface(0).WriteText(e.Message, true);
                    Me.GetSurface(0).WriteText("\n", true);
                    Me.GetSurface(0).WriteText(subsystemManager.GetStatus(), true);
                }
            }
        }
    }
}
