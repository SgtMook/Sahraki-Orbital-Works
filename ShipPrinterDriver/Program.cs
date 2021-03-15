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
        // Starts at the layer in front of the PB itself
        int[] BlocksPerLayer;
        HashSet<IMySlimBlock> GotBlocks = new HashSet<IMySlimBlock>();

        public Program()
        {
            GetLayers();
            StatusBuilder.Clear();
            foreach (var x in BlocksPerLayer)
            {
                StatusBuilder.AppendLine(x.ToString());
            }

            Me.CustomData = StatusBuilder.ToString();
        }

        StringBuilder StatusBuilder = new StringBuilder();

        public void Save()
        {
        }

        public void Main(string argument, UpdateType updateSource)
        {
        }

        bool CollectParts(IMyTerminalBlock block)
        {
            return false;
        }

        void GetLayers()
        {
            //for (int i = Me.CubeGrid.Min.X; i <= Me.CubeGrid.Max.X; i++)
            //{
            //    for (int j = Me.CubeGrid.Min.Y; i <= Me.CubeGrid.Max.Y; i++)
            //    {
            //        for (int k = Me.CubeGrid.Min.Z; i <= Me.CubeGrid.Max.Z; k++)
            //        {
            //            var coord = new Vector3I(i, j, k);
            //            var blockCoord = GridTerminalHelper.TransformGridPosToBlockPos(coord, Me);
            //            var forwardLayer = Vector3I.Forward.Dot(ref coord);
            //            if (forwardLayer > 0)
            //            {
            //
            //            }
            //        }
            //    }
            //}


            var MyMax = GridTerminalHelper.TransformGridPosToBlockPos(Me.CubeGrid.Max, Me);
            var MyMin = GridTerminalHelper.TransformGridPosToBlockPos(Me.CubeGrid.Min, Me);

            int forwardMost = -Math.Min(MyMax.Dot(ref Vector3I.Forward), MyMin.Dot(ref Vector3I.Forward));

            BlocksPerLayer = new int[forwardMost + 1];

            for (int i = forwardMost; i > 0; i --)
            {
                var maxLeft = Math.Max(MyMax.Dot(ref Vector3I.Left), MyMin.Dot(ref Vector3I.Left));
                var minLeft = Math.Min(MyMax.Dot(ref Vector3I.Left), MyMin.Dot(ref Vector3I.Left));

                for (int j = maxLeft; j >= minLeft; j--)
                {
                    var maxUp = Math.Max(MyMax.Dot(ref Vector3I.Up), MyMin.Dot(ref Vector3I.Up));
                    var minUp = Math.Min(MyMax.Dot(ref Vector3I.Up), MyMin.Dot(ref Vector3I.Up));

                    for (int k = maxUp; k >= minUp; k--)
                    {
                        var localCoords = -i * Vector3I.Forward + j * Vector3I.Left + k * Vector3I.Up;
                        var globalPos = GridTerminalHelper.TransformBlockPosToGridPos(localCoords, Me);

                        var block = Me.CubeGrid.GetCubeBlock(globalPos);
                        if (!GotBlocks.Contains(block))
                        {
                            GotBlocks.Add(block);
                            BlocksPerLayer[i] += 1;
                        }
                    }
                }
            }
        }
    }
}
