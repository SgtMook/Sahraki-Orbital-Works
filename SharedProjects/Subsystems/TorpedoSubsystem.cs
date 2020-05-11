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
        }

        public void DeserializeSubsystem(string serialized)
        {
        }

        public string GetStatus()
        {
            //debugBuilder.AppendLine(TorpedoTubes[1].Release == null ? "NULL RELEASE" : TorpedoTubes[1].Release.CustomName);
            //debugBuilder.AppendLine(GridTerminalHelper.OtherMergeBlock(TorpedoTubes[1].Release) == null ? "NULL OTHER RELEASE" : GridTerminalHelper.OtherMergeBlock(TorpedoTubes[1].Release).CustomName);
            //
            //PartsScratchpad.Clear();
            //GridTerminalHelper.Base64BytePosToBlockList(GridTerminalHelper.OtherMergeBlock(TorpedoTubes[1].Release).CustomData, GridTerminalHelper.OtherMergeBlock(TorpedoTubes[1].Release), ref PartsScratchpad);
            //
            //debugBuilder.AppendLine(PartsScratchpad.Count().ToString());
            //debugBuilder.AppendLine(TorpedoTubes[1].LoadedTorpedo == null ? "NULL Torpedo" : TorpedoTubes[1].LoadedTorpedo.SubTorpedos.Count.ToString());

            //foreach (var t in Torpedos)
            //{
            //    if (t.Camera == null) debugBuilder.AppendLine("NULL");
            //    else debugBuilder.AppendLine(t.Camera.DisplayName);
            //
            //    debugBuilder.AppendLine(Vector3D.TransformNormal(t.AccelerationVector, MatrixD.Transpose(t.Controller.WorldMatrix)).ToString());
            //    debugBuilder.Append("===");
            //}
            return debugBuilder.ToString();
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
            if ((updateFlags & UpdateFrequency.Update100) != 0)
            {
                // Update Tubes
                for (int i = 0; i < TorpedoTubes.Count(); i++)
                {
                    if (TorpedoTubes[i] != null && TorpedoTubes[i].OK())
                    {
                        TorpedoTubes[i].Update(timestamp);
                    }
                }
            }
            if ((updateFlags & UpdateFrequency.Update10) != 0)
            {
                // Update Torpedos
                TorpedoScratchpad.Clear();

                var intelItems = IntelProvider.GetFleetIntelligences(timestamp);
                var canonicalTime = timestamp + IntelProvider.CanonicalTimeDiff;

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

                            double dist = (enemyIntel.GetPositionFromCanonicalTime(canonicalTime) - torp.Controller.WorldMatrix.Translation).Length();
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
            if ((updateFlags & UpdateFrequency.Update1) != 0)
            {
                TorpedoScratchpad.Clear();
                foreach (var torp in Torpedos)
                {
                    torp.FastUpdate();
                    if (torp.proxArmed)
                    {
                        if (torp.Camera != null && torp.Camera.IsWorking)
                        {
                            var extend = torp.Camera.CubeGrid.GridSizeEnum == MyCubeSize.Large ? 11.5 : 3;
                            MyDetectedEntityInfo detected = torp.Camera.Raycast(torp.Controller.GetShipSpeed() * 0.017 + extend);
                            if (detected.EntityId != 0 && (detected.Relationship == MyRelationsBetweenPlayerAndBlock.Enemies || detected.Relationship == MyRelationsBetweenPlayerAndBlock.Neutral))
                            {
                                TorpedoScratchpad.Add(torp);
                                torp.Detonate();
                            }
                        }
                        if (torp.Sensor != null && torp.Sensor.IsWorking)
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

        MyGridProgram Program;
        IIntelProvider IntelProvider;

        StringBuilder debugBuilder = new StringBuilder();

        public MyIni IniParser = new MyIni();

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
        }

        private bool CollectParts(IMyTerminalBlock block)
        {
            if (!Program.Me.IsSameConstructAs(block)) return false; // Allow subgrid
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

        void Fire(object argument, TimeSpan localTime)
        {
            int index;

            if ((string)argument == "all")
            {
                for (int i = 0; i < TorpedoTubes.Count(); i++)
                {
                    Fire(localTime, TorpedoTubes[i]);
                }
            }
            else if (TorpedoTubeGroups.ContainsKey((string)argument))
            {
                Fire(localTime, TorpedoTubeGroups[(string)argument]);
            }
            else
            {
                if (!(argument is string)) return;
                if (!int.TryParse((string)argument, out index)) return;
                Fire(localTime, TorpedoTubes[index]);
            }
        }

        public bool Fire(TimeSpan localTime, ITorpedoControllable unit, EnemyShipIntel target = null)
        {
            if (unit == null || !unit.Ready) return false;
            var torp = unit.Fire(localTime + IntelProvider.CanonicalTimeDiff, target);
            if (torp != null)
            {
                Torpedos.Add(torp);
                foreach (var subtorp in torp.SubTorpedos) Torpedos.Add(subtorp);
                return true;
            }
            return false;
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
        public IMyCameraBlock Camera;
        public IMySensorBlock Sensor;
        public IMyShipController Controller;
        public string Tag; // HE, CLST, MICRO, etc
        public HashSet<IMyShipMergeBlock> Splitters = new HashSet<IMyShipMergeBlock>();

        public HashSet<Torpedo> SubTorpedos = new HashSet<Torpedo>();

        GyroControl gyroControl;

        PDController yawController = new PDController(DEF_PD_P_GAIN, DEF_PD_D_GAIN, 6);
        PDController pitchController = new PDController(DEF_PD_P_GAIN, DEF_PD_D_GAIN, 6);

        TimeSpan launchTime = TimeSpan.Zero;

        public bool Disabled = false;
        public EnemyShipIntel Target = null;

        double lastSpeed;
        Vector3D lastTargetVelocity;

        public Vector3D AccelerationVector;

        bool initialized = false;
        int runs = 0;
        public int Detonating = -1;

        Vector3D RandomOffset;

        bool UseTrickshot = true;
        Vector3D TrickshotOffset = Vector3D.Zero;
        public bool proxArmed = false;

        public bool AddPart(IMyTerminalBlock block)
        {
            if (block is IMyShipController) { Controller = (IMyShipController)block; return true; }
            if (block is IMyGyro) { Gyros.Add((IMyGyro)block); return true; }
            if (block is IMyCameraBlock) { Camera = (IMyCameraBlock)block; Camera.EnableRaycast = true; return true; }
            if (block is IMySensorBlock) { Sensor = (IMySensorBlock)block; return true; }
            if (block is IMyThrust) { Thrusters.Add((IMyThrust)block); return true; }
            if (block is IMyWarhead) { Warheads.Add((IMyWarhead)block); return true; }
            if (block is IMyShipMergeBlock) { Splitters.Add((IMyShipMergeBlock)block); return true; }
            if (block is IMyBatteryBlock) { Batteries.Add((IMyBatteryBlock)block); return true; }
            if (block is IMyGasTank) { Tanks.Add((IMyGasTank)block); return true; }
            return false;
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

        private void Split()
        {
            foreach (var merge in Splitters)
            {
                merge.Enabled = false;
            }
            foreach (var torp in SubTorpedos)
            {
                torp.Init(launchTime);
                torp.Target = Target;
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
            if (CanonicalTime - launchTime < TimeSpan.FromSeconds(2)) return;
            if (Target == null) return;

            if (CanonicalTime - launchTime > TimeSpan.FromSeconds(3) && SubTorpedos.Count > 0) Split();

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
            Vector3D velocityVector = Target.CurrentVelocity - linearVelocity;
            var speed = Controller.GetShipSpeed();

            if (linearVelocity.Dot(ref rangeVector) > 0)
            {
                Vector3D rangeDivSqVector = rangeVector / rangeVector.LengthSquared();
                Vector3D compensateVector = velocityVector - (velocityVector.Dot(ref rangeVector) * rangeDivSqVector);

                Vector3D targetANVector;
                var targetAccel = lastTargetVelocity - Target.CurrentVelocity;

                var grav = Controller.GetNaturalGravity();
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

            lastTargetVelocity = Target.CurrentVelocity;
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

        private void CheckGyro()
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
        Torpedo Fire(TimeSpan canonicalTime, EnemyShipIntel target = null);
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

        public Torpedo Fire(TimeSpan canonicalTime, EnemyShipIntel target = null)
        {
            foreach (ITorpedoControllable tube in Children)
            {
                if (tube.Ready) return tube.Fire(canonicalTime, target);
            }
            return null;
        }

        public TorpedoTubeGroup(string name)
        {
            Name = name;
            Children = new List<ITorpedoControllable>();
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

        public Torpedo Fire(TimeSpan canonicalTime, EnemyShipIntel target = null)
        {
            if (canonicalTime == TimeSpan.Zero) return null;
            if (LoadedTorpedo == null) return null;

            var releaseOther = GridTerminalHelper.OtherMergeBlock(Release);
            if (releaseOther == null) return null;
            releaseOther.Enabled = false;

            if (Connector != null && Connector.Status == MyShipConnectorStatus.Connected) Connector.OtherConnector.Enabled = false;
            var torp = LoadedTorpedo;
            torp.Init(canonicalTime);
            LoadedTorpedo = null;
            torp.Target = target;
            return torp;
        }
    }
}
