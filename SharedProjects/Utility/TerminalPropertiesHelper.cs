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
    public static class TerminalPropertiesHelper
    {
        static Dictionary<string, ITerminalAction> _terminalActionDict = new Dictionary<string, ITerminalAction>();
        static Dictionary<string, ITerminalProperty> _terminalPropertyDict = new Dictionary<string, ITerminalProperty>();

        public static void ApplyAction(IMyTerminalBlock block, string actionName)
        {
            ITerminalAction act;
            if (_terminalActionDict.TryGetValue(actionName, out act))
            {
                act.Apply(block);
                return;
            }

            act = block.GetActionWithName(actionName);
            _terminalActionDict[actionName] = act;
            act.Apply(block);
        }

        public static void SetValue<T>(IMyTerminalBlock block, string propertyName, T value)
        {
            ITerminalProperty prop;
            if (_terminalPropertyDict.TryGetValue(propertyName, out prop))
            {
                prop.Cast<T>().SetValue(block, value);
                return;
            }

            prop = block.GetProperty(propertyName);
            _terminalPropertyDict[propertyName] = prop;
            prop.Cast<T>().SetValue(block, value);
        }

        public static T GetValue<T>(IMyTerminalBlock block, string propertyName)
        {
            ITerminalProperty prop;
            if (_terminalPropertyDict.TryGetValue(propertyName, out prop))
            {
                return prop.Cast<T>().GetValue(block);
            }

            prop = block.GetProperty(propertyName);
            _terminalPropertyDict[propertyName] = prop;
            return prop.Cast<T>().GetValue(block);
        }
    }
}
