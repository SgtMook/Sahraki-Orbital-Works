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
    public static class GridTerminalHelper
    {
        public static IMyTerminalBlock GetBlockFromReferenceAndPosition(IMyTerminalBlock reference, Vector3I position)
        {
            var matrix = new MatrixI(reference.Orientation);
            var pos = new Vector3I(-position.X, position.Y, -position.Z);
            Vector3I transformed;
            Vector3I.Transform(ref pos, ref matrix, out transformed);
            transformed += reference.Position;
            var slim = reference.CubeGrid.GetCubeBlock(transformed);
            return slim == null ? null : slim.FatBlock as IMyTerminalBlock;
        }

        public static IMyShipMergeBlock OtherMergeBlock(IMyShipMergeBlock merge)
        {
            if (merge == null) { return null; }
            Vector3I vec1 = Base6Directions.GetIntVector(Base6Directions.GetOppositeDirection(merge.Orientation.Left));
            Vector3I vec2 = merge.Position + vec1;
            IMyShipMergeBlock m2 = merge.CubeGrid.GetCubeBlock(vec2)?.FatBlock as IMyShipMergeBlock;
            if (m2 == merge) { return null; }
            return m2;
        }

        public static string BlockListBytePosToBase64<T>(List<T> blocks, IMyTerminalBlock origin) where T : class, IMyTerminalBlock
        {
            if (blocks == null || blocks.Count == 0)
            {
                return "";
            }
            else
            {
                StringBuilder sb = new StringBuilder(blocks.Count * 5);
                foreach (IMyTerminalBlock block in blocks)
                {
                    sb.Append(BlockBytePosToBase64(block, origin)).Append(',');
                }
                return sb.ToString();
            }
        }

        public static string BlockBytePosToBase64(IMyTerminalBlock block, IMyTerminalBlock origin)
        {
            if (block == null)
            {
                return "";
            }
            else
            {
                Vector3I[] vec = { -Base6Directions.GetIntVector(origin.Orientation.Left), Base6Directions.GetIntVector(origin.Orientation.Up), -Base6Directions.GetIntVector(origin.Orientation.Forward) };
                Vector3I[] inv = { new Vector3I(vec[0].X, vec[1].X, vec[2].X), new Vector3I(vec[0].Y, vec[1].Y, vec[2].Y), new Vector3I(vec[0].Z, vec[1].Z, vec[2].Z) };

                Vector3I input = block.Position - origin.Position;
                input = (input.X * inv[0]) + (input.Y * inv[1]) + (input.Z * inv[2]);

                var result = (input.X * vec[0]) + (input.Y * vec[1]) + (input.Z * vec[2]) + origin.Position;

                return input.X + "." + input.Y + "." + input.Z;
            }
        }

        public static bool Base64BytePosToBlockList<T>(string input, IMyTerminalBlock origin, ref List<T> result) where T : class, IMyTerminalBlock
        {
            if (input == null)
            {
                return true;
            }

            string[] values = input.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (string line in values)
            {
                IMySlimBlock slim = origin.CubeGrid.GetCubeBlock(Base64ByteToVector3I(line, origin));
                if (slim != null)
                {
                    T block = slim.FatBlock as T;
                    if (block != null)
                    {
                        result.Add(block);
                    }
                    else
                    {
                        return false;
                    }
                }
                else
                {
                    return false;
                }
            }

            return true;
        }

        public static Vector3I Base64ByteToVector3I(string input, IMyTerminalBlock origin)
        {
            Vector3I[] vec = { -Base6Directions.GetIntVector(origin.Orientation.Left), Base6Directions.GetIntVector(origin.Orientation.Up), -Base6Directions.GetIntVector(origin.Orientation.Forward) };
            Vector3I result = new Vector3I();
            if (input != null)
            {
                var split = input.Split('.');
                result.X = int.Parse(split[0]);
                result.Y = int.Parse(split[1]);
                result.Z = int.Parse(split[2]);
                result = (result.X * vec[0]) + (result.Y * vec[1]) + (result.Z * vec[2]) + origin.Position;
            }
            return result;
        }
    }

    public static class SensorBounderHelper
    {
        public static IEnumerable<IMyTerminalBlock> GetBlocksFromSensorBoundingBox(IMySensorBlock sensor)
        {
            var size = sensor.CubeGrid.GridSize;

            int xmin = -(int)Math.Ceiling((sensor.RightExtend - (size * 0.5)) / size);
            int xmax = (int)Math.Ceiling((sensor.LeftExtend - (size * 0.5)) / size);
            int ymin = -(int)Math.Ceiling((sensor.BottomExtend - (size * 0.5)) / size);
            int ymax = (int)Math.Ceiling((sensor.TopExtend - (size * 0.5)) / size);
            int zmin = -(int)Math.Ceiling(sensor.BackExtend / size);
            int zmax = (int)Math.Ceiling((sensor.FrontExtend - size) / size);

            for (int x = xmin; x <= xmax; x++)
            {
                for (int y = ymin; y <= ymax; y++)
                {
                    for (int z = zmin; z <= zmax; z++)
                    {
                        var part = GridTerminalHelper.GetBlockFromReferenceAndPosition(sensor, new Vector3I(x, y, z));
                        if (part != null)
                        {
                            yield return part;
                        }
                    }
                }
            }
        }
    }
}