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
    public class RoundRobin<T>
    {
        public List<T> Items;

        private int current;

        public RoundRobin(List<T> dispatchItems = null)
        {
            Items = dispatchItems;

            if (Items == null)
                Items = new List<T>();
        }


        public T GetAndAdvance()
        {
            if (current >= Items.Count)
            {
                current = 0;
                if ( Items.Count == 0 )
                    return default(T);
            }

            return Items[current++];
        }
    }
}