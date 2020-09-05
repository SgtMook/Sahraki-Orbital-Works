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
    public struct PEC
    {
        public double[] records;
        int PeriodSegments;
        int Period;
        float Power;
        public PEC(int periodSegments, float power = 0.4f)
        {
            PeriodSegments = periodSegments;
            records = new double[periodSegments];
            Period = 0;
            Power = power;
        }

        public double Adjust(double error)
        {
            error += records[(Period + 1) % PeriodSegments] * Power;
            records[Period] = error;
            Period = (Period + 1) % PeriodSegments;
            return error;
        }
    }
}
