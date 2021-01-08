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
        IMyCockpit Cockpit;
        AtmoDrive Drive;
        List<IMyTextSurface> Displays = new List<IMyTextSurface>();
        StringBuilder OutputBuilder = new StringBuilder();
        MatrixD lockMatrix;
        int runs = 0;

        public Program()
        {
            subsystemManager = new SubsystemManager(this, null, false);
            subsystemManager.OutputMode = OutputMode.Debug;
            Runtime.UpdateFrequency = UpdateFrequency.Update1;

            GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(null, CollectBlocks);

            if (Cockpit != null)
                Drive = new AtmoDrive(Cockpit, 5, Me);

            if (Drive != null)
            {
                Drive.MaxAngleDegrees = 1;
                subsystemManager.AddSubsystem("autopilot", Drive);
            }
            else
            {
                Runtime.UpdateFrequency = UpdateFrequency.None;
                Echo("CANNOT FIND COCKPIT TAGGED [SPINMINER]");
            }

            ParseConfigs();
        }

        private bool CollectBlocks(IMyTerminalBlock block)
        {
            if (Me.CubeGrid.EntityId != block.CubeGrid.EntityId)
                return false;

            if (block is IMyCockpit && block.CustomName.Contains("[SPINMINER]"))
            {
                Cockpit = (IMyCockpit)block;
                Displays.Add(Cockpit.GetSurface(0));
            }
            return false;
        }
        MyCommandLine commandLine = new MyCommandLine();

        SubsystemManager subsystemManager;

        public void Save()
        {
            string v = subsystemManager.SerializeManager();
            Storage = v;
        }

        int Mode = 0; // 0 = level, 1 = desc, 2 = asce

        float DescendSpeed = 0.5f;
        float AscendSpeed = 1.5f;
        float RotateSpeed = 1f;
        // [StickMiner]
        // DescendSpeed = 0.5
        // AscendSpeed = 1.5
        // RotateSpeed = 1
        void ParseConfigs()
        {
            MyIni Parser = new MyIni();
            MyIniParseResult result;
            if (!Parser.TryParse(Me.CustomData, out result))
                return;

            DescendSpeed = (float)Parser.Get("StickMiner", "DescendSpeed").ToDecimal((decimal)DescendSpeed);
            AscendSpeed = (float)Parser.Get("StickMiner", "AscendSpeed").ToDecimal((decimal)AscendSpeed);
            RotateSpeed = (float)Parser.Get("StickMiner", "RotateSpeed").ToDecimal((decimal)RotateSpeed);
        }

        public void Main(string argument, UpdateType updateSource)
        {
            subsystemManager.UpdateTime();

            if (argument == "descend")
            {
                Mode = 1;
                lockMatrix = Drive.Controller.WorldMatrix;
            }
            else if (argument == "ascend")
            {
                Mode = 2;
                lockMatrix = Drive.Controller.WorldMatrix;
            }
            else if (argument == "stop")
            {
                Mode = 0;
                lockMatrix = MatrixD.Zero;
                Drive.Clear();
            }
            else if (commandLine.TryParse(argument))
            {
                subsystemManager.Command(commandLine.Argument(0), commandLine.Argument(1), commandLine.ArgumentCount > 2 ? commandLine.Argument(2) : null);
            }
            else
            {
                try
                {
                    subsystemManager.Update(updateSource);

                    runs++;
                    if (runs % 5 == 0)
                    {
                        OutputBuilder.Clear();

                        OutputBuilder.Append($"STATUS: ").AppendLine(Mode == 0 ? "STOP" : (Mode == 1 ? "DESC" : "ASCE"));

                        foreach (var screen in Displays)
                        {
                            screen.ContentType = ContentType.TEXT_AND_IMAGE;
                            screen.WriteText(OutputBuilder.ToString());
                        }

                        var destination = new Waypoint();

                        if (Mode == 0)
                        {
                            Drive.Clear();
                        }
                        else if (Mode == 1)
                        {
                            destination.MaxSpeed = DescendSpeed;
                            var gravdir = Cockpit.GetNaturalGravity();
                            if (gravdir == Vector3D.Zero)
                            {
                                gravdir.Normalize();

                                destination.Position = gravdir * 10 + Cockpit.GetPosition();

                                var flatForward = Cockpit.WorldMatrix.Forward - VectorHelpers.VectorProjection(Cockpit.WorldMatrix.Forward, gravdir);
                                flatForward.Normalize();
                                var flatLeft = Cockpit.WorldMatrix.Left - VectorHelpers.VectorProjection(Cockpit.WorldMatrix.Forward, gravdir);
                                flatLeft.Normalize();

                                destination.Direction = flatForward * TrigHelpers.FastCos(0.02 * RotateSpeed) + flatLeft * TrigHelpers.FastSin(0.02 * RotateSpeed);
                            }
                            else
                            {
                                destination.Position = lockMatrix.Down * 10 + Cockpit.GetPosition();
                                destination.DirectionUp = lockMatrix.Up;

                                var flatForward = Cockpit.WorldMatrix.Forward - VectorHelpers.VectorProjection(Cockpit.WorldMatrix.Forward, lockMatrix.Down);
                                flatForward.Normalize();
                                var flatLeft = Cockpit.WorldMatrix.Left - VectorHelpers.VectorProjection(Cockpit.WorldMatrix.Forward, lockMatrix.Down);
                                flatLeft.Normalize();

                                destination.Direction = flatForward * TrigHelpers.FastCos(0.02 * RotateSpeed) + flatLeft * TrigHelpers.FastSin(0.02 * RotateSpeed);
                            }
                        }
                        else if (Mode == 2)
                        {
                            destination.MaxSpeed = AscendSpeed;
                            if (Drive.Controller == Cockpit)
                            {
                                var gravdir = Cockpit.GetNaturalGravity();
                                gravdir.Normalize();

                                destination.Position = -gravdir * 10 + Cockpit.GetPosition();
                            }
                            else
                            {
                                destination.Position = lockMatrix.Down * 10 + Cockpit.GetPosition();
                                destination.Direction = lockMatrix.Forward;
                            }
                        }

                        Drive.Move(destination.Position);
                        Drive.Turn(destination.Direction);
                        Drive.Spin(destination.DirectionUp);
                        Drive.SetMaxSpeed(destination.MaxSpeed);
                    }
                }
                catch (Exception e)
                {
                    Me.GetSurface(0).WriteText(e.Message);
                    Me.GetSurface(0).WriteText("\n", true);
                    Me.GetSurface(0).WriteText(e.StackTrace);
                    Me.GetSurface(0).WriteText("\n", true);
                    Me.GetSurface(0).WriteText(e.ToString());
                }
                var s = subsystemManager.GetStatus();
                if (!string.IsNullOrEmpty(s)) Echo(s);
                else Echo(((int)subsystemManager.OutputMode).ToString());
            }
        }
    }
}
