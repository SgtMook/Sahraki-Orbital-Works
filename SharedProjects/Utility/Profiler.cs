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
    public class Profiler
    {
        public int HistoryMaxCount;
        public double NewValueFactor;

        public double AverageRuntime;
        public double PeakRuntime;

        public double AverageComplexity;
        public double PeakComplexity;

        public IMyGridProgramRuntimeInfo Runtime { get; set; }
        public readonly Queue<double> HistoryRuntime = new Queue<double>();
        public readonly Queue<double> HistoryComplexity = new Queue<double>();
        public readonly Dictionary<string, SectionValues> AverageBreakdown = new Dictionary<string, SectionValues>();

        double invMaxRuntimePercent;
        double invMaxInstCountPercent;

        public Profiler(IMyGridProgramRuntimeInfo runtime, int historyMaxCount, double newValueFactor)
        {
            Runtime = runtime;
            HistoryMaxCount = historyMaxCount;
            NewValueFactor = newValueFactor;

            invMaxRuntimePercent = 6;
            invMaxInstCountPercent = 100.0 / Runtime.MaxInstructionCount;
        }

        public void Clear()
        {
            AverageRuntime = 0;
            HistoryRuntime.Clear();
            PeakRuntime = 0;

            AverageComplexity = 0;
            HistoryComplexity.Clear();
            PeakComplexity = 0;
        }

        public void UpdateRuntime()
        {
            double runtime = Runtime.LastRunTimeMs;
            AverageRuntime += (runtime - AverageRuntime) * NewValueFactor;

            HistoryRuntime.Enqueue(runtime);
            if (HistoryRuntime.Count > HistoryMaxCount) 
                HistoryRuntime.Dequeue();
            PeakRuntime = HistoryRuntime.Max();
        }

        public void UpdateComplexity()
        {
            double complexity = Runtime.CurrentInstructionCount;
            AverageComplexity += (complexity - AverageComplexity) * NewValueFactor;

            HistoryComplexity.Enqueue(complexity);
            if (HistoryComplexity.Count > HistoryMaxCount) HistoryComplexity.Dequeue();
            PeakComplexity = HistoryComplexity.Max();
        }

        public void PrintPerformance(StringBuilder sb)
        {
            sb.AppendLine($"Avg Runtime = {AverageRuntime:0.0000}ms   ({AverageRuntime * invMaxRuntimePercent:0.00}%)");
            sb.AppendLine($"Peak Runtime = {PeakRuntime:0.0000}ms\n");
            sb.AppendLine($"Avg Complexity = {AverageComplexity:0.00}   ({AverageComplexity * invMaxInstCountPercent:0.00}%)");
            sb.AppendLine($"Peak Complexity = {PeakComplexity:0.00}");
        }

        public void StartSectionWatch(string section)
        {
            SectionValues sectionValues;
 
            if (!AverageBreakdown.TryGetValue(section, out sectionValues))
            {
                sectionValues = new SectionValues();
                AverageBreakdown[section] = sectionValues;
            }

            sectionValues.StartTicks = DateTime.Now.Ticks;
        }

        public void StopSectionWatch(string section)
        {
            long current = DateTime.Now.Ticks;

            SectionValues sectionValues;
            if (AverageBreakdown.TryGetValue(section, out sectionValues))
            {
                double runtime = (current - sectionValues.StartTicks) * 0.0001;

                sectionValues.AccumulatedCount++;
                if (sectionValues.AccumulatedCount > sectionValues.MaxRuntimeCount + 1000) sectionValues.MaxRuntime = 0;
                sectionValues.AccumulatedRuntime += runtime;
                sectionValues.StartTicks = current;
                if (sectionValues.MaxRuntime < runtime)
                {
                    sectionValues.MaxRuntime = runtime;
                    sectionValues.MaxRuntimeCount = sectionValues.AccumulatedCount;
                }
            }
        }

        public void PrintSectionBreakdown(StringBuilder sb)
        {
            foreach (KeyValuePair<string, SectionValues> entry in AverageBreakdown)
            {
                double runtime = entry.Value.AccumulatedRuntime/ entry.Value.AccumulatedCount;
                sb.AppendLine($"{entry.Key} = {runtime:0.0000}ms");
            }
        }

        public class SectionValues
        {
            public long AccumulatedCount;
            public double AccumulatedRuntime;
            public double MaxRuntime;
            public long StartTicks;
            public long MaxRuntimeCount;
        }
    }

}
