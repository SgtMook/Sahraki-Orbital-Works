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
    public class AlarmSubsystem : ISubsystem
    {
        #region ISubsystem
        public UpdateFrequency UpdateFrequency => UpdateFrequency.Update1;

        public void Command(TimeSpan timestamp, string command, object argument)
        {
        }

        public void CommandV2(TimeSpan timestamp, CommandLine command)
        {
            if (command.Argument(0) == "tagconnected") TryTagConnected();
            if (command.Argument(0) == "cleartags") ClearTags();
        }

        public void DeserializeSubsystem(string serialized)
        {
        }

        public string GetStatus()
        {
            return "";
        }

        public string SerializeSubsystem()
        {
            return string.Empty;
        }

        public void Setup(ExecutionContext context, string name)
        {
            Context = context;

            ParseConfigs();
            GetParts();
        }

        public void Update(TimeSpan timestamp, UpdateFrequency updateFlags)
        {
            runs++;

            if (runs % 60 == 0)
            {
                CheckTags();
            }
        }
        #endregion
        const string kAlarmSection = "Alarm";
        ExecutionContext Context;

        List<IMyTerminalBlock> TaggedBlocks = new List<IMyTerminalBlock>();
        List<IMyShipConnector> Connectors = new List<IMyShipConnector>();
        Dictionary<MyDetectedEntityInfo, float> ThreatScratchpad = new Dictionary<MyDetectedEntityInfo, float>();
        IMyLightingBlock AlarmLight;

        string TempStatus = string.Empty;
        int TempStatusSeconds = -1;

        public AlarmSubsystem()
        {
        }

        int runs = 0;

        // [Alarm]
        void ParseConfigs()
        {
            MyIni Parser = new MyIni();
            if (!Parser.TryParse(Context.Reference.CustomData))
                return;

            // TurnOffWheels = Parser.Get(kUtilitySection, "TurnOffWheels").ToBoolean(true);
            // TurnOffGyros = Parser.Get(kUtilitySection, "TurnOffGyros").ToBoolean(true);
            // TurnOffHydros = Parser.Get(kUtilitySection, "TurnOffHydros").ToBoolean(true);
            // AutoAddWheels = Parser.Get(kUtilitySection, "AutoAddWheels").ToBoolean(true);
        }

        void GetParts()
        {
            Context.Terminal.GetBlocksOfType<IMyTerminalBlock>(null, CollectBlocks);
        }

        bool CollectBlocks(IMyTerminalBlock block)
        {
            if (!block.IsSameConstructAs(Context.Reference)) return false;
            if (block is IMyShipConnector) Connectors.Add(block as IMyShipConnector);
            if (block is IMyLightingBlock && block.CustomName.Contains("[ALM]")) AlarmLight = block as IMyLightingBlock;

            return false;
        }

        void TryTagConnected()
        {
            if (Context.WCAPI == null)
            {
                TempStatus = "API NOT INITIALIZED";
                TempStatusSeconds = 5;
                return;
            }

            if (Connectors.Count == 0)
            {
                TempStatus = "NOT DOCKED";
                TempStatusSeconds = 5;
            }

            foreach (var connector in Connectors)
            {
                if (connector.Status == MyShipConnectorStatus.Connected)
                {
                    var otherCon = connector.OtherConnector;
                    if (Context.WCAPI.HasGridAi(otherCon.CubeGrid.EntityId))
                    {
                        TempStatus = $"TAGGED {otherCon.CubeGrid.CustomName}";
                        TempStatusSeconds = 5;
                        TaggedBlocks.Clear();
                        TaggedBlocks.Add(otherCon);
                        return;
                    }
                    else
                    {
                        TempStatus = $"NO RADAR ON CONNECTED GRIDS";
                        TempStatusSeconds = 5;
                    }
                }
            }
        }

        void ClearTags()
        {
            TaggedBlocks.Clear();
        }

        void CheckTags()
        {
            if (AlarmLight == null) return;
            AlarmLight.ShowOnHUD = true;
            var index = AlarmLight.CustomName.IndexOf('|') + 1;
            if (index <= 0) return;
            string originalName = AlarmLight.CustomName.Substring(0, index);
            string status = "NO TAGS";
            var alarmOn = false;
            if (TaggedBlocks.Count > 0)
            {
                status = $" TAG: {TaggedBlocks[0].CubeGrid.CustomName}";

                if (Context.WCAPI != null)
                {
                    ThreatScratchpad.Clear();
                    Context.WCAPI.GetSortedThreats(TaggedBlocks[0], ThreatScratchpad);

                    if (ThreatScratchpad.Count > 0)
                    {
                        var highestThreat = -1f;
                        MyDetectedEntityInfo highestThreatEntity = new MyDetectedEntityInfo();

                        foreach (var kvp in ThreatScratchpad)
                        {
                            if (kvp.Value > highestThreat)
                            {
                                highestThreatEntity = kvp.Key;
                                highestThreat = kvp.Value;
                            }
                        }

                        if (highestThreat >= 0)
                        {
                            status = $"ALARM: THREAT {highestThreatEntity.Name}";
                            alarmOn = true;
                        }
                    }
                }
            }

            if (!string.IsNullOrEmpty(TempStatus) && TempStatusSeconds > 0)
            {
                status = TempStatus;
                TempStatusSeconds--;
            }

            AlarmLight.Enabled = alarmOn;
            AlarmLight.CustomName = originalName + " " + status;
        }
    }
}
