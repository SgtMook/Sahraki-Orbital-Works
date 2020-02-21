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
    // Canonical hangar request format is (long requesterID, long hangarID, int hangarRequest)
    public enum HangarRequest
    {
        Claim,
        Unclaim,
        Reserve,
        Open,
    }

    class Hangar
    {
        private List<IMyAirtightHangarDoor> gates = new List<IMyAirtightHangarDoor>();
        private List<IMyInteriorLight> lights = new List<IMyInteriorLight>();
        private IMyTextPanel display;
        StringBuilder statusBuilder = new StringBuilder();

        public IMyShipConnector Connector;
        public IMyInteriorLight DirectionIndicator;
        public HangarStatus hangarStatus = HangarStatus.None;
        public long OwnerID = -1;

        public DockIntel Intel = new DockIntel();

        public int Index = 0;

        bool docked;
        TimeSpan lastClaimTime;
        TimeSpan kClaimTimeout = TimeSpan.FromSeconds(1);

        public Hangar(int index)
        {
            Index = index;
        }

        public void AddPart(IMyTerminalBlock part)
        {
            if (part is IMyShipConnector) Connector = (IMyShipConnector)part;
            if (part is IMyAirtightHangarDoor) gates.Add((IMyAirtightHangarDoor)part);
            if (part is IMyTextPanel) display = (IMyTextPanel)part;
            if (part is IMyInteriorLight)
            {
                IMyInteriorLight light = (IMyInteriorLight)part;
                if (light.CustomName.Contains("<DI>"))
                {
                    DirectionIndicator = light;
                    light.Color = Color.Red;
                    light.Intensity = 0.5f;
                    light.BlinkIntervalSeconds = 1;
                    light.BlinkLength = 0.1f;
                }
                else
                {
                    lights.Add(light);
                    light.Intensity = 2f;
                    light.Radius = 12f;
                }
            }
        }

        public void Clear()
        {
            Connector = null;
            DirectionIndicator = null;
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

        public void TakeRequest(long requesterID, HangarRequest request, TimeSpan timestamp)
        {
            if (request == HangarRequest.Claim)
            {
                if (OwnerID != -1 && OwnerID != requesterID) return;
                Claim(requesterID, timestamp);
            }
            else if (request == HangarRequest.Reserve)
            {
                if (OwnerID != -1 && OwnerID != requesterID) return;
                Claim(requesterID, timestamp);
                hangarStatus |= HangarStatus.Reserved;
            }
            else if (request == HangarRequest.Unclaim)
            {
                if (OwnerID != requesterID) return;
                Unclaim();
            }
        }

        private void Claim(long requesterID, TimeSpan timestamp)
        {
            OwnerID = requesterID;
            lastClaimTime = timestamp;
        }

        private void Unclaim()
        {
            OwnerID = -1;
            hangarStatus &= ~HangarStatus.Reserved;
        }

        public void Update(TimeSpan timestamp, Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence> intelItems)
        {
            if (Connector == null) return;
            if ((hangarStatus & HangarStatus.Reserved) == 0)
            {
                if (lastClaimTime + kClaimTimeout < timestamp && Connector.Status != MyShipConnectorStatus.Connected)
                    OwnerID = -1;
            }
            else
            {
                if (OwnerID == -1) Unclaim();
                else if (lastClaimTime + kClaimTimeout < timestamp)
                {
                    var intelKey = MyTuple.Create(IntelItemType.Friendly, OwnerID);
                    if (!intelItems.ContainsKey(intelKey))
                    {
                        Unclaim();
                        return;
                    }
                    if (((FriendlyShipIntel)intelItems[intelKey]).HomeID != Connector.EntityId) Unclaim();
                }
            }
            docked = Connector.Status == MyShipConnectorStatus.Connected;
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
            return string.Empty;
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

            HangarChannelTag = program.Me.CubeGrid.EntityId.ToString() + "-HANGAR";
            HangarListener = program.IGC.RegisterBroadcastListener(HangarChannelTag);
        }

        public void Update(TimeSpan timestamp, UpdateFrequency updateFlags)
        {
            ReportAndUpdateHangars(timestamp);
            ProcessHangarRequests(timestamp);
        }
        #endregion

        MyGridProgram Program;
        string Tag;
        string TagPrefix;

        string HangarChannelTag;
        IMyBroadcastListener HangarListener;

        StringBuilder StatusBuilder = new StringBuilder();
        StringBuilder builder = new StringBuilder();
        IIntelProvider IntelProvider;

        IMyShipController controller;

        Hangar[] Hangars = new Hangar[64];
        Dictionary<long, Hangar> HangarsDict = new Dictionary<long, Hangar>(64);

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
            HangarsDict.Clear();

            Program.GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(null, CollectParts);

            for (int i = 0; i < Hangars.Count(); i++)
                if (Hangars[i] != null && Hangars[i].Connector != null) HangarsDict[Hangars[i].Connector.EntityId] = Hangars[i];
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

            hangar.Intel.UndockNear = hangar.Connector.CubeGrid.GridSizeEnum == MyCubeSize.Large ? 1.3f : 0.55f;

            hangar.Intel.IndicatorDir = hangar.DirectionIndicator == null ? Vector3D.Zero : hangar.DirectionIndicator.WorldMatrix.Forward;
            hangar.Intel.HangarChannelTag = HangarChannelTag;

            return hangar.Intel;
        }

        private void ReportAndUpdateHangars(TimeSpan timestamp)
        {
            var intelItems = IntelProvider.GetFleetIntelligences(timestamp);
            for (int i = 0; i < Hangars.Count(); i++)
            {
                if (Hangars[i] != null && Hangars[i].OK())
                {
                    IntelProvider.ReportFleetIntelligence(GetHangarIntel(Hangars[i], timestamp), timestamp);
                    Hangars[i].Update(timestamp, intelItems);
                }
            }
        }

        private void ProcessHangarRequests(TimeSpan timestamp)
        {
            while (HangarListener.HasPendingMessage)
            {
                var msg = HangarListener.AcceptMessage();
                if (!(msg.Data is MyTuple<long, long, int>)) return;
                var unpacked = (MyTuple<long, long, int>)msg.Data;
                if (!HangarsDict.ContainsKey(unpacked.Item2)) return;
                HangarsDict[unpacked.Item2].TakeRequest(unpacked.Item1, (HangarRequest)unpacked.Item3, timestamp);
            }
        }
    }
}
