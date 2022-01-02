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

            // This is a massive hack. Keen fucked up when they made the Small Grid 1x1x1 merge and it is oriented so the connection
            // is Base6Directions.Direction.Up, not Base6Directions.Direction.Right like the previous merges. This uses the length
            // of the subtype name (24 characters) of the 1x1x1 small grid merge to do the exception, where as the older two are 19
            // characters. If you ever have a mod merge block that is 24 characters, you'll need to do something else here.
            Vector3I otherPosition = merge.Position + Base6Directions.GetIntVector(merge.Orientation.TransformDirection(
                merge.BlockDefinition.SubtypeName.Length == 24? Base6Directions.Direction.Up : Base6Directions.Direction.Right));
            
//            Vector3I vec1 = Base6Directions.GetIntVector(Base6Directions.GetOppositeDirection(merge.Orientation.Left));
//            Vector3I vec2 = merge.Position + vec1;
            IMyShipMergeBlock otherMerge = merge.CubeGrid.GetCubeBlock(otherPosition)?.FatBlock as IMyShipMergeBlock;
            if (otherMerge == merge) { return null; }
            return otherMerge;
        }

        public static string BlockListBytePosToBase64<T>(List<T> blocks, IMyCubeBlock origin) where T : class, IMyTerminalBlock
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

        public static string BlockBytePosToBase64(IMyCubeBlock block, IMyCubeBlock origin)
        {
            if (block == null)
            {
                return "";
            }
            else
            {
                Vector3I input = TransformGridPosToBlockPos(block.Position, origin);

                return input.X + "." + input.Y + "." + input.Z;
            }
        }

        public static Vector3I TransformGridPosToBlockPos(Vector3I blockPos, IMyCubeBlock origin)
        {
            Vector3I[] vec = { -Base6Directions.GetIntVector(origin.Orientation.Left), Base6Directions.GetIntVector(origin.Orientation.Up), -Base6Directions.GetIntVector(origin.Orientation.Forward) };
            Vector3I[] inv = { new Vector3I(vec[0].X, vec[1].X, vec[2].X), new Vector3I(vec[0].Y, vec[1].Y, vec[2].Y), new Vector3I(vec[0].Z, vec[1].Z, vec[2].Z) };

            Vector3I input = blockPos - origin.Position;
            input = (input.X * inv[0]) + (input.Y * inv[1]) + (input.Z * inv[2]);

            var result = (input.X * vec[0]) + (input.Y * vec[1]) + (input.Z * vec[2]) + origin.Position;
            return input;
        }

        public static bool Base64BytePosToBlockList<T>(string input, IMyCubeBlock origin, ref List<T> result) where T : class, IMyCubeBlock 
        {
            if (string.IsNullOrEmpty(input))
            {
                return false;
            }

            string[] values = input.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (string line in values)
            {
                if (string.IsNullOrEmpty(line)) continue;
                IMySlimBlock slim = origin.CubeGrid.GetCubeBlock(Base64ByteToVector3I(line, origin));
                if (slim != null && slim.IsFullIntegrity)
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

        public static Vector3I Base64ByteToVector3I(string input, IMyCubeBlock origin)
        {
            Vector3I result = new Vector3I();
            if (input != null)
            {
                var split = input.Split('.');

                int x, y, z;

                if (!int.TryParse(split[0], out x) ||
                    !int.TryParse(split[1], out y) ||
                    !int.TryParse(split[2], out z))
                {
                    throw new Exception($"Failed parsing {input}");
                }

                result.X = x;
                result.Y = y;
                result.Z = z;
                result = TransformBlockPosToGridPos(result, origin);
            }
            return result;
        }

        public static Vector3I TransformBlockPosToGridPos(Vector3I blockPos, IMyCubeBlock origin)
        {
            Vector3I[] vec = { -Base6Directions.GetIntVector(origin.Orientation.Left), Base6Directions.GetIntVector(origin.Orientation.Up), -Base6Directions.GetIntVector(origin.Orientation.Forward) };
            blockPos = (blockPos.X * vec[0]) + (blockPos.Y * vec[1]) + (blockPos.Z * vec[2]) + origin.Position;
            return blockPos;
        }
    }
}