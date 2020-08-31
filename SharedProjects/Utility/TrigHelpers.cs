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
    public static class TrigHelpers
    {
        public static double FastAsin(double x)
        {
            return x * (1 + x * x * (0.1666666 + 0.075 * x * x));
        }

        public static double FastCos(double x)
        {
            if (x > Math.PI || x < -Math.PI) return Math.Cos(x);
            return 1 + x * x * (-0.5 + 0.04166666 * x * x);
        }

        public static double FastSin(double x)
        {
            if (x > Math.PI || x < -Math.PI) return Math.Sin(x);
            return x * (1 + x * x * (-0.1666666 + 0.00833333 * x * x));
        }

        public static double fastTan(double x)
        {
            if (x > Math.PI || x < -Math.PI) return Math.Tan(x);
            return FastSin(x) / FastCos(x);
        }
    }
}
