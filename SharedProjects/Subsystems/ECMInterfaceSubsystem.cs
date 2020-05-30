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
    public class ECMInterfaceSubsystem : ISubsystem
    {
        #region ISubsystem
        public UpdateFrequency UpdateFrequency { get; set; }

        public void Command(TimeSpan timestamp, string command, object argument)
        {
            if (command == "toggleproject") projecting = !projecting;
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
            if (projecting)
            {
                var intelItems = IntelProvider.GetFleetIntelligences(timestamp);

                foreach(var kvp in intelItems)
                {
                    if (kvp.Key.Item1 == IntelItemType.Enemy)
                    {
                        Program.IGC.SendBroadcastMessage("ECMPROJECT", MyTuple.Create(kvp.Value.GetPositionFromCanonicalTime(timestamp + IntelProvider.CanonicalTimeDiff), kvp.Value.GetVelocity()), TransmissionDistance.CurrentConstruct);
                    }
                }
            }
        }
        #endregion

        public ECMInterfaceSubsystem(IIntelProvider intelProvider)
        {
            UpdateFrequency = UpdateFrequency.Update10;
            IntelProvider = intelProvider;
        }
        MyGridProgram Program;
        IIntelProvider IntelProvider;
        bool projecting = false;
        void GetParts()
        {
            Program.GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(null, CollectParts);
        }

        bool CollectParts(IMyTerminalBlock block)
        {
            if (!Program.Me.IsSameConstructAs(block)) return false;

            return false;
        }
    }
}
