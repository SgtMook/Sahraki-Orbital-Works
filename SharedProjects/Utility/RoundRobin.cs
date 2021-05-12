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
        private List<T> items;
        private Func<T, bool> isReady;

        private int start;
        private int current;
        private bool available;

        public RoundRobin(List<T> dispatchItems, Func<T, bool> isReadyFunc = null)
        {
            items = dispatchItems;
            isReady = isReadyFunc;

            start = current = 0;
            available = false;

            if (items == null) items = new List<T>();
        }

        public void Reset()
        {
            start = current = 0;
        }

        public void Begin()
        {
            start = current;
            available = (items.Count > 0);
        }

        public T GetNext()
        {
            if (start >= items.Count) start = 0;
            if (current >= items.Count)
            {
                current = 0;
                available = (items.Count > 0);
            }

            T result = default(T);

            while (available)
            {
                T item = items[current++];

                if (current >= items.Count) current = 0;
                if (current == start) available = false;

                if (isReady == null || isReady(item))
                {
                    result = item;
                    break;
                }
            }

            return result;
        }

        public void ReloadList(List<T> dispatchItems)
        {
            items = dispatchItems;
            if (items == null) items = new List<T>();

            if (start >= items.Count) start = 0;
            if (current >= items.Count) current = 0;

            available = false;
        }
    }
}