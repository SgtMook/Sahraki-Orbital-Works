using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using VRage;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.Game.ObjectBuilders.Definitions;
using VRageMath;

namespace IngameScript
{
    partial class Program : MyGridProgram
    {
        // Use this by placing it on the sprue containing the micro missile release hinges.
        // Hinge Top Parts do not have custom data, so the data must be stored sprue side.

        class ProxyTube
        {
            MyGridProgram Context;
            public TorpedoTube DummyTube;
            public List<IMyTerminalBlock> PartsOfInterest = new List<IMyTerminalBlock>();
            public IMyTerminalBlock Base; // Merge OR Sprue Hinge OR Thruster

            public ProxyTube(MyGridProgram context)
            {
                Context = context;
                DummyTube = new TorpedoTube(1, new TorpedoSubsystem(null));
                DummyTube.LoadedTorpedo = new Torpedo();
            }
            public bool CollectParts(IMyTerminalBlock block)
            {
                IMyMechanicalConnectionBlock mech = Base as IMyMechanicalConnectionBlock;
                if (mech != null)
                {
                    if (mech.TopGrid != null)
                    {
                        if (block.CubeGrid != mech.TopGrid)
                        {
                            return false;
                        }
                    }
                    else
                    {
                        Context.Me.GetSurface(0).ContentType = ContentType.TEXT_AND_IMAGE;
                        Context.Me.GetSurface(0).FontSize = 10;
                        Context.Me.GetSurface(0).FontColor = Color.Red;
                        Context.Me.GetSurface(0).WriteText("ERR NO MECH TOP" + mech.CustomName);

                        return false;
                    }
                }

                if (!Context.Me.IsSameConstructAs(block))
                    return false;

                if (DummyTube.AddTorpedoPart(block))
                {
                    PartsOfInterest.Add(block);
                }

                if (block is IMyRadioAntenna)
                    PartsOfInterest.Add(block);

                if (block.CustomName.Contains("<BASE>") &&
                     (block is IMyShipMergeBlock || block is IMyThrust ))
                {
                    Base = block;
                }

                return false;
            }

            private bool CheckTorpedo(Torpedo torpedo, out string output)
            {
                var OK = true;

                StringBuilder builder = new StringBuilder();

                builder.AppendLine("======== ERRORS ========");

                if (torpedo.Controller == null) builder.AppendLine("=> NO REMOTE CONTROL!");
                if (torpedo.Thrusters.Count == 0) builder.AppendLine("=> NO THRUSTERS!");
                if (torpedo.SubTorpedos.Count > torpedo.Splitters.Count) builder.AppendLine("=> CANNOT SEPARATE CLUSTER!");
                if (Base == null)
                {
                    builder.AppendLine("=> BASE MISSING!");
                    OK = false;
                }

                builder.AppendLine("======== WARNINGS ========");

                if (torpedo.Cameras.Count == 0) builder.AppendLine("=> No camera.");
                if (torpedo.Sensor == null) builder.AppendLine("=> No sensor.");
                if (torpedo.Fuse == null) builder.AppendLine("=> No fuse.");
                if (torpedo.Warheads == null) builder.AppendLine("=> No warheads (OK for kinetic or trainer).");

                foreach (var torp in torpedo.SubTorpedos)
                {
                    builder.AppendLine();
                    builder.AppendLine($"Cluster Missile {torp.Tag}");
                    string suboutput;
                    OK &= CheckTorpedo(torp, out suboutput);
                    builder.Append(suboutput);
                }

                output = builder.ToString();
                return torpedo.OK() && OK;
            }

            public bool CheckTorpedo(out string output)
            {
                return CheckTorpedo(DummyTube.LoadedTorpedo, out output);
            }

            public void SaveTorpedo()
            {
                IMyMechanicalConnectionBlock mech = Base as IMyMechanicalConnectionBlock;
                if ( mech != null )
                    Base.CustomData = GridTerminalHelper.BlockListBytePosToBase64(PartsOfInterest, mech.Top);
                else
                    Base.CustomData = GridTerminalHelper.BlockListBytePosToBase64(PartsOfInterest, Base);
            }
        }

        
        List<ProxyTube> ProxyTubes = null;

        public Program()
        {
            Runtime.UpdateFrequency = UpdateFrequency.None;
        }



        public void Info(string echoInfo)
        {
            Output(false, echoInfo);
        }
        public void Error(string echoInfo)
        {
            Output(true, echoInfo);
        }
        public void Output(bool error, string echoInfo)
        {
            Me.GetSurface(0).ContentType = ContentType.TEXT_AND_IMAGE;
            Me.GetSurface(0).FontSize = 10;
            Me.GetSurface(0).FontColor = error ? Color.Red : Color.Green;

            if ( error )
                Me.GetSurface(0).WriteText("ERR");
            else
                Me.GetSurface(0).WriteText("AOK");

            Echo(echoInfo);
        }

        public void BuildProxyTubes()
        {
            int ThrusterDetach = 0;
            List<IMyThrust> Thrusters = new List<IMyThrust>();
            GridTerminalSystem.GetBlocksOfType<IMyThrust>(Thrusters, t => t.CubeGrid == Me.CubeGrid);
            foreach( var thruster in Thrusters)
            {
                if (thruster.CustomName.Contains("[TRP"))
                    ThrusterDetach++;
            }
            if (ThrusterDetach > 1)
            {
                Error("Multiple Detach Thrusters Detected\nProbably wanted THRUSTERDETACH argument.");
                return;
            }

            List<IMyMechanicalConnectionBlock> Mechanicals = new List<IMyMechanicalConnectionBlock>();
            GridTerminalSystem.GetBlocksOfType<IMyMechanicalConnectionBlock>(Mechanicals, t => t.CubeGrid == Me.CubeGrid);
            if (Mechanicals.Count > 0)
            {
                ProxyTubes = new List<ProxyTube>(Mechanicals.Count);
                for (int i = 0; i < Mechanicals.Count; ++i)
                {
                    ProxyTubes.Add(new ProxyTube(this));
                    ProxyTubes[i].Base = Mechanicals[i];
                }
                return;
            }
            else
            {
                ProxyTubes = new List<ProxyTube>(1);
                ProxyTubes.Add(new ProxyTube(this));
            }
        }

        public void Main(string argument, UpdateType updateSource)
        {
            if (argument == "THRUSTERDETACH")
            {
                IMyTerminalBlock ThrusterControl = null;
                List<MyTuple<IMyThrust, int>> DetachThrusters = new List<MyTuple<IMyThrust, int>>();

                List<IMyTerminalBlock> blocks = new List<IMyTerminalBlock>();
                GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(blocks, t => t.CubeGrid == Me.CubeGrid);

                foreach ( var block in blocks )
                {
                    if (block.CustomName.Contains("[TRPT]"))
                        ThrusterControl = block;

                    IMyThrust thrust = block as IMyThrust;
                    if ( thrust != null)
                    {
                        var tagindex = block.CustomName.IndexOf("[TRP");
                        if (tagindex == -1)
                            continue;

                        var indexTagEnd = block.CustomName.IndexOf(']', tagindex);
                        if (indexTagEnd == -1)
                            continue;

                        int index;
                        var numString = block.CustomName.Substring(tagindex + 4, indexTagEnd - tagindex - 4);
                        if (!int.TryParse(numString, out index))
                            continue;

                        DetachThrusters.Add(MyTuple.Create(thrust, index));
                    }
                }

                if ( ThrusterControl == null )
                {
                    Error("No [TRPT] tagged block for ThrusterDetachControl");
                    return;
                }

                MyIni IniParser = new MyIni();
                IniParser.TryParse(ThrusterControl.CustomData);
                IniParser.DeleteSection("Torpedo");

                StringBuilder thrusterData = new StringBuilder();
                
                for( int i = 0; i < DetachThrusters.Count; ++i )
                {
                    var position = GridTerminalHelper.BlockBytePosToBase64(DetachThrusters[i].Item1, ThrusterControl);
                    thrusterData.Append(DetachThrusters[i].Item2 + "^" + position + ((i != DetachThrusters.Count-1) ? "," : ""));
                }

                IniParser.Set("Torpedo", "ThrusterReleases", thrusterData.ToString());
                ThrusterControl.CustomData = IniParser.ToString();

                Info("Thruster Control: " + thrusterData.ToString());
                return;
            }

            BuildProxyTubes();

            foreach ( var tube in ProxyTubes)
            {
                GetParts(tube);
            }

            if (argument == "LOAD")
            {
                List<IMyTerminalBlock> b = new List<IMyTerminalBlock>();
                for( int i = 0; i < ProxyTubes.Count; ++i)
                {
                    ProxyTube tube = ProxyTubes[i];
                    IMyMechanicalConnectionBlock mech = tube.Base as IMyMechanicalConnectionBlock;
                    if (mech != null)
                    {
                        GridTerminalHelper.Base64BytePosToBlockList(tube.Base.CustomData, mech.Top, ref b);
                    }
                    else
                    {
                        GridTerminalHelper.Base64BytePosToBlockList(tube.Base.CustomData, tube.Base, ref b);
                    }
                    
                    Echo("Tube" + i + ": " + b.Count().ToString());
                }
            }
            else
            {
                StringBuilder builder = new StringBuilder(256);
                bool aok = true;
                for (int i = 0; i < ProxyTubes.Count; ++i)
                {
                    ProxyTube tube = ProxyTubes[i];
                    string output;
                    if ( tube.CheckTorpedo(out output) )
                    {
                        tube.SaveTorpedo();
                        builder.AppendLine(i.ToString() + " [AOK] :");
                        builder.Append(output);
                    }
                    else
                    {
                        builder.AppendLine(i.ToString() + " [ERR] :");
                        builder.Append(output);
                        aok = false;
                        break;
                    }

                }
                if ( aok )
                {
                    Info(builder.ToString());
                }
                else
                {
                    Error(builder.ToString());
                }

            }
            ProxyTubes.Clear();

        }
        void GetParts(ProxyTube tube)
        {
            Func<IMyTerminalBlock, bool> CollectPartsForTube = block => tube.CollectParts(block);
            GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(null, CollectPartsForTube);
        }
    }
}
