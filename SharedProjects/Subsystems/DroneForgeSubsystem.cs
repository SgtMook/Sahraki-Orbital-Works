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
    public class DroneForge
    {
        public List<IMyShipWelder> Welders = new List<IMyShipWelder>();
        public IMyProjector Projector;
        public MyGridProgram Program;

        IMyProgrammableBlock droneMainframe;
        IMyShipMergeBlock droneRelease;

        DroneForgeSubsystem Host;

        public DroneForge(MyGridProgram program, DroneForgeSubsystem host)
        {
            Program = program;
            Host = host;
        }

        public void AddPart(IMyTerminalBlock part)
        {
            if (part is IMyShipWelder)
            {
                IMyShipWelder welder = (IMyShipWelder)part;
                Welders.Add(welder);
                welder.Enabled = false;
            }
            if (part is IMyProjector)
            {
                Projector = (IMyProjector)part;
                Projector.Enabled = false;
            }
        }

        public void Update(TimeSpan localTime)
        {
            if (Projector.Enabled && Projector.RemainingBlocks == 0)
            {
                Release(localTime);
            }
        }

        public void Print()
        {
            foreach (var welder in Welders) welder.Enabled = true;
            Projector.Enabled = true;
        }

        // [Autoforge]
        // Autoname = Name
        private void Release(TimeSpan localTime)
        {
            Program.GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(null, CheckRelease);

            if (droneMainframe != null)
            {
                string name = "New Drone";
                if (Host.IniParser.TryParse(droneMainframe.CustomData))
                {
                    string autoName = Host.IniParser.Get("Autoforge", "Autoname").ToString() + " ";
                    if (!string.IsNullOrEmpty(autoName))
                    {
                        var intels = Host.IntelProvider.GetFleetIntelligences(localTime);
                        foreach (var kvp in intels)
                        {
                            if (kvp.Key.Item1 == IntelItemType.Friendly) Host.FriendlyNameScratchpad.Add(kvp.Value.DisplayName);
                        }

                        for (int serial = 1; serial < 64; serial++)
                        {
                            string serializedName = autoName + serial.ToString();
                            if (!Host.FriendlyNameScratchpad.Contains(serializedName))
                            {
                                name = serializedName;
                                break;
                            }
                        }

                    }
                }
                droneMainframe.TryRun($"manager activate \"{name}\"");
                droneMainframe = null;
            }

            droneRelease.Enabled = false;

            droneRelease = null;

            foreach (var welder in Welders) welder.Enabled = false;
            Projector.Enabled = false;
        }

        private bool CheckRelease(IMyTerminalBlock block)
        {
            if (block.CubeGrid.EntityId != Projector.CubeGrid.EntityId) return false;
            if ((block is IMyProgrammableBlock) && block.CustomName.Contains("Mainframe")) droneMainframe = (IMyProgrammableBlock)block;
            if ((block is IMyShipMergeBlock) && block.CustomName.Contains("[RL]")) droneRelease = (IMyShipMergeBlock)block;
            return false;
        }

        public bool OK()
        {
            return Projector != null && Welders.Count > 0;
        }
    }

    public class DroneForgeSubsystem : ISubsystem
    {
        #region ISubsystem
        public UpdateFrequency UpdateFrequency => UpdateFrequency.Update100;

        public void Command(TimeSpan timestamp, string command, object argument)
        {
            if (command == "print")
            {
                int index;
                if (!int.TryParse((string)argument, out index)) return;
                if (DroneForges[index] != null && DroneForges[index].OK()) DroneForges[index].Print();
            }
        }

        public void DeserializeSubsystem(string serialized)
        {
        }

        public string GetStatus()
        {
            return string.Empty;
        }

        public string SerializeSubsystem()
        {
            return string.Empty;
        }

        public void Setup(MyGridProgram program, string name)
        {
            Program = program;
            GetParts();
        }

        public void Update(TimeSpan timestamp, UpdateFrequency updateFlags)
        {
            UpdateForges(timestamp);
        }
        #endregion

        MyGridProgram Program;
        public IIntelProvider IntelProvider;
        string Tag;
        string TagPrefix;

        StringBuilder StatusBuilder = new StringBuilder();

        public DroneForge[] DroneForges = new DroneForge[64];
        public HashSet<string> FriendlyNameScratchpad = new HashSet<string>();
        public MyIni IniParser = new MyIni();

        // Prints drones
        // Drones should be printable just by turning on the projector and welders
        public DroneForgeSubsystem(IIntelProvider intelProvider, string tag = "DF")
        {
            Tag = tag;
            TagPrefix = "[" + tag;
            IntelProvider = intelProvider;
        }

        void GetParts()
        {
            for (int i = 0; i < DroneForges.Count(); i++)
                DroneForges[i] = null;

            Program.GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(null, CollectParts);
        }

        private bool CollectParts(IMyTerminalBlock block)
        {
            if (!Program.Me.IsSameConstructAs(block)) return false;

            if (!block.CustomName.StartsWith(TagPrefix)) return false;
            var indexTagEnd = block.CustomName.IndexOf(']');
            if (indexTagEnd == -1) return false;

            var numString = block.CustomName.Substring(TagPrefix.Length, indexTagEnd - TagPrefix.Length);

            int index;
            if (!int.TryParse(numString, out index)) return false;
            if (DroneForges[index] == null) DroneForges[index] = new DroneForge(Program, this);
            DroneForges[index].AddPart(block);

            return false;
        }

        private void UpdateForges(TimeSpan localTime)
        {
            for (int i = 0; i < DroneForges.Count(); i++)
            {
                if (DroneForges[i] != null && DroneForges[i].OK())
                {
                    DroneForges[i].Update(localTime);
                }
            }
        }
    }
}
