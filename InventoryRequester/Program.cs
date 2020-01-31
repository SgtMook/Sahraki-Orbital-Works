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
    partial class Program : MyGridProgram
    {
        // This file contains your actual script.
        //
        // You can either keep all your code here, or you can create separate
        // code files to make your program easier to navigate while coding.
        //
        // In order to add a new utility class, right-click on your project, 
        // select 'New' then 'Add Item...'. Now find the 'Space Engineers'
        // category under 'Visual C# Items' on the left hand side, and select
        // 'Utility Class' in the main area. Name it in the box below, and
        // press OK. This utility class will be merged in with your code when
        // deploying your final script.
        //
        // You can also simply create a new utility class manually, you don't
        // have to use the template if you don't want to. Just do so the first
        // time to see what a utility class looks like.
        // 
        // Go to:
        // https://github.com/malware-dev/MDK-SE/wiki/Quick-Introduction-to-Space-Engineers-Ingame-Scripts
        //
        // to learn more about ingame scripts.

        public Program()
        {
            // The constructor, called only once every session and
            // always before any other method is called. Use it to
            // initialize your script. 
            //     
            // The constructor is optional and can be removed if not
            // needed.
            // 
            // It's recommended to set Runtime.UpdateFrequency 
            // here, which will allow your script to run itself without a 
            // timer block.
        }

        public void Save()
        {
            // Called when the program needs to save its state. Use
            // this method to save your state to the Storage field
            // or some other means. 
            // 
            // This method is optional and can be removed if not
            // needed.
        }

        public void Main(string argument, UpdateType updateSource)
        {
            // The main entry point of the script, invoked every time
            // one of the programmable block's Run actions are invoked,
            // or the script updates itself. The updateSource argument
            // describes where the update came from. Be aware that the
            // updateSource is a  bitfield  and might contain more than 
            // one update type.
            // 
            // The method itself is required, but the arguments above
            // can be removed if not needed.
        }

        // Helper functions
        bool AutoputItem(MyInventoryItem item, IMyInventory source)
        {
            var targetFlag = oreFlag;
            var itemInfo = item.Type.GetItemInfo();
            var runningAmt = item.Amount;

            if (itemInfo.IsIngot) targetFlag = ingotFlag;
            else if (itemInfo.IsComponent) targetFlag = compFlag;
            else if (itemInfo.IsTool) targetFlag = toolFlag;
            else if (itemInfo.IsAmmo) targetFlag = ammoFlag;

            if (flaggedContainers[targetFlag].Contains(source.Owner)) return true;

            var enumerator = flaggedContainers[targetFlag].GetEnumerator();
            while (enumerator.MoveNext())
            {
                var current = enumerator.Current;
                IMyInventory inv = current.GetInventory();

                var remainingVolume = inv.MaxVolume - inv.CurrentVolume;

                // If at least 1% volume left
                if (source.CanTransferItemTo(inv, item.Type) && remainingVolume > inv.MaxVolume * 0.01f)
                {
                    var totalVolume = runningAmt * itemInfo.Volume;
                    var transferAmt = runningAmt;

                    if (totalVolume > remainingVolume)
                        transferAmt = remainingVolume * (1f / itemInfo.Volume);

                    if (!itemInfo.UsesFractions)
                        transferAmt = MyFixedPoint.Floor(transferAmt);

                    if (source.TransferItemTo(inv, item, transferAmt))
                        runningAmt -= transferAmt;

                    if (runningAmt == 0)
                        return true;
                }
            }
            return false;
        }
    }
}
