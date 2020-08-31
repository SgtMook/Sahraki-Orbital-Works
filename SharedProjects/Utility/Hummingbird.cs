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

        public const string GroupName = "[HB-GROUP]";
        List<IMySmallGatlingGun> Gats = new List<IMySmallGatlingGun>();
        TriskelionDrive Drive = new TriskelionDrive();
        IMyLargeTurretBase Designator;
        IMyShipController Controller;
        IMyMotorStator TurretRotor;
        Vector3D destination = new Vector3D();
        Vector3D target = Vector3D.Zero;

        Vector3D PlanetPos;

        public string Status;

        PID TurretPID;

        float TP = 30;
        float TI = 1.25f;
        float TD = 20;
        float TC = 0.95f;

        int run = 0;
        int kRunEveryXUpdates = 5;

        int fireTicks = 0;

        Vector3D linearVelocity = Vector3D.Zero;
        Vector3D LastLinearVelocity = Vector3D.Zero;
        Vector3D LastAcceleration = Vector3D.Zero;
        MatrixD LastReference = MatrixD.Zero;

        public void SetTarget(Vector3 newTarget)
        {
            target = newTarget;
        }

        public void SetDest(Vector3 newDest)
        {
            destination = newDest;
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
        }

        bool CollectParts(IMyTerminalBlock block, IMyCubeBlock reference)
        {
            if (reference.CubeGrid.EntityId != block.CubeGrid.EntityId) return false;

            if (block is IMyGyro)
                Drive.AddGyro((IMyGyro)block);

            if (block is IMySmallGatlingGun)
                Gats.Add((IMySmallGatlingGun)block);

            if (block is IMyMotorStator)
                TurretRotor = (IMyMotorStator)block;

            if (block is IMyLargeTurretBase)
                Designator = (IMyLargeTurretBase)block;

            if (block is IMyThrust)
                Drive.AddEngine(new HoverEngineDriver((IMyThrust)block));

            if (block is IMyShipController)
            {
                Controller = (IMyShipController)block;
                Controller.TryGetPlanetPosition(out PlanetPos);
                Drive.Controller = Controller;
            }

            return false;
        }

        public void Update()
        {
            run++;
            if (run % kRunEveryXUpdates == 0)
            {
                Vector3D PlanetDir = PlanetPos - Controller.WorldMatrix.Translation;
                PlanetDir.Normalize();

                // Get target
                if (Designator != null && Designator.HasTarget)
                {
                    linearVelocity = Controller.GetShipVelocities().LinearVelocity;
                    var targetEntity = Designator.GetTargetedEntity();

                    var Acceleration = linearVelocity - LastLinearVelocity;
                    if (LastAcceleration == Vector3D.Zero) LastAcceleration = Acceleration;
                    if (LastReference == MatrixD.Zero) LastReference = Gats[0].WorldMatrix;

                    var CurrentAccelerationPreviousFrame = Vector3D.TransformNormal(Acceleration, MatrixD.Transpose(LastReference));

                    var accelerationAdjust = Vector3D.TransformNormal(CurrentAccelerationPreviousFrame, Gats[0].WorldMatrix);
                    var velocityAdjust = linearVelocity + (accelerationAdjust) * 0.05;

                    Vector3D relativeAttackPoint = AttackHelpers.GetAttackPoint(targetEntity.Velocity - velocityAdjust, targetEntity.Position + targetEntity.Velocity * 0.05f - (Gats[0].WorldMatrix.Translation + velocityAdjust * 0.22), 400);

                    LastAcceleration = linearVelocity - LastLinearVelocity;
                    LastReference = Gats[0].WorldMatrix;
                    LastLinearVelocity = linearVelocity;

                    target = relativeAttackPoint + Gats[0].WorldMatrix.Translation;

                    // Testing - chase target
                    var diff = Gats[0].WorldMatrix.Translation - targetEntity.Position;
                    diff.Normalize();
                    var orbit = diff.Cross(PlanetDir);
                    diff *= 400;
                    destination = targetEntity.Position + diff + orbit * 100;
                }
                else
                {
                    target = Vector3D.Zero;
                }

                // Orient Self
                Drive.Turn(Gats.Count > 0 ? Gats[0].WorldMatrix : Controller.WorldMatrix, target);

                // Aim Turret
                if (TurretRotor != null)
                {
                    if (target != Vector3D.Zero)
                    {
                        var TurretAngle = TrigHelpers.FastAsin(Gats[0].WorldMatrix.Forward.Dot(PlanetDir));

                        Vector3D TargetDir = target - Gats[0].WorldMatrix.Translation;
                        TargetDir.Normalize();
                        var TargetAngle = TrigHelpers.FastAsin(TargetDir.Dot(PlanetDir));

                        var angleDiff = TargetAngle - TurretAngle;

                        if (VectorHelpers.VectorAngleBetween(Gats[0].WorldMatrix.Forward, TargetDir) < 0.05)
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
                if (destination != Vector3D.Zero)
                {
                    var usedEngines = new List<IMyTerminalBlock>();
                    var destDir = destination - Controller.WorldMatrix.Translation;
                    destDir -= VectorHelpers.VectorProjection(destDir, PlanetDir);
                    var dist = destDir.Length();

                    // Status = dist.ToString();

                    if (dist < 20)
                    {
                        destDir = Vector3D.Zero;
                    }
                    else
                    {
                        destDir.Normalize();
                        var shipVel = Controller.GetShipVelocities().LinearVelocity;
                        shipVel -= VectorHelpers.VectorProjection(shipVel, PlanetDir);
                        shipVel *= 0.02;
                        destDir -= shipVel;
                        
                        //Status = $"SHIPVEL {shipVel}, DESTDIR {destDir}, SHIPSPD {shipVel.Length() / 0.02}";
                        destDir.Normalize();
                    }

                    Drive.Drive(destDir);
                    Status = Drive.Status;
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
