﻿using Sandbox.Game.EntityComponents;
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
    // Sample Config:
    // [Hangar]
    // Mutex = 1,2,3,4,5
    // ClearanceDist = 40
    // Tags = ABCDE
    public enum HangarRequest
    {
        RequestDock,
        Unclaim,
        Reserve,
        Open,
        RequestLaunch,
    }

    public class Hangar
    {
        List<IMyDoor> gates = new List<IMyDoor>();
        List<IMyInteriorLight> lights = new List<IMyInteriorLight>();
        IMyTextPanel display;

        public IMyShipConnector Connector;
        public IMyInteriorLight DirectionIndicator;
        public HangarStatus hangarStatus = HangarStatus.None;
        public long OwnerID = -1;

        public DockIntel Intel = new DockIntel();

        public int Index = 0;

        public HangarSubsystem Host;

        TimeSpan lastClaimTime;
        TimeSpan lastLaunchTime;
        TimeSpan kClaimTimeout = TimeSpan.FromSeconds(3);
        TimeSpan kLaunchTimeout = TimeSpan.FromSeconds(3);

        List<int> MutexHangars = new List<int>();
        public float ClearanceDist = 40;

        public HangarTags HangarTags = HangarTags.None;

        bool init = false;

        public Hangar(int index, HangarSubsystem host)
        {
            Index = index;
            Host = host;
        }

        public void AddPart(IMyTerminalBlock part)
        {
            if (part is IMyShipConnector)
            {
                Connector = (IMyShipConnector)part;
                ParseConfigs();
            }
            if (part is IMyDoor) gates.Add((IMyDoor)part);
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

        void ParseConfigs()
        {
            MutexHangars.Clear();

            if (!Host.IniParser.TryParse(Connector.CustomData))
                return;

            string mutexes = Host.IniParser.Get("Hangar", "Mutex").ToString();
            if (mutexes != string.Empty)
            {
                var split = mutexes.Split(',');
                foreach (var i in split)
                {
                    int index;
                    if (int.TryParse(i, out index)) MutexHangars.Add(index);
                }
            }

            float dist = Host.IniParser.Get("Hangar", "ClearanceDist").ToInt16();
            if (dist != 0) ClearanceDist = dist;

            string tagString = Host.IniParser.Get("Hangar", "Tags").ToString();
            if (tagString.Contains("A")) HangarTags |= HangarTags.A;
            if (tagString.Contains("B")) HangarTags |= HangarTags.B;
            if (tagString.Contains("C")) HangarTags |= HangarTags.C;
            if (tagString.Contains("D")) HangarTags |= HangarTags.D;
            if (tagString.Contains("E")) HangarTags |= HangarTags.E;
            if (tagString.Contains("F")) HangarTags |= HangarTags.F;
            if (tagString.Contains("G")) HangarTags |= HangarTags.G;
            if (tagString.Contains("H")) HangarTags |= HangarTags.H;
            if (tagString.Contains("I")) HangarTags |= HangarTags.I;
            if (tagString.Contains("J")) HangarTags |= HangarTags.J;
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

        public void TakeRequest(long requesterID, HangarRequest request, TimeSpan timestamp)
        {
            if (request == HangarRequest.RequestDock)
            {
                if (OwnerID != -1 && OwnerID != requesterID) return;
                Claim(requesterID, timestamp);
                MakeReadyToDock();
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
            else if (request == HangarRequest.RequestLaunch)
            {
                lastLaunchTime = timestamp;
                MakeReadyToLaunch();
            }
        }

        bool HasClearance()
        {
            bool ready = true;
            foreach (var index in MutexHangars)
            {
                if (Host.Hangars[index] == null) continue;
                if ((Host.Hangars[index].hangarStatus & (HangarStatus.Docking | HangarStatus.Launching)) == 0) continue;
                ready = false;
            }
            foreach (var gate in gates)
            {
                if (gate.Status == DoorStatus.Open) continue;
                ready = false;
            }
            return ready;
        }

        void OpenGates()
        {
            foreach (var door in gates)
            {
                if (door.Status != DoorStatus.Opening && door.Status != DoorStatus.Open) door.OpenDoor();
            }
        }

        void CloseGates()
        {
            foreach (var door in gates)
            {
                if (door.Status != DoorStatus.Closed && door.Status != DoorStatus.Closing) door.CloseDoor();
            }
        }

        void MakeReadyToDock()
        {
            OpenGates();
            if (HasClearance()) hangarStatus |= HangarStatus.Docking;
        }

        void MakeReadyToLaunch()
        {
            OpenGates();
            if (HasClearance()) hangarStatus |= HangarStatus.Launching;
        }

        void Claim(long requesterID, TimeSpan timestamp)
        {
            OwnerID = requesterID;
            lastClaimTime = timestamp;
        }

        void Unclaim()
        {
            hangarStatus &= ~HangarStatus.Reserved;
        }

        public void Update(TimeSpan timestamp, Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence> intelItems)
        {
            if (Connector == null) return;

            if (!init)
            {
                init = true;
                if (Connector.Status == MyShipConnectorStatus.Connected)
                {
                    hangarStatus |= HangarStatus.Reserved;
                    OwnerID = Connector.OtherConnector.CubeGrid.EntityId;
                }
            }

            UpdateHangarStatus(timestamp, intelItems);
        }

        void UpdateHangarStatus(TimeSpan timestamp, Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence> intelItems)
        {
            bool ClaimElapsed = lastClaimTime + kClaimTimeout < timestamp;
            bool LaunchElapsed = lastLaunchTime + kLaunchTimeout < timestamp;
            if ((hangarStatus & HangarStatus.Reserved) == 0)
            {
                if (ClaimElapsed && Connector.Status != MyShipConnectorStatus.Connected)
                    OwnerID = -1;
            }
            else
            {
                if (OwnerID == -1)
                {
                    Unclaim();
                }
                else if (ClaimElapsed && Connector.Status != MyShipConnectorStatus.Connected)
                {
                    var intelKey = MyTuple.Create(IntelItemType.Friendly, OwnerID);
                    if (!intelItems.ContainsKey(intelKey))
                    {
                        Unclaim();
                        return;
                    }
                    if (((FriendlyShipIntel)intelItems[intelKey]).HomeID != Connector.EntityId)
                    {
                        Unclaim();
                    }
                }
            }
            if (ClaimElapsed)
            {
                if ((hangarStatus & HangarStatus.Docking) != 0)
                {
                    CloseGates();
                    hangarStatus &= ~HangarStatus.Docking;
                }
            }
            if (LaunchElapsed)
            {
                if ((hangarStatus & HangarStatus.Launching) != 0)
                {
                    CloseGates();
                    hangarStatus &= ~HangarStatus.Launching;
                }
            }

            if ((hangarStatus & (HangarStatus.Docking | HangarStatus.Launching)) != 0) SetLights(Color.Yellow);
            else if (!HasClearance()) SetLights(Color.Red);
            else SetLights(Color.Green);
        }

        void SetLights(Color color)
        {
            foreach (var light in lights)
            {
                light.Color = color;
            }
        }

        public void UpdateDisplays()
        {
            if (display == null) return;
            var statusBuilder = Host.Context.SharedStringBuilder;
            statusBuilder.Clear();
            statusBuilder.AppendLine("Exclusive with:");
            foreach (int i in MutexHangars)
                statusBuilder.Append(i).Append(' ');

            statusBuilder.AppendLine(((int)hangarStatus).ToString());
            statusBuilder.AppendLine(((int)HangarTags).ToString());

            display.WriteText(statusBuilder.ToString());
        }

        public string Serialize()
        {
            return $"{(int)hangarStatus}|{OwnerID}";
        }
        public void Deserialize(string serialized)
        {
            var split = serialized.Split('|');
            hangarStatus = (HangarStatus)int.Parse(split[0]);
            OwnerID = long.Parse(split[1]);
        }
    }

    public class HangarSubsystem : ISubsystem, IInventoryRefreshRequester
    {
        #region ISubsystem
        public UpdateFrequency UpdateFrequency => UpdateFrequency.Update10 | UpdateFrequency.Update100;

        public void Command(TimeSpan timestamp, string command, object argument)
        {
            if (command == "clear") ClearOwners(argument);
        }
        public void CommandV2(TimeSpan timestamp, CommandLine command)
        {

        }
        public void DeserializeSubsystem(string serialized)
        {
            var reader = new MyStringReader(serialized);

            while (reader.HasNextLine)
            {
                var split = reader.NextLine().Split('-');
                if (split.Length != 2) continue;
                var n = int.Parse(split[0]);
                if (Hangars[n] == null) continue;
                Hangars[n].Deserialize(split[1]);
            }
        }

        public string GetStatus()
        {
            return string.Empty;
        }

        public string SerializeSubsystem()
        {
            var saveBuilder = Context.SharedStringBuilder;
            saveBuilder.Clear();
            for (int i = 0; i < Hangars.Count(); i++)
            {
                if (Hangars[i] != null)
                {
                    saveBuilder.AppendLine($"{i}-{Hangars[i].Serialize()}");
                }
            }
            return saveBuilder.ToString();
        }

        public void Setup(ExecutionContext context, string name)
        {
            Context = context;

            GetParts();

            HangarChannelTag = Context.Reference.CubeGrid.EntityId.ToString() + "-HANGAR";
            HangarListener = Context.IGC.RegisterBroadcastListener(HangarChannelTag);
        }

        public void Update(TimeSpan timestamp, UpdateFrequency updateFlags)
        {
            if ((updateFlags & UpdateFrequency.Update10) != 0)
            {
                ReportAndUpdateHangars(timestamp);
                ProcessHangarRequests(timestamp);
            }
            else if ((updateFlags & UpdateFrequency.Update100) != 0)
            {
                UpdateHangardisplays();
            }
        }
        #endregion

        #region IInventoryRefreshRequester
        public bool RequestingRefresh()
        {
            return requestingRefresh;
        }

        public void AcknowledgeRequest()
        {
            requestingRefresh = false;
        }

        bool requestingRefresh = false;
        Dictionary<Hangar, MyShipConnectorStatus> lastConnectorStatuses = new Dictionary<Hangar, MyShipConnectorStatus>();

        void UpdateRefreshRequests()
        {
            
        }
        #endregion

        public ExecutionContext Context;
        string Tag;
        string TagPrefix;

        string HangarChannelTag;
        IMyBroadcastListener HangarListener;

        IIntelProvider IntelProvider;

        IMyShipController controller;
        IMyInteriorLight DirectionIndicator;

        public Dictionary<long, Hangar> HangarsDict = new Dictionary<long, Hangar>(64);
        public List<Hangar> SortedHangarsList = new List<Hangar>();

        public Hangar[] Hangars = new Hangar[64];
        public MyIni IniParser = new MyIni();

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
            DirectionIndicator = null;
            foreach (var hangar in Hangars)
                hangar?.Clear();
            HangarsDict.Clear();
            lastConnectorStatuses.Clear();
            SortedHangarsList.Clear();

            Context.Terminal.GetBlocksOfType<IMyTerminalBlock>(null, CollectParts);
            foreach (var hangar in Hangars)
            {
                if (IsHangarOk(hangar))
                {
                    SortedHangarsList.Add(hangar);
                    HangarsDict[hangar.Connector.EntityId] = hangar;
                }
            }
        }

        bool CollectParts(IMyTerminalBlock block)
        {
            if (!Context.Reference.IsSameConstructAs(block)) return false;

            if (block is IMyShipController)
            {
                controller = (IMyShipController)block;
                return false;
            }

            var tagindex = block.CustomName.IndexOf(TagPrefix);

            if (tagindex == -1) return false;

            var indexTagEnd = block.CustomName.IndexOf(']', tagindex);
            if (indexTagEnd == -1) return false;

            var numString = block.CustomName.Substring(tagindex + TagPrefix.Length, indexTagEnd - tagindex - TagPrefix.Length);

            if (numString == string.Empty)
            {
                // No number - master systems
                if (block is IMyInteriorLight && block.CustomName.Contains("<DI>")) DirectionIndicator = (IMyInteriorLight)block;
                return false;
            }

            int hangarIndex;
            if (!int.TryParse(numString, out hangarIndex)) return false;
            if (Hangars[hangarIndex] == null)
                Hangars[hangarIndex] = new Hangar(hangarIndex, this);
            Hangars[hangarIndex].AddPart(block);

            return false;
        }

        DockIntel GetHangarIntel(Hangar hangar, TimeSpan timestamp)
        {
            var builder = Context.SharedStringBuilder;
            builder.Clear();
            hangar.Intel.WorldMatrix = hangar.Connector.WorldMatrix;
            hangar.Intel.CurrentVelocity = controller.GetShipVelocities().LinearVelocity;
            hangar.Intel.Status = hangar.hangarStatus;
            hangar.Intel.OwnerID = hangar.OwnerID;
            hangar.Intel.CurrentCanonicalTime = timestamp + IntelProvider.CanonicalTimeDiff;

            hangar.Intel.ID = hangar.Connector.EntityId;
            if (string.IsNullOrEmpty(hangar.Intel.DisplayName))
            {
                hangar.Intel.DisplayName = builder.Append(hangar.Connector.CustomName).Append(" - ").Append(Context.Reference.CubeGrid.CustomName).ToString();
            }

            hangar.Intel.UndockNear = hangar.Connector.CubeGrid.GridSizeEnum == MyCubeSize.Large ? 1.3f : 0.55f;
            hangar.Intel.UndockFar = hangar.ClearanceDist;

            hangar.Intel.IndicatorDir = hangar.DirectionIndicator == null ? (DirectionIndicator == null ? Vector3D.Zero : DirectionIndicator.WorldMatrix.Forward) : hangar.DirectionIndicator.WorldMatrix.Forward;
            hangar.Intel.HangarChannelTag = HangarChannelTag;
            hangar.Intel.Tags = hangar.HangarTags;

            return hangar.Intel;
        }

        void ClearOwners(object argument)
        {
            if (argument == null)
            {
                foreach (var hangar in Hangars)
                {
                    if (IsHangarOk(hangar))
                    {
                        hangar.OwnerID = -1;
                    }
                }
            }
            else if (argument is string)
            {
                int index;
                if (int.TryParse((string)argument, out index))
                {
                    Hangars[index].OwnerID = -1;
                }
            }
        }

        void ReportAndUpdateHangars(TimeSpan timestamp)
        {
            var intelItems = IntelProvider.GetFleetIntelligences(timestamp);
            foreach (var hangar in Hangars)
            {
                if (IsHangarOk(hangar))
                {
                    IntelProvider.ReportFleetIntelligence(GetHangarIntel(hangar, timestamp), timestamp);
                    hangar.Update(timestamp, intelItems);

                    if (requestingRefresh == false && lastConnectorStatuses.ContainsKey(hangar) && 
                        ((lastConnectorStatuses[hangar] == MyShipConnectorStatus.Connected && hangar.Connector.Status != MyShipConnectorStatus.Connected) ||
                        (lastConnectorStatuses[hangar] != MyShipConnectorStatus.Connected && hangar.Connector.Status == MyShipConnectorStatus.Connected)))
                    {
                        requestingRefresh = true;
                    }
                    lastConnectorStatuses[hangar] = hangar.Connector.Status;
                }
            }
        }

        void ProcessHangarRequests(TimeSpan timestamp)
        {
            while (HangarListener.HasPendingMessage)
            {
                var data = HangarListener.AcceptMessage().Data;
                if (!(data is MyTuple<long, long, int>)) return;
                var unpacked = (MyTuple<long, long, int>)data;

                Hangar hangar; 
                if (!HangarsDict.TryGetValue(unpacked.Item2, out hangar))
                    return;
                hangar.TakeRequest(unpacked.Item1, (HangarRequest)unpacked.Item3, timestamp);
            }
        }

        bool IsHangarOk(Hangar hangar)
        {
            return hangar != null && hangar.Connector != null;
        }
        void UpdateHangardisplays()
        {
            foreach (var hangar in Hangars)
            {
                if (IsHangarOk(hangar))
                {
                    hangar.UpdateDisplays();
                }
            }
        }
    }
}
