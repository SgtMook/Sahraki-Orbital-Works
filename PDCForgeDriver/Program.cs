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
        public Program()
        {
            Runtime.UpdateFrequency = UpdateFrequency.Update100;

            GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(null, CollectParts);

            StatusBuilder.Clear();
            var AOK = true;

            if (SmallHinge == null)
            {
                AOK = false;
                StatusBuilder.AppendLine("SMALL HINGE NOT FOUND!");
            }
            if (LargeHinge == null)
            {
                AOK = false;
                StatusBuilder.AppendLine("LARGE HINGE NOT FOUND!");
            }
            if (Sweeper == null)
            {
                AOK = false;
                StatusBuilder.AppendLine("SWEEPER NOT FOUND!");
            }
            if (BaseProjector == null)
            {
                AOK = false;
                StatusBuilder.AppendLine("BASE PROJECTOR NOT FOUND!");
            }
            if (TopProjector == null)
            {
                AOK = false;
                StatusBuilder.AppendLine("TOP PROJECTOR NOT FOUND!");
            }
            if (TopMerge == null)
            {
                AOK = false;
                StatusBuilder.AppendLine("TOP MERGE NOT FOUND!");
            }
            if (BaseMerge == null)
            {
                AOK = false;
                StatusBuilder.AppendLine("BASE MERGE NOT FOUND!");
            }

            if (Welders.Count < 5)
            {
                AOK = false;
                StatusBuilder.AppendLine("NOT ENOUGH WELDERS!");
            }

            if (Display != null)
            {
                Display.ContentType = ContentType.TEXT_AND_IMAGE;
                Display.WriteText(StatusBuilder.ToString());
            }

            if (!AOK)
            {
                Echo(StatusBuilder.ToString());
                Runtime.UpdateFrequency = UpdateFrequency.None;
            }
            else
            {
                var baseOtherMerge = GridTerminalHelper.OtherMergeBlock(BaseMerge);
                var topOtherMerge = GridTerminalHelper.OtherMergeBlock(TopMerge);
                if (topOtherMerge != null && baseOtherMerge == null) State = 4;
            }

            ReleaseListener = IGC.RegisterBroadcastListener("[PDCFORGE]");
            ReleaseListener.SetMessageCallback("release");
        }

        StringBuilder StatusBuilder = new StringBuilder();

        public void Save()
        {
        }

        IMyMotorAdvancedStator SmallHinge;
        IMyMotorAdvancedStator LargeHinge;
        IMyMotorStator Sweeper;

        List<IMyShipWelder> Welders = new List<IMyShipWelder>();

        IMyProjector BaseProjector;
        IMyProjector TopProjector;

        IMyTextPanel Display;

        IMyShipMergeBlock TopMerge;
        IMyShipMergeBlock BaseMerge;

        IMyMotorAdvancedStator Elevation;
        List<IMyMotorAdvancedStator> ElevationScratchpad = new List<IMyMotorAdvancedStator>();

        IMyBroadcastListener ReleaseListener;

        int State = 7;

        int GraceTimer = 10;

        public void Main(string argument, UpdateType updateSource)
        {
            if (argument == "releaseremote")
            {
                IGC.SendBroadcastMessage("[PDCFORGE]", "");
            }
            else if (argument == "save")
            {
                if (Elevation != null && BaseMerge != null)
                {
                    var list = new List<IMyTerminalBlock>();
                    list.Add(Elevation);
                    BaseMerge.CustomData = GridTerminalHelper.BlockListBytePosToBase64(list, BaseMerge);
                }
            }
            else if(argument == "release")
            {
                while (ReleaseListener.HasPendingMessage)
                {
                    var msg = ReleaseListener.AcceptMessage();
                    if (msg.Data is string && Me.CustomName.Contains((string)msg.Data))
                    {
                        var otherMerge = GridTerminalHelper.OtherMergeBlock(TopMerge);
                        if (otherMerge != null)
                        {
                            otherMerge.Enabled = false;
                        }
                    }
                }
            }
            else
            {
                if (State == 0)
                {
                    foreach (var welder in Welders) welder.Enabled = true;

                    if (Display != null) Display.WriteText("STATE 0: WELDING - WAITING FOR COMPLETION");

                    if (TopProjector.RemainingBlocks == 0 && BaseProjector.RemainingBlocks == 0)
                    {
                        GraceTimer = 5;
                        State = 1;
                    }
                }
                else if (State == 1)
                {
                    GraceTimer--;

                    if (Display != null) Display.WriteText($"STATE 1: WELDING GRACE PERIOD - {GraceTimer}");

                    if (GraceTimer <= 0)
                    {
                        foreach (var welder in Welders) welder.Enabled = false;
                        BaseProjector.Enabled = false;
                        TopProjector.Enabled = false;

                        State = 2;
                    }
                }
                else if (State == 2)
                {
                    ElevationScratchpad.Clear();
                    var block = GridTerminalHelper.Base64BytePosToBlockList(BaseMerge.CustomData, BaseMerge, ref ElevationScratchpad);

                    if (ElevationScratchpad.Count == 1)
                    {
                        Elevation = ElevationScratchpad[0];

                        if (Display != null) Display.WriteText($"ELEVATION ROTOR FOUND", true);

                        Elevation.Detach();

                        var degrees = -90f * Math.PI / 180f;
                        DriveHinge(SmallHinge, (float)(degrees), 0.3f);
                        DriveHinge(LargeHinge, (float)(degrees), 1f);
                        Sweeper.TargetVelocityRPM = -10;

                        if (Display != null) Display.WriteText($"STATE 2: SWINGING HINGES SMALL - {SmallHinge.Angle * 180 / Math.PI} / -90 | LARGE - {LargeHinge.Angle * 180 / Math.PI} / -90");

                        if (Math.Abs(SmallHinge.Angle - degrees) < 0.01 && Math.Abs(LargeHinge.Angle - degrees) < 0.01)
                        {
                            State = 3;
                        }
                    }
                    else
                    {
                        if (Display != null) Display.WriteText($"\nCRITICAL ERROR: ELEVATION ROTOR NOT FOUND", true);
                    }
                }
                else if (State == 3)
                {
                    if (Display != null) Display.WriteText($"STATE 3: ATTEMPTING ATTACH");

                    ElevationScratchpad.Clear();
                    var block = GridTerminalHelper.Base64BytePosToBlockList(BaseMerge.CustomData, BaseMerge, ref ElevationScratchpad);

                    if (ElevationScratchpad.Count == 1)
                    {
                        Elevation = ElevationScratchpad[0];

                        if (Display != null) Display.WriteText($"ELEVATION ROTOR FOUND", true);

                        Elevation.Attach();

                        if (Elevation.IsAttached)
                        {
                            GridTerminalHelper.OtherMergeBlock(BaseMerge).Enabled = false;
                            State = 4;
                        }
                    }
                    else
                    {
                        if (Display != null) Display.WriteText($"\nCRITICAL ERROR: ELEVATION ROTOR NOT FOUND", true);
                    }
                }
                else if (State == 4)
                {
                    var degrees = 90f * Math.PI / 180f;
                    DriveHinge(SmallHinge, (float)(degrees), 0.3f);

                    if (Display != null) Display.WriteText($"STATE 4: SWINGING SMALL HINGE - {SmallHinge.Angle * 180 / Math.PI} / 90");

                    if (Math.Abs(SmallHinge.Angle - degrees) < 0.01)
                    {
                        State = 5;
                    }
                }
                else if (State == 5)
                {
                    if (Display != null) Display.WriteText($"STATE 5: COMPLETE - AWAITING PICKUP");
                    if (GridTerminalHelper.OtherMergeBlock(TopMerge) == null)
                    {
                        State = 6;
                        GraceTimer = 5;
                    }
                }
                else if (State == 6)
                {
                    GraceTimer--;

                    if (Display != null) Display.WriteText($"STATE 6: DETACH GRACE PERIOD - {GraceTimer}");

                    if (GraceTimer <= 0)
                    {
                        State = 7;
                    }
                }
                else if (State == 7)
                {
                    var degrees = 90f * Math.PI / 180f;
                    var degrees2 = 0;
                    DriveHinge(LargeHinge, (float)(degrees), 1f);
                    DriveHinge(SmallHinge, (float)(degrees2), 1f);
                    Sweeper.TargetVelocityRPM = 10;

                    BaseProjector.Enabled = true;
                    TopProjector.Enabled = true;

                    if (Display != null) Display.WriteText($"STATE 7: RESETTING SMALL - {SmallHinge.Angle * 180 / Math.PI} / 0 | LARGE - {LargeHinge.Angle * 180 / Math.PI} / 90");

                    if (Math.Abs(SmallHinge.Angle - degrees2) < 0.01 && Math.Abs(LargeHinge.Angle - degrees) < 0.01)
                    {
                        State = 0;
                    }
                }
            }
        }

        bool CollectParts(IMyTerminalBlock block)
        {
            if (!block.IsSameConstructAs(Me)) return false;
            //if (!block.CustomName.Contains(Me.CustomName.Last())) return false;
            if (block is IMyMotorAdvancedStator && block.CustomName.Contains("Small Hinge")) SmallHinge = (IMyMotorAdvancedStator)block;
            if (block is IMyMotorAdvancedStator && block.CustomName.Contains("Large Hinge")) LargeHinge = (IMyMotorAdvancedStator)block;
            if (block is IMyMotorAdvancedStator && block.CustomName.Contains("Elevation")) Elevation = (IMyMotorAdvancedStator)block;
            if (block is IMyMotorStator && block.CustomName.Contains("Sweeper")) Sweeper = (IMyMotorStator)block;

            if (block is IMyProjector && block.CustomName.Contains("Base Projector")) BaseProjector = (IMyProjector)block;
            if (block is IMyProjector && block.CustomName.Contains("Top Projector")) TopProjector = (IMyProjector)block;

            if (block is IMyShipMergeBlock && block.CustomName.Contains("Top Release")) TopMerge = (IMyShipMergeBlock)block;
            if (block is IMyShipMergeBlock && block.CustomName.Contains("Base Release")) BaseMerge = (IMyShipMergeBlock)block;

            if (block is IMyShipWelder) Welders.Add((IMyShipWelder)block);
            if (block is IMyTextPanel) Display = (IMyTextPanel)block;

            return false;
        }

        static public void DriveHinge(IMyMotorStator hinge, float angleRad, float Velocity, float minApproach = 0.0f)
        {
            if (hinge.Angle == angleRad)
            {
                hinge.UpperLimitRad = angleRad;
                hinge.LowerLimitRad = angleRad;
                hinge.RotorLock = true;
                hinge.TargetVelocityRad = 0.0f;

                return;
            }

            hinge.RotorLock = false;
            float delta = angleRad - hinge.Angle;
            float scalar = Math.Max(Math.Min(Math.Abs(delta) / 0.3f, 1.0f), minApproach);
            Velocity *= scalar;

            if (hinge.Angle < angleRad)
            {
                hinge.UpperLimitRad = angleRad;
                hinge.LowerLimitRad = hinge.Angle;
                hinge.TargetVelocityRad = Velocity;
            }
            else
            {
                hinge.LowerLimitRad = angleRad;
                hinge.UpperLimitRad = hinge.Angle;
                hinge.TargetVelocityRad = -Velocity;
            }
        }
    }
}
