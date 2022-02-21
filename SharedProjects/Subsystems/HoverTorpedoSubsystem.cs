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
    public class HoverTorpedoSubsystem : ISubsystem
    {
        #region ISubsystem
        public UpdateFrequency UpdateFrequency { get; set; }

        public void Command(TimeSpan timestamp, string command, object argument)
        {
            if (command == "toggleauto")
            {
                HoverTorpedoTubeGroup group;
                if (HoverTorpedoTubeGroups.TryGetValue((string)argument, out group))
                {
                    group.AutoFire = !group.AutoFire;
                    // Update Indicators
                    foreach (var light in group.AutofireIndicator) light.Color = group.AutoFire ? Color.LightPink : Color.LightGreen;
                    EngagedTargetsCullScratchpad.AddRange(group.EngagedTargets.Keys);
                }
            }
        }
        public void CommandV2(TimeSpan timestamp, CommandLine command)
        {
//            Context.Log.Debug("Torpedo.CommandV2 Argument(0)="+ command.Argument(0) );
            if (command.Argument(0) == "fire")
            {
                CommandFire(command, timestamp, FireCommandType.Normal);
            }
            else if (command.Argument(0) == "firetrick")
            {
                CommandFire(command, timestamp, FireCommandType.Trickshot);
            }
            else if (command.Argument(0) == "firespread")
            {
                CommandFire(command, timestamp, FireCommandType.Spread);
            }
        }

        public void DeserializeSubsystem(string serialized)
        {
        }

        public string GetStatus()
        {
            var debugBuilder = Context.SharedStringBuilder;
            debugBuilder.Clear();
            foreach (var kvp in HoverTorpedoTubeGroups)
                debugBuilder.AppendLine("Grp [" + kvp.Key + "] #" + kvp.Value.Children.Count);

            for (int i = 0; i < HoverTorpedoTubes.Length; ++i)
            {
                var tube = HoverTorpedoTubes[i];
                if (tube == null)
                    debugBuilder.AppendLine("[" + i + "]=null");
                else if (!tube.HasRelease() || !tube.Ready)
                    debugBuilder.AppendLine("[" + i + "]= Rdy:" + tube.Ready + " Rls:" + tube.HasRelease());
            }

            return debugBuilder.ToString();
        }

        public string SerializeSubsystem()
        {
            return string.Empty;
        }

        public void Setup( ExecutionContext context, string name )        
        {
            Context = context;

            HoverTorpedoTubeGroups["SM"] = new HoverTorpedoTubeGroup(this, "SM");
            HoverTorpedoTubeGroups["LG"] = new HoverTorpedoTubeGroup(this, "LG");

            ParseConfigs();

            GetParts();

            foreach (var group in HoverTorpedoTubeGroups.Values)
            {
                foreach (var light in group.AutofireIndicator) light.Color = group.AutoFire ? Color.LightPink : Color.LightGreen;
            }

            // JIT
            CommandFire(null, TimeSpan.Zero, FireCommandType.Normal);
        }

        bool FilterAutofireTarget(TimeSpan timestamp, TimeSpan canonicalTime, HoverTorpedoTubeGroup group, EnemyShipIntel target, TargetInfo info)
        {
            bool valid = true;
//             if (info != null)
//                 valid &= timestamp - info.LastLaunch < TimeSpan.FromMilliseconds(group.AutoFireTargetMS);

            int targetSizeFlag = (1 << (int)(target.CubeSize));
            targetSizeFlag &= group.AutoFireSizeMask;

            valid &= targetSizeFlag != 0
                            && target.Radius > group.AutoFireRadius
                            && (target.GetPositionFromCanonicalTime(canonicalTime) - Context.Reference.GetPosition()).Length() < group.AutoFireRange;
            
            return valid;
        }

        bool FilterNormalTarget(TimeSpan timestamp, TimeSpan canonicalTime, HoverTorpedoTubeGroup group, EnemyShipIntel target, TargetInfo info)
        {
            return true;
        }

//         void AssignAutoFireTargets()
//         {
// 
//         }

        private void CollectTargets(TimeSpan timestamp, HoverTorpedoTubeGroup group, TimeSpan canonicalTime, TimeSpan targetInterval, Func<TimeSpan, TimeSpan, HoverTorpedoTubeGroup, EnemyShipIntel, TargetInfo, bool> funcValidTarget )
        {
            var intelItems = IntelProvider.GetFleetIntelligences(timestamp);

            // NOTE: This may be slow in fleet engagement.
            foreach (var kvp in intelItems)
            {
                if (kvp.Key.Item1 != IntelItemType.Enemy)
                    continue;

                var target = (EnemyShipIntel)kvp.Value;

                TargetInfo targetInfo;
                group.EngagedTargets.TryGetValue(target, out targetInfo);

                if (funcValidTarget(timestamp, canonicalTime, group, target, targetInfo))
                {
                    if (targetInfo == null)
                    {
                        targetInfo = new TargetInfo();
                        group.EngagedTargets.Add(target, targetInfo);
                        targetInfo.LastLaunch = TimeSpan.Zero;
                        targetInfo.Launches = 0;
                        targetInfo.Interval = targetInterval;
                    }
                }
            }
        }

        public void Update(TimeSpan timestamp, UpdateFrequency updateFlags)
        {
            if ((updateFlags & UpdateFrequency.Update100) != 0)
            {
                for (int i = 0; i < HoverTorpedoTubes.Count(); i++)
                {
                    var tube = HoverTorpedoTubes[i];
                    if (tube != null)
                    {
                        if (tube.HasRelease())
                            tube.Update(timestamp);

                        if (tube.Ready)
                            tube.Group.LoadingMask &= ~(1L << tube.Index);
                        else
                            tube.Group.LoadingMask |= 1L << tube.Index;
                    }
                }

                var welder = TorpedoWeldersRR.GetAndAdvance();
                if (welder != null)
                {
                    welder.UpdateWelder(timestamp, this);
                }
            }

            if ((updateFlags & UpdateFrequency.Update1) != 0)
            {
                var canonicalTime = timestamp + IntelProvider.CanonicalTimeDiff;
                runs++;

                if (runs % 9931 == 0)
                {
                    foreach (var group in HoverTorpedoTubeGroups.Values)
                    {
                        foreach (var kvp in group.EngagedTargets)
                        {
                            if (IntelProvider.GetLastUpdatedTime(MyTuple.Create(IntelItemType.Enemy, kvp.Key.ID)) == TimeSpan.MaxValue )
                            {
                                EngagedTargetsCullScratchpad.Add(kvp.Key);
//                                Context.Log.Debug("Expired K:" + kvp.Key.DisplayName);
                            }    
                        }
                    }
                }

                if ( EngagedTargetsCullScratchpad.Count > 0 )
                {
                    foreach (var group in HoverTorpedoTubeGroups.Values)
                    {
                        foreach (var cull in EngagedTargetsCullScratchpad)
                        {
                            group.EngagedTargets.Remove(cull);
                        }
                    }
                    EngagedTargetsCullScratchpad.Clear();
                }

                if (runs % 60 == 0)
                {
                    foreach (var group in HoverTorpedoTubeGroups.Values)
                    {
                        if (group.AutoFire)
                        {
                            CollectTargets(timestamp, group, canonicalTime, TimeSpan.FromMilliseconds(group.AutoFireTargetMS), FilterAutofireTarget );
                        }
                    }
                }

                foreach (var group in HoverTorpedoTubeGroups.Values)
                {
                    if (group.EngagedTargets.Count > 0 && 
                        group.NumReady > 0 &&
                        timestamp - group.LastLaunch > TimeSpan.FromMilliseconds(group.AutoFireTubeMS) )
                    {
                        
                        EnemyShipIntel neglectedTarget = null;
                        TargetInfo neglectedInfo = null;
                        int neglectedLaunches = int.MaxValue;

                        foreach (var kvp in group.EngagedTargets)
                        {
                            var info = kvp.Value;

                            if (timestamp - info.LastLaunch < info.Interval)
                                continue;

                            if (info.Requests > 0)
                            {
                                if (Fire(timestamp, group, kvp.Key) != null)
                                {
                                    info.Launches++;
                                    if (info.Requests > 0)
                                    {
                                        info.Requests--;
                                        if (info.Requests == 0)
                                            EngagedTargetsCullScratchpad.Add(kvp.Key);
                                    }
                                    info.LastLaunch = timestamp;
                                    neglectedTarget = null;
                                    break;
                                }
                            }
                            else if ( info.Launches < neglectedLaunches)
                            {
                                neglectedInfo = info;
                                neglectedTarget = kvp.Key;
                                neglectedLaunches = info.Launches;
                            }
                        }

                        if ( neglectedTarget != null )
                        {
//                            Context.Log.Debug("Autofire Load Balancing");
                            if ( Fire(timestamp, group, neglectedTarget) != null )
                            {
                                neglectedInfo.Launches++;
                                neglectedInfo.LastLaunch = timestamp;
                            }
                        }
                    }
                }

                if (runs % 6 == 0)
                {
                    // Update Torpedos
                    TorpedoScratchpad.Clear();

                    var intelItems = IntelProvider.GetFleetIntelligences(timestamp);

                    foreach (var torp in Torpedos)
                    {
                        if (torp.Target != null)
                        {
                            var intelKey = MyTuple.Create(IntelItemType.Enemy, torp.Target.ID);
                            if (!intelItems.ContainsKey(intelKey))
                            {
                                torp.Target = null;
                            }
                            else
                            {
                                torp.Update((EnemyShipIntel)intelItems[intelKey], canonicalTime);
                            }
                        }

                        if (torp.Target == null)
                        {
                            EnemyShipIntel combatIntel = null;
                            double closestIntelDist = double.MaxValue;
                            foreach (var intel in intelItems)
                            {
                                if (intel.Key.Item1 != IntelItemType.Enemy) continue;
                                var enemyIntel = (EnemyShipIntel)intel.Value;

                                if (!EnemyShipIntel.PrioritizeTarget(enemyIntel)) continue;

                                if (IntelProvider.GetPriority(enemyIntel.ID) < 2) continue;

                                if (enemyIntel.Radius < 30 || enemyIntel.CubeSize != MyCubeSize.Large) continue;

                                double dist = (enemyIntel.GetPositionFromCanonicalTime(canonicalTime) - torp.Controller.WorldMatrix.Translation).Length();
                                if (IntelProvider.GetPriority(enemyIntel.ID) == 3) dist -= 1000;
                                if (IntelProvider.GetPriority(enemyIntel.ID) == 4) dist -= 1000;
                                if (dist < closestIntelDist)
                                {
                                    closestIntelDist = dist;
                                    combatIntel = enemyIntel;
                                }
                            }
                            torp.Update(combatIntel, canonicalTime);
                        }
                        if (torp.Disabled) TorpedoScratchpad.Add(torp);
                    }

                    foreach (var torp in TorpedoScratchpad) Torpedos.Remove(torp);
                }

                TorpedoScratchpad.Clear();
                foreach (var torp in Torpedos)
                {
                    var extend = torp.Controller.GetShipSpeed() * 0.017 + (torp.Controller.CubeGrid.GridSizeEnum == MyCubeSize.Large ? 11.5 : 3);

//                     if (torp.TubeIndex >= 0 && !torp.Controller.CubeGrid.IsSameConstructAs(Context.Reference.CubeGrid))
//                     {
//                         Context.Log.Debug("Detach Tube #" + torp.TubeIndex);
//                         torp.TubeIndex = -1;
//                     }

                    torp.FastUpdate();
                    if (torp.proxArmed && torp.Target != null)
                    {
                        if ((torp.Controller.GetPosition() - torp.Target.GetPositionFromCanonicalTime(canonicalTime)).LengthSquared() < extend)
                        {
                            TorpedoScratchpad.Add(torp);
                            torp.Detonate();
                        }
                        else if (torp.Fuse != null)
                        {
                            if (torp.Fuse.CubeGrid == null || !torp.Fuse.CubeGrid.CubeExists(torp.Fuse.Position))
                            {
                                TorpedoScratchpad.Add(torp);
                                torp.Detonate();
                            }
                        }
                        else if (torp.Cameras.Count > 0)
                        {
                            for (int i = 0; i < torp.Cameras.Count; i++)
                            {
                                if (!torp.Cameras[i].IsWorking) continue;
                                MyDetectedEntityInfo detected = torp.Cameras[i].Raycast(extend + torp.CameraExtends[i]);
                                if (detected.EntityId != 0 && (detected.Relationship == MyRelationsBetweenPlayerAndBlock.Enemies || detected.Relationship == MyRelationsBetweenPlayerAndBlock.Neutral || detected.Relationship == MyRelationsBetweenPlayerAndBlock.NoOwnership))
                                {
                                    TorpedoScratchpad.Add(torp);
                                    torp.Detonate();
                                }
                            }
                        }
                        else if (torp.Sensor != null && torp.Sensor.IsWorking)
                        {
                            torp.Sensor.DetectedEntities(DetectedInfoScratchpad);
                            if (DetectedInfoScratchpad.Count > 0)
                            {
                                DetectedInfoScratchpad.Clear();
                                TorpedoScratchpad.Add(torp);
                                torp.Detonate();
                            }
                        }
                    }
                }
                foreach (var torp in TorpedoScratchpad) Torpedos.Remove(torp);
            }
        }
        #endregion

        public HoverTorpedoSubsystem(IIntelProvider intelProvider)
        {
            UpdateFrequency = UpdateFrequency.Update1 | UpdateFrequency.Update10 | UpdateFrequency.Update100;
            IntelProvider = intelProvider;
        }

        public HoverTorpedoTube[] HoverTorpedoTubes = new HoverTorpedoTube[16];
        public Dictionary<string, HoverTorpedoTubeGroup> HoverTorpedoTubeGroups = new Dictionary<string, HoverTorpedoTubeGroup>();
        public List<EnemyShipIntel> EngagedTargetsCullScratchpad = new List<EnemyShipIntel>();

        public HashSet<HoverTorpedo> Torpedos = new HashSet<HoverTorpedo>();

        public List<HoverTorpedo> TorpedoScratchpad = new List<HoverTorpedo>();
        public List<IMyTerminalBlock> PartsScratchpad = new List<IMyTerminalBlock>();
        List<MyDetectedEntityInfo> DetectedInfoScratchpad = new List<MyDetectedEntityInfo>();

        public class TorpedoWelder
        {
            IMyShipWelder Welder;
            TimeSpan WelderWake;
            Dictionary<HoverTorpedoTubeGroup, long> Tubes = new Dictionary<HoverTorpedoTubeGroup, long>();
            public TorpedoWelder(HoverTorpedoSubsystem Host, IMyShipWelder welder)
            {
                Welder = welder;
                
//                Host.Context.Log.Debug("WELDER INIT " + Welder.CustomName);
                
                Host.IniParser.TryParse(welder.CustomData);
                var tubeProperty = Host.IniParser.Get("Torpedo", "Tubes").ToString();

                if (tubeProperty != string.Empty)
                {
                    var tubeList = tubeProperty.Split(',');
                    foreach (var token in tubeList)
                    {
                        var tubePair = token.Split('^');
                        HoverTorpedoTubeGroup group = Host.HoverTorpedoTubeGroups[tubePair[0]];

                        long mask;
                        if (!Tubes.TryGetValue(group, out mask))
                            Tubes.Add(group, 0);

                        Tubes[group] = mask | 1L << int.Parse(tubePair[1]);
                    }
//                     foreach (var pair in Tubes)
//                     {
//                         Host.Context.Log.Debug(pair.Key.Name + " " + pair.Value);
//                     }
                }
            }
            public void SupressWelder(TimeSpan localTime, HoverTorpedoTubeGroup group, int index)
            {
                long mask;
                if (Tubes.TryGetValue(group, out mask))
                {
                    if ((mask & (1L << index)) == 0)
                        return;

                    Welder.Enabled = false;
                    WelderWake = localTime + TimeSpan.FromMilliseconds(group.ReloadCooldownMS);
                    
//                     group.Host.Context.Log.Debug("SUPRESSED WELDER VIA TUBE "+ index );
                }
            }

            public void UpdateWelder(TimeSpan localTime, HoverTorpedoSubsystem host)
            {
//                host.Context.Log.Debug("Supressed: " + (localTime < WelderWake).ToString());
                if (localTime < WelderWake)
                    return;

                foreach( var group in host.HoverTorpedoTubeGroups)
                {
                    long mask;
                    if (Tubes.TryGetValue(group.Value, out mask))
                    {
                        if ((group.Value.LoadingMask & mask) != 0)
                        {
                            Welder.Enabled = true;
                            return;
                        }
                    }
//                     else
//                     {
//                         host.Context.Log.Debug("Missing: "+ group.Key); 
//                     }
                }

                Welder.Enabled = false;
            }
        }

        public RoundRobin<TorpedoWelder> TorpedoWeldersRR = new RoundRobin<TorpedoWelder>();


        //         HashSet<long> AutofireTargetLog = new HashSet<long>();
        //         Queue<Torpedo> ReserveTorpedoes = new Queue<Torpedo>();

        public ExecutionContext Context;

        public IIntelProvider IntelProvider;

        public MyIni IniParser = new MyIni();

        long runs;
        void ParseTubeGroupConfig(MyIni parser, string groupName, HoverTorpedoTubeGroup group)
        {
            string section = "HoverTorpedo";
            if (parser.ContainsSection(section + "_" + groupName))
            {
                section = section + "_" + groupName;
            }
            group.GuidanceStartSeconds = (float)parser.Get(section, "GuidanceStartSeconds").ToDouble(2.0);

            group.PlungeDist = parser.Get(section, "PlungeDist").ToInt16(1000);
            group.HitOffset = parser.Get(section, "HitOffset").ToDouble(0.0);
            group.ReloadCooldownMS = parser.Get(section, "ReloadCooldownMS").ToInt32(group.ReloadCooldownMS);

            group.AutoFire = parser.Get(section, "AutoFire").ToBoolean();
            group.AutoFireRange = parser.Get(section, "AutoFireRange").ToInt16(15000);
            group.AutoFireTubeMS = parser.Get(section, "AutoFireTubeMS").ToInt16(500);
            group.AutoFireTargetMS = parser.Get(section, "AutoFireTargetMS").ToInt16(2000);
            group.AutoFireRadius = parser.Get(section, "AutoFireRadius").ToInt16(30);
            group.AutoFireSizeMask = parser.Get(section, "AutoFireSizeMask").ToInt16(1);

            string partsSection = "Torpedo_Parts";
            if (parser.ContainsSection(partsSection + "_" + groupName))
            {
                partsSection = partsSection + "_" + groupName;
            }

            List<MyIniKey> partKeys = new List<MyIniKey>();
            parser.GetKeys(partsSection, partKeys);
            foreach (var key in partKeys)
            {
                var count = parser.Get(key).ToInt32();
                if (count == 0)
                    continue;
                var type = MyItemType.Parse(key.Name);
                group.TorpedoParts[type] = count;
            }
        }

        // [DEPRECIATED] AutoFireSeconds = 2.0;

        // [Torpedo] 
        // TorpedoTubeCount = 16
        // NOTE: [Torpedo_SM] & [Torpedo_LG] work for specific torpedo groups, otherwise it uses the default tag.
        // [Torpedo] 
        // GuidanceStartSeconds = 2
        // PlungeDist = 1000
        // HitOffset = 0
        // ReloadCooldownMS = 3500

        // AutoFire = True
        // AutoFireRange = 3000
        // AutoFireTubeMS = 500;
        // AutoFireTargetMS = 2000;
        // AutoFireRadius = 30; In Meters
        // AutoFireSizeMask = 1; 1 = LG 2 = SM 3 = BOTH

        // Trickshot = false
        // TrickshotDistance = 1200
        // TrickshotTerminalDistance = 1000

        // Evasion = False
        // EvasionDistStart = 2000
        // EvasionDistEnd = 800
        // EvasionAdjTimeMin = 500
        // EvasionAdjTimeMax = 1000
        // EvasionOffsetMagnitude = 5

        void ParseConfigs()
        {
            MyIni Parser = new MyIni();
            if (!Parser.TryParse(Context.Reference.CustomData))
                return;

            string section = "Torpedo";
            if (Parser.ContainsSection(section))
            {
                int TorpedoTubeCount = Parser.Get(section, "TorpedoTubeCount").ToInt16(16);
                if (HoverTorpedoTubes.Length != TorpedoTubeCount)
                {
                    HoverTorpedoTubes = new HoverTorpedoTube[TorpedoTubeCount];
                }
            }

            foreach (var group in HoverTorpedoTubeGroups)
            {
                ParseTubeGroupConfig(Parser, group.Key, group.Value);
            }
        }

        void GetParts()
        {
            // This may be a problem at some future time, but I removed the clearing of lists on GetParts call.
            // All grids I know of only collect on rebuild.

            Context.Terminal.GetBlocksOfType<IMyTerminalBlock>(null, CollectParts);
            Context.Terminal.GetBlocksOfType<IMyTerminalBlock>(null, CollectLights);
        }


        HoverTorpedoTube GetOrCreateTubeAtIndex(int index, MyCubeSize size)
        {
            if (index >= HoverTorpedoTubes.Length)
                return null;

            var tube = HoverTorpedoTubes[index];
            if ( tube == null )
            {
                tube = new HoverTorpedoTube(index, this);
                tube.Size = size;                
                HoverTorpedoTubes[index] = tube;
                var group = HoverTorpedoTubeGroups[size == MyCubeSize.Small ? "SM" : "LG"];
                group.AddTube(tube);
                tube.Group = group;
            }

            return tube;
        }

        bool CollectParts(IMyTerminalBlock block)
        {
            if (!Context.Reference.IsSameConstructAs(block)) return false; // Allow subgrid

            var tagindex = block.CustomName.IndexOf("[TRP");

            if (tagindex == -1) return false;

            var size = block.CubeGrid.GridSizeEnum;
            var torpedoWelder = block.CustomName.IndexOf("[TRPW]");

            if (torpedoWelder != -1)
                TorpedoWeldersRR.Items.Add(new TorpedoWelder(this, block as IMyShipWelder));

            var thrusterDetach = block.CustomName.IndexOf("[TRPT]");
            if ( thrusterDetach != -1 )
            {
                IniParser.TryParse(block.CustomData);
                var releasesProperty = IniParser.Get("Torpedo", "ThrusterReleases").ToString();

                var releases = releasesProperty.Split(',');

                foreach (var release in releases )
                {
                    var tuple = release.Split('^');
                    
                    var tube = GetOrCreateTubeAtIndex(int.Parse(tuple[0]), size);
                    if (tube == null) return false;

                    if (tube.Release == null)
                        tube.Release = new ThrusterRelease(block, tuple[1]);
                }
            }
            else
            {
                var indexTagEnd = block.CustomName.IndexOf(']', tagindex);
                if (indexTagEnd == -1) return false;

                var numString = block.CustomName.Substring(tagindex + 4, indexTagEnd - tagindex - 4);

                int index;
                if (!int.TryParse(numString, out index))
                    return false;

                var tube = GetOrCreateTubeAtIndex(index, size);
                if (tube == null) 
                    return false;

                if ( tube.Release == null )
                {
                    var mech = block as IMyMechanicalConnectionBlock;
                    if (mech != null)
                        tube.Release = new MechanicalRelease(mech);

                    var merge = block as IMyShipMergeBlock;
                    if (merge != null)
                        tube.Release = new MergeRelease(merge);
                }

                if (block is IMyShipConnector)
                    tube.Connector = (IMyShipConnector)block;
            }

            return false;
        }

        bool CollectLights(IMyTerminalBlock block)
        {
            if (!Context.Reference.IsSameConstructAs(block)) return false; // Allow subgrid

            if (!block.CustomName.Contains("[TRP]")) return false;

            if (!(block is IMyInteriorLight)) return false;

            var groupName = block.CubeGrid.GridSizeEnum == MyCubeSize.Small ? "SM" : "LG";
            if (block.CustomName.Contains("LG")) groupName = "LG";
            if (block.CustomName.Contains("SM")) groupName = "SM";
            HoverTorpedoTubeGroup group;
            if (HoverTorpedoTubeGroups.TryGetValue(groupName, out group))
            {
                var light = (IMyInteriorLight)block;
                group.AutofireIndicator.Add(light);
            }

            return false;
        }

        public enum FireCommandType
        {
            Normal,
            Trickshot,
            Spread,
        };

        void CommandFire(CommandLine commandLine, TimeSpan localTime, FireCommandType type)
        {
            if (commandLine == null)
                return;

            if (commandLine.Argument(1) == null)
                return;

//            Context.Log.Debug("CommandFire "+ commandLine.Argument(1));

            HoverTorpedoTubeGroup group = null;
            int LaunchIntervalMS = 500;

            if (HoverTorpedoTubeGroups.TryGetValue(commandLine.Argument(1), out group))
                LaunchIntervalMS = group.AutoFireTubeMS;
            else
                return;

            int LaunchCount = 1;
            if (commandLine.Argument(2) != null)
                int.TryParse(commandLine.Argument(2), out LaunchCount);


            if (commandLine.Argument(3) != null)
                int.TryParse(commandLine.Argument(3), out LaunchIntervalMS);

//            Context.Log.Debug("Firing " + type.ToString() + " C" + LaunchCount + " I" + LaunchIntervalMS);

            if (FireCommandType.Spread == type )
            {
                int readyTorps = Math.Min(group.NumReady, LaunchCount);
                if (readyTorps == 0)
                    return;

                var canonicalTime = localTime + IntelProvider.CanonicalTimeDiff;
                TimeSpan LaunchInterval = TimeSpan.FromMilliseconds(LaunchIntervalMS);
                CollectTargets(localTime, group, canonicalTime, LaunchInterval, FilterNormalTarget);

                if ( group.EngagedTargets.Count > 0 )
                {
                    int launchesPer = readyTorps / group.EngagedTargets.Count;
                    int remainder = readyTorps % group.EngagedTargets.Count;

                    foreach (var kvp in group.EngagedTargets)
                    {
                        kvp.Value.Requests += launchesPer;
                        if (remainder-- > 0)
                            kvp.Value.Requests++;

//                        Context.Log.Debug("Firing @" + kvp.Key.DisplayName + " R" + kvp.Value.Requests);
                    }
                }

            }
            else
            {
                var intelItems = IntelProvider.GetFleetIntelligences(localTime);
                var key = MyTuple.Create(IntelItemType.Enemy, (long)-1);
                EnemyShipIntel target = (EnemyShipIntel)(intelItems.GetValueOrDefault(key, null));

                if ( LaunchCount > 1 )
                {
                    if ( target != null )
                    {
                        TargetInfo targetInfo;
                        if (!group.EngagedTargets.TryGetValue(target, out targetInfo))
                        {
                            targetInfo = new TargetInfo();
                            group.EngagedTargets.Add(target, targetInfo);
                        }
                        targetInfo.Requests = LaunchCount - 1;
                        targetInfo.LastLaunch = localTime;
                        targetInfo.Interval = TimeSpan.FromMilliseconds(LaunchIntervalMS);
                    }
                }

                Fire(localTime, group, target);
            }
        }

        public HoverTorpedo Fire(TimeSpan localTime, HoverTorpedoTubeGroup unit, EnemyShipIntel target = null)
        {
            if (unit == null || !unit.Ready) return null;
            var torp = unit.Fire(localTime, localTime + IntelProvider.CanonicalTimeDiff, target);
            if (torp != null)
            {
                Torpedos.Add(torp);
                foreach (var subtorp in torp.SubTorpedos) 
                    Torpedos.Add(subtorp);
                return torp;
            }
            return null;
        }
    }

    // This is a refhax torpedo
    public class HoverTorpedo
    {
        public enum AltModeStage
        {
            Off,
            Setup,
            Active,
            Terminal,
        }

        public HoverTorpedoTubeGroup Group;
        public List<IMyGyro> Gyros = new List<IMyGyro>();
        public HashSet<IMyWarhead> Warheads = new HashSet<IMyWarhead>();
        public HashSet<IMyThrust> Thrusters = new HashSet<IMyThrust>();
        public HashSet<IMyBatteryBlock> Batteries = new HashSet<IMyBatteryBlock>();
        public HashSet<IMyGasTank> Tanks = new HashSet<IMyGasTank>();
        public List<IMyCameraBlock> Cameras = new List<IMyCameraBlock>();
        public List<float> CameraExtends = new List<float>();
        public IMySensorBlock Sensor;
        public IMyShipController Controller;
        public int TubeIndex;
        public string Tag; // This is the number of the index of SubMissiles. Legacy: HE, CLST, MICRO, etc
        public HashSet<IMyShipMergeBlock> Splitters = new HashSet<IMyShipMergeBlock>();

        public HashSet<HoverTorpedo> SubTorpedos = new HashSet<HoverTorpedo>();

        public IMyTerminalBlock Fuse;

        GyroControl gyroControl;
        GyroController gyroControllerV2;

        PDController yawController = new PDController(DEF_PD_P_GAIN, DEF_PD_D_GAIN, 10);
        PDController pitchController = new PDController(DEF_PD_P_GAIN, DEF_PD_D_GAIN, 10);
        PDController rollController = new PDController(DEF_PD_P_GAIN, DEF_PD_D_GAIN, 10);

        TimeSpan launchTime = TimeSpan.Zero;

        public bool Reserve = false;
        public TimeSpan ReserveTime;
        public bool Disabled = false;
        public IFleetIntelligence Target = null;

        double lastSpeed;
        Vector3D lastTargetVelocity;

        public Vector3D AccelerationVector;

        bool initialized = false;
        public bool canInitialize = true;
        int runs = 0;

        Vector3D RandomHitboxOffset;

        public bool proxArmed = false;
        public HoverTorpedoSubsystem HostSubsystem = null;

        public bool AddPart(IMyTerminalBlock block)
        {
            bool part = false;
            if (block.CustomName.Contains("[F]")) { Fuse = block; part = true; }
            if (block is IMyShipController) { Controller = (IMyShipController)block; part = true; }
            if (block is IMyGyro) { Gyros.Add((IMyGyro)block); part = true; }
            if (block is IMyCameraBlock)
            {
                var camera = (IMyCameraBlock)block;
                Cameras.Add(camera);
                camera.EnableRaycast = true;
                float extents;
                float.TryParse(camera.CustomData, out extents);
                CameraExtends.Add(extents);
                part = true;
            }
            if (block is IMySensorBlock) { Sensor = (IMySensorBlock)block; part = true; }
            if (block is IMyThrust) { Thrusters.Add((IMyThrust)block); ((IMyThrust)block).Enabled = false; part = true; }
            if (block is IMyWarhead) { Warheads.Add((IMyWarhead)block); part = true; }
            if (block is IMyShipMergeBlock) { Splitters.Add((IMyShipMergeBlock)block); part = true; }
            if (block is IMyBatteryBlock) { Batteries.Add((IMyBatteryBlock)block); ((IMyBatteryBlock)block).Enabled = false; part = true; }
            if (block is IMyGasTank) { Tanks.Add((IMyGasTank)block); ((IMyGasTank)block).Enabled = true; part = true; }
            return part;
        }

        public void Init(TimeSpan CanonicalTime, HoverTorpedoTubeGroup group)
        {
            initialized = true;
            Group = group;
            gyroControl = new GyroControl(Gyros);
            gyroControllerV2 = new GyroController(Controller, Gyros);
            var refWorldMatrix = Controller.WorldMatrix;
            gyroControl.Init(ref refWorldMatrix);
            foreach (var tank in Tanks) 
                tank.Stockpile = false;

            foreach (var Gyro in Gyros)
            {
                Gyro.GyroOverride = true;
                Gyro.Enabled = true;
            }
            gyroControllerV2.SetEnabled(true);
            gyroControllerV2.SetOverride(true);

            launchTime = CanonicalTime;

            var rand = HostSubsystem.Context.Random;
            RandomHitboxOffset = new Vector3D(rand.NextDouble() - 0.5, rand.NextDouble() - 0.5, rand.NextDouble() - 0.5);
        }

        void Split()
        {
            foreach (var merge in Splitters)
            {
                merge.Enabled = false;
            }
            foreach (var torp in SubTorpedos)
            {
                torp.canInitialize = true;
                if (!torp.Reserve)
                {
                    torp.Init(launchTime, Group);
                    if (torp.Target == null) torp.Target = Target;
                }
            }
            SubTorpedos.Clear();
        }

        public void Update(EnemyShipIntel Target, TimeSpan CanonicalTime)
        {
            if (!initialized) return;
            if (!OK())
            {
                foreach (var Gyro in Gyros)
                {
                    Gyro.Enabled = false;
                }
                Arm();
                Disabled = true;
            }
            if (Disabled) return;

            if (CanonicalTime - launchTime < TimeSpan.FromSeconds(Group.GuidanceStartSeconds))
                return;
            if (Target == null) return;

            if (CanonicalTime - launchTime > TimeSpan.FromSeconds(Group.GuidanceStartSeconds + 1) && SubTorpedos.Count > 0) Split();

            this.Target = Target;

            /*
                Range of Projectile = Vx * [Vy + √(Vy² +2 * g * h)] / g
                Vx = Velocity Toward Target
                Vy = Velocity Up Target
                g = Gravity
                h = Initial Height
            */

            var targetPos = Target.GetPositionFromCanonicalTime(CanonicalTime);
            var torpedoPos = Controller.WorldMatrix.Translation;
            var velocity = Controller.GetShipVelocities().LinearVelocity;
            var velocityNormal = Vector3D.Normalize(velocity);
            var gravity = Controller.GetNaturalGravity();
            var gravityNormal = Vector3D.Normalize(gravity);
            var right = gravityNormal.Cross(velocityNormal);
            var targetVector = targetPos - torpedoPos;
            var targetNormal = Vector3D.Normalize(targetVector);
//            var gravAlignedDistance = (-gravityNormal).Cross(right).Dot(targetVector);

            var Vx = velocity.Dot(targetNormal);
            var Vy = velocity.Dot(-gravityNormal);
            var h = torpedoPos.Dot(-gravityNormal) - targetPos.Dot(-gravityNormal);
            var g = gravity.Length();
            var ballisticRange = Vx * (Vy + Math.Sqrt(Vy * Vy + 2 * g * h)) / g;
            //Group.Host.Context.Log.Debug("Vx: " + Vx.ToString("n2") + " Vy: " + Vy.ToString("n2") + " g: " + g.ToString("n2") + " h: " + h.ToString("n2"));
            var range = Math.Abs(targetVector.Length() - ballisticRange);
            //Group.Host.Context.Log.Debug("Range: " + range.ToString("n2") + "<" + ballisticRange.ToString("n2"));
            if (range < Target.Radius)
            {
                foreach (var thruster in Thrusters)
                    thruster.Enabled = false;
                Arm();
            }
            else
            {
                Vector3D normalAccelerationVector = RefreshNavigation(CanonicalTime);
                AimAtTarget(normalAccelerationVector);
            }
        }

        public void FastUpdate()
        {
            if (initialized)
            {
                runs++;
                if (runs == 2)
                {
//                    HostSubsystem.Context.Log.Debug("Fast Tube #" + TubeIndex);
                    foreach (var thruster in Thrusters)
                    {
                        thruster.Enabled = true;
                        thruster.ThrustOverridePercentage = 1;
                    }
                    foreach (var battery in Batteries)
                    {
                        battery.Enabled = true;
                    }
                }
            }
        }

        public bool OK()
        {
            return Gyros.Count > 0 && Controller != null && Controller.IsFunctional && Thrusters.Count > 0;
        }
        //The GyroController module is based on Flight Assist's GyroController and HoverModule, sharing code in places.
        public class GyroController
        {
            const float dampeningFactor = 25.0f;

            private IMyShipController controller;
            private List<IMyGyro> gyroscopes;

            public GyroController(IMyShipController controller, List<IMyGyro> gyroscopes)
            {
                this.controller = controller;
                this.gyroscopes = new List<IMyGyro>(gyroscopes);
            }

            public void Update(IMyShipController controller, List<IMyGyro> gyroscopes)
            {
                SetController(controller);
                AddGyroscopes(gyroscopes);
            }

            public void AddGyroscopes(List<IMyGyro> gyroscopes)
            {
                this.gyroscopes.AddList(gyroscopes);
                this.gyroscopes = this.gyroscopes.Distinct().ToList();
            }

            public void SetController(IMyShipController controller)
            {
                this.controller = controller;
            }

            public void SetEnabled(bool setEnabled)
            {
                foreach (var gyroscope in gyroscopes)
                {
                    gyroscope.Enabled = setEnabled;
                }
            }

            public void SetOverride(bool setOverride)
            {
                foreach (var gyroscope in gyroscopes)
                {
                    gyroscope.GyroOverride = setOverride;
                }
            }

            public static float NotNaN(float value)
            {
                return float.IsNaN(value) ? 0 : value;
            }

            public static float MinAbs(float value1, float value2)
            {
                return Math.Min(Math.Abs(value1), Math.Abs(value2)) * (value1 < 0 ? -1 : 1);
            }
            static double DegToRad = (Math.PI / 180);

            public Vector2 CalculatePitchRollToAchiveVelocity(Vector3 targetVelocity)
            {
                Vector3 diffrence = Vector3.Normalize(controller.GetShipVelocities().LinearVelocity - targetVelocity);
                Vector3 gravity = -Vector3.Normalize(controller.GetNaturalGravity());
                float velocity = (float)controller.GetShipSpeed();
                float proportionalModifier = (float)Math.Pow(Math.Abs(diffrence.Length()), 2);

                float pitch = NotNaN(Vector3.Dot(diffrence, Vector3.Cross(gravity, controller.WorldMatrix.Right)) * velocity) * proportionalModifier / dampeningFactor;
                float roll = NotNaN(Vector3.Dot(diffrence, Vector3.Cross(gravity, controller.WorldMatrix.Forward)) * velocity) * proportionalModifier / dampeningFactor;

                pitch = MinAbs(pitch, 90.0f * (float)DegToRad);
                roll = MinAbs(roll, 90.0f * (float)DegToRad);

                return new Vector2(roll, pitch);
            }

            public Vector3 CalculateVelocityToAlign(float offsetPitch = 0.0f, float offsetRoll = 0.0f)
            {
                var gravity = -Vector3.Normalize(Vector3.TransformNormal(controller.GetNaturalGravity(), Matrix.Transpose(controller.WorldMatrix)));
                var target = Vector3.Normalize(Vector3.Transform(gravity, Matrix.CreateFromAxisAngle(Vector3.Right, offsetPitch) * Matrix.CreateFromAxisAngle(Vector3.Forward, offsetRoll)));

                var pitch = Vector3.Dot(Vector3.Forward, target);
                var roll = Vector3.Dot(Vector3.Right, target);

                return new Vector3(pitch, 0, roll);
            }

            public void SetAngularVelocity(Vector3 velocity)
            {
                var cockpitLocalVelocity = Vector3.TransformNormal(velocity, controller.WorldMatrix);
                foreach (var gyro in gyroscopes)
                {
                    var gyroLocalVelocity = Vector3.TransformNormal(cockpitLocalVelocity, Matrix.Transpose(gyro.WorldMatrix));

                    gyro.Pitch = gyroLocalVelocity.X;
                    gyro.Yaw = gyroLocalVelocity.Y;
                    gyro.Roll = gyroLocalVelocity.Z;
                }
            }
        }

        Vector3D RefreshNavigation(TimeSpan CanonicalTime)
        {
            var targetPosition = Target.GetPositionFromCanonicalTime(CanonicalTime);
            targetPosition += (RandomHitboxOffset * Target.Radius * Group.HitOffset);
            var rangeVector = targetPosition - Controller.WorldMatrix.Translation;
            var waypointVector = rangeVector;
            var distTargetSq = rangeVector.LengthSquared();

            var grav = Controller.GetNaturalGravity();
            bool inGrav = grav != Vector3D.Zero;
            if (!inGrav)
                return Controller.WorldMatrix.Translation;

            var gravNormal = grav;
            gravNormal.Normalize();
            var planetCenter = Vector3D.Zero;
            Controller.TryGetPlanetPosition(out planetCenter);
            var targetGravNormal = planetCenter - targetPosition;
            targetGravNormal.Normalize();

            var rand = HostSubsystem.Context.Random;
            proxArmed = proxArmed ? proxArmed : distTargetSq < 120 * 120;

            var linearVelocity = Controller.GetShipVelocities().LinearVelocity;
            Vector3D velocityVector = Target.GetVelocity() - linearVelocity;
            var speed = Controller.GetShipSpeed();

            double alignment = linearVelocity.Dot(ref waypointVector);
            if (alignment > 0)
            {
                Vector3D rangeDivSqVector = waypointVector / waypointVector.LengthSquared();
                Vector3D compensateVector = velocityVector - (velocityVector.Dot(ref waypointVector) * rangeDivSqVector);

                Vector3D targetANVector;
                var targetAccel = (lastTargetVelocity - Target.GetVelocity()) * 0.16667;

                targetANVector = targetAccel /*- grav*/ - (targetAccel.Dot(ref waypointVector) * rangeDivSqVector);

                bool accelerating = speed > lastSpeed + 1;
                if (accelerating)
                {
                    AccelerationVector = linearVelocity + (3.5 * 1.5 * (compensateVector + (0.5 * targetANVector)));
                }
                else
                {
                    AccelerationVector = linearVelocity + (3.5 * (compensateVector + (0.5 * targetANVector)));
                }
            }
            // going backwards or perpendicular
            else
            {
                AccelerationVector = (waypointVector * 0.1) + velocityVector;
            }

            lastTargetVelocity = Target.GetVelocity();
            lastSpeed = speed;

            // 
                         AccelerationVector = Vector3D.ProjectOnPlane(ref AccelerationVector, ref targetGravNormal);
            //             AccelerationVector.Normalize();
            //             Vector3D right = Controller.GetNaturalGravity().Cross(AccelerationVector);
            //             double targetGravAngle = VectorHelpers.VectorAngleBetween(AccelerationVector, Controller.GetNaturalGravity());
            // 
            //             double forwardThrustAngle = 1.0;
            //             MatrixD matrix = MatrixD.CreateFromAxisAngle(right, forwardThrustAngle); // * VectorHelpers.VectorAngleBetween(AccelerationVector, Controller.GetNaturalGravity())
            //             Vector3D TargetGravityTilt = Vector3D.Rotate(Controller.GetNaturalGravity(), matrix);
            // 
            //             Vector3D right = AccelerationVector.Cross(Controller.GetNaturalGravity());
            //            double targetGravAngle = VectorHelpers.VectorAngleBetween(AccelerationVector, Controller.GetNaturalGravity());
            //             double rotateAngle = Math.PI / 4 - (Math.PI / 2 - targetGravAngle);
            // 

            AccelerationVector = Vector3D.ProjectOnPlane(ref AccelerationVector, ref targetGravNormal);
            AccelerationVector.Normalize();

            //             var AccelMatrix = MatrixD.CreateFromDir(AccelerationVector, -targetGravNormal);
            //             AccelMatrix = MatrixD.Transform(AccelMatrix, Quaternion.CreateFromYawPitchRoll(0, (float)(-Math.PI/8), 0));
            //             AccelMatrix = MatrixD.Transform(AccelMatrix, Quaternion.CreateFromRotationMatrix(MatrixD.Transpose(Controller.WorldMatrix)));
            // 
            //             Vector3D angles;
            //             MatrixD.GetEulerAnglesXYZ(ref AccelMatrix, out angles);
            //             return angles;

//             MatrixD WorldSpaceAimMatrix = MatrixD.CreateLookAt(YourAimPivotPosition, Target, YourAimPivotUp);
//             MatrixD localMatrix = WorldSpaceAimMatrix * Matrix.Transform(MatrixD.Transpose(YourCenteredAimPivotWorldMatrix));
//             Vector3D angles;
//             MatrixD.GetEulerAnglesXYZ(ref localMatrix, out angles);
            return Vector3D.TransformNormal(AccelerationVector, MatrixD.Transpose(Controller.WorldMatrix));
        }

        void AimAtTarget(Vector3D input)
        {
            //         https://mathworld.wolfram.com/EulerAngles.html
            //         https://physics.stackexchange.com/questions/379845/intrinsic-concurrent-pitch-yaw-roll-rotation-between-two-rotation-matrices
            //          https://stackoverflow.com/questions/11514063/extract-yaw-pitch-and-roll-from-a-rotationmatrix

//            HostSubsystem.Context.Log.Debug("AimAtTarget "+ input);

            double maxFlightPitch = MathHelper.ToRadians(40);
            double maxFlightRoll = MathHelper.ToRadians(40);

            double pitch = input.Z * maxFlightPitch;
            double roll = input.X * maxFlightRoll;

//            var dampeningRotation = gyroControllerV2.CalculatePitchRollToAchiveVelocity(Vector3.Zero);
//            dampeningRotation = Vector2.Min(dampeningRotation, new Vector2(maxFlightRoll, maxFlightPitch));
            gyroControllerV2.SetAngularVelocity(gyroControllerV2.CalculateVelocityToAlign((float)pitch, (float)roll));

            /*
                        double yawInput = 0;
                        double pitchInput = 0;
                        double rollInput = 0;

                        pitchInput = angles.X;
                        rollInput = 0; //-angles.Z;

                        //yawInput = yawController.Filter(yawInput, 2);
                        pitchInput = pitchController.Filter(pitchInput, 2);
                        rollInput = rollController.Filter(rollInput, 2);

                        if (Math.Abs(yawInput) + Math.Abs(pitchInput) > DEF_PD_AIM_LIMIT)
                        {
                            double adjust = DEF_PD_AIM_LIMIT / (Math.Abs(yawInput) + Math.Abs(pitchInput) + Math.Abs(rollInput));
                            yawInput *= adjust;
                            pitchInput *= adjust;
                            rollInput *= adjust;
                        }
                        //---------- Set Gyroscope Parameters ----------

                        gyroControl.SetGyroYaw((float)yawInput);
                        gyroControl.SetGyroPitch((float)pitchInput);
                        gyroControl.SetGyroRoll((float)rollInput);
            */
        }
        
        const double DEF_PD_P_GAIN = 2;
        const double DEF_PD_D_GAIN = .4;
        const double DEF_PD_AIM_LIMIT = 6.3;

//        float ROLL_THETA = 0;


        void Arm()
        {
            foreach (var warhead in Warheads) warhead.IsArmed = true;
        }

        public void Detonate()
        {
            foreach (var warhead in Warheads)
            {
                warhead.IsArmed = true;
                warhead.Detonate();
            }
        }

        // FastArcTan
        double FastAT(double x)
        {
            return 0.785375 * x - x * (x - 1.0) * (0.2447 + 0.0663 * x);
        }

        double GetSign(double value)
        {
            return value < 0 ? -1 : 1;
        }

    }

    public class HoverTorpedoTubeGroup
    {
        public string Name { get; set; }
        public HoverTorpedoSubsystem Host;

        public bool Ready
        {
            get
            {
                foreach (HoverTorpedoTube tube in Children)
                {
                    if (tube.Ready) return true;
                }
                return false;
            }
        }

        public float GuidanceStartSeconds = 2;
        public double CruiseDistSqMin = 10000;
        public int PlungeDist = 1000;
        public double HitOffset = 0;
        public int ReloadCooldownMS = 5000;
        public Dictionary<MyItemType, int> TorpedoParts = new Dictionary<MyItemType, int>();

        public bool AutoFire = false;
        public int AutoFireRange = 15000;
        public int AutoFireTubeMS = 500;
        public int AutoFireTargetMS = 2000;
        public int AutoFireRadius = 30;
        public int AutoFireSizeMask = 1;
        public List<IMyInteriorLight> AutofireIndicator = new List<IMyInteriorLight>();

        public Dictionary<EnemyShipIntel, TargetInfo> EngagedTargets = new Dictionary<EnemyShipIntel, TargetInfo>();
        public TimeSpan LastLaunch;
        public long LoadingMask;
        public long SuppressMask;

        public HashSet<HoverTorpedoTube> Children { get; set; }

        public HoverTorpedo Fire(TimeSpan localTime, TimeSpan canonicalTime, EnemyShipIntel target = null)
        {
            foreach (HoverTorpedoTube tube in Children)
            {
                if (tube.Ready) return tube.Fire( localTime, canonicalTime, target);
            }
            return null;
        }

        public HoverTorpedoTubeGroup(HoverTorpedoSubsystem host, string name)
        {
            Name = name;
            Host = host;
            Children = new HashSet<HoverTorpedoTube>();
        }

        public void AddTube(HoverTorpedoTube tube)
        {
            Children.Add(tube);
        }

        public int NumReady
        {
            get
            {
                int count = 0;

                foreach (var tube in Children)
                {
                    if (tube.Ready) count++;
                }
                return count;
            }
        }
    }
    public class HoverTorpedoTube
    {
        public ITorpedoTubeRelease Release;
        public IMyShipConnector Connector;
        public TimeSpan ReloadCooldown;

        public HoverTorpedo LoadedTorpedo;

        HoverTorpedoSubsystem Host;
        HoverTorpedo[] SubTorpedosScratchpad = new HoverTorpedo[16];

        public MyCubeSize Size;
        public int Index;
//        public string Name { get; set; }
        public HoverTorpedoTubeGroup Group;
        public bool Ready => LoadedTorpedo != null;
        public List<HoverTorpedoTube> Children { get; set; }

        public HoverTorpedoTube(int index, HoverTorpedoSubsystem host)
        {
            Host = host;
            Children = new List<HoverTorpedoTube>();
            Index = index;
//            Name = index.ToString("00");
            Fire(TimeSpan.Zero, TimeSpan.Zero, null);
        }

        public bool HasRelease()
        {
            return Release != null;
        }

        public void Update(TimeSpan LocalTime)
        {
            if (ReloadCooldown > LocalTime)
                return;
            if (LoadedTorpedo == null)
                GetTorpedo();
        }

        public bool AddTorpedoPart(IMyTerminalBlock part)
        {
            if (part.CustomName.StartsWith("<SUB"))
            {
                var indexTagEnd = part.CustomName.IndexOf('>');
                if (indexTagEnd == -1) return false;

                var numString = part.CustomName.Substring(4, indexTagEnd - 4);

                int index;
                if (!int.TryParse(numString, out index)) return false;
                if (SubTorpedosScratchpad[index] == null)
                {
                    SubTorpedosScratchpad[index] = new HoverTorpedo();
                    SubTorpedosScratchpad[index].Tag = index.ToString();

                    LoadedTorpedo.SubTorpedos.Add(SubTorpedosScratchpad[index]);
                }
                return SubTorpedosScratchpad[index].AddPart(part);
            }
            else
            {
                return LoadedTorpedo.AddPart(part);
            }
        }

        void GetTorpedo()
        {
            LoadedTorpedo = new HoverTorpedo();

            for (int i = 0; i < SubTorpedosScratchpad.Length; i++)
            {
                SubTorpedosScratchpad[i] = null;
            }

            Host.PartsScratchpad.Clear();
            if (!Release.LoadTorpedoParts(ref Host.PartsScratchpad))
            {
//                Host.Context.Log.Debug("LoadTorpedoParts FAILURE #" + Host.PartsScratchpad.Count());
                LoadedTorpedo = null;
                return;
            }

//            Host.Context.Log.Debug("Load Tube #"+Index);
            foreach (var part in Host.PartsScratchpad)
            {
                AddTorpedoPart(part);
            }

            if (!LoadedTorpedo.OK())
            {
                LoadedTorpedo = null;
                return;
            }

            foreach( var subTorp in SubTorpedosScratchpad)
            {
                if (subTorp != null)
                {
                    if (!subTorp.OK())
                    {
                        LoadedTorpedo = null;
                        return;
                    }
                }
            }

            if (Connector != null)
            {
                if (Connector.Status == MyShipConnectorStatus.Connectable) 
                    Connector.Connect();
                if (Connector.Status != MyShipConnectorStatus.Connected)
                {
                    LoadedTorpedo = null;
                    return;
                }
            }

            foreach (var tank in LoadedTorpedo.Tanks) 
                tank.Stockpile = true;
        }

        public HoverTorpedo Fire(TimeSpan localTime, TimeSpan canonicalTime, EnemyShipIntel target = null)
        {
            if (canonicalTime == TimeSpan.Zero)
                return null;
            if (LoadedTorpedo == null)
                return null;
            if (!Release.Detach())
                return null;

//            Host.Context.Log.Debug("Fire Tube #"+Index);

            ReloadCooldown = localTime + TimeSpan.FromMilliseconds(Group.ReloadCooldownMS);
            // Torpedo Welder Control
            // Thruster Release Torpedoes need to temporarily disable welders to fire.
            if ( Release.NeedWelderSupress )
            {
                foreach( var welder in Host.TorpedoWeldersRR.Items)
                {
                    welder.SupressWelder(localTime, Group, Index);
                }
            }
            
            // The missile has fired, it will need to reload.
            Group.LoadingMask |= 1L << Index;

            if (Connector != null
                && Connector.Status == MyShipConnectorStatus.Connected)
                Connector.OtherConnector.Enabled = false;

            var torp = LoadedTorpedo;
            LoadedTorpedo = null;

            foreach (var sub in torp.SubTorpedos)
            {
                sub.HostSubsystem = Host;
                sub.canInitialize = false;
            }
            torp.HostSubsystem = Host;
            torp.TubeIndex = Index;
//            Host.Context.Log.Debug("Init Tube #" + Index);
            torp.Init(canonicalTime, Group);
            torp.Target = target;
            return torp;
        }
    }

}