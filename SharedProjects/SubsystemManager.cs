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

namespace SharedProjects
{
    public class SubsystemManager
    {
        MyGridProgram Program;

        Dictionary<string, ISubsystem> Subsystems = new Dictionary<string, ISubsystem>(StringComparer.OrdinalIgnoreCase);

        int UpdateCounter = 0;

        StringBuilder StatusBuilder = new StringBuilder();
        StringBuilder DebugBuilder = new StringBuilder();

        public SubsystemManager(MyGridProgram program)
        {
            Program = program;
        }

        public string AddSubsystem(string name, ISubsystem subsystem)
        {
            if (!Subsystems.ContainsKey(name))
            {
                Subsystems[name] = subsystem;
                subsystem.Setup(Program);
                return "AOK";
            }
            else
            {
                return "ERR: Name already added";
            }
        }


        public string SerializeManager()
        {
            StringBuilder saveBuilder = new StringBuilder();

            // Save my settings here

            // Save subsystem settings here
            saveBuilder.AppendLine(Subsystems.Count().ToString());
            foreach (KeyValuePair<string, ISubsystem> kvp in Subsystems)
            {
                string subsystemSave = kvp.Value.SerializeSubsystem();
                int lns = subsystemSave.Split(
                    new[] { "\r\n", "\r", "\n" },
                    StringSplitOptions.None
                ).Count();

                saveBuilder.AppendLine($"{kvp.Key} {lns.ToString()}");
                saveBuilder.AppendLine(subsystemSave);
            }

            return saveBuilder.ToString();
        }



        public void DeserializeManager(string serialized)
        {
            try
            {
                var loadBuilder = new StringBuilder();
                var reader = new MyStringReader(serialized);

                loadBuilder.Clear();

                // Load subsystem settings here
                int numSubsystems = int.Parse(reader.NextLine());


                for (int i = 0; i < numSubsystems; i++)
                {
                    string[] header = reader.NextLine().Split(' ');
                    string name = header[0];

                    int numLines = int.Parse(header[1]);

                    for (int j = 0; j < numLines; j++)
                    {
                        loadBuilder.AppendLine(reader.NextLine());
                    }

                    if (Subsystems.ContainsKey(name))
                    {
                        Subsystems[name].DeserializeSubsystem(loadBuilder.ToString());
                    }

                    loadBuilder.Clear();
                }
            }
            catch (Exception e)
            {
                DebugBuilder.AppendLine(e.StackTrace);
            }
        }



        public void Update()
        {
            UpdateCounter++;
            foreach (ISubsystem subsystem in Subsystems.Values)
            {
                if (UpdateCounter % subsystem.UpdateFrequency == 0)
                {
                    subsystem.Update();
                }
            }
        }

        public string GetStatus()
        {
            StatusBuilder.Clear();

            StatusBuilder.AppendLine(DebugBuilder.ToString());

            StatusBuilder.AppendLine("=====");

            StatusBuilder.AppendLine($"Cycle {UpdateCounter}");
            StatusBuilder.AppendLine($"{Subsystems.Count} systems connected");

            foreach (KeyValuePair<string, ISubsystem> kvp in Subsystems)
            {
                StatusBuilder.AppendLine(kvp.Key);
                StatusBuilder.AppendLine(kvp.Value.GetStatus());
            }

            return StatusBuilder.ToString();
        }

        public void Command(string subsystem, string command, object argument)
        {
            if (Subsystems.ContainsKey(subsystem))
            {
                Subsystems[subsystem].Command(command, argument);
            }
            else
            {
                // TODO: error handle
            }
        }
    }
}
