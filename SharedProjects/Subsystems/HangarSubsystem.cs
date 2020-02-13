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
    class Hangar
    {
        private IMyPistonBase extender;
        private IMyMotorAdvancedStator rotor;
        private List<IMyAirtightHangarDoor> gates = new List<IMyAirtightHangarDoor>();
        private List<IMyInteriorLight> lights = new List<IMyInteriorLight>();
        private IMyTextPanel display;
        StringBuilder statusBuilder = new StringBuilder();

        public IMyShipConnector Connector;
        public HangarStatus hangarStatus = HangarStatus.None;
        public long OwnerID;

        public DockIntel Intel = new DockIntel();

        public int Index = 0;

        public Hangar(int index)
        {
            Index = index;
        }

        public void AddPart(IMyTerminalBlock part)
        {
            if (part is IMyShipConnector) Connector = (IMyShipConnector)part;
            if (part is IMyPistonBase) extender = (IMyPistonBase)part;
            if (part is IMyMotorAdvancedStator) rotor = (IMyMotorAdvancedStator)part;
            if (part is IMyAirtightHangarDoor) gates.Add((IMyAirtightHangarDoor)part);
            if (part is IMyTextPanel) display = (IMyTextPanel)part;
            if (part is IMyInteriorLight)
            {
                IMyInteriorLight light = (IMyInteriorLight)part;
                lights.Add(light);
                light.Intensity = 2f;
                light.Radius = 12f;
            }
        }

        public void Clear()
        {
            Connector = null;
            extender = null;
            rotor = null;
            gates.Clear();
            lights.Clear();
            display = null;
            OwnerID = 0;
            hangarStatus = HangarStatus.None;
        }

        public bool OK()
        {
            return Connector != null;
        }
    }

    public class HangarSubsystem : ISubsystem
    {
        #region ISubsystem
        public UpdateFrequency UpdateFrequency => UpdateFrequency.Update10;

        public void Command(TimeSpan timestamp, string command, object argument)
        {
        }

        public void DeserializeSubsystem(string serialized)
        {
            // TODO: Save... probably owner states?
        }

        public string GetStatus()
        {
            StatusBuilder.Clear();
            int OKhangars = 0;
            for (int i = 0; i < Hangars.Count(); i++)
            {
                if (Hangars[i] != null && Hangars[i].OK())
                    OKhangars++;
            }
            StatusBuilder.Append(OKhangars.ToString()).AppendLine(" hangars connected");
            return StatusBuilder.ToString();
        }

        public string SerializeSubsystem()
        {
            // TODO: Save... probably owner states?
            return string.Empty;
        }

        public void Setup(MyGridProgram program, string name)
        {
            Program = program;
            GetParts();
        }

        public void Update(TimeSpan timestamp, UpdateFrequency updateFlags)
        {
            for (int i = 0; i < Hangars.Count(); i++)
            {
                if (Hangars[i] != null && Hangars[i].OK())
                    IntelProvider.ReportFleetIntelligence(GetHangarIntel(Hangars[i], timestamp), timestamp);
            }
        }
        #endregion

        MyGridProgram Program;
        string Tag;
        string TagPrefix;
        StringBuilder StatusBuilder = new StringBuilder();
        StringBuilder builder = new StringBuilder();
        IIntelProvider IntelProvider;

        IMyShipController controller;

        Hangar[] Hangars = new Hangar[64];

        // Hangars should be named $"[{tag}{#}] name"
        // For example "[H15] Connector"
        // Up to 64 hangars - that really should be more than enough
        public HangarSubsystem(IIntelProvider provider, string tag = "H")
        {
            Tag = tag;
            TagPrefix = "[" + tag;
            IntelProvider = provider;
        }

        void GetParts()
        {
            controller = null;
            for (int i = 0; i < Hangars.Count(); i++) 
                if (Hangars[i] != null) Hangars[i].Clear();
            Program.GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(null, CollectParts);
        }

        private bool CollectParts(IMyTerminalBlock block)
        {
            if (!Program.Me.IsSameConstructAs(block)) return false;

            if (block is IMyShipController)
            {
                controller = (IMyShipController)block;
                return false;
            }

            if (!block.CustomName.StartsWith(TagPrefix)) return false;
            var indexTagEnd = block.CustomName.IndexOf(']');
            if (indexTagEnd == -1) return false;

            var numString = block.CustomName.Substring(TagPrefix.Length, indexTagEnd - TagPrefix.Length);
            int hangarIndex;
            if (!int.TryParse(numString, out hangarIndex)) return false;
            if (Hangars[hangarIndex] == null) Hangars[hangarIndex] = new Hangar(hangarIndex);
            Hangars[hangarIndex].AddPart(block);

            return false;
        }

        private DockIntel GetHangarIntel(Hangar hangar, TimeSpan timestamp)
        {
            builder.Clear();
            hangar.Intel.WorldMatrix = hangar.Connector.WorldMatrix;
            hangar.Intel.CurrentVelocity = controller.GetShipVelocities().LinearVelocity;
            hangar.Intel.Status = hangar.hangarStatus;
            hangar.Intel.OwnerID = hangar.OwnerID;
            hangar.Intel.CurrentCanonicalTime = timestamp + IntelProvider.CanonicalTimeDiff;

            hangar.Intel.ID = hangar.Connector.EntityId;
            hangar.Intel.DisplayName = builder.Append("H").Append(hangar.Index.ToString()).Append('-').Append(Program.Me.CubeGrid.CustomName).ToString();

            return hangar.Intel;
        }
    }
}
