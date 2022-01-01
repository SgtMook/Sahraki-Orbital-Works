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

    partial class Program : MyGridProgram
    {
        ExecutionContext Context;
        public Program()
        {
            Context = new ExecutionContext(this);
            subsystemManager = new SubsystemManager(Context);
            Runtime.UpdateFrequency = UpdateFrequency.Update1;

            // Add subsystems
            // AutopilotSubsystem autopilotSubsystem = new AutopilotSubsystem();
            IntelSubsystem intelSubsystem = new IntelSubsystem();
            Context.IntelSystem = intelSubsystem;

            TacMapSubsystem tacMapSubsystem = new TacMapSubsystem(intelSubsystem);

            // subsystemManager.AddSubsystem("autopilot", autopilotSubsystem);
            subsystemManager.AddSubsystem("intel", intelSubsystem);
            subsystemManager.AddSubsystem("tacmap", tacMapSubsystem);

            subsystemManager.DeserializeManager(Storage);
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
                }
                catch (Exception e)
                {
                    Me.GetSurface(0).WriteText(e.Message);
                    Me.GetSurface(0).WriteText("\n", true);
                    Me.GetSurface(0).WriteText(e.StackTrace);
                    Me.GetSurface(0).WriteText("\n", true);
                    Me.GetSurface(0).WriteText(e.ToString());
                }
                var s = subsystemManager.GetStatus();
                if (!string.IsNullOrEmpty(s)) Echo(s);
            }
        }
    }
}
