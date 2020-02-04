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

using SharedProjects.Utility;

namespace SharedProjects.Subsystems
{
    /// <summary>
    /// This subsystem is capable of producing a dictionary of intel items
    /// </summary>
    public interface IIntelProvider
    {
        Dictionary<long, IFleetIntelligence> GetFleetIntelligences();
    }

    // Handles tracking, updating, and transmitting fleet intelligence
    // TODO: Serialize and deserialize intel items
    // TODO: Send sync messages containing all the fleet intelligences
    // TODO: Receive update messages
    // TODO: Remove items as necessary
    // TODO: Support more intel types
    public class IntelMasterSubsystem : ISubsystem, IIntelProvider
    {
        #region ISubsystem
        public int UpdateFrequency => 1;
        MyGridProgram Program;
        IMyBroadcastListener ReportListener;
        StringBuilder statusBuilder = new StringBuilder();

        public void Command(string command, object argument)
        {
            if (command == "additem") AddItem((IFleetIntelligence)argument);
        }


        public string GetStatus()
        {
            statusBuilder.Clear();
            foreach (IFleetIntelligence intel in IntelItems.Values)
                statusBuilder.AppendLine(intel.Serialize());
            return statusBuilder.ToString();
        }

        public void DeserializeSubsystem(string serialized)
        {
        }

        public string SerializeSubsystem()
        {
            return string.Empty;
        }

        public void Setup(MyGridProgram program, SubsystemManager manager)
        {
            Program = program;
            ReportListener = program.IGC.RegisterBroadcastListener(FleetIntelligenceConfig.IntelReportChannelTag);
        }

        public void Update(TimeSpan timestamp)
        {
            while (ReportListener.HasPendingMessage)
            {
                var msg = ReportListener.AcceptMessage();
                AddItem(FleetIntelligenceConfig.IGCUnpackGeneric(msg.Data));
            }
        }
        #endregion

        #region IIntelProvider
        public Dictionary<long, IFleetIntelligence> GetFleetIntelligences()
        {
            return IntelItems;
        }
        #endregion

        #region Debug
        StringBuilder debugBuilder = new StringBuilder();
        #endregion

        Dictionary<long, IFleetIntelligence> IntelItems = new Dictionary<long, IFleetIntelligence>();

        void AddItem(IFleetIntelligence item)
        {
            IntelItems.Add(item.ID, item);
        }

        void AddItem(MyDetectedEntityInfo info)
        {
            // TODO
        }
    }

    // TODO: Build
    //public class IntelSlaveSubsystem : ISubsystem, IIntelProvider
    //{
    //    #region ISubsystem
    //    public int UpdateFrequency => throw new NotImplementedException();
    //
    //    public void Command(string command, object argument)
    //    {
    //        throw new NotImplementedException();
    //    }
    //
    //    public void DeserializeSubsystem(string serialized)
    //    {
    //        throw new NotImplementedException();
    //    }
    //
    //    public string GetStatus()
    //    {
    //        throw new NotImplementedException();
    //    }
    //
    //    public string SerializeSubsystem()
    //    {
    //        throw new NotImplementedException();
    //    }
    //
    //    public void Setup(MyGridProgram program, SubsystemManager manager)
    //    {
    //        throw new NotImplementedException();
    //    }
    //
    //    public void Update(TimeSpan timestamp)
    //    {
    //        throw new NotImplementedException();
    //    }
    //    #endregion
    //
    //    #region IIntelProvider
    //    public Dictionary<long, IFleetIntelligence> GetFleetIntelligences()
    //    {
    //        throw new NotImplementedException();
    //    }
    //    #endregion
    //}
}
