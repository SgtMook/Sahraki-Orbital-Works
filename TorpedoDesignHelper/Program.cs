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
            Runtime.UpdateFrequency = UpdateFrequency.None;
        }

        public void Save()
        {
        }

        public void Main(string argument, UpdateType updateSource)
        {
            DummyTube = new TorpedoTube(1, this, new TorpedoSubsystem(null));
            DummyTube.LoadedTorpedo = new Torpedo();

            GetParts();

            if (argument == "LOAD")
            {
                List<IMyTerminalBlock> b = new List<IMyTerminalBlock>();
                GridTerminalHelper.Base64BytePosToBlockList(Base.CustomData, Base, ref b);
                Echo(b.Count().ToString());
            }
            else
            {
                string output;
                if (CheckTorpedo(DummyTube.LoadedTorpedo, out output))
                {
                    Me.GetSurface(0).ContentType = ContentType.TEXT_AND_IMAGE;
                    Me.GetSurface(0).FontSize = 10;
                    Me.GetSurface(0).FontColor = Color.Green;
                    Me.GetSurface(0).WriteText("AOK");
                    SaveTorpedo();
                }
                else
                {
                    Me.GetSurface(0).ContentType = ContentType.TEXT_AND_IMAGE;
                    Me.GetSurface(0).FontSize = 10;
                    Me.GetSurface(0).FontColor = Color.Red;
                    Me.GetSurface(0).WriteText("ERR");
                }

                Echo(output);
            }

            DummyTube = null;
            PartsOfInterest.Clear();
            Base = null;
        }

        TorpedoTube DummyTube;
        List<IMyTerminalBlock> PartsOfInterest = new List<IMyTerminalBlock>();
        IMyShipMergeBlock Base;

        void GetParts()
        {
            GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(null, CollectParts);
        }

        private bool CollectParts(IMyTerminalBlock block)
        {
            if (!Me.IsSameConstructAs(block)) return false;
            
            if (DummyTube.AddTorpedoPart(block))
            {
                PartsOfInterest.Add(block);
            }

            if (block is IMyShipMergeBlock && block.CustomName.Contains("<BASE>")) Base = (IMyShipMergeBlock)block;

            return false;
        }

        bool CheckTorpedo(Torpedo torpedo, out string output)
        {
            var OK = true;

            StringBuilder builder = new StringBuilder();

            builder.AppendLine("======== ERRORS ========");

            if (torpedo.Controller == null) builder.AppendLine("=> NO REMOTE CONTROL!");
            if (torpedo.Gyro == null) builder.AppendLine("=> NO GYROSCOPE!");
            if (torpedo.Thrusters.Count == 0) builder.AppendLine("=> NO THRUSTERS!");
            if (torpedo.SubTorpedos.Count > torpedo.Splitters.Count) builder.AppendLine("=> CANNOT SEPARATE CLUSTER!");
            if (Base == null)
            {
                builder.AppendLine("=> BASE MISSING!");
                OK = false;
            }

            builder.AppendLine("======== WARNINGS ========");

            if (torpedo.Camera == null) builder.AppendLine("=> No camera.");
            if (torpedo.Sensor == null) builder.AppendLine("=> No sensor.");
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

        private void SaveTorpedo()
        {
            Base.CustomData = GridTerminalHelper.BlockListBytePosToBase64(PartsOfInterest, Base);
        }
    }
}
