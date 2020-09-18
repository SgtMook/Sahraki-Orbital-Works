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

                Connector.Connect();
                if (Host.AmmoBox != null)
                {
                    var inventoryItem = Host.AmmoBox.GetItemAt(0);
                    if (inventoryItem != null)
                    {
                        Host.AmmoBox.TransferItemTo(Connector.OtherConnector.GetInventory(0), (MyInventoryItem)inventoryItem);
                    }
                }
                Connector.Disconnect();

                releaseStage = 1;
                Piston.Velocity = -0.2f;
            }
            else if (releaseStage == 1)
            {
                // Move pistons
                if (Piston.CurrentPosition == Piston.MinLimit)
                {
                    releaseStage = 2;
                }
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
            if (releaseStage == 5)
            {

            }
            else if (releaseStage == 7)
            {

            }
            else if (releaseStage == 8)
            {
                GridTerminalHelper.OtherMergeBlock(TurretMerge).Enabled = false;
                TurretMerge.Enabled = false;
            }
            else if (releaseStage == 9)
            {
                turretRotor.Attach();
            }
            else if (releaseStage == 10)
            {
                Piston.Velocity = 0.2f;
            }
            else if (releaseStage > 11 && Piston.CurrentPosition == Piston.MaxLimit)
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
        IMyShipController Controller;
        public IMyInventory AmmoBox;

        Dictionary<Hummingbird, ScannerGroup> BirdScannerGroups = new Dictionary<Hummingbird, ScannerGroup>();

        int runs;


        // TODO: Move to config?
        const double BirdSineConstantSeconds = 6;
        const double BirdPendulumConstantSeconds = 12;
        const double BirdOrbitSeconds = 30;
        const int MaxEngagementDist = 4000;
        const int MinEngagementSize = 20;
        const int BirdOrbitDist = 100;

        Dictionary<int, int[]> EnemyCountToNumBirdsPerEnemy = new Dictionary<int, int[]> ()
        {
            { 1, new [] {2} },
            { 2, new [] {2, 2} },
            { 3, new [] {2, 1, 1} },
            { 4, new [] {1, 1, 1, 1} },
        };


        Dictionary<EnemyShipIntel, int> EnemyToNumBirds = new Dictionary<EnemyShipIntel, int>();
        Dictionary<EnemyShipIntel, int> EnemyToAssignedBirds = new Dictionary<EnemyShipIntel, int>();
        Dictionary<Hummingbird, long> BirdToEnemy = new Dictionary<Hummingbird, long>();
        List<EnemyShipIntel> TopEnemies = new List<EnemyShipIntel>();
        Dictionary<EnemyShipIntel, int> EnemyToScore = new Dictionary<EnemyShipIntel, int>();

        bool NeedsMoreBirds = false;
        int BirdReleaseTimeout = 0;

        public HummingbirdCommandSubsystem(IIntelProvider intelProvider, ScannerNetworkSubsystem scannerSubsystem)
        {
            IntelProvider = intelProvider;
            ScannerSubsystem = scannerSubsystem;
        }

        #region ISubsystem
        public void Command(TimeSpan timestamp, string command, object argument)
        {
            if (command == "SetTarget") SetTarget(ParseGPS((string)argument));
            if (command == "SetDest") SetDest(ParseGPS((string)argument));
            if (command == "Release") Release();
            if (command == "Recall") Recall();
        }

        IMyTerminalBlock ProgramReference;
        public void Setup(MyGridProgram program, string name, IMyTerminalBlock programReference = null)
        {
            ProgramReference = programReference;
            if (ProgramReference == null) ProgramReference = program.Me;
            Program = program;

            UpdateFrequency = UpdateFrequency.Update1;

            GetParts();
            Update(TimeSpan.Zero, UpdateFrequency.None);

            // JIT
            TriskelionDrive drive = new TriskelionDrive();
            drive.SetUp(0);
        }

        void GetParts()
        {
            Program.GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(null, CollectParts);
        }

        bool CollectParts(IMyTerminalBlock block)
        {
            if (block is IMyShipController && ProgramReference.CubeGrid.EntityId == block.CubeGrid.EntityId)
                Controller = (IMyShipController)block;

            if (block.HasInventory && block.CustomName.StartsWith("HB-AMMO"))
                AmmoBox = block.GetInventory(0);

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
            if (timestamp == TimeSpan.Zero) return;
            foreach (var bird in Hummingbirds)
            {
                if (bird.IsAlive()) bird.Update();
                else DeadBirds.Add(bird);
            }

            foreach (var bird in DeadBirds)
            {
                DeregisterBird(bird);
            }

            runs++;
            if (runs % 20 == 0)
            {
                var intelItems = IntelProvider.GetFleetIntelligences(timestamp);

                // Get the top targets
                TopEnemies.Clear();
                EnemyToScore.Clear();

                foreach (var intelItem in intelItems)
                {
                    if (intelItem.Key.Item1 == IntelItemType.Enemy)
                    {
                        var esi = (EnemyShipIntel)intelItem.Value;
                        var dist = (esi.GetPositionFromCanonicalTime(timestamp + IntelProvider.CanonicalTimeDiff) - ProgramReference.WorldMatrix.Translation).Length();
                        if (dist > MaxEngagementDist) continue;

                        var priority = IntelProvider.GetPriority(esi.ID);
                        var size = esi.Radius;

                        if (size < MinEngagementSize && priority < 3) continue;

                        if (priority < 2) continue;

                        int score = (int)(priority * 10000 + size);

                        EnemyToScore[esi] = score;

                        for (int i = 0; i <= TopEnemies.Count; i++)
                        {
                            if (i == TopEnemies.Count || score > EnemyToScore[TopEnemies[i]])
                            {
                                TopEnemies.Insert(i, esi);
                                break;
                            }
                        }
                    }
                }

                // Determine how many birds should be assigned to each enemy
                EnemyToNumBirds.Clear();

                int totalNeededBirds = 0;
                
                for (int i = 0; i < TopEnemies.Count && i < 4; i++)
                {
                    EnemyToNumBirds[TopEnemies[i]] = EnemyCountToNumBirdsPerEnemy[TopEnemies.Count][i];
                    EnemyToAssignedBirds[TopEnemies[i]] = 0;
                    totalNeededBirds += EnemyToNumBirds[TopEnemies[i]];
                }

                // Remove excess birds from enemies
                foreach (var bird in Hummingbirds)
                {
                    var birdTargetID = BirdToEnemy[bird];
                    if (birdTargetID == 0) continue;

                    if (!bird.IsCombatCapable())
                    {
                        BirdToEnemy[bird] = 0;
                        continue;
                    }

                    var birdTargetKey = MyTuple.Create(IntelItemType.Enemy, birdTargetID);
                    if (!intelItems.ContainsKey(birdTargetKey))
                    {
                        BirdToEnemy[bird] = 0;
                        continue;
                    }

                    var birdTarget = (EnemyShipIntel)intelItems[birdTargetKey];
                    if (!EnemyToNumBirds.ContainsKey(birdTarget) || EnemyToNumBirds[birdTarget] == 0)
                    {
                        BirdToEnemy[bird] = 0;
                        continue;
                    }

                    EnemyToNumBirds[birdTarget]--;
                    totalNeededBirds--;
                }

                // Assign birds to enemies
                foreach (var bird in Hummingbirds)
                {
                    if (totalNeededBirds == 0) break;

                    // Bird can't fight, keep looking
                    if (!bird.IsCombatCapable()) continue;

                    // Bird already has target, keep looking
                    if (BirdToEnemy[bird] != 0) continue;

                    EnemyShipIntel targetEnemy = null;
                    foreach (var enemy in EnemyToNumBirds.Keys)
                    {
                        if (EnemyToNumBirds[enemy] > 0)
                        {
                            targetEnemy = enemy;
                            break;
                        }
                    }

                    BirdToEnemy[bird] = targetEnemy.ID;
                    EnemyToNumBirds[targetEnemy]--;
                    totalNeededBirds--;
                }

                NeedsMoreBirds = totalNeededBirds > 0;
                int birdIndex;

                // ASSUME birds are not far enough from main controller that gravity direction matters too much
                var gravDir = Controller.GetTotalGravity();
                gravDir.Normalize();

                // For each enemy, assign bird target and destination
                foreach (var enemy in TopEnemies)
                {
                    birdIndex = 0;
                    foreach (var bird in Hummingbirds)
                    {
                        if (BirdToEnemy[bird] != enemy.ID) continue;

                        if (bird.Gats.Count == 0) continue;
                        var birdAltitudeTheta = Math.PI * ((runs / (BirdSineConstantSeconds * 30) % 2) - 1);
                        var birdSwayTheta = Math.PI * ((runs / (BirdPendulumConstantSeconds * 30) % 2) - 1);
                        var targetPos = enemy.GetPositionFromCanonicalTime(timestamp + IntelProvider.CanonicalTimeDiff);

                        bird.SetTarget(targetPos, enemy.GetVelocity() - gravDir * (float)TrigHelpers.FastCos(birdAltitudeTheta) * 2);

                        var targetToBase = bird.Base.WorldMatrix.Translation - targetPos;
                        targetToBase -= VectorHelpers.VectorProjection(targetToBase, gravDir);
                        var targetToBaseDist = targetToBase.Length();
                        targetToBase.Normalize();

                        var engageLocationLocus = targetToBase * Math.Min(600, targetToBaseDist + 400) + targetPos;
                        var engageLocationSwayDir = targetToBase.Cross(gravDir);
                        var engageLocationSwayDist = (TrigHelpers.FastCos(birdSwayTheta) - EnemyToAssignedBirds[enemy] * 0.5 + birdIndex + 0.5) * 100;

                        bird.SetDest(engageLocationLocus + engageLocationSwayDist * engageLocationSwayDir);

                        var birdDir = bird.Controller.WorldMatrix.Translation - Controller.WorldMatrix.Translation;
                        birdDir -= VectorHelpers.VectorProjection(birdDir, gravDir); ;
                        var birdDist = birdDir.Length();

                        bird.Drive.DesiredAltitude = birdDist < 100 ? Hummingbird.RecommendedServiceCeiling : 
                            (float)TrigHelpers.FastSin(birdAltitudeTheta + Math.PI * 0.5) *
                            (Hummingbird.RecommendedServiceCeiling - Hummingbird.RecommendedServiceFloor) + Hummingbird.RecommendedServiceFloor;

                        birdIndex++;
                    }
                }

                // Assign orbit task for unassigned birds
                int numReserveBirds = 0;
                foreach (var bird in Hummingbirds) if (BirdToEnemy[bird] == 0 && bird.IsCombatCapable()) numReserveBirds++;
                birdIndex = 0;
                var randomPoint = new Vector3D(190, 2862, 809);
                randomPoint -= VectorHelpers.VectorProjection(randomPoint, gravDir);
                randomPoint.Normalize();

                var randomPointCross = randomPoint.Cross(gravDir);

                foreach (var bird in Hummingbirds)
                {
                    if (BirdToEnemy[bird] != 0) continue;
                    bird.SetTarget(Vector3D.Zero, Vector3D.Zero);
                    bird.Drive.DesiredAltitude = 30;

                    if (bird.IsCombatCapable() && !bird.IsRetiring)
                    {
                        var birdOrbitTheta = Math.PI * (((2 * birdIndex / (float)numReserveBirds) + runs / (BirdOrbitSeconds * 30)) % 2 - 1);

                        var birdOrbitDest = Controller.WorldMatrix.Translation +
                            TrigHelpers.FastCos(birdOrbitTheta) * randomPoint * BirdOrbitDist +
                            TrigHelpers.FastSin(birdOrbitTheta) * randomPointCross * BirdOrbitDist;

                        bird.SetDest(birdOrbitDest);

                        birdIndex++;
                    }
                    else if (!bird.IsRetiring)
                    {
                        RetireBird(bird);
                    }
                    else if (bird.IsRetiring)
                    {
                        if (!bird.IsLanding)
                        {
                            var birdDir = bird.Controller.WorldMatrix.Translation - bird.Destination;
                            birdDir -= VectorHelpers.VectorProjection(birdDir, gravDir);
                            var birdDist = birdDir.Length();
                            birdDir.Normalize();

                            if (birdDist < 50)
                            {
                                bird.SetDest(Vector3D.Zero);
                                bird.Drive.Flush();
                                foreach (var engine in bird.Drive.HoverEngines)
                                {
                                    engine.AltitudeMin = 0;
                                }
                                bird.IsLanding = true;
                            }
                        }
                        else
                        {
                            double altitude;
                            bird.Controller.TryGetPlanetElevation(MyPlanetElevation.Surface, out altitude);
                            if (altitude < 6)
                            {
                                foreach (var engine in bird.Drive.HoverEngines)
                                {
                                    engine.Block.Enabled = false;
                                    DeadBirds.Add(bird);
                                }
                            }
                        }
                    }
                }
            }

            foreach (var cradle in Cradles)
            {
                if (cradle == null) continue;
                cradle.Update();
            }

            if (runs % 60 == 0)
            {
                BirdReleaseTimeout--;
                for (int i = 0; i < Cradles.Count(); i++)
                {
                    if (Cradles[i] == null) continue;
                    if (Cradles[i].Hummingbird == null)
                    {
                        Cradles[i].CheckHummingbird();
                    }
                    else if (NeedsMoreBirds && BirdReleaseTimeout <= 0)
                    {
                        Hummingbird bird = Cradles[i].Release();
                        bird.Base = ProgramReference;
                        RegisterBird(bird);
                        BirdReleaseTimeout = 5;
                    }
                }
            }

            DeadBirds.Clear();
        }

        private void RetireBird(Hummingbird bird)
        {
            bird.SetDest(Controller.WorldMatrix.Translation);
            bird.IsRetiring = true;
        }

        public string GetStatus()
        {

            statusbuilder.Clear();

            //statusbuilder.AppendLine($"NEEDSMOREBIRDS {NeedsMoreBirds}, TIMEOUT {BirdReleaseTimeout}");
            //statusbuilder.AppendLine($"ENEMIES {TopEnemies.Count}");
            //
            //foreach (var kvp in EnemyToNumBirds)
            //{
            //    statusbuilder.AppendLine($"- {kvp.Key.DisplayName} : {kvp.Value}");
            //}

            var inventoryItem = AmmoBox.GetItemAt(0);
            if (inventoryItem != null)
            {
                statusbuilder.AppendLine(inventoryItem.Value.Type.SubtypeId);
            }
            else
            {
                statusbuilder.AppendLine("NULL");

            }
            return statusbuilder.ToString();
        }

        public MyGridProgram Program { get; private set; }
        public UpdateFrequency UpdateFrequency { get; set; }
        public IIntelProvider IntelProvider;
        public ScannerNetworkSubsystem ScannerSubsystem;

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
            if (Cradles[1] != null && Cradles[1].Hummingbird != null)
                RegisterBird(Cradles[1].Release());
        }

        void Recall()
        {
            foreach (var bird in Hummingbirds)
                RetireBird(bird);
        }

        Vector3D ParseGPS(string s)
        {
            var split = s.Split(':');
            return new Vector3(float.Parse(split[2]), float.Parse(split[3]), float.Parse(split[4]));
        }

        #endregion

        void RegisterBird(Hummingbird newBird)
        {
            Hummingbirds.Add(newBird);
            BirdToEnemy.Add(newBird, 0);

            var birdCamGroup = new ScannerGroup(newBird.Cameras);
            BirdScannerGroups[newBird] = birdCamGroup;
            ScannerSubsystem.AddScannerGroup(birdCamGroup);
        }

        void DeregisterBird(Hummingbird bird)
        {
            Hummingbirds.Remove(bird);
            BirdToEnemy.Remove(bird);
            ScannerSubsystem.RemoveScannerGroup(BirdScannerGroups[bird]);
            BirdScannerGroups.Remove(bird);
        }
    }
}