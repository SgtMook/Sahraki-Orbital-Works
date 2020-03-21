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
    enum OutputMode
    {
        Debug,
        Profile,
        None
    }

    public class SubsystemManager
    {
        MyGridProgram Program;

        Dictionary<string, ISubsystem> Subsystems = new Dictionary<string, ISubsystem>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, HashSet<string>> CommandMultiplexors = new Dictionary<string, HashSet<string>>();

        int UpdateCounter = 0;

        StringBuilder StatusBuilder = new StringBuilder();
        StringBuilder DebugBuilder = new StringBuilder();

        TimeSpan Timestamp = new TimeSpan();

        const double PROFILER_NEW_VALUE_FACTOR = 0.01;
        const int PROFILER_HISTORY_COUNT = (int)(1 / PROFILER_NEW_VALUE_FACTOR);
        Profiler profiler;

        OutputMode OutputMode = OutputMode.None;

        public SubsystemManager(MyGridProgram program)
        {
            Program = program;
            ParseConfigs();
            if (OutputMode == OutputMode.Profile) profiler = new Profiler(program.Runtime, PROFILER_HISTORY_COUNT, PROFILER_NEW_VALUE_FACTOR);
        }

        // [Manager]
        // OutputMode = 0
        private void ParseConfigs()
        {
            MyIni Parser = new MyIni();
            MyIniParseResult result;
            if (!Parser.TryParse(Program.Me.CustomData, out result))
                return;

            OutputMode mode;
            if (Enum.TryParse(Parser.Get("Manager", "OutputMode").ToString(), out mode)) OutputMode = mode;
        }

        public string AddSubsystem(string name, ISubsystem subsystem)
        {
            if (!Subsystems.ContainsKey(name))
            {
                Subsystems[name] = subsystem;
                subsystem.Setup(Program, name);
                return "AOK";
            }
            else
            {
                return "ERR: Name already added";
            }
        }

        /// <summary>
        /// A multiplexor is used to send one command to multiple subsystems.
        /// For example:
        /// AddCommandMultiplexor("group1", "subsystem1");
        /// AddCommandMultiplexor("group1", "subsystem2");
        /// Command("group1", "Hello", "HelloArgs");
        /// Will send the command "Hello" with argument "HelloArgs" to both subsystem1 and subsystem 2
        /// </summary>
        public void AddCommandMultiplexor(string multiplexorName, string subsystemName)
        {
            if (!CommandMultiplexors.ContainsKey(multiplexorName)) CommandMultiplexors[multiplexorName] = new HashSet<string>();
            CommandMultiplexors[multiplexorName].Add(subsystemName);
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

        public void Update(UpdateType updateSource)
        {
            if (OutputMode == OutputMode.Profile) profiler.StartSectionWatch("Setup frequencies");
            if (OutputMode == OutputMode.Profile) profiler.UpdateRuntime();
            UpdateCounter++;

            UpdateFrequency updateFrequency = UpdateFrequency.None;
            if ((updateSource & UpdateType.Update1) != 0) updateFrequency |= UpdateFrequency.Update1;
            if ((updateSource & UpdateType.Update10) != 0) updateFrequency |= UpdateFrequency.Update10;
            if ((updateSource & UpdateType.Update100) != 0) updateFrequency |= UpdateFrequency.Update100;

            UpdateFrequency targetFrequency = UpdateFrequency.Update1;
            if (OutputMode == OutputMode.Profile) profiler.StopSectionWatch("Setup frequencies");
            foreach (var subsystem in Subsystems)
            {
                if (OutputMode == OutputMode.Profile) profiler.StartSectionWatch(subsystem.Key);
                ISubsystem system = subsystem.Value;
                if ((system.UpdateFrequency & updateFrequency) != 0)
                {
                    system.Update(Timestamp, updateFrequency);
                }
                targetFrequency |= system.UpdateFrequency;
                if (OutputMode == OutputMode.Profile) profiler.StopSectionWatch(subsystem.Key);
            }

            Program.Runtime.UpdateFrequency = targetFrequency;
        }

        public string GetStatus()
        {
            StatusBuilder.Clear();

            if (OutputMode == OutputMode.Profile)
            {
                profiler.StartSectionWatch("Profiler");
                profiler.PrintPerformance(StatusBuilder);
                StatusBuilder.AppendLine("============");
                profiler.PrintSectionBreakdown(StatusBuilder);
                profiler.StopSectionWatch("Profiler");
            }
            else if (OutputMode == OutputMode.Debug)
            {
                StatusBuilder.AppendLine(DebugBuilder.ToString());

                StatusBuilder.AppendLine("=====");

                StatusBuilder.Append("Cycle ").AppendLine(UpdateCounter.ToString());
                StatusBuilder.Append(Subsystems.Count.ToString()).AppendLine("systems connected");

                foreach (KeyValuePair<string, ISubsystem> kvp in Subsystems)
                {
                    StatusBuilder.AppendLine(kvp.Key);
                    StatusBuilder.AppendLine(kvp.Value.GetStatus());
                }
            }
            else
            {
                return string.Empty;
            }

            return StatusBuilder.ToString();
        }

        public void Command(string subsystem, string command, object argument)
        {
            if (Subsystems.ContainsKey(subsystem))
            {
                Subsystems[subsystem].Command(Timestamp, command, argument);
            }
            else if (CommandMultiplexors.ContainsKey(subsystem))
            {
                foreach (var system in CommandMultiplexors[subsystem]) Command(system, command, argument);
            }
        }

        public void UpdateTime()
        {
            Timestamp += Program.Runtime.TimeSinceLastRun;
        }
    }
}
