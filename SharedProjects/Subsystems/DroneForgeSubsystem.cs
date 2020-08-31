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
        public List<IMyMotorAdvancedStator> Motors = new List<IMyMotorAdvancedStator>();
        public List<IMyMotorAdvancedStator> ReverseMotors = new List<IMyMotorAdvancedStator>();
        public IMyProjector Projector;
        public MyGridProgram Program;

        IMyProgrammableBlock droneMainframe;
        IMyShipMergeBlock droneRelease;
        IMyShipMergeBlock forgeRelease;

        public Action<long, TimeSpan> callback;

        DroneForgeSubsystem Host;

        int releaseCounter = 0;

        StringBuilder debugBuilder = new StringBuilder();
        List<IMyTerminalBlock> blockScratchpad = new List<IMyTerminalBlock>();

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
            if (part is IMyShipMergeBlock)
            {
                forgeRelease = (IMyShipMergeBlock)part;
            }
        }

        public void Update(TimeSpan localTime)
        {
            debugBuilder.Clear();
            debugBuilder.AppendLine(releaseCounter.ToString());
            if (releaseCounter == 1)
            {
                if (Projector.RemainingBlocks == 0) releaseCounter ++;
                Projector.Enabled = true;
                foreach (var welder in Welders) welder.Enabled = true;
            }
            else if (releaseCounter > 1 && releaseCounter < 5)
            {
                releaseCounter++;

            }
            else if (releaseCounter == 5)
            {
                Release(localTime);
                if (droneMainframe != null)
                    releaseCounter++;
            }
            else if (releaseCounter == 6)
            {
                if (droneMainframe != null && callback != null)
                    callback(droneMainframe.CubeGrid.EntityId, localTime);
                droneMainframe = null;
                releaseCounter ++;
            }
            else if (releaseCounter > 6)
            {
                releaseCounter++;
                if (releaseCounter > 8)
                {
                    releaseCounter = 0;
                }
            }
        }

        public void Print()
        {
            if (releaseCounter == 0) releaseCounter++;
        }

        // [Autoforge]
        // Autoname = Name
        void Release(TimeSpan localTime)
        {
            droneRelease = GridTerminalHelper.OtherMergeBlock(forgeRelease);
            if (droneRelease == null) return;
            debugBuilder.AppendLine("DRONE RELEASE FOUND");
            blockScratchpad.Clear();
            if (!GridTerminalHelper.Base64BytePosToBlockList(droneRelease.CustomData, droneRelease, ref blockScratchpad)) return;
            if (!(blockScratchpad[0] is IMyProgrammableBlock)) return;
            debugBuilder.AppendLine("DRONE MAINFRAME FOUND");
            droneMainframe = (IMyProgrammableBlock)blockScratchpad[0];

            if (droneMainframe != null && droneRelease != null)
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
                droneMainframe.Enabled = true;
                droneMainframe.TryRun($"manager activate \"{name}\"");

                droneRelease.Enabled = false;

                foreach (var welder in Welders) welder.Enabled = false;
                Projector.Enabled = false;

                Host.RequestRefresh = true;
            }

            droneRelease = null;
        }

        public bool OK()
        {
            return Projector != null && Welders.Count > 0;
        }

        public string GetStatus()
        {
            return debugBuilder.ToString();
        }
    }

    public class DroneForgeSubsystem : ISubsystem, IInventoryRefreshRequester
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
            return DroneForges[1].GetStatus();
            //return string.Empty;
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

        public bool RequestRefresh = false;

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

        bool CollectParts(IMyTerminalBlock block)
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

        void UpdateForges(TimeSpan localTime)
        {
            for (int i = 0; i < DroneForges.Count(); i++)
            {
                if (DroneForges[i] != null && DroneForges[i].OK())
                {
                    DroneForges[i].Update(localTime);
                }
            }
        }

        public void PrintDrone(int index = 0, Action<long, TimeSpan> Callback = null)
        {
            DroneForges[index].Print();
            DroneForges[index].callback = Callback;
        }

        #region IInventoryRefreshRequester
        public bool RequestingRefresh()
        {
            return RequestRefresh;
        }

        public void AcknowledgeRequest()
        {
            RequestRefresh = false;
        }
        #endregion
    }
}
