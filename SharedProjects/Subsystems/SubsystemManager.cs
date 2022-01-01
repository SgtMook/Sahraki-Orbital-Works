﻿using Sandbox.Game.EntityComponents;
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
        const int LineCount = 40;
        string[] LogLines;
        int LogIndex = 0;

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

    public struct ScriptTime
    {
//         long ticks;
//         public static readonly TimeSpan Zero;
        public static TimeSpan FromMilliseconds(double value) { return TimeSpan.FromMilliseconds(value); }
    }

    // Wrapping for functionality & minification
    public class CommandLine
    {
        public string Subsystem;
        MyCommandLine myCommandLine = new MyCommandLine();

        public string JoinArguments(StringBuilder builder, int start, int end)
        {
            builder.Clear();
            int termination = Math.Max(ArgumentCount-1, end);
            for ( int i = start; i <= termination; ++i )
            {
                builder.Append(myCommandLine.Items[i]);
                if ( i != termination)
                    builder.Append(' ');
            }
            return builder.ToString();
        }
        public bool TryParse(string line)
        {
            bool success = myCommandLine.TryParse(line);
            if ( success )
            {
                Subsystem = myCommandLine.Argument(0);
                int index = line.IndexOf(Subsystem);
                line = line.Remove(index, Subsystem.Length);

                success = myCommandLine.TryParse(line);
            }
            return success;
        }
        public string Argument(int index)
        {
            return myCommandLine.Argument(index);
        }
        public int ArgumentCount => myCommandLine.ArgumentCount;
    }

    public class ExecutionContext
    {
        public MyGridProgram Program;
        public IMyIntergridCommunicationSystem IGC;
        public IMyGridTerminalSystem Terminal;
        public Logger Log = new Logger();
        public IMyTerminalBlock Reference;
        public StringBuilder StringBuilder= new StringBuilder();
        public Random Random = new Random();
        public long Frame = 0;
        public TimeSpan CurrentTime;
        
        // The Intel system uses Canonical Time, since each ship
        // can have its own time offset based on the timezone of
        // the player.
        public TimeSpan CanonicalTime;
        public IIntelProvider IntelSystem;
        public ExecutionContext(MyGridProgram program, IMyTerminalBlock reference = null)
        { 
            Program = program;
            IGC = program.IGC;
            Terminal = program.GridTerminalSystem;

            Reference = (reference == null) ? program.Me : reference;
        }
        public void UpdateTime()
        {
            CurrentTime += Program.Runtime.TimeSinceLastRun;
            CanonicalTime = CurrentTime + (IntelSystem != null ? IntelSystem.CanonicalTimeDiff : TimeSpan.Zero);
        }

    }

    public class SubsystemManager
    {
        ExecutionContext Context;

        public Dictionary<string, ISubsystem> Subsystems = new Dictionary<string, ISubsystem>(StringComparer.OrdinalIgnoreCase);

        StringBuilder StatusBuilder = new StringBuilder();
        StringBuilder DebugBuilder = new StringBuilder();
        StringBuilder ExceptionBuilder = new StringBuilder();

        const double PROFILER_NEW_VALUE_FACTOR = 0.01;
        const int PROFILER_HISTORY_COUNT = (int)(1 / PROFILER_NEW_VALUE_FACTOR);
        Profiler profiler;

        public OutputMode OutputMode = OutputMode.None;
        bool Active = true;
        bool Activating = false;

        string myName;

        IMyBroadcastListener GeneralListener;
        const string GeneralChannel = "[FLT-GNR]";

        CommandLine commandLine = new CommandLine();

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
                        CommandV2(commandLine);
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
                        system.Update(Context.CurrentTime, updateFrequency);
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

        public void CommandLegacy(string subsystem, string command, string argument)
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
                Subsystems[subsystem].Command(Context.CurrentTime, command, argument);
            }
        }
        public void CommandV2(CommandLine command)
        {
            if ( command.Subsystem == "manager" )
            {
                if (command.Argument(0) == "reset")
                    Reset();
                if (command.Argument(0) == "activate")
                {
                    myName = command.Argument(1);
                    Activating = true;
                }
                if (command.Argument(0) == "broadcast")
                {
                    Context.IGC.SendBroadcastMessage(GeneralChannel, command.JoinArguments(Context.StringBuilder, 1, command.ArgumentCount - 1));
                }
            }
            else if( command.Subsystem != null )
            {
                ISubsystem subsystem;
                if ( Subsystems.TryGetValue(command.Subsystem, out subsystem) )
                {
                    subsystem.Command(Context.CurrentTime, command.Argument(0), command.Argument(1));
                    subsystem.CommandV2(Context.CurrentTime, command);
                }

            }
        }
    }
}
