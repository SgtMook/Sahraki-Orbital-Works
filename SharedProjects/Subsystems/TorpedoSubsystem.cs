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
    public class TorpedoSubsystem : ISubsystem
    {
        #region ISubsystem
        public UpdateFrequency UpdateFrequency { get; set; }

        public void Command(TimeSpan timestamp, string command, object argument)
        {
            if (command == "fire")
            {
                Fire(argument, timestamp);
            }
            if (command == "firetrick")
            {
                Fire(argument, timestamp, true);
            }
            if (command == "toggleauto")
            {
                TorpedoTubeGroup group;
                if (TorpedoTubeGroups.TryGetValue((string)argument, out group))
                {
                    group.AutoFire = !group.AutoFire;
                }
            }
        }

        public void DeserializeSubsystem(string serialized)
        {
        }

        public string GetStatus()
        {
            return debugBuilder.ToString();
        }

        public string SerializeSubsystem()
        {
            return string.Empty;
        }

        IMyTerminalBlock ProgramReference;
        public void Setup(MyGridProgram program, string name, IMyTerminalBlock programReference = null)
        {
            ProgramReference = programReference;
            if (ProgramReference == null) ProgramReference = program.Me;
            Program = program;
            GetParts();

            ParseConfigs();

            foreach (var group in TorpedoTubeGroups.Values)
                group.AutoFire = AutoFire;

            Fire(null, TimeSpan.Zero);
        }

        public void Update(TimeSpan timestamp, UpdateFrequency updateFlags)
        {
            if ((updateFlags & UpdateFrequency.Update100) != 0)
            {
                for (int i = 0; i < TorpedoTubes.Count(); i++)
                {
                    if (TorpedoTubes[i] != null && TorpedoTubes[i].OK())
                    {
                        TorpedoTubes[i].Update(timestamp);
                    }
                }
            }
            if ((updateFlags & UpdateFrequency.Update1) != 0)
            {
                var canonicalTime = timestamp + IntelProvider.CanonicalTimeDiff;
                runs++;
                if (runs % 120 == 0)
                {
                    TorpedoTubeGroupScratchpad.Clear();
                    foreach (var kvp in TorpedoTubeGroups)
                    {
                        if (kvp.Value.AutoFire && kvp.Value.NumReady > 0)
                            TorpedoTubeGroupScratchpad.Add(kvp.Value);
                    }

                    if (TorpedoTubeGroupScratchpad.Count > 0)
                    {
                        var intelItems = IntelProvider.GetFleetIntelligences(timestamp);
                        MissileDumpScratchpad.Clear();

                        foreach (var kvp in intelItems)
                        {
                            if (kvp.Key.Item1 != IntelItemType.Enemy) continue;

                            var target = (EnemyShipIntel)kvp.Value;
                            var isValidDumpTarget = target.Radius > 30 && (target.GetPositionFromCanonicalTime(canonicalTime) - ProgramReference.GetPosition()).Length() < 15000 && target.CubeSize == MyCubeSize.Large;

                            if (isValidDumpTarget)
                            {
                                MissileDumpScratchpad.Add(target);
                                continue;
                            }
                        }

                        if (MissileDumpScratchpad.Count > 0)
                        {
                            foreach (var group in TorpedoTubeGroupScratchpad)
                                Fire(timestamp, group, MissileDumpScratchpad[rand.Next(MissileDumpScratchpad.Count)], false);
                        }
                    }
                }
                if (runs % 8 == 0)
                {
                    // Update Indicators
                    foreach (TorpedoTubeGroup group in TorpedoTubeGroups.Values)
                        if (group.AutofireIndicator != null) group.AutofireIndicator.Color = group.AutoFire ? Color.LightPink : Color.LightGreen;

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
                                torp.Update((EnemyShipIntel)intelItems[intelKey], canonicalTime, GuidanceStartSeconds);
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
                            torp.Update(combatIntel, canonicalTime, GuidanceStartSeconds);
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

        public HashSet<Torpedo> Torpedos = new HashSet<Torpedo>();
        public List<Torpedo> TorpedoScratchpad = new List<Torpedo>();
        public List<IMyTerminalBlock> PartsScratchpad = new List<IMyTerminalBlock>();
        List<MyDetectedEntityInfo> DetectedInfoScratchpad = new List<MyDetectedEntityInfo>();
        List<EnemyShipIntel> MissileDumpScratchpad = new List<EnemyShipIntel>();
        List<TorpedoTubeGroup> TorpedoTubeGroupScratchpad = new List<TorpedoTubeGroup>();

        HashSet<long> AutofireTargetLog = new HashSet<long>();
        Queue<Torpedo> ReserveTorpedoes = new Queue<Torpedo>();

        MyGridProgram Program;
        IIntelProvider IntelProvider;
        Random rand = new Random();

        StringBuilder debugBuilder = new StringBuilder();

        public MyIni IniParser = new MyIni();

        long runs;

        bool AutoFire = false;
        float GuidanceStartSeconds = 2;
        public int PlungeDist = 1000;
        int AutoFireRange = 15000;

        // [Torpedo]
        // AutoFire = False
        // GuidanceStartSeconds = 2
        // PlungeDist = 1000
        // AutoFireRange = 15000
        void ParseConfigs()
        {
            MyIni Parser = new MyIni();
            MyIniParseResult result;
            if (!Parser.TryParse(ProgramReference.CustomData, out result))
                return;

            AutoFire = Parser.Get("Torpedo", "AutoFire").ToBoolean();

            var flo = Parser.Get("Torpedo", "GuidanceStartSeconds").ToDecimal();
            if (flo != 0) GuidanceStartSeconds = (float)flo;

            var num = Parser.Get("Torpedo", "PlungeDist").ToInt16();
            if (num != 0) PlungeDist = num;

            num = Parser.Get("Torpedo", "AutoFireRange").ToInt16();
            if (num != 0) AutoFireRange = num;
        }

        void GetParts()
        {
            for (int i = 0; i < TorpedoTubes.Count(); i++)
            {
                TorpedoTubes[i] = null;
            }
            TorpedoTubeGroups.Clear();

            Program.GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(null, CollectParts);

            for (int i = 0; i < TorpedoTubes.Count(); i++)
            {
                if (TorpedoTubes[i] != null && TorpedoTubes[i].OK())
                {
                    if (!TorpedoTubeGroups.ContainsKey(TorpedoTubes[i].GroupName))
                        TorpedoTubeGroups[TorpedoTubes[i].GroupName] = new TorpedoTubeGroup(TorpedoTubes[i].GroupName);

                    TorpedoTubeGroups[TorpedoTubes[i].GroupName].AddTube(TorpedoTubes[i]);
                }
            }

            Program.GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(null, CollectLights);
        }

        bool CollectParts(IMyTerminalBlock block)
        {
            if (!ProgramReference.IsSameConstructAs(block)) return false; // Allow subgrid

            if (!block.CustomName.StartsWith("[TRP")) return false;

            var indexTagEnd = block.CustomName.IndexOf(']');
            if (indexTagEnd == -1) return false;

            var numString = block.CustomName.Substring(4, indexTagEnd - 4);

            int index;
            if (!int.TryParse(numString, out index)) return false;
            if (TorpedoTubes[index] == null) TorpedoTubes[index] = new TorpedoTube(index, Program, this);
            TorpedoTubes[index].AddPart(block);

            return false;
        }

        bool CollectLights(IMyTerminalBlock block)
        {
            if (!ProgramReference.IsSameConstructAs(block)) return false; // Allow subgrid

            if (!block.CustomName.StartsWith("[TRP")) return false;

            if (!(block is IMyInteriorLight)) return false;

            var groupName = block.CubeGrid.GridSizeEnum == MyCubeSize.Small ? "SM" : "LG";
            TorpedoTubeGroup group;
            if (TorpedoTubeGroups.TryGetValue(groupName, out group))
            {
                var light = (IMyInteriorLight)block;
                group.AutofireIndicator = light;
            }

            return false;
        }

        void Fire(object argument, TimeSpan localTime, bool trickshot = false)
        {
            if (argument == null) return;

            int index;

            if ((string)argument == "all")
            {
                for (int i = 0; i < TorpedoTubes.Count(); i++)
                {
                    Fire(localTime, TorpedoTubes[i], null, trickshot);
                }
            }
            else if (TorpedoTubeGroups.ContainsKey((string)argument))
            {
                Fire(localTime, TorpedoTubeGroups[(string)argument], null, trickshot);
            }
            else
            {
                if (!(argument is string)) return;
                if (!int.TryParse((string)argument, out index)) return;
                Fire(localTime, TorpedoTubes[index], null, trickshot);
            }
        }

        public Torpedo Fire(TimeSpan localTime, ITorpedoControllable unit, EnemyShipIntel target = null, bool trickshot = true)
        {
            if (unit == null || !unit.Ready) return null;
            var torp = unit.Fire(localTime + IntelProvider.CanonicalTimeDiff, target, trickshot);
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
        public List<IMyGyro> Gyros = new List<IMyGyro>();
        public HashSet<IMyWarhead> Warheads = new HashSet<IMyWarhead>();
        public HashSet<IMyThrust> Thrusters = new HashSet<IMyThrust>();
        public HashSet<IMyBatteryBlock> Batteries = new HashSet<IMyBatteryBlock>();
        public HashSet<IMyGasTank> Tanks = new HashSet<IMyGasTank>();
        public List<IMyCameraBlock> Cameras = new List<IMyCameraBlock>();
        public float[] CameraExtends = new float[8];
        public IMySensorBlock Sensor;
        public IMyShipController Controller;
        public string Tag; // HE, CLST, MICRO, etc
        public HashSet<IMyShipMergeBlock> Splitters = new HashSet<IMyShipMergeBlock>();

        public HashSet<Torpedo> SubTorpedos = new HashSet<Torpedo>();

        public IMyTerminalBlock Fuse;

        GyroControl gyroControl;

        PDController yawController = new PDController(DEF_PD_P_GAIN, DEF_PD_D_GAIN, 6);
        PDController pitchController = new PDController(DEF_PD_P_GAIN, DEF_PD_D_GAIN, 6);

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
        public bool canInitialize = true;
        int runs = 0;

        Vector3D RandomOffset;

        public bool UseTrickshot = false;
        Vector3D TrickshotOffset = Vector3D.Zero;
        public bool proxArmed = false;
        public TorpedoSubsystem HostSubsystem = null;

        public bool AddPart(IMyTerminalBlock block)
        {
            bool part = false;
            if (block.CustomName.Contains("[F]")) { Fuse = block; part = true; }
            if (block is IMyShipController) { Controller = (IMyShipController)block; part = true; }
            if (block is IMyGyro) { Gyros.Add((IMyGyro)block); part = true; }
            if (block is IMyCameraBlock) { var camera = (IMyCameraBlock)block; Cameras.Add(camera); camera.EnableRaycast = true; float.TryParse(camera.CustomData, out CameraExtends[Cameras.Count]); part = true; }
            if (block is IMySensorBlock) { Sensor = (IMySensorBlock)block; part = true; }
            if (block is IMyThrust) { Thrusters.Add((IMyThrust)block); ((IMyThrust)block).Enabled = false ; part = true; }
            if (block is IMyWarhead) { Warheads.Add((IMyWarhead)block); part = true; }
            if (block is IMyShipMergeBlock) { Splitters.Add((IMyShipMergeBlock)block); part = true; }
            if (block is IMyBatteryBlock) { Batteries.Add((IMyBatteryBlock)block); ((IMyBatteryBlock)block).Enabled = false; part = true; }
            if (block is IMyGasTank) { Tanks.Add((IMyGasTank)block); ((IMyGasTank)block).Enabled = true; part = true; }
            return part;
        }

        public void Init(TimeSpan CanonicalTime)
        {
            initialized = true;
            gyroControl = new GyroControl(Gyros);
            var refWorldMatrix = Controller.WorldMatrix;
            gyroControl.Init(ref refWorldMatrix);
            foreach (var tank in Tanks) tank.Stockpile = false;

            foreach (var Gyro in Gyros)
            {
                Gyro.GyroOverride = true;
                Gyro.Enabled = true;
            }

            launchTime = CanonicalTime;

            var rand = new Random();
            RandomOffset = new Vector3D(rand.NextDouble() - 0.5, rand.NextDouble() - 0.5, rand.NextDouble() - 0.5);
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
                    torp.Init(launchTime);
                    if (torp.Target == null) torp.Target = Target;
                }
            }
            SubTorpedos.Clear();
        }

        public void Update(EnemyShipIntel Target, TimeSpan CanonicalTime, float SeparationTime = 2)
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
            if (CanonicalTime - launchTime < TimeSpan.FromSeconds(SeparationTime)) return;
            if (Target == null) return;

            if (CanonicalTime - launchTime > TimeSpan.FromSeconds(SeparationTime + 1) && SubTorpedos.Count > 0) Split();

            this.Target = Target;

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

        const float ROLL_THETA = 0;

        Vector3D RefreshNavigation(TimeSpan CanonicalTime)
        {
            Vector3D rangeVector = Target.GetPositionFromCanonicalTime(CanonicalTime) + (RandomOffset * Target.Radius * 0) - Controller.WorldMatrix.Translation;

            if (rangeVector.LengthSquared() < 120 * 120) proxArmed = true;

            rangeVector += TrickshotOffset;

            var grav = Controller.GetNaturalGravity();
            if (plunging)
            {

                if (grav == Vector3D.Zero)
                {
                    plunging = false;
                }

                var gravDir = grav;
                gravDir.Normalize();

                var targetHeightDiff = rangeVector.Dot(-gravDir); // Positive if target is higher than missile

                if (rangeVector.LengthSquared() < HostSubsystem.PlungeDist * HostSubsystem.PlungeDist && targetHeightDiff > 0)
                {
                    plunging = false;
                }

                if (plunging)
                {
                    rangeVector -= gravDir * HostSubsystem.PlungeDist;
                    if (rangeVector.LengthSquared() < 200 * 200)
                        plunging = false;
                }
            }

            if (TrickshotOffset == Vector3D.Zero && UseTrickshot)
            {
                var perp = new Vector3D(1, 1, -(rangeVector.X + rangeVector.Y) / rangeVector.Z);

                var coperp = perp.Cross(rangeVector);
                perp.Normalize();
                coperp.Normalize();

                var rand = new Random();
                var theta = rand.NextDouble() * Math.PI * 2;

                var dist = -rangeVector;
                dist.Normalize();
                dist *= 1200;

                TrickshotOffset = perp * Math.Sin(theta) + coperp * Math.Cos(theta);

                TrickshotOffset *= 1200;
                // TrickshotOffset += dist;
                UseTrickshot = false;
            }
            if (TrickshotOffset != Vector3D.Zero && rangeVector.LengthSquared() < 100*100)
            {
                TrickshotOffset = Vector3D.Zero;
            }

            var linearVelocity = Controller.GetShipVelocities().LinearVelocity;
            Vector3D velocityVector = Target.GetVelocity() - linearVelocity;
            var speed = Controller.GetShipSpeed();

            if (linearVelocity.Dot(ref rangeVector) > 0)
            {
                Vector3D rangeDivSqVector = rangeVector / rangeVector.LengthSquared();
                Vector3D compensateVector = velocityVector - (velocityVector.Dot(ref rangeVector) * rangeDivSqVector);

                Vector3D targetANVector;
                var targetAccel = (lastTargetVelocity - Target.GetVelocity()) * 0.16667;

                targetANVector = targetAccel - grav - (targetAccel.Dot(ref rangeVector) * rangeDivSqVector);

                if (speed > lastSpeed + 1)
                {
                    AccelerationVector = linearVelocity + (3.5 * 1.5 * (compensateVector + (0.5 * targetANVector)));
                }
                else
                {
                    AccelerationVector = linearVelocity + (3.5 * (compensateVector + (0.5 * targetANVector)));
                }
            }
            else
            {
                AccelerationVector = (rangeVector * 0.1) + velocityVector;
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
        string Name { get; }
        bool Ready { get; }
        Torpedo Fire(TimeSpan canonicalTime, EnemyShipIntel target = null, bool trickshot = true);
    }

    public class TorpedoTubeGroup : ITorpedoControllable
    {
        public string Name { get; set; }

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

        public bool AutoFire { get; set; }

        public List<ITorpedoControllable> Children { get; set; }

        public Torpedo Fire(TimeSpan canonicalTime, EnemyShipIntel target = null, bool trickshot = true)
        {
            foreach (ITorpedoControllable tube in Children)
            {
                if (tube.Ready) return tube.Fire(canonicalTime, target, trickshot);
            }
            return null;
        }

        public TorpedoTubeGroup(string name)
        {
            Name = name;
            Children = new List<ITorpedoControllable>();
            AutoFire = false;
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

        public IMyInteriorLight AutofireIndicator;
    }

    public class TorpedoTube : ITorpedoControllable
    {
        public IMyShipMergeBlock Release;
        public IMyShipConnector Connector;

        public Torpedo LoadedTorpedo;

        MyGridProgram Program;
        TorpedoSubsystem Host;
        Torpedo[] SubTorpedosScratchpad = new Torpedo[16];

        public MyCubeSize Size;
        public bool AutoFire { get; set; }
        public string Name { get; set; }
        public string GroupName;
        public bool Ready => LoadedTorpedo != null;
        public List<ITorpedoControllable> Children { get; set; }

        public TorpedoTube(int index, MyGridProgram program, TorpedoSubsystem host)
        {
            Program = program;
            Host = host;
            Children = new List<ITorpedoControllable>();
            Name = index.ToString("00");
            Fire(TimeSpan.Zero, null);
        }

        public bool OK()
        {
            return Release != null;
        }

        public void AddPart(IMyTerminalBlock block)
        {
            if (block is IMyShipMergeBlock)
            {
                Release = (IMyShipMergeBlock)block;
                Size = Release.CubeGrid.GridSizeEnum;
                AutoFire = Size == MyCubeSize.Small;
                GroupName = Size == MyCubeSize.Small ? "SM" : "LG";
            }
            if (block is IMyShipConnector) Connector = (IMyShipConnector)block;
        }

        public void Update(TimeSpan LocalTime)
        {
            if (LoadedTorpedo == null)
            {
                GetTorpedo();
            }
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

        bool LoadTorpedoParts(ref List<IMyTerminalBlock> results)
        {
            var releaseOther = GridTerminalHelper.OtherMergeBlock(Release);
            if (releaseOther == null || !releaseOther.IsFunctional || !releaseOther.Enabled) return false;

            return GridTerminalHelper.Base64BytePosToBlockList(releaseOther.CustomData, releaseOther, ref results);
        }

        void GetTorpedo()
        {
            LoadedTorpedo = new Torpedo();

            for (int i = 0; i < SubTorpedosScratchpad.Length; i++)
            {
                SubTorpedosScratchpad[i] = null;
            }

            Host.PartsScratchpad.Clear();
            if (!LoadTorpedoParts(ref Host.PartsScratchpad))
            {
                LoadedTorpedo = null;
                return;
            }

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

        public Torpedo Fire(TimeSpan canonicalTime, EnemyShipIntel target = null, bool trickshot = true)
        {
            if (canonicalTime == TimeSpan.Zero) return null;
            if (LoadedTorpedo == null) return null;

            var releaseOther = GridTerminalHelper.OtherMergeBlock(Release);
            if (releaseOther == null) return null;
            releaseOther.Enabled = false;

            if (Connector != null && Connector.Status == MyShipConnectorStatus.Connected) Connector.OtherConnector.Enabled = false;
            var torp = LoadedTorpedo;
            torp.UseTrickshot = trickshot;
            foreach (var sub in torp.SubTorpedos)
            {
                sub.HostSubsystem = Host;
                sub.UseTrickshot = trickshot;
                sub.canInitialize = false;
            }
            torp.Init(canonicalTime);
            LoadedTorpedo = null;
            torp.Target = target;
            torp.HostSubsystem = Host;
            return torp;
        }
    }
}
