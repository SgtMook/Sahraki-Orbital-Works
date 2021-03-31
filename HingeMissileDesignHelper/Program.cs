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
            public IMyTerminalBlock Base; // Merge OR Sprue Hinge 

            public ProxyTube(MyGridProgram context)
            {
                Context = context;
                DummyTube = new TorpedoTube(1, Context, new TorpedoSubsystem(null));
                DummyTube.LoadedTorpedo = new Torpedo();
            }
            public bool CollectParts(IMyTerminalBlock block)
            {
                IMyMechanicalConnectionBlock mech = Base as IMyMechanicalConnectionBlock;
                if (mech != null )
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
                        Context.Me.GetSurface(0).WriteText("ERR NO MECH TOP");
                        
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

                if (block is IMyShipMergeBlock && block.CustomName.Contains("<BASE>")) 
                    Base = (IMyShipMergeBlock)block;

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

        List<IMyMechanicalConnectionBlock > Mechanicals = new List<IMyMechanicalConnectionBlock>();
        List<ProxyTube> ProxyTubes = null;

        public Program()
        {
            Runtime.UpdateFrequency = UpdateFrequency.None;
        }

        public void Main(string argument, UpdateType updateSource)
        {
            GridTerminalSystem.GetBlocksOfType<IMyMechanicalConnectionBlock>(Mechanicals, t => t.CubeGrid == Me.CubeGrid);

            if ( Mechanicals.Count > 0 )
            {
                ProxyTubes = new List<ProxyTube>(Mechanicals.Count);
                for (int i = 0; i < Mechanicals.Count; ++i)
                {
                    ProxyTubes.Add(new ProxyTube(this));
                    ProxyTubes[i].Base = Mechanicals[i];
                }
            }
            else
            {
                ProxyTubes = new List<ProxyTube>(1);
                ProxyTubes.Add(new ProxyTube(this));
            }

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
                    Me.GetSurface(0).ContentType = ContentType.TEXT_AND_IMAGE;
                    Me.GetSurface(0).FontSize = 10;
                    Me.GetSurface(0).FontColor = Color.Green;
                    Me.GetSurface(0).WriteText("AOK");
                }
                else
                {
                    Me.GetSurface(0).ContentType = ContentType.TEXT_AND_IMAGE;
                    Me.GetSurface(0).FontSize = 10;
                    Me.GetSurface(0).FontColor = Color.Red;
                    Me.GetSurface(0).WriteText("ERR");
                }

                Echo(builder.ToString());
            }

            Mechanicals.Clear();
            ProxyTubes.Clear();

        }
        void GetParts(ProxyTube tube)
        {
            Func<IMyTerminalBlock, bool> CollectPartsForTube = block => tube.CollectParts(block);
            GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(null, CollectPartsForTube);
        }
    }
}
