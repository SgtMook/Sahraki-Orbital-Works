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
    // This is a Hummingbird class hover tank
    public class Hummingbird
    {
        public static Hummingbird GetHummingbird(IMyTerminalBlock reference, IMyBlockGroup group)
        {
            if (group == null) throw new Exception($"CANNOT FIND GROUP {GroupName}");

            Hummingbird hummingbird = new Hummingbird();

            group.GetBlocksOfType<IMyTerminalBlock>(null, (x) => hummingbird.CollectParts(x, reference));
            if (hummingbird.TurretRotor != null)
                group.GetBlocksOfType<IMyTerminalBlock>(null, (x) => hummingbird.CollectParts(x, hummingbird.TurretRotor.Top));

            hummingbird.SetUp();

            return hummingbird;
        }

        public static bool CheckHummingbirdComponents(IMyShipMergeBlock baseplate, ref IMyShipConnector connector, ref IMyMotorStator turretRotor, ref List<IMyTerminalBlock> PartsScratchpad, ref string status)
        {
            var releaseOther = GridTerminalHelper.OtherMergeBlock(baseplate);
            if (releaseOther == null || !releaseOther.IsFunctional || !releaseOther.Enabled) return false;

            PartsScratchpad.Clear();

            var gotParts = GridTerminalHelper.Base64BytePosToBlockList(releaseOther.CustomData, releaseOther, ref PartsScratchpad);

            if (!gotParts) return false;

            foreach (var block in PartsScratchpad)
            {
                if (!block.IsFunctional) return false;
                if (block is IMyMotorStator) turretRotor = (IMyMotorStator)block;
                if (block is IMyShipConnector) connector = (IMyShipConnector)block;
            }
            return true;
        }

        public const string GroupName = "[HB-GROUP]";
        public const float RecommendedServiceFloor = 15;
        public const float RecommendedServiceCeiling = 35;
        public List<IMySmallGatlingGun> Gats = new List<IMySmallGatlingGun>();
        public List<IMyCameraBlock> Cameras = new List<IMyCameraBlock>();
        public TriskelionDrive Drive = new TriskelionDrive();
        public IMyLargeTurretBase Designator;
        public IMyShipController Controller;
        public IMyMotorStator TurretRotor;
        public IMyTerminalBlock Base;
        public IMyRadioAntenna Antenna;
        public bool IsRetiring = false;
        public bool IsLanding = false;
        public Vector3D Destination = new Vector3D();
        Vector3D target = Vector3D.Zero;

        Vector3D PlanetPos;

        public string Status;

        PID TurretPID;

        float TP = 50;
        float TI = 5f;
        float TD = 100;
        float TC = 0.95f;

        public int LifeTimeTicks = 0;
        int kRunEveryXUpdates = 5;

        int fireTicks = 0;

        Vector3D linearVelocity = Vector3D.Zero;
        Vector3D LastLinearVelocity = Vector3D.Zero;
        Vector3D LastAcceleration = Vector3D.Zero;
        MatrixD LastReference = MatrixD.Zero;

        public void SetTarget(Vector3D targetPos, Vector3D targetVel)
        {
            if (targetPos == Vector3D.Zero)
            {
                target = Vector3D.Zero;
                return;
            }

            while (Gats.Count > 0 && !Gats[0].IsWorking) Gats.RemoveAtFast(0);
            if (Gats.Count == 0) return;

            linearVelocity = Controller.GetShipVelocities().LinearVelocity;

            var Acceleration = linearVelocity - LastLinearVelocity;
            if (LastAcceleration == Vector3D.Zero) LastAcceleration = Acceleration;
            if (LastReference == MatrixD.Zero) LastReference = Gats[0].WorldMatrix;

            var CurrentAccelerationPreviousFrame = Vector3D.TransformNormal(Acceleration, MatrixD.Transpose(LastReference));

            var accelerationAdjust = Vector3D.TransformNormal(CurrentAccelerationPreviousFrame, Gats[0].WorldMatrix);
            var velocityAdjust = linearVelocity + (accelerationAdjust) * 0.05;

            Vector3D relativeAttackPoint = AttackHelpers.GetAttackPoint(targetVel - velocityAdjust, targetPos + targetVel * 0.05f - (Gats[0].WorldMatrix.Translation + velocityAdjust * 0.22), 400);

            LastAcceleration = linearVelocity - LastLinearVelocity;
            LastReference = Gats[0].WorldMatrix;
            LastLinearVelocity = linearVelocity;

            target = relativeAttackPoint + Gats[0].WorldMatrix.Translation;
        }

        public void SetDest(Vector3 newDest)
        {
            Destination = newDest;
        }

        public Hummingbird()
        {
            TurretPID = new PID(TP, TI, TD, TC, kRunEveryXUpdates);
        }

        public void SetUp()
        {
            if (TurretRotor != null)
            {
                TurretRotor.LowerLimitDeg = -90;
                TurretRotor.UpperLimitDeg = 30;
            }
            Drive.SetUp(kRunEveryXUpdates);
            Drive.DesiredAltitude = RecommendedServiceFloor;
        }

        bool CollectParts(IMyTerminalBlock block, IMyCubeBlock reference)
        {
            if (reference.CubeGrid.EntityId != block.CubeGrid.EntityId) return false;

            if (block is IMyGyro)
                Drive.AddGyro((IMyGyro)block);

            if (block is IMySmallGatlingGun)
                Gats.Add((IMySmallGatlingGun)block);

            if (block is IMyCameraBlock)
                Cameras.Add((IMyCameraBlock)block);

            if (block is IMyMotorStator)
                TurretRotor = (IMyMotorStator)block;

            if (block is IMyLargeTurretBase)
                Designator = (IMyLargeTurretBase)block;

            if (block is IMyThrust)
                Drive.AddEngine(new HoverEngineDriver((IMyThrust)block));

            if (block is IMyRadioAntenna)
                Antenna = (IMyRadioAntenna)block;

            if (block is IMyBeacon)
                ((IMyBeacon)block).Enabled = false;

            if (block is IMyShipController)
            {
                Controller = (IMyShipController)block;
                Controller.TryGetPlanetPosition(out PlanetPos);
                Drive.Controller = Controller;
            }

            return false;
        }

        public bool IsAlive()
        {
            if (Controller.WorldMatrix.Translation == Vector3D.Zero || !Controller.IsWorking) return false;

            foreach (var engine in Drive.HoverEngines)
            {
                if (engine.Block.WorldMatrix.Translation == Vector3D.Zero || !engine.Block.IsFunctional) return false;
            }

            return true;
        }

        public bool IsCombatCapable()
        {
            // TODO: Check ammo
            // TODO: Check power?
            while (Gats.Count > 0 && !Gats[0].IsWorking) Gats.RemoveAtFast(0);
            if (Gats.Count == 0) return false;

            return true;
        }

        public void Update()
        {
            LifeTimeTicks++;

            // Startup Subroutine
            if (LifeTimeTicks == 1)
            {
                foreach (var engine in Drive.HoverEngines)
                {
                    engine.AltitudeMin = 1;
                    engine.PushOnly = true;
                }
            }

            if (LifeTimeTicks == 60)
            {
                foreach (var engine in Drive.HoverEngines)
                {
                    engine.AltitudeMin = 2;
                }
            }

            if (LifeTimeTicks == 120)
            {
                foreach (var engine in Drive.HoverEngines)
                {
                    engine.AltitudeMin = 7;
                }
            }

            if (LifeTimeTicks % kRunEveryXUpdates == 0)
            {
                Vector3D PlanetDir = PlanetPos - Controller.WorldMatrix.Translation;
                PlanetDir.Normalize();

                // Orient Self
                Drive.Turn(Gats.Count > 0 ? Gats[0].WorldMatrix : Controller.WorldMatrix, target);

                // Aim Turret
                if (TurretRotor != null)
                {
                    if (target != Vector3D.Zero && Drive.GravAngle < Math.PI * 0.1)
                    {
                        var TurretAngle = TrigHelpers.FastAsin(Gats[0].WorldMatrix.Forward.Dot(PlanetDir));

                        Vector3D TargetDir = target - Gats[0].WorldMatrix.Translation;
                        var targetDist = TargetDir.Length();
                        TargetDir.Normalize();
                        var TargetAngle = TrigHelpers.FastAsin(TargetDir.Dot(PlanetDir));

                        var angleDiff = TargetAngle - TurretAngle;

                        if (VectorHelpers.VectorAngleBetween(Gats[0].WorldMatrix.Forward, TargetDir) < 0.05 && targetDist < 800)
                        {
                            Fire();
                        }

                        TurretRotor.TargetVelocityRPM = (float)TurretPID.Control(angleDiff);
                    }
                    else
                    {
                        TurretPID.Reset();
                        TurretRotor.TargetVelocityRPM = 0;
                    }
                }

                // Check your fire
                fireTicks--;
                if (fireTicks == -1)
                {
                    foreach (var gat in Gats)
                    {
                        TerminalPropertiesHelper.SetValue(gat, "Shoot", false);
                    }
                }

                // Check Movement
                if (Destination != Vector3D.Zero)
                {
                    Drive.Drive(Destination);
                    if (Drive.Arrived)
                    {
                        Destination = Vector3D.Zero;
                        Drive.Flush();
                    }
                }
            }
        }

        void Fire()
        {
            if (fireTicks <= 0)
            {
                foreach (var gat in Gats)
                {
                    TerminalPropertiesHelper.SetValue(gat, "Shoot", true);
                }
            }
            fireTicks = 4;
        }
    }
}
