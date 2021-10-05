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

// 
// 
//     public class LaunchSequencer
//     {
// 
//         public List<EnemyShipIntel, ITorpedoControllable> Launchers = new List<ITorpedoControllable>();
// 
// 
//         public TimeSpan LastLaunch, Interval, TargetCooldown;
// 
//         public int Launches;
// 
//         public void Reset()
//         {
//             Launchers.Clear();
//             Intels.Clear();
//             EngagedTargets.Clear();
// 
//             Launches = 0;
//         }
// 
//     }
    public class TorpedoSubsystem : ISubsystem
    {
        #region ISubsystem
        public UpdateFrequency UpdateFrequency { get; set; }

        public void Command(TimeSpan timestamp, string command, object argument)
        {
            if (command == "toggleauto")
            {
                TorpedoTubeGroup group;
                if (TorpedoTubeGroups.TryGetValue((string)argument, out group))
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
            debugBuilder.Clear();
            foreach (var kvp in TorpedoTubeGroups)
                debugBuilder.AppendLine("Grp [" + kvp.Key + "] #" + kvp.Value.Children.Count);

            for (int i = 0; i < TorpedoTubes.Length; ++i)
            {
                var tube = TorpedoTubes[i];
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

            TorpedoTubeGroups["SM"] = new TorpedoTubeGroup(this, "SM");
            TorpedoTubeGroups["LG"] = new TorpedoTubeGroup(this, "LG");

            ParseConfigs();

            GetParts();

            foreach (var group in TorpedoTubeGroups.Values)
            {
                foreach (var light in group.AutofireIndicator) light.Color = group.AutoFire ? Color.LightPink : Color.LightGreen;
            }

            // JIT
            CommandFire(null, TimeSpan.Zero, FireCommandType.Normal);
        }

        bool FilterAutofireTarget(TimeSpan timestamp, TimeSpan canonicalTime, TorpedoTubeGroup group, EnemyShipIntel target, TargetInfo info)
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

        bool FilterNormalTarget(TimeSpan timestamp, TimeSpan canonicalTime, TorpedoTubeGroup group, EnemyShipIntel target, TargetInfo info)
        {
            return true;
        }

//         void AssignAutoFireTargets()
//         {
// 
//         }

        private void CollectTargets(TimeSpan timestamp, TorpedoTubeGroup group, TimeSpan canonicalTime, TimeSpan targetInterval, Func<TimeSpan, TimeSpan, TorpedoTubeGroup, EnemyShipIntel, TargetInfo, bool> funcValidTarget )
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
                for (int i = 0; i < TorpedoTubes.Count(); i++)
                {
                    var tube = TorpedoTubes[i];
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
                    foreach (var group in TorpedoTubeGroups.Values)
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
                    foreach (var group in TorpedoTubeGroups.Values)
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
                    foreach (var group in TorpedoTubeGroups.Values)
                    {
                        if (group.AutoFire)
                        {
                            CollectTargets(timestamp, group, canonicalTime, TimeSpan.FromMilliseconds(group.AutoFireTargetMS), FilterAutofireTarget );
                        }
                    }
                }

                foreach (var group in TorpedoTubeGroups.Values)
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
                                if (Fire(timestamp, group, kvp.Key, false) != null)
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
                            if ( Fire(timestamp, group, neglectedTarget, false) != null )
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

        public TorpedoSubsystem(IIntelProvider intelProvider)
        {
            UpdateFrequency = UpdateFrequency.Update1 | UpdateFrequency.Update10 | UpdateFrequency.Update100;
            IntelProvider = intelProvider;
        }

        public TorpedoTube[] TorpedoTubes = new TorpedoTube[16];
        public Dictionary<string, TorpedoTubeGroup> TorpedoTubeGroups = new Dictionary<string, TorpedoTubeGroup>();
        public List<EnemyShipIntel> EngagedTargetsCullScratchpad = new List<EnemyShipIntel>();

        public HashSet<Torpedo> Torpedos = new HashSet<Torpedo>();

        public List<Torpedo> TorpedoScratchpad = new List<Torpedo>();
        public List<IMyTerminalBlock> PartsScratchpad = new List<IMyTerminalBlock>();
        List<MyDetectedEntityInfo> DetectedInfoScratchpad = new List<MyDetectedEntityInfo>();

        public class TorpedoWelder
        {
            IMyShipWelder Welder;
            TimeSpan WelderWake;
            Dictionary<TorpedoTubeGroup, long> Tubes = new Dictionary<TorpedoTubeGroup, long>();
            public TorpedoWelder(TorpedoSubsystem Host, IMyShipWelder welder)
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
                        TorpedoTubeGroup group = Host.TorpedoTubeGroups[tubePair[0]];

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
            public void SupressWelder(TimeSpan localTime, TorpedoTubeGroup group, int index)
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

            public void UpdateWelder(TimeSpan localTime, TorpedoSubsystem host)
            {
//                host.Context.Log.Debug("Supressed: " + (localTime < WelderWake).ToString());
                if (localTime < WelderWake)
                    return;

                foreach( var group in host.TorpedoTubeGroups)
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

        StringBuilder debugBuilder = new StringBuilder();

        public MyIni IniParser = new MyIni();

        long runs;
        void ParseTubeGroupConfig(MyIni parser, string groupName, TorpedoTubeGroup group)
        {
            string section = "Torpedo";
            if (parser.ContainsSection(section + "_" + groupName))
            {
                section = section + "_" + groupName;
            }
            group.GuidanceStartSeconds = (float)parser.Get(section, "GuidanceStartSeconds").ToDouble(2.0);

            group.CruiseDistSqMin = parser.Get(section, "CruiseDistMin").ToDouble(1000);
            group.CruiseDistSqMin *= group.CruiseDistSqMin;

            group.PlungeDist = parser.Get(section, "PlungeDist").ToInt16(1000);
            group.HitOffset = parser.Get(section, "HitOffset").ToDouble(0.0);
            group.ReloadCooldownMS = parser.Get(section, "ReloadCooldownMS").ToInt32(group.ReloadCooldownMS);

            group.AutoFire = parser.Get(section, "AutoFire").ToBoolean();
            group.AutoFireRange = parser.Get(section, "AutoFireRange").ToInt16(15000);
            group.AutoFireTubeMS = parser.Get(section, "AutoFireTubeMS").ToInt16(500);
            group.AutoFireTargetMS = parser.Get(section, "AutoFireTargetMS").ToInt16(2000);
            group.AutoFireRadius = parser.Get(section, "AutoFireRadius").ToInt16(30);
            group.AutoFireSizeMask = parser.Get(section, "AutoFireSizeMask").ToInt16(1);

            group.Trickshot = parser.Get(section, "Trickshot").ToBoolean(false);
            group.TrickshotDistance = parser.Get(section, "TrickshotDistance").ToInt32(1200);
            group.TrickshotTerminalDistanceSq = parser.Get(section, "TrickshotTerminalDistance").ToInt32(1000);
            group.TrickshotTerminalDistanceSq *= group.TrickshotTerminalDistanceSq;

            group.Evasion = parser.Get(section, "Evasion").ToBoolean();
            group.EvasionDistSqStart = parser.Get(section, "EvasionDistStart").ToInt32(2000);
            group.EvasionDistSqStart *= group.EvasionDistSqStart;
            group.EvasionDistSqEnd = parser.Get(section, "EvasionDistEnd").ToInt32(500);
            group.EvasionDistSqEnd *= group.EvasionDistSqEnd;
            group.EvasionOffsetMagnitude = parser.Get(section, "EvasionOffsetMagnitude").ToDouble(group.EvasionOffsetMagnitude);
            group.EvasionAdjTimeMin = parser.Get(section, "EvasionAdjTimeMin").ToInt32(group.EvasionAdjTimeMin);
            group.EvasionAdjTimeMax = parser.Get(section, "EvasionAdjTimeMax").ToInt32(group.EvasionAdjTimeMax);

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
            MyIniParseResult result;
            if (!Parser.TryParse(Context.Reference.CustomData, out result))
                return;

            string section = "Torpedo";
            if (Parser.ContainsSection(section))
            {
                int TorpedoTubeCount = Parser.Get(section, "TorpedoTubeCount").ToInt16(16);
                if (TorpedoTubes.Length != TorpedoTubeCount)
                {
                    TorpedoTubes = new TorpedoTube[TorpedoTubeCount];
                }
            }

            foreach (var group in TorpedoTubeGroups)
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


        TorpedoTube GetOrCreateTubeAtIndex(int index, MyCubeSize size)
        {
            if (index >= TorpedoTubes.Length)
                return null;

            var tube = TorpedoTubes[index];
            if ( tube == null )
            {
                tube = new TorpedoTube(index, this);
                tube.Size = size;                
                TorpedoTubes[index] = tube;
                var group = TorpedoTubeGroups[size == MyCubeSize.Small ? "SM" : "LG"];
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
            TorpedoTubeGroup group;
            if (TorpedoTubeGroups.TryGetValue(groupName, out group))
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

            TorpedoTubeGroup group = null;
            int LaunchIntervalMS = 500;

            if (TorpedoTubeGroups.TryGetValue(commandLine.Argument(1), out group))
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
                        group.EngagedTargets.TryGetValue(target, out targetInfo);
                        if (targetInfo == null)
                        {
                            targetInfo = new TargetInfo();
                            targetInfo.Requests = LaunchCount - 1;
                            targetInfo.LastLaunch = localTime;
                            targetInfo.Interval = TimeSpan.FromMilliseconds(LaunchIntervalMS);
                        }
                    }
                }

                Fire(localTime, group, target, FireCommandType.Trickshot == type);
            }
        }

        public Torpedo Fire(TimeSpan localTime, ITorpedoControllable unit, EnemyShipIntel target = null, bool trickshot = false)
        {
            if (unit == null || !unit.Ready) return null;
            var torp = unit.Fire(localTime, localTime + IntelProvider.CanonicalTimeDiff, target, trickshot);
            if (torp != null)
            {
                Torpedos.Add(torp);
                foreach (var subtorp in torp.SubTorpedos) Torpedos.Add(subtorp);
                return torp;
            }
            return null;
        }
    }

    // This is a refhax torpedo
    public class Torpedo
    {
        public enum AltModeStage
        {
            Off,
            Setup,
            Active,
            Terminal,
        }

        public TorpedoTubeGroup Group;
        public List<IMyGyro> Gyros = new List<IMyGyro>();
        public HashSet<IMyWarhead> Warheads = new HashSet<IMyWarhead>();
        public HashSet<IMyThrust> Thrusters = new HashSet<IMyThrust>();
        public HashSet<IMyBatteryBlock> Batteries = new HashSet<IMyBatteryBlock>();
        public HashSet<IMyGasTank> Tanks = new HashSet<IMyGasTank>();
        public List<IMyCameraBlock> Cameras = new List<IMyCameraBlock>();
        public List<float> CameraExtends = new List<float>();
        public IMySensorBlock Sensor;
        public IMyShipController Controller;
        public string Tag; // This is the number of the index of SubMissiles. Legacy: HE, CLST, MICRO, etc
        public HashSet<IMyShipMergeBlock> Splitters = new HashSet<IMyShipMergeBlock>();

        public HashSet<Torpedo> SubTorpedos = new HashSet<Torpedo>();

        public IMyTerminalBlock Fuse;

        GyroControl gyroControl;

        PDController yawController = new PDController(DEF_PD_P_GAIN, DEF_PD_D_GAIN, 10);
        PDController pitchController = new PDController(DEF_PD_P_GAIN, DEF_PD_D_GAIN, 10);

        TimeSpan launchTime = TimeSpan.Zero;

        public bool Reserve = false;
        public TimeSpan ReserveTime;
        public bool Disabled = false;
        public IFleetIntelligence Target = null;

        double lastSpeed;
        Vector3D lastTargetVelocity;

        public Vector3D AccelerationVector;

        bool initialized = false;
        bool plunging = true;
        bool cruising = false;
        bool canCruise = false;
        public bool canInitialize = true;
        int runs = 0;

        Vector3D RandomHitboxOffset;

        public AltModeStage TrickshotMode = AltModeStage.Off;
        Vector3D TrickshotOffset = Vector3D.Zero;

        AltModeStage EvasionMode = AltModeStage.Off;
        Vector3D EvasionOffset = Vector3D.Zero;
        TimeSpan LastCourseAdjustTime;

        public bool proxArmed = false;
        public TorpedoSubsystem HostSubsystem = null;

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

        public void Init(TimeSpan CanonicalTime, TorpedoTubeGroup group)
        {
            initialized = true;
            Group = group;
            EvasionMode = group.Evasion ? AltModeStage.Setup : AltModeStage.Off;
            gyroControl = new GyroControl(Gyros);
            var refWorldMatrix = Controller.WorldMatrix;
            gyroControl.Init(ref refWorldMatrix);
            foreach (var tank in Tanks) 
                tank.Stockpile = false;

            foreach (var Gyro in Gyros)
            {
                Gyro.GyroOverride = true;
                Gyro.Enabled = true;
            }

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
            if (CanonicalTime - launchTime < TimeSpan.FromSeconds(Group.GuidanceStartSeconds)) return;
            if (Target == null) return;

            if (CanonicalTime - launchTime > TimeSpan.FromSeconds(Group.GuidanceStartSeconds + 1) && SubTorpedos.Count > 0) Split();

            this.Target = Target;

            Vector3D normalAccelerationVector = RefreshNavigation(CanonicalTime);

            canCruise = canCruise && normalAccelerationVector.Dot(Vector3D.Normalize(Controller.GetShipVelocities().LinearVelocity)) > .98;
            if ( cruising != canCruise )
            {
                cruising = canCruise;
                foreach (var thruster in Thrusters) thruster.ThrustOverridePercentage = canCruise ? .25f : 1f;
            }

            AimAtTarget(RefreshNavigation(CanonicalTime));
        }

        public void FastUpdate()
        {
            if (initialized)
            {
                runs++;
                if (runs == 2)
                {
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

        void AimAtTarget(Vector3D TargetVector)
        {
            //TargetVector.Normalize();
            //TargetVector += Controller.WorldMatrix.Up * 0.1;

            //---------- Activate Gyroscopes To Turn Towards Target ----------

            double absX = Math.Abs(TargetVector.X);
            double absY = Math.Abs(TargetVector.Y);
            double absZ = Math.Abs(TargetVector.Z);

            double yawInput, pitchInput;
            if (absZ < 0.00001)
            {
                yawInput = pitchInput = MathHelperD.PiOver2;
            }
            else
            {
                bool flipYaw = absX > absZ;
                bool flipPitch = absY > absZ;

                yawInput = FastAT(Math.Max(flipYaw ? (absZ / absX) : (absX / absZ), 0.00001));
                pitchInput = FastAT(Math.Max(flipPitch ? (absZ / absY) : (absY / absZ), 0.00001));

                if (flipYaw) yawInput = MathHelperD.PiOver2 - yawInput;
                if (flipPitch) pitchInput = MathHelperD.PiOver2 - pitchInput;

                if (TargetVector.Z > 0)
                {
                    yawInput = (Math.PI - yawInput);
                    pitchInput = (Math.PI - pitchInput);
                }
            }

            //---------- PID Controller Adjustment ----------

            if (double.IsNaN(yawInput)) yawInput = 0;
            if (double.IsNaN(pitchInput)) pitchInput = 0;

            yawInput *= GetSign(TargetVector.X);
            pitchInput *= GetSign(TargetVector.Y);

            yawInput = yawController.Filter(yawInput, 2);
            pitchInput = pitchController.Filter(pitchInput, 2);

            if (Math.Abs(yawInput) + Math.Abs(pitchInput) > DEF_PD_AIM_LIMIT)
            {
                double adjust = DEF_PD_AIM_LIMIT / (Math.Abs(yawInput) + Math.Abs(pitchInput));
                yawInput *= adjust;
                pitchInput *= adjust;
            }

            //---------- Set Gyroscope Parameters ----------

            gyroControl.SetGyroYaw((float)yawInput);
            gyroControl.SetGyroPitch((float)pitchInput);
            gyroControl.SetGyroRoll(ROLL_THETA);
        }

        const double DEF_PD_P_GAIN = 10;
        const double DEF_PD_D_GAIN = 5;
        const double DEF_PD_AIM_LIMIT = 6.3;

        float ROLL_THETA = 0;

        Vector3D RefreshNavigation(TimeSpan CanonicalTime)
        {
            var targetPosition = Target.GetPositionFromCanonicalTime(CanonicalTime);
            targetPosition += (RandomHitboxOffset * Target.Radius * Group.HitOffset);
            var rangeVector = targetPosition - Controller.WorldMatrix.Translation;
            var waypointVector = rangeVector;
            var distTargetSq = rangeVector.LengthSquared();

            var rand = HostSubsystem.Context.Random;
            proxArmed = proxArmed ? proxArmed : distTargetSq < 120 * 120;

            var grav = Controller.GetNaturalGravity();
            bool inGrav = grav != Vector3D.Zero;
            // plunging makes no sense in space;
            plunging = inGrav && plunging;
            // Trickshot a bad idea in gravity
            TrickshotMode = inGrav ? AltModeStage.Off : TrickshotMode;
            // Can't cruise in gravity or too close to target
            canCruise = !inGrav && (distTargetSq > Group.CruiseDistSqMin);

            // TRICKSHOT - SETUP
            if (TrickshotMode == AltModeStage.Setup)
            {
                TrickshotOffset = TrigHelpers.GetRandomPerpendicularNormalToDirection(rand, rangeVector);
                TrickshotOffset *= Group.TrickshotDistance;

                TrickshotMode = AltModeStage.Active;
            }

            // EVASION - SETUP
            if (EvasionMode == AltModeStage.Setup)
            {
                EvasionMode = AltModeStage.Active;
            }

            // EVASION - ACTIVE
            if (EvasionMode == AltModeStage.Active)
            {
                if (distTargetSq <= Group.EvasionDistSqStart &&
                    distTargetSq >= Group.EvasionDistSqEnd)
                {
                    if (LastCourseAdjustTime - CanonicalTime < TimeSpan.Zero)
                    {
                        var invlerp = VectorHelpers.InvLerp(distTargetSq, Group.EvasionDistSqStart, Group.EvasionDistSqEnd);
                        var nextCourseTime = VectorHelpers.Lerp(invlerp, Group.EvasionAdjTimeMax, Group.EvasionAdjTimeMin);
                        LastCourseAdjustTime = CanonicalTime + TimeSpan.FromMilliseconds(nextCourseTime);

                        EvasionOffset = TrigHelpers.GetRandomPerpendicularNormalToDirection(rand, rangeVector);

                        var offsetMag = VectorHelpers.Lerp(invlerp, Group.EvasionOffsetMagnitude*Target.Radius, Target.Radius);
                        EvasionOffset *= offsetMag;

                        // Whip did this but less cool:
                        // _maxRandomAccelRatio = 0.25
                        // double angle = RNGesus.NextDouble() * Math.PI * 2.0;
                        // _randomizedHeadingVector = Math.Sin(angle) * _missileReference.WorldMatrix.Up + Math.Cos(angle) * _missileReference.WorldMatrix.Right;
                        // _randomizedHeadingVector *= _maxRandomAccelRatio;
                    }
                }
                else
                {
                    EvasionOffset = Vector3D.Zero;
                }
            }

            // TRICKSHOT - ACTIVE
            if (TrickshotMode == AltModeStage.Active)
            {
                waypointVector += TrickshotOffset;
                if ( waypointVector.LengthSquared() < 100 * 100 ||
                    distTargetSq < Group.TrickshotTerminalDistanceSq ) 
                {
                    TrickshotOffset = Vector3D.Zero;
                    TrickshotMode = AltModeStage.Off;
                }
            }

            // PLUNGING 
            if (plunging)
            {
                var gravDir = grav;
                gravDir.Normalize();

                var targetHeightDiff = rangeVector.Dot(-gravDir); // Positive if target is higher than missile

                if ( (rangeVector.LengthSquared() < Group.PlungeDist * Group.PlungeDist)
                   && targetHeightDiff > 0 )
                {
                    plunging = false;
                }

                if (plunging)
                {
                    waypointVector -= gravDir * Group.PlungeDist;
                    if (waypointVector.LengthSquared() < 300 * 300)
                        plunging = false;
                }
            }

            // EVASION - We apply the evasion effect last as it can
            // interfere with plunge & trickshot detecting that they are complete.
            waypointVector += EvasionOffset;

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

                targetANVector = targetAccel - grav - (targetAccel.Dot(ref waypointVector) * rangeDivSqVector);

                bool accelerating = speed > lastSpeed + 1;
                if (accelerating)
                {
                    canCruise = false;
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

            return Vector3D.TransformNormal(AccelerationVector, MatrixD.Transpose(Controller.WorldMatrix));
        }

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

        double FastAT(double x)
        {
            return 0.785375 * x - x * (x - 1.0) * (0.2447 + 0.0663 * x);
        }

        double GetSign(double value)
        {
            return value < 0 ? -1 : 1;
        }

    }

    public class GyroControl
    {
        Action<IMyGyro, float>[] profiles =
        {
            (g, v) => { g.Yaw = -v; },
            (g, v) => { g.Yaw = v; },
            (g, v) => { g.Pitch = -v; },
            (g, v) => { g.Pitch = v; },
            (g, v) => { g.Roll = -v; },
            (g, v) => { g.Roll = v; }
        };

        List<IMyGyro> gyros;

        byte[] gyroYaw;
        byte[] gyroPitch;
        byte[] gyroRoll;

        int activeGyro = 0;

        public GyroControl(List<IMyGyro> newGyros)
        {
            gyros = newGyros;
        }

        public void Init(ref MatrixD refWorldMatrix)
        {
            if (gyros == null)
            {
                gyros = new List<IMyGyro>();
            }

            gyroYaw = new byte[gyros.Count];
            gyroPitch = new byte[gyros.Count];
            gyroRoll = new byte[gyros.Count];

            for (int i = 0; i < gyros.Count; i++)
            {
                gyroYaw[i] = SetRelativeDirection(gyros[i].WorldMatrix.GetClosestDirection(refWorldMatrix.Up));
                gyroPitch[i] = SetRelativeDirection(gyros[i].WorldMatrix.GetClosestDirection(refWorldMatrix.Left));
                gyroRoll[i] = SetRelativeDirection(gyros[i].WorldMatrix.GetClosestDirection(refWorldMatrix.Forward));
            }

            activeGyro = 0;
        }

        public byte SetRelativeDirection(Base6Directions.Direction dir)
        {
            switch (dir)
            {
                case Base6Directions.Direction.Up:
                    return 1;
                case Base6Directions.Direction.Down:
                    return 0;
                case Base6Directions.Direction.Left:
                    return 2;
                case Base6Directions.Direction.Right:
                    return 3;
                case Base6Directions.Direction.Forward:
                    return 4;
                case Base6Directions.Direction.Backward:
                    return 5;
            }
            return 0;
        }

        public void SetGyroOverride(bool bOverride)
        {
            CheckGyro();

            for (int i = 0; i < gyros.Count; i++)
            {
                if (i == activeGyro) gyros[i].GyroOverride = bOverride;
                else gyros[i].GyroOverride = false;
            }
        }

        public void SetGyroYaw(float yawRate)
        {
            CheckGyro();

            if (activeGyro < gyros.Count)
            {
                profiles[gyroYaw[activeGyro]](gyros[activeGyro], yawRate);
            }
        }

        public void SetGyroPitch(float pitchRate)
        {
            if (activeGyro < gyros.Count)
            {
                profiles[gyroPitch[activeGyro]](gyros[activeGyro], pitchRate);
            }
        }

        public void SetGyroRoll(float rollRate)
        {
            if (activeGyro < gyros.Count)
            {
                profiles[gyroRoll[activeGyro]](gyros[activeGyro], rollRate);
            }
        }

        void CheckGyro()
        {
            while (activeGyro < gyros.Count)
            {
                if (gyros[activeGyro].IsFunctional)
                {
                    break;
                }
                else
                {
                    IMyGyro gyro = gyros[activeGyro];

                    gyro.Enabled = gyro.GyroOverride = false;
                    gyro.Yaw = gyro.Pitch = gyro.Roll = 0f;

                    activeGyro++;
                }
            }
        }
    }

    public class PDController
    {
        double lastInput;

        public double gain_p;
        public double gain_d;

        double second;

        public PDController(double pGain, double dGain, float stepsPerSecond = 60f)
        {
            gain_p = pGain;
            gain_d = dGain;
            second = stepsPerSecond;
        }

        public double Filter(double input, int round_d_digits)
        {
            double roundedInput = Math.Round(input, round_d_digits);

            double derivative = (roundedInput - lastInput) * second;
            lastInput = roundedInput;

            return (gain_p * input) + (gain_d * derivative);
        }

        public void Reset()
        {
            lastInput = 0;
        }
    }

    public interface ITorpedoControllable
    {
 //       string Name { get; }
        bool Ready { get; }
        Torpedo Fire(TimeSpan localTime, TimeSpan canonicalTime, EnemyShipIntel target = null, bool trickshot = true);
    }
    public class TargetInfo
    {
        public TimeSpan LastLaunch, Interval;
        public int Launches, Requests;
    }

    public class TorpedoTubeGroup : ITorpedoControllable
    {
        public string Name { get; set; }
        public TorpedoSubsystem Host;

        public bool Ready
        {
            get
            {
                foreach (ITorpedoControllable tube in Children)
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
        public int ReloadCooldownMS = 3500;
        public Dictionary<MyItemType, int> TorpedoParts = new Dictionary<MyItemType, int>();

        public bool AutoFire = false;
        public int AutoFireRange = 15000;
        public int AutoFireTubeMS = 500;
        public int AutoFireTargetMS = 2000;
        public int AutoFireRadius = 30;
        public int AutoFireSizeMask = 1;
        public List<IMyInteriorLight> AutofireIndicator = new List<IMyInteriorLight>();

//        public int AutofireClockMS = 0;
//        public TimeSpan AutofireLastFire;

        public bool Trickshot = false;
        public float TrickshotDistance = 1200;
        public float TrickshotTerminalDistanceSq = 1000;

        public bool Evasion = false;
        public float EvasionDistSqStart = 2000 * 2000;
        public float EvasionDistSqEnd = 800 * 800;
        public int EvasionAdjTimeMin = 500;
        public int EvasionAdjTimeMax = 1000;
        public double EvasionOffsetMagnitude = 2; // 2x radius


        public Dictionary<EnemyShipIntel, TargetInfo> EngagedTargets = new Dictionary<EnemyShipIntel, TargetInfo>();
        public TimeSpan LastLaunch;
        public long LoadingMask;
        public long SuppressMask;

        public HashSet<ITorpedoControllable> Children { get; set; }

        public Torpedo Fire(TimeSpan localTime, TimeSpan canonicalTime, EnemyShipIntel target = null, bool trickshot = true)
        {
            foreach (ITorpedoControllable tube in Children)
            {
                if (tube.Ready) return tube.Fire( localTime, canonicalTime, target, trickshot);
            }
            return null;
        }

        public TorpedoTubeGroup(TorpedoSubsystem host, string name)
        {
            Name = name;
            Host = host;
            Children = new HashSet<ITorpedoControllable>();
        }

        public void AddTube(TorpedoTube tube)
        {
            Children.Add(tube);
        }

        public int NumReady
        {
            get
            {
                int count = 0;

                foreach (ITorpedoControllable tube in Children)
                {
                    if (tube.Ready) count++;
                }
                return count;
            }
        }
    }

    public interface ITorpedoTubeRelease
    {
        bool NeedWelderSupress { get; }

        bool Detach();
        bool LoadTorpedoParts(ref List<IMyTerminalBlock> results);
    }

    public class MechanicalRelease : ITorpedoTubeRelease
    {
        public bool NeedWelderSupress => false;

        private IMyMechanicalConnectionBlock Mech;
        public MechanicalRelease(IMyMechanicalConnectionBlock mech)
        {
            Mech = mech;
        }

        public bool Detach()
        {
            if (Mech.Top == null)
                return false;
            Mech.Detach();
            return true;
        }
        public bool LoadTorpedoParts(ref List<IMyTerminalBlock> results)
        {
            if (Mech.Top == null)
                return false;

            return GridTerminalHelper.Base64BytePosToBlockList(Mech.CustomData, Mech.Top, ref results);
        }
    }

    public class MergeRelease : ITorpedoTubeRelease
    {
        public bool NeedWelderSupress => false;

        private IMyShipMergeBlock Merge;
        public MergeRelease(IMyShipMergeBlock merge)
        {
            Merge = merge;
        }
        public bool Detach()
        {
            var releaseOther = GridTerminalHelper.OtherMergeBlock(Merge);
            if (releaseOther == null)
                return false;
            releaseOther.Enabled = false;
            return true;
        }
        public bool LoadTorpedoParts(ref List<IMyTerminalBlock> results)
        {
            var releaseOther = GridTerminalHelper.OtherMergeBlock(Merge);
            if (releaseOther == null || !releaseOther.IsFunctional || !releaseOther.Enabled)
                return false;

            return GridTerminalHelper.Base64BytePosToBlockList(releaseOther.CustomData, releaseOther, ref results);
        }
    }

    public class ThrusterRelease : ITorpedoTubeRelease
    {
        public bool NeedWelderSupress => true;

        IMyTerminalBlock Reference;
        Vector3I Offset;

        public ThrusterRelease(IMyTerminalBlock reference, string offset)
        {
            Reference = reference;
            Offset = GridTerminalHelper.Base64ByteToVector3I(offset, reference);
        }
        public bool Detach()
        {
            var thruster = Reference.CubeGrid.GetCubeBlock(Offset)?.FatBlock as IMyThrust;
            if (thruster == null)
                return false;
            thruster.Enabled = true;
            thruster.ThrustOverridePercentage = 1;
            return true;
        }
        public bool LoadTorpedoParts(ref List<IMyTerminalBlock> results)
        {
            var thruster = Reference.CubeGrid.GetCubeBlock(Offset)?.FatBlock as IMyThrust;
            if ( thruster == null || !thruster.IsFunctional )
                return false;

            return GridTerminalHelper.Base64BytePosToBlockList(thruster.CustomData, thruster, ref results);
        }
    }

    public class TorpedoTube : ITorpedoControllable
    {
        public ITorpedoTubeRelease Release;
        public IMyShipConnector Connector;
        public TimeSpan ReloadCooldown;

        public Torpedo LoadedTorpedo;

        TorpedoSubsystem Host;
        Torpedo[] SubTorpedosScratchpad = new Torpedo[16];

        public MyCubeSize Size;
        public int Index;
//        public string Name { get; set; }
        public TorpedoTubeGroup Group;
        public bool Ready => LoadedTorpedo != null;
        public List<ITorpedoControllable> Children { get; set; }

        public TorpedoTube(int index, TorpedoSubsystem host)
        {
            Host = host;
            Children = new List<ITorpedoControllable>();
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
                    SubTorpedosScratchpad[index] = new Torpedo();
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
            LoadedTorpedo = new Torpedo();

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

//            Host.Context.Log.Debug("LoadTorpedoParts SUCCESS");
            foreach (var part in Host.PartsScratchpad)
            {
                AddTorpedoPart(part);
            }

            if (!LoadedTorpedo.OK())
            {
                LoadedTorpedo = null;
                return;
            }

            for (int i = 0; i < SubTorpedosScratchpad.Length; i++)
            {
                if (SubTorpedosScratchpad[i] != null)
                {
                    if (!SubTorpedosScratchpad[i].OK())
                    {
                        LoadedTorpedo = null;
                        return;
                    }
                }
            }

            if (Connector != null)
            {
                if (Connector.Status == MyShipConnectorStatus.Connectable) Connector.Connect();
                if (Connector.Status != MyShipConnectorStatus.Connected)
                {
                    LoadedTorpedo = null;
                    return;
                }
            }

            foreach (var tank in LoadedTorpedo.Tanks) tank.Stockpile = true;
        }

        public Torpedo Fire(TimeSpan localTime, TimeSpan canonicalTime, EnemyShipIntel target = null, bool trickshot = true)
        {
            if (canonicalTime == TimeSpan.Zero)
                return null;
            if (LoadedTorpedo == null)
                return null;
            if (!Release.Detach())
                return null;

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
            torp.TrickshotMode = Group.Trickshot || trickshot ? Torpedo.AltModeStage.Setup : Torpedo.AltModeStage.Off;
            foreach (var sub in torp.SubTorpedos)
            {
                sub.HostSubsystem = Host;
                sub.TrickshotMode = torp.TrickshotMode;
                sub.canInitialize = false;
            }
            torp.HostSubsystem = Host;
            torp.Init(canonicalTime, Group);
            LoadedTorpedo = null;
            torp.Target = target;
            return torp;
        }
    }

}