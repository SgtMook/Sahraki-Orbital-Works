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
    public class Landpedo
    {
        // This is a landpedo disposable rocket drone
        public Landpedo()
        {

        }

        public List<IMyFunctionalBlock> Launchers = new List<IMyFunctionalBlock>();
        public List<IMyGyro> Gyros = new List<IMyGyro>();
        public List<IMyThrust> Thrusters = new List<IMyThrust>();
        public List<IMyLightingBlock> Lights = new List<IMyLightingBlock>();
        public List<IMyBatteryBlock> Batteries = new List<IMyBatteryBlock>();
        public IMyShipConnector Connector = null;
        public IMyShipController Controller = null;

        public PID PitchPID = new PID(3, 0.01, 0.5, 0.04, 10);
        public PID YawPID = new PID(3, 0.01, 0.5, 0.04, 10);

        public HeliDrive Drive = new HeliDrive();

        public long TargetID = -1;
        public Vector3D TargetPosition = Vector3D.Zero;
        public Vector3D TargetVelocity = Vector3D.Zero;
        public Vector3D lastTargetVelocity = Vector3D.Zero;
        public double lastSpeed = 0;

        public float desiredAltitude = 10;
        public double minDist = double.MaxValue;
        public int runs = 0;

        public int DeadCount = 10;
        public bool Fired = false;

        public static Landpedo GetLandpedo(List<IMyTerminalBlock> blocks)
        {
            Landpedo landpedo = new Landpedo();

            foreach (var block in blocks)
            {
                if (block is IMyGyro) landpedo.Gyros.Add(block as IMyGyro);
                else if (block is IMyThrust) landpedo.Thrusters.Add(block as IMyThrust);
                else if (block is IMyBatteryBlock) landpedo.Batteries.Add(block as IMyBatteryBlock);
                else if (block is IMyLightingBlock) landpedo.Lights.Add(block as IMyLightingBlock);
                else if (block is IMyShipConnector) landpedo.Connector = block as IMyShipConnector;
                else if (block is IMyShipController) landpedo.Controller = block as IMyShipController;
                else if (block.CustomName.Contains("launcher")) landpedo.Launchers.Add(block as IMyFunctionalBlock);
            }

            if (landpedo.Gyros.Count == 0 ||
                landpedo.Thrusters.Count == 0 ||
                landpedo.Connector == null ||
                landpedo.Controller == null)
                return null;

            foreach (var light in landpedo.Lights)
            {
                light.Color = Color.Green;
            }

            return landpedo;
        }

        public bool IsOK()
        {
            if (DeadCount <= 0) return false;

            var OK = false;
            foreach (var gyro in Gyros)
                if (gyro.IsFunctional) OK = true;
            if (!OK) return false;

            OK = false;
            foreach (var thruster in Thrusters)
                if (thruster.IsFunctional) OK = true;
            if (!OK) return false;

            if (!Controller.IsFunctional) return false;

            return true;
        }
    }

    public class LandpedoTube
    {
        public IMyShipConnector Connector = null;
        public IMyShipMergeBlock Merge = null;
        public Landpedo Landpedo;

        StringBuilder statusBuilder = new StringBuilder();

        List<IMyTerminalBlock> PartScratchpad = new List<IMyTerminalBlock>();

        public LandpedoTube(IMyShipConnector connector)
        {
            Connector = connector;
        }

        public LandpedoTube(IMyShipMergeBlock merge)
        {
            Merge = merge;
        }

        public string GetStatus()
        {
            return statusBuilder.ToString();
        }

        public void CheckLandpedo()
        {
            statusBuilder.Clear();
            if (Landpedo != null) return;

            if (Connector != null)
            {
                if (!Connector.IsWorking) return;
                if (Connector.Status == MyShipConnectorStatus.Unconnected) return;
                if (Connector.Status == MyShipConnectorStatus.Connectable) Connector.Connect();

                if (Connector.Status != MyShipConnectorStatus.Connected) return;

                var other = Connector.OtherConnector;
                var lines = other.CustomData.Split('\n');
                // Parts
                // Projector
                // Merge

                IMyProjector projector = null;
                IMyShipMergeBlock merge = null;


                if (GridTerminalHelper.Base64BytePosToBlockList(lines[1], other, ref PartScratchpad))
                {
                    projector = PartScratchpad[0] as IMyProjector;
                }

                PartScratchpad.Clear();
                if (GridTerminalHelper.Base64BytePosToBlockList(lines[2], other, ref PartScratchpad))
                {
                    merge = PartScratchpad[0] as IMyShipMergeBlock;
                }

                if (projector != null && merge != null)
                {
                    projector.Enabled = true;
                    merge.Enabled = false;
                }

                PartScratchpad.Clear();
                if (!GridTerminalHelper.Base64BytePosToBlockList(lines[0], other, ref PartScratchpad))
                    return;

                Landpedo = Landpedo.GetLandpedo(PartScratchpad);
            }
            else
            {

            }
        }

        public Landpedo Launch()
        {
            foreach (var thruster in Landpedo.Thrusters) thruster.Enabled = true;
            foreach (var gyro in Landpedo.Gyros) gyro.Enabled = true;
            foreach (var bat in Landpedo.Batteries) bat.ChargeMode = ChargeMode.Auto;

            Landpedo.Drive = new HeliDrive();
            Landpedo.Drive.Setup(Landpedo.Controller);

            Landpedo.Drive.gyroController.Update(Landpedo.Controller, Landpedo.Gyros);
            Landpedo.Drive.thrustController.Update(Landpedo.Controller, Landpedo.Thrusters);

            Landpedo.Drive.maxFlightPitch = 60;
            Landpedo.Drive.maxFlightRoll = 60;

            Landpedo.Drive.SwitchToMode("flight");

            var torp = Landpedo;
            torp.Connector.Enabled = false;
            Landpedo = null;
            return torp;
        }
    }

    public class LandpedoSubsystem : ISubsystem
    {
        #region ISubsystem
        public UpdateFrequency UpdateFrequency => UpdateFrequency.Update1;
        
        public void Command(TimeSpan timestamp, string command, object argument)
        {
        }

        public void CommandV2(TimeSpan timestamp, CommandLine command)
        {
            if (command.Argument(0) == "Launch")
                Launch();
        }

        public void DeserializeSubsystem(string serialized)
        {
        }
        
        public string GetStatus()
        {
            return statusBuilder.ToString();
        }
        
        public string SerializeSubsystem()
        {
            return string.Empty;
        }

        public void Setup(ExecutionContext context, string name)
        {
            Context = context;

            ParseConfigs();
            GetParts();
        }

        public void Update(TimeSpan timestamp, UpdateFrequency updateFlags)
        {
            if (runs % 240 == 0)
            {
                statusBuilder.Clear();
                foreach (var tube in LandpedoTubes)
                {
                    tube.CheckLandpedo();
                    // statusBuilder.AppendLine(tube.GetStatus());
                }
            }

            if (runs % 10 == 0)
            {
                statusBuilder.Clear();
                var targets = IntelProvider.GetFleetIntelligences(timestamp);
                var canonicalTime = IntelProvider.CanonicalTimeDiff + timestamp;
                DeadLandpedos.Clear();
                foreach (var landpedo in Landpedos)
                {
                    statusBuilder.AppendLine("LANDPEDO===");

                    if (landpedo.Fired) landpedo.DeadCount--;

                    if (!landpedo.IsOK())
                    {
                        DeadLandpedos.Add(landpedo);
                        continue;
                    }
                    landpedo.runs++;
                    var landpedoPosition = landpedo.Controller.WorldMatrix.Translation;
                    var landpedoVelocity = landpedo.Controller.GetShipVelocities().LinearVelocity;
                    var gravDir = landpedo.Controller.GetNaturalGravity();
                    var gravStr = gravDir.Normalize();
                    var planarVelocity = landpedoVelocity - VectorHelpers.VectorProjection(landpedoVelocity, gravDir);

                    if (landpedo.TargetID != -1)
                    {
                        var key = MyTuple.Create(IntelItemType.Enemy, landpedo.TargetID);
                        if (targets.ContainsKey(key))
                        {
                            var target = targets[key];
                            landpedo.TargetPosition = target.GetPositionFromCanonicalTime(canonicalTime);
                            landpedo.TargetVelocity = target.GetVelocity();
                        }
                    }

                    statusBuilder.AppendLine(landpedo.TargetPosition.ToString());

                    if (landpedo.TargetPosition != Vector3D.Zero)
                    {
                        var relativeVector = landpedo.TargetPosition - landpedoPosition;
                        var planarVector = relativeVector - VectorHelpers.VectorProjection(relativeVector, gravDir);

                        var targetPoint = AttackHelpers.GetAttackPoint(landpedo.TargetVelocity, landpedo.TargetPosition - landpedoPosition, landpedo.lastSpeed);
                        if (targetPoint == Vector3D.Zero) targetPoint = landpedo.TargetPosition - landpedoPosition;
                        var targetPointDist = targetPoint.Length();

                        var planarDist = planarVector.Length();
                        var velocity = landpedo.Controller.GetShipVelocities().LinearVelocity;

                        var verticalVelocity = VectorHelpers.VectorProjection(velocity, gravDir);

                        var planarLeft = landpedo.Controller.WorldMatrix.Left - VectorHelpers.VectorProjection(landpedo.Controller.WorldMatrix.Left, gravDir);
                        planarLeft.Normalize();

                        var planarForward = landpedo.Controller.WorldMatrix.Forward - VectorHelpers.VectorProjection(landpedo.Controller.WorldMatrix.Forward, gravDir);
                        planarForward.Normalize();

                        double altitude;
                        landpedo.Controller.TryGetPlanetElevation(MyPlanetElevation.Surface, out altitude);

                        if (targetPointDist > 250 || landpedo.Launchers.Count == 0)
                        {
                            landpedo.desiredAltitude = planarVector.Length() > 200 ? 10 : 50;
                            planarVector.Normalize();

                            MatrixD orientationMatrix = MatrixD.Identity;
                            orientationMatrix.Translation = landpedo.Controller.WorldMatrix.Translation;

                            orientationMatrix.Up = -gravDir;
                            orientationMatrix.Left = planarLeft;
                            orientationMatrix.Forward = planarForward;

                            var spinAngle = VectorHelpers.VectorAngleBetween(planarForward, planarVector) * Math.Sign(-planarLeft.Dot(planarVector));

                            // var planarVelocity = velocity - verticalVelocity;
                            // var velocityAdjust = planarVelocity - VectorHelpers.VectorProjection(velocity, planarVector);
                            // velocityAdjust.Normalize();
                            // planarVector -= velocityAdjust;
                            // var MoveIndicator = Vector3D.TransformNormal(planarVector, MatrixD.Transpose(orientationMatrix));

                            var rangeVector = planarVector;
                            var waypointVector = rangeVector;
                            var distTargetSq = rangeVector.LengthSquared();

                            var targetPlanarVelocity = landpedo.TargetVelocity - VectorHelpers.VectorProjection(landpedo.TargetVelocity, gravDir);


                            Vector3D velocityVector = targetPlanarVelocity - planarVelocity;
                            var speed = planarVelocity.Length();

                            Vector3D AccelerationVector;

                            double alignment = planarVelocity.Dot(ref waypointVector);
                            if (alignment > 0)
                            {
                                Vector3D rangeDivSqVector = waypointVector / waypointVector.LengthSquared();
                                Vector3D compensateVector = velocityVector - (velocityVector.Dot(ref waypointVector) * rangeDivSqVector);

                                Vector3D targetANVector;
                                var targetAccel = (landpedo.lastTargetVelocity - targetPlanarVelocity) * 0.16667;

                                targetANVector = targetAccel - (targetAccel.Dot(ref waypointVector) * rangeDivSqVector);

                                bool accelerating = speed > landpedo.lastSpeed + 1;
                                if (accelerating)
                                {
                                    AccelerationVector = planarVelocity + (10 * 1.5 * (compensateVector + (0.5 * targetANVector)));
                                }
                                else
                                {
                                    AccelerationVector = planarVelocity + (10 * (compensateVector + (0.5 * targetANVector)));
                                }
                            }
                            // going backwards or perpendicular
                            else
                            {
                                AccelerationVector = (waypointVector * 0.1) + velocityVector;
                            }

                            landpedo.lastTargetVelocity = landpedo.TargetVelocity;
                            landpedo.lastSpeed = speed;

                            var MoveIndicator = Vector3D.TransformNormal(AccelerationVector, MatrixD.Transpose(orientationMatrix));

                            MoveIndicator.Y = 0;
                            MoveIndicator.Normalize();

                            statusBuilder.AppendLine(MoveIndicator.ToString());

                            landpedo.Drive.MoveIndicators = MoveIndicator;
                            landpedo.Drive.RotationIndicators = new Vector3(0, spinAngle, 0);

                            landpedo.Drive.Drive();

                            if (verticalVelocity.Length() > 10 && verticalVelocity.Dot(gravDir) > 0)
                            {
                                landpedo.Drive.maxFlightPitch = 45;
                                landpedo.Drive.maxFlightRoll = 45;
                            }
                            else
                            {
                                landpedo.Drive.maxFlightPitch = 70;
                                landpedo.Drive.maxFlightRoll = 70;
                            }

                            if (targetPointDist < 600)
                            {
                                landpedo.Drive.maxFlightPitch = 20;
                            }
                        }
                        else if (landpedo.TargetID != -1)
                        {
                            var key = MyTuple.Create(IntelItemType.Enemy, landpedo.TargetID);
                            if (!targets.ContainsKey(key)) return;
                            var target = targets[key];

                            var posDiff = target.GetPositionFromCanonicalTime(canonicalTime) - landpedoPosition;

                            var avgVel = 400 * Math.Sqrt(posDiff.Length() / 400);

                            var relativeAttackPoint = AttackHelpers.GetAttackPoint(landpedo.TargetVelocity - velocity, posDiff, avgVel);
                            targetPointDist = relativeAttackPoint.Length();

                            double yawAngle, pitchAngle;

                            TrigHelpers.GetRotationAngles(relativeAttackPoint, landpedo.Controller.WorldMatrix.Forward, landpedo.Controller.WorldMatrix.Left, landpedo.Controller.WorldMatrix.Up, out yawAngle, out pitchAngle);
                            TrigHelpers.ApplyGyroOverride(landpedo.PitchPID.Control(pitchAngle), landpedo.YawPID.Control(yawAngle), 0, landpedo.Gyros, landpedo.Controller.WorldMatrix);

                            if ((targetPointDist < 125 || landpedo.minDist < planarDist))
                            {
                                foreach (var weapon in landpedo.Launchers) weapon.Enabled = true;
                                landpedo.Fired = true;
                            }

                            landpedo.minDist = Math.Min(landpedo.minDist, planarDist);
                        }

                        var verticalThrustRatio = TrigHelpers.FastCos(VectorHelpers.VectorAngleBetween(landpedo.Controller.WorldMatrix.Down, gravDir));
                        var desiredThrust = ((landpedo.desiredAltitude - altitude) + verticalVelocity.LengthSquared() * 0.1 + gravStr) * landpedo.Controller.CalculateShipMass().PhysicalMass / verticalThrustRatio;
                        var individualThrust = desiredThrust / landpedo.Thrusters.Count();
                        if (individualThrust <= 0) individualThrust = 0.001;

                        foreach (var thruster in landpedo.Thrusters)
                        {
                            thruster.Enabled = ((verticalVelocity.Length() > 20 && verticalVelocity.Dot(gravDir) < 0) || ((verticalVelocity.Length() > 2 && verticalVelocity.Dot(gravDir) < 0) && targetPointDist < 1000)) ? false : true;
                            thruster.ThrustOverride = (float)individualThrust;
                        }

                        statusBuilder.AppendLine($"{landpedo.Drive.thrustController.CalculateThrustToHover()}");
                        statusBuilder.AppendLine($"{landpedo.Drive.thrustController.upThrusters.Count} : {landpedo.Drive.thrustController.downThrusters.Count}");
                    }
                }

                foreach (var landpedo in DeadLandpedos)
                {
                    Landpedos.Remove(landpedo);
                }
            }
            runs++;
        }
        #endregion
        const string kLandpedoSection = "Landpedo";
        ExecutionContext Context;

        StringBuilder statusBuilder = new StringBuilder();

        MyIni iniParser = new MyIni();
        List<MyIniKey> iniKeyScratchpad = new List<MyIniKey>();

        public IIntelProvider IntelProvider;
        public int runs = 0;

        List<LandpedoTube> LandpedoTubes = new List<LandpedoTube>();
        List<Landpedo> Landpedos = new List<Landpedo>();
        List<Landpedo> DeadLandpedos = new List<Landpedo>();

        string LandpedoTag;

        public LandpedoSubsystem(IIntelProvider intelProvider, string landpedoTag = "[LG-LANDPEDO]")
        {
            IntelProvider = intelProvider;
            LandpedoTag = landpedoTag;
        }

        void GetParts()
        {
            Context.Terminal.GetBlocksOfType<IMyTerminalBlock>(null, GetBlocks);
        }

        bool GetBlocks(IMyTerminalBlock block)
        {
            if (!block.IsSameConstructAs(Context.Reference)) return false;
            if (!block.CustomName.Contains(LandpedoTag)) return false;
            if (block is IMyShipConnector) LandpedoTubes.Add(new LandpedoTube(block as IMyShipConnector));
            return false;
        }

        // [LandPedo]
        // MinEngagementSize = 1
        void ParseConfigs()
        {
            iniParser.Clear();
            MyIniParseResult result;
            if (!iniParser.TryParse(Context.Reference.CustomData, out result))
                return;

            // MinEngagementSize = iniParser.Get(kTurretSection, "MinEngagementSize").ToInt32(1);
        }

        void Launch()
        {
            if (Context.WCAPI == null) return;
            var target = Context.WCAPI.GetAiFocus(Context.Reference.CubeGrid.EntityId);
            if (target == null) return;

            Landpedo landpedo = null;
            foreach (var tube in LandpedoTubes)
            {
                if (tube.Landpedo != null)
                {
                    landpedo = tube.Launch();
                    break;
                }
            }

            if (landpedo == null) return;
            Landpedos.Add(landpedo);
            landpedo.TargetID = target.Value.EntityId;
        }
    }
}
