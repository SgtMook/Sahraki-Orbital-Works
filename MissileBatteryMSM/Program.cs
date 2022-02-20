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
        ExecutionContext Context;
        CommandLine commandLine = new CommandLine();
        
        public IntelSubsystem IntelSubsystem;
        public ScannerNetworkSubsystem ScannerSubsystem;
        public HoverTorpedoSubsystem TorpedoSubsystem;

        public Program()
        {

            Context = new ExecutionContext(this);

            subsystemManager = new SubsystemManager(Context);
            Runtime.UpdateFrequency = UpdateFrequency.Update1;
            
            IntelSubsystem = new IntelSubsystem();
            Context.IntelSystem = IntelSubsystem;

            TorpedoSubsystem = new HoverTorpedoSubsystem(IntelSubsystem);
            ScannerSubsystem = new ScannerNetworkSubsystem(IntelSubsystem);
            subsystemManager.AddSubsystem("intel", IntelSubsystem);
            subsystemManager.AddSubsystem("scanner", ScannerSubsystem);
            subsystemManager.AddSubsystem("torpedo", TorpedoSubsystem);

            subsystemManager.DeserializeManager(Storage);
        }

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
                    if (status != string.Empty) 
                        Echo(subsystemManager.GetStatus());
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
