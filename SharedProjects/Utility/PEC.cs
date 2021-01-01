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
    // Periodic error compensator
    public struct PEC
    {
        public Vector3D[] records;
        int PeriodSegments;
        int Period;
        float Power;
        public PEC(int periodSegments, float power = 1f)
        {
            PeriodSegments = periodSegments;
            records = new Vector3D[periodSegments];
            Period = 0;
            Power = power;
        }

        public Vector3D Adjust(Vector3D error)
        {
            records[Period] = records[Period] * 0.8 + error;
            Period = (Period + 1) % PeriodSegments;
            return error + records[Period % PeriodSegments] * Power;
        }

        public void Clear()
        {
            records = new Vector3D[records.Count()];
        }
    }
}
