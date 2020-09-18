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
    public static class AttackHelpers
    {
        //https://stackoverflow.com/questions/17204513/how-to-find-the-interception-coordinates-of-a-moving-target-in-3d-space
        public static Vector3D GetAttackPoint(Vector3D relativeVelocity, Vector3D relativePosition, double projectileSpeed)
        {
            if (relativeVelocity == Vector3D.Zero) return relativePosition;

            var s1 = projectileSpeed;

            var P0 = relativePosition;
            var V0 = relativeVelocity;

            var a = V0.Dot(V0) - (s1 * s1);
            var b = 2 * P0.Dot(V0);
            var c = P0.Dot(P0);

            double det = (b * b) - (4 * a * c);

            if (det < 0 || a == 0) return Vector3D.Zero;

            var t1 = (-b + Math.Sqrt(det)) / (2 * a);
            var t2 = (-b - Math.Sqrt(det)) / (2 * a);

            if (t1 <= 0) t1 = double.MaxValue;
            if (t2 <= 0) t2 = double.MaxValue;

            var t = Math.Min(t1, t2);

            if (t == double.MaxValue) return Vector3D.Zero;

            return relativePosition + relativeVelocity * t;
        }
    }

    public static class InventoryHelpers
    {
        /// <param name="source">The source inventory</param>
        /// <param name="target">The target inventory</param>
        /// <param name="item">The item to be transfered</param>
        /// <param name="amount">The maximum amount to transfer</param>
        /// <returns>The amount that still needs to be transfered</returns>
        public static MyFixedPoint TransferAsMuchAsPossible(IMyInventory source, IMyInventory target, MyInventoryItem item, MyFixedPoint amount)
        {
            var remainingVolume = target.MaxVolume - target.CurrentVolume;
            MyItemInfo itemInfo = item.Type.GetItemInfo();

            // If at least 1% volume left
            float minEmptyVol = 0.01f;

            if (remainingVolume > target.MaxVolume * minEmptyVol)
            {
                var transferAmt = MyFixedPoint.Min(item.Amount, amount);
                var totalVolume = transferAmt * itemInfo.Volume;

                if (totalVolume > remainingVolume)
                    transferAmt = remainingVolume * (1f / itemInfo.Volume);

                if (!itemInfo.UsesFractions)
                    transferAmt = MyFixedPoint.Floor(transferAmt);

                if (source.TransferItemTo(target, item, transferAmt))
                    amount -= transferAmt;
            }

            return amount;
        }
    }
}
