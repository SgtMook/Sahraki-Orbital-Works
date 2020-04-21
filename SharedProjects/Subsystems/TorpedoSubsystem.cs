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
            debugBuilder.Clear();

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
            //    if (t.Target == null) debugBuilder.AppendLine("NULL");
            //    else debugBuilder.AppendLine(t.Target.DisplayName);
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

            // Update Tubes
            for (int i = 0; i < TorpedoTubes.Count(); i++)
            {
                if (TorpedoTubes[i] != null && TorpedoTubes[i].OK())
                {
                    TorpedoTubes[i].Update(timestamp);
                }
            }
        }
        #endregion

        public TorpedoSubsystem(IIntelProvider intelProvider)
        {
            UpdateFrequency = UpdateFrequency.Update10;
            IntelProvider = intelProvider;
        }

        public TorpedoTube[] TorpedoTubes = new TorpedoTube[16];

        public HashSet<Torpedo> Torpedos = new HashSet<Torpedo>();
        public List<Torpedo> TorpedoScratchpad = new List<Torpedo>();
        public List<IMyTerminalBlock> PartsScratchpad = new List<IMyTerminalBlock>();

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

            Program.GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(null, CollectParts);
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
            if (TorpedoTubes[index] == null) TorpedoTubes[index] = new TorpedoTube(Program, this);
            TorpedoTubes[index].AddPart(block);

            return false;
        }

        void Fire(object argument, TimeSpan localTime)
        {
            int index;
            if (!(argument is string)) return;
            if (!int.TryParse((string)argument, out index)) return;
            if (TorpedoTubes[index] != null && TorpedoTubes[index].OK())
            {
                var torp = TorpedoTubes[index].Fire(localTime + IntelProvider.CanonicalTimeDiff);
                if (torp != null)
                {
                    Torpedos.Add(torp);
                    foreach (var subtorp in torp.SubTorpedos) Torpedos.Add(subtorp);
                }
            }
        }
    }

    // This is a refhax torpedo
    public class Torpedo
    {
        public IMyGyro Gyro;
        public HashSet<IMyWarhead> Warheads = new HashSet<IMyWarhead>();
        public HashSet<IMyThrust> Thrusters = new HashSet<IMyThrust>();
        public HashSet<IMyBatteryBlock> Batteries = new HashSet<IMyBatteryBlock>();
        public HashSet<IMyGasTank> Tanks = new HashSet<IMyGasTank>();
        public IMyCameraBlock Camera;
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

        Vector3D RandomOffset;

        public bool AddPart(IMyTerminalBlock block)
        {
            if (block is IMyShipController) { Controller = (IMyShipController)block; return true; }
            if (block is IMyGyro) { Gyro = (IMyGyro)block; return true; }
            if (block is IMyCameraBlock) { Camera = (IMyCameraBlock)block; return true; }
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
            List<IMyGyro> gyros = new List<IMyGyro>();
            gyros.Add(Gyro);
            gyroControl = new GyroControl(gyros);
            var refWorldMatrix = Controller.WorldMatrix;
            gyroControl.Init(ref refWorldMatrix);

            foreach (var thruster in Thrusters)
            {
                thruster.Enabled = true;
                thruster.ThrustOverridePercentage = 1;
            }

            Gyro.GyroOverride = true;
            Gyro.Enabled = true;

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
            }
            SubTorpedos.Clear();
        }

        public void Update(EnemyShipIntel Target, TimeSpan CanonicalTime)
        {
            if (!initialized) return;
            if (!OK())
            {
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

        public bool OK()
        {
            return Gyro != null && Gyro.IsFunctional && Controller != null && Controller.IsFunctional && Thrusters.Count > 0;
        }

        void AimAtTarget(Vector3D TargetVector)
        {
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
        }

        const double DEF_PD_P_GAIN = 10;
        const double DEF_PD_D_GAIN = 5;
        const double DEF_PD_AIM_LIMIT = 6.3;

        Vector3D RefreshNavigation(TimeSpan CanonicalTime)
        {
            Vector3D rangeVector = Target.GetPositionFromCanonicalTime(CanonicalTime) + (RandomOffset * Target.Radius * 0.3) - Controller.WorldMatrix.Translation;

            if (rangeVector.LengthSquared() < 50 * 50) Arm();

            var linearVelocity = Controller.GetShipVelocities().LinearVelocity;
            Vector3D velocityVector = Target.CurrentVelocity - linearVelocity;
            var speed = Controller.GetShipSpeed();

            if (linearVelocity.Dot(ref rangeVector) > 0)
            {
                Vector3D rangeDivSqVector = rangeVector / rangeVector.LengthSquared();
                Vector3D compensateVector = velocityVector - (velocityVector.Dot(ref rangeVector) * rangeDivSqVector);

                Vector3D targetANVector;
                var targetAccel = (lastTargetVelocity - Target.CurrentVelocity);
                targetANVector = targetAccel - (targetAccel.Dot(ref rangeVector) * rangeDivSqVector);

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

        double FastAT(double x)
        {
            //Removed Math.Abs() since x is always positive in this script
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

    public struct TorpedoConfig
    {
        int SeparationDelay;
        int SplitDelay;
        float kP;
        float kD;
    }

    public class TorpedoTube
    {
        public IMyShipMergeBlock Release;
        public IMyShipConnector Connector;

        public Torpedo LoadedTorpedo;

        MyGridProgram Program;
        TorpedoSubsystem Host;
        Torpedo[] SubTorpedosScratchpad = new Torpedo[16];

        public TorpedoTube(MyGridProgram program, TorpedoSubsystem host)
        {
            Program = program;
            Host = host;
        }

        public bool OK()
        {
            return Release != null;
        }

        public void AddPart(IMyTerminalBlock block)
        {
            if (block is IMyShipMergeBlock) Release = (IMyShipMergeBlock)block;
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
            if (releaseOther == null) return false;

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

            foreach (var part in Host.PartsScratchpad) AddTorpedoPart(part);

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

        public Torpedo Fire(TimeSpan canonicalTime)
        {
            if (LoadedTorpedo == null) return LoadedTorpedo;

            var releaseOther = GridTerminalHelper.OtherMergeBlock(Release);
            if (releaseOther == null) return null;
            releaseOther.Enabled = false;

            if (Connector != null && Connector.Status == MyShipConnectorStatus.Connected) Connector.OtherConnector.Enabled = false;
            foreach (var tank in LoadedTorpedo.Tanks) tank.Stockpile = false;

            var torp = LoadedTorpedo;
            torp.Init(canonicalTime);
            LoadedTorpedo = null;
            return torp;
        }
    }
}