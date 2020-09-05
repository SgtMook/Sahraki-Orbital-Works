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
    public class HummingbirdCradle
    {
        public IMyShipConnector Connector;
        public IMyShipMergeBlock TurretMerge;
        public IMyPistonBase Piston;
        public IMyShipMergeBlock ChasisMerge;
        public HummingbirdCommandSubsystem Host;

        public Hummingbird Hummingbird;

        IMyMotorStator turretRotor;
        IMyShipConnector hummingbirdConnector;

        int releaseStage = 0;

        public string status;

        List<IMyTerminalBlock> PartScratchpad = new List<IMyTerminalBlock>();

        public HummingbirdCradle(HummingbirdCommandSubsystem host)
        {
            Host = host;
        }

        bool setup = false;

        public void CheckHummingbird()
        {
            status = releaseStage.ToString();
            if (releaseStage == 0)
            {
                // Check for completeness
                if (!Hummingbird.CheckHummingbirdComponents(ChasisMerge, ref hummingbirdConnector, ref turretRotor, ref PartScratchpad, ref status)) return;

                if (!Hummingbird.CheckHummingbirdComponents(TurretMerge, ref hummingbirdConnector, ref turretRotor, ref PartScratchpad, ref status)) return;

                if (turretRotor == null || hummingbirdConnector == null) return;

                turretRotor.Detach();
                releaseStage = 1;
                Piston.Velocity = -0.2f;
            }
            else if (releaseStage == 1)
            {
                // Move pistons
                if (Piston.CurrentPosition == Piston.MinLimit) releaseStage = 2;
            }
            else if (releaseStage < 0)
            {
                releaseStage++;
            }
            
        }

        public void Update()
        {
            if (!setup)
            {
                setup = true;
                Piston.Velocity = 0.2f;

                if (!Hummingbird.CheckHummingbirdComponents(ChasisMerge, ref hummingbirdConnector, ref turretRotor, ref PartScratchpad, ref status)) return;

                if (Hummingbird.CheckHummingbirdComponents(TurretMerge, ref hummingbirdConnector, ref turretRotor, ref PartScratchpad, ref status))
                {
                    turretRotor.Detach();
                    releaseStage = 1;
                    Piston.Velocity = -0.2f;
                    return;
                }

                if (turretRotor != null)
                {
                    Hummingbird = Hummingbird.GetHummingbird(turretRotor, Host.Program.GridTerminalSystem.GetBlockGroupWithName(Hummingbird.GroupName));
                    if (Hummingbird.Gats.Count == 0)
                    {
                        Hummingbird = null;
                        TurretMerge.Enabled = true;
                        return;
                    }
                    releaseStage = 20;
                    turretRotor.Displacement = 0.11f;
                    return;
                }
            }


            if (releaseStage > 1 && releaseStage < 20) releaseStage++;
            if (releaseStage == 2)
            {
                Connector.Connect();

            }
            else if (releaseStage == 3)
            {
                // Cargo transfer here
            }
            else if (releaseStage == 4)
            {
                Connector.Disconnect();
            }
            else if (releaseStage == 5)
            {
                GridTerminalHelper.OtherMergeBlock(TurretMerge).Enabled = false;
                TurretMerge.Enabled = false;
            }
            else if (releaseStage == 6)
            {
                turretRotor.Attach();
            }
            else if (releaseStage == 8)
            {
                Piston.Velocity = 0.2f;
            }
            else if (releaseStage > 8 && Piston.CurrentPosition == Piston.MaxLimit)
            {
                turretRotor.Displacement = 0.11f;
                Hummingbird = Hummingbird.GetHummingbird(turretRotor, Host.Program.GridTerminalSystem.GetBlockGroupWithName(Hummingbird.GroupName));
            }
        }

        public Hummingbird Release()
        {
            releaseStage = -10;
            var bird = Hummingbird;
            Hummingbird = null;
            turretRotor = null;
            hummingbirdConnector = null;
            GridTerminalHelper.OtherMergeBlock(ChasisMerge).Enabled = false;
            TurretMerge.Enabled = true;

            return bird;
        }
    }

    public class HummingbirdCommandSubsystem : ISubsystem
    {
        StringBuilder statusbuilder = new StringBuilder();
        List<Hummingbird> Hummingbirds = new List<Hummingbird>();
        List<Hummingbird> DeadBirds = new List<Hummingbird>();

        HummingbirdCradle[] Cradles = new HummingbirdCradle[8];

        int runs;

        const double BirdSineConstantSeconds = 6;
        const double BirdPendulumConstantSeconds = 12;

        public HummingbirdCommandSubsystem(IIntelProvider intelProvider)
        {
            IntelProvider = intelProvider;
        }

        #region ISubsystem
        public void Command(TimeSpan timestamp, string command, object argument)
        {
            if (command == "SetTarget") SetTarget(ParseGPS((string)argument));
            if (command == "SetDest") SetDest(ParseGPS((string)argument));
            if (command == "Release") Release();
        }

        IMyTerminalBlock ProgramReference;
        public void Setup(MyGridProgram program, string name, IMyTerminalBlock programReference = null)
        {
            ProgramReference = programReference;
            if (ProgramReference == null) ProgramReference = program.Me;
            Program = program;

            UpdateFrequency = UpdateFrequency.Update1;

            GetParts();
        }

        void GetParts()
        {
            Program.GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(null, CollectParts);
        }

        bool CollectParts(IMyTerminalBlock block)
        {
            if (!ProgramReference.IsSameConstructAs(block)) return false; // Allow subgrid

            bool Chasis = block.CustomName.StartsWith("[HBC-CHS");
            bool Turret = block.CustomName.StartsWith("[HBC-TRT");
            bool Connector = block.CustomName.StartsWith("[HBC-CON");
            bool Piston = block.CustomName.StartsWith("[HBC-PST");

            if (!Chasis && !Turret && !Connector && !Piston) return false;

            var indexTagEnd = block.CustomName.IndexOf(']');
            if (indexTagEnd == -1) return false;

            var numString = block.CustomName.Substring(8, indexTagEnd - 8);

            int index;
            if (!int.TryParse(numString, out index)) return false;
            if (Cradles[index] == null) Cradles[index] = new HummingbirdCradle(this);

            if (Chasis) Cradles[index].ChasisMerge = (IMyShipMergeBlock)block;
            if (Turret) Cradles[index].TurretMerge = (IMyShipMergeBlock)block;
            if (Connector) Cradles[index].Connector = (IMyShipConnector)block;
            if (Piston) Cradles[index].Piston = (IMyPistonBase)block;

            return false;
        }

        public void Update(TimeSpan timestamp, UpdateFrequency updateFlags)
        {
            foreach (var bird in Hummingbirds)
            {
                if (bird.IsOK()) bird.Update();
                else DeadBirds.Add(bird);
            }

            foreach (var bird in DeadBirds)
            {
                Hummingbirds.Remove(bird);
            }

            var hasTarget = false;

            runs++;
            if (runs % 20 == 0)
            {
                var intelItems = IntelProvider.GetFleetIntelligences(timestamp);
                foreach (var intelItem in intelItems)
                {
                    if (intelItem.Key.Item1 == IntelItemType.Enemy)
                    {
                        hasTarget = true;
                        int birdIndex = 0;
                        // Be smart about picking enemies later
                        foreach (var bird in Hummingbirds)
                        {
                            if (bird.Gats.Count == 0) continue;
                            var birdAltitudeTheta = Math.PI * ((runs / (BirdSineConstantSeconds * 30) % 2) - 1);
                            var birdSwayTheta = Math.PI * ((runs / (BirdPendulumConstantSeconds * 30) % 2) - 1);
                            var targetPos = intelItem.Value.GetPositionFromCanonicalTime(timestamp + IntelProvider.CanonicalTimeDiff);

                            var gravDir = bird.Controller.GetTotalGravity();
                            gravDir.Normalize();

                            bird.SetTarget(targetPos, intelItem.Value.GetVelocity() - gravDir * (float)TrigHelpers.FastCos(birdAltitudeTheta) * 2);

                            var targetToBase = bird.Base.WorldMatrix.Translation - targetPos;
                            targetToBase -= VectorHelpers.VectorProjection(targetToBase, gravDir);
                            var targetToBaseDist = targetToBase.Length();
                            targetToBase.Normalize();

                            var engageLocationLocus = targetToBase * Math.Min(600, targetToBaseDist + 400) + targetPos;
                            var engageLocationSwayDir = targetToBase.Cross(gravDir);
                            var engageLocationSwayDist = (TrigHelpers.FastCos(birdSwayTheta) - Hummingbirds.Count * 0.5 + birdIndex + 0.5) * 100;

                            bird.SetDest(engageLocationLocus + engageLocationSwayDist * engageLocationSwayDir);

                            //var diff = bird.Controller.WorldMatrix.Translation - targetPos;
                            //var orbit = diff.Cross(gravDir);
                            //
                            //diff.Normalize();
                            //orbit.Normalize();
                            //
                            //if ((int)(bird.LifeTimeTicks/(BirdPendulumConstantSeconds*60)) % 2 == 1)
                            //{
                            //    orbit *= -1;
                            //}
                            //
                            //diff *= 600;
                            //bird.SetDest(targetPos + diff + orbit * 200);

                            bird.Drive.DesiredAltitude = (float)TrigHelpers.FastSin(birdAltitudeTheta + Math.PI * 0.5) * 
                                (Hummingbird.RecommendedServiceCeiling - Hummingbird.RecommendedServiceFloor) + Hummingbird.RecommendedServiceFloor;

                            birdIndex++;
                        }
                    }
                }


                foreach (var bird in Hummingbirds)
                {
                    if (!hasTarget || bird.Gats.Count == 0) // || Bird.OutOfAmmo
                    {
                        // Retire drone - return to mothership and land nearby, then power off for recovery
                        bird.SetTarget(Vector3D.Zero, Vector3D.Zero);
                        bird.SetDest(Program.Me.WorldMatrix.Translation + Program.Me.WorldMatrix.Forward * 50);
                        bird.Drive.SpeedLimit = 0;
                    }
                    else
                    {
                        // Drone finds target and engage
                    }
                }
            }

            foreach (var cradle in Cradles)
            {
                if (cradle == null) continue;
                cradle.Update();
                if (hasTarget && cradle.Hummingbird != null && Hummingbirds.Count < 3)
                {
                    Hummingbird bird = cradle.Release();
                    bird.Base = Program.Me;
                    Hummingbirds.Add(bird);
                }
            }

            if (runs % 60 == 0)
            {
                for (int i = 0; i < Cradles.Count(); i++)
                {
                    if (Cradles[i] == null) continue;
                    if (Cradles[i].Hummingbird == null)
                        Cradles[i].CheckHummingbird();
                }
            }

            DeadBirds.Clear();
        }

        public string GetStatus()
        {
            if (Hummingbirds.Count > 0)
            {
                double elev;
                Hummingbirds[0].Controller.TryGetPlanetElevation(MyPlanetElevation.Surface, out elev);
                return $"ALT: {elev}, BIRD: {Hummingbirds[0].Drive.Status}";
            }
            return ""; //$"CRADLE1: {Cradles[1].releaseStage}, BIRDS: {Hummingbirds.Count.ToString()}";
        }

        public MyGridProgram Program { get; private set; }
        public UpdateFrequency UpdateFrequency { get; set; }
        public IIntelProvider IntelProvider;

        public string SerializeSubsystem()
        {
            return string.Empty;
        }

        public void DeserializeSubsystem(string serialized)
        {
        }

        void SetTarget(Vector3D argument)
        {
            foreach (var bird in Hummingbirds)
                bird.SetTarget(argument, Vector3D.Zero);
        }
        void SetDest(Vector3D argument)
        {
            foreach (var bird in Hummingbirds)
                bird.SetDest(argument);
        }
        void Release()
        {
            if (Cradles[1].Hummingbird != null)
                Hummingbirds.Add(Cradles[1].Release());
        }
        Vector3D ParseGPS(string s)
        {
            var split = s.Split(':');
            return new Vector3(float.Parse(split[2]), float.Parse(split[3]), float.Parse(split[4]));
        }

        #endregion
    }
}