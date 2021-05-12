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
    public enum OutputMode
    {
        Debug,
        Profile,
        None
    }
    public class Logger
    {
        private const int LineCount = 20;
        private string[] LogLines;
        private int LogIndex = 0;

        public Logger()
        {
            LogLines = new string[LineCount];
        }
        public void Debug(string msg)
        {
            LogLines[LogIndex] = "> " + msg;
            LogIndex = (LogIndex + 1) % LineCount;
        }
        public string GetOutput()
        {
            StringBuilder builder = new StringBuilder(512);
            GetOutput(builder);
            return builder.ToString();
        }

        public void GetOutput(StringBuilder builder)
        {
            for (int i = 0; i < LogLines.Length; ++i)
            {
                if (i == LogIndex)
                    builder.AppendLine("==== WRAP ====");
                else
                    builder.AppendLine(LogLines[i]);
            }
        }
    }

    public class ExecutionContext
    {
        public MyGridProgram Program;
        public IMyIntergridCommunicationSystem IGC;
        public IMyGridTerminalSystem Terminal;
        public Logger Log = new Logger();
        public IMyTerminalBlock Reference;
        public long Frame = 0;
        public ExecutionContext(MyGridProgram program, IMyTerminalBlock reference = null)
        { 
            Program = program;
            IGC = program.IGC;
            Terminal = program.GridTerminalSystem;

            Reference = (reference == null) ? program.Me : reference;
        }
    }

    public class SubsystemManager
    {
        ExecutionContext Context;

        public Dictionary<string, ISubsystem> Subsystems = new Dictionary<string, ISubsystem>(StringComparer.OrdinalIgnoreCase);

        StringBuilder StatusBuilder = new StringBuilder();
        StringBuilder DebugBuilder = new StringBuilder();
        StringBuilder ExceptionBuilder = new StringBuilder();

        public TimeSpan Timestamp = new TimeSpan();

        const double PROFILER_NEW_VALUE_FACTOR = 0.01;
        const int PROFILER_HISTORY_COUNT = (int)(1 / PROFILER_NEW_VALUE_FACTOR);
        Profiler profiler;

        public OutputMode OutputMode = OutputMode.None;
        bool Active = true;
        bool Activating = false;

        string myName;

        IMyBroadcastListener GeneralListener;
        const string GeneralChannel = "[FLT-GNR]";

        MyCommandLine commandLine = new MyCommandLine();

        public SubsystemManager(ExecutionContext context)
        {
            Context = context;

            ParseConfigs();
            if (OutputMode == OutputMode.Profile) 
                profiler = new Profiler(Context.Program.Runtime, PROFILER_HISTORY_COUNT, PROFILER_NEW_VALUE_FACTOR);

            GeneralListener = Context.IGC.RegisterBroadcastListener(GeneralChannel);
        }

        // [Manager]
        // OutputMode = 0
        // StartActive = true
        void ParseConfigs()
        {
            MyIni Parser = new MyIni();
            MyIniParseResult result;
            if (!Parser.TryParse(Context.Reference.CustomData, out result))
                return;

            OutputMode mode;
            if (Enum.TryParse(Parser.Get("Manager", "OutputMode").ToString(), out mode)) 
                OutputMode = mode;
            if (Parser.ContainsKey("Manager", "StartActive")) 
                Active = Parser.Get("Manager", "StartActive").ToBoolean();
        }

        public void AddSubsystem(string name, ISubsystem subsystem)
        {
            Subsystems[name] = subsystem;
            subsystem.Setup(Context, name);
        }

        public void Reset()
        {
            if (OutputMode == OutputMode.Profile) 
                profiler.StartSectionWatch("Reset");
            foreach (var kvp in Subsystems) 
                kvp.Value.Setup(Context, kvp.Key);
            if (OutputMode == OutputMode.Profile) 
                profiler.StopSectionWatch("Reset");
        }

        public void Activate()
        {
            Reset();
            Active = true;
            if (Subsystems.ContainsKey("docking")) Subsystems["docking"].Command(TimeSpan.Zero, "dock", null);

            MyIni Parser = new MyIni();
            MyIniParseResult result;
            if (!Parser.TryParse(Context.Reference.CustomData, out result))
                return;

            Parser.Delete("Manager", "StartActive");
            Context.Reference.CustomData = Parser.ToString();
            Context.Reference.CubeGrid.CustomName = myName;
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
            catch (Exception exc)
            {
                ExceptionBuilder.AppendLine(exc.StackTrace);
            }
        }

        public void Update(UpdateType updateSource)
        {
            if (Active)
            {
                while (GeneralListener.HasPendingMessage)
                {
                    var msg = GeneralListener.AcceptMessage();
                    var data = msg.Data.ToString();
                    if (commandLine.TryParse(data))
                    {
                        Command(commandLine.Argument(0), commandLine.Argument(1), commandLine.ArgumentCount > 2 ? commandLine.Argument(2) : null);
                    }
                }


                if (OutputMode == OutputMode.Profile) profiler.StartSectionWatch("Setup frequencies");
                if (OutputMode == OutputMode.Profile) profiler.UpdateRuntime();
                Context.Frame++;

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

                Context.Program.Runtime.UpdateFrequency = targetFrequency;
            }
            else if (Activating)
            {
                Activating = false;
                Activate();
            }
        }

        public string GetStatus()
        {
            StatusBuilder.Clear();
            StatusBuilder.AppendLine($"OUTPUT MODE: {(int)OutputMode}");
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
                StatusBuilder.AppendLine(ExceptionBuilder.ToString());

                StatusBuilder.AppendLine("=====");

                StatusBuilder.Append("Cycle ").AppendLine(Context.Frame.ToString());
                StatusBuilder.Append(Subsystems.Count.ToString()).AppendLine("systems connected");

                foreach (KeyValuePair<string, ISubsystem> kvp in Subsystems)
                {
                    StatusBuilder.AppendLine("["+kvp.Key+"]");
                    StatusBuilder.AppendLine(kvp.Value.GetStatus());
                }

                Context.Log.GetOutput(DebugBuilder);
                StatusBuilder.AppendLine(DebugBuilder.ToString());
                DebugBuilder.Clear();
            }
            else
            {
                return string.Empty;
            }

            return StatusBuilder.ToString();
        }

        public void Command(string subsystem, string command, string argument)
        {
            if (subsystem == "manager")
            {
                if (command == "reset") 
                    Reset();
                if (command == "activate")
                {
                    myName = argument;
                    Activating = true;
                }
                if (command == "broadcast")
                {
                    Context.IGC.SendBroadcastMessage(GeneralChannel, argument);
                }
            }
            else if (Subsystems.ContainsKey(subsystem))
            {
                Subsystems[subsystem].Command(Timestamp, command, argument);
            }
        }

        public void UpdateTime()
        {
            Timestamp += Context.Program.Runtime.TimeSinceLastRun;
        }
    }
}
