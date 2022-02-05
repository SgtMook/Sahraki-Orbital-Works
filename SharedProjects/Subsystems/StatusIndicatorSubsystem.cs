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

    public class StatusIndicatorSubsystem : ISubsystem
    {
        #region ISubsystem
        public UpdateFrequency UpdateFrequency => UpdateFrequency.Update100;

        public void Command(TimeSpan timestamp, string command, object argument)
        {
        }
        public void CommandV2(TimeSpan timestamp, CommandLine command)
        {

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

        public void Setup(ExecutionContext context, string name)
        {
            Context = context;

            GetParts();
        }

        public void Update(TimeSpan timestamp, UpdateFrequency updateFlags)
        {
            UpdateIndicator();
        }
        #endregion

        ExecutionContext Context;

        IMyInteriorLight IndicatorLight;

        IDockingSubsystem DockingSubsystem;
        IIntelProvider IntelProvider;

        public StatusIndicatorSubsystem(IDockingSubsystem dockingSubsystem, IIntelProvider intelProvider)
        {
            DockingSubsystem = dockingSubsystem;
            IntelProvider = intelProvider;
        }

        void GetParts()
        {
            IndicatorLight = null;
            Context.Terminal.GetBlocksOfType<IMyTerminalBlock>(null, CollectParts);
            if (IndicatorLight != null)
            {
                IndicatorLight.Radius = 10;
            }
        }

        bool CollectParts(IMyTerminalBlock block)
        {
            if (Context.Reference.CubeGrid.EntityId != block.CubeGrid.EntityId) return false;

            if (block is IMyInteriorLight)
                IndicatorLight = (IMyInteriorLight)block;

            return false;
        }

        void UpdateIndicator()
        {
            if (IndicatorLight == null) return;
// 
//             Color color = Color.Green;
//             if (!IntelProvider.HasMaster)
//             {
//                 color = Color.Red;
//             }
//             else if (DockingSubsystem.HomeID == -1)
//             {
//                 color = Color.Yellow;
//             }

            // I need more characters Keen.. I NEED THEM
            IndicatorLight.Color = !IntelProvider.HasMaster ? Color.Red : DockingSubsystem.HomeID == -1 ? Color.Yellow : Color.Green;
        }
    }
}
