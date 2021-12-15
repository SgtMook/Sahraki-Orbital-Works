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
    public class RotorHingeTurret
    {
        // This is a rotor hinge turret
        // It uses a rotor for azimuth and a hinge for altitude control
        // In the neutral position, the turret aims towards the base rotor's -forward direction
        // When a turret fires, all weapons are turned on. When it stops firing, all weapons are turned off.

        public IMyMotorStator Azimuth;
        public IMyMotorAdvancedStator Elevation;
        public List<IMyFunctionalBlock> Weapons = new List<IMyFunctionalBlock>();
        public TurretSubsystem Host;

        public PID AzimuthPID;
        public PID ElevationPID;

        public int range;
        public int projectileSpeed;
        public float fireTolerance = 0.05f;

        public bool targetLarge = true;
        public bool targetSmall = true;
        public bool snapAim = true;

        public float AzimuthMax;
        public float ElevationMax;
        public float AzimuthMin;
        public float ElevationMin;

        public double targetAzimuth = 0;
        public double targetElevation = 0;

        int fireTicks = 0;

        StringBuilder statusBuilder = new StringBuilder();

        public void SelectTarget(List<EnemyShipIntel> targets, TimeSpan timestamp)
        {
            // Auto reset
            targetAzimuth = 0;
            targetElevation = 0;

            //statusBuilder.Clear();
            if (Weapons.Count == 0) return;

            //statusBuilder.AppendLine("TGTS: " + targets.Count.ToString());
            while (Weapons[0].Closed)
            {
                Weapons.RemoveAtFast(0);
                if (Weapons.Count == 0) return;
            }
            IMyTerminalBlock reference = Weapons[0];

            var myVel = Host.IntelProvider.Controller.GetShipVelocities().LinearVelocity;
            var canonicalTime = Host.IntelProvider.CanonicalTimeDiff + timestamp;
            foreach (var target in targets)
            {
                var targetVel = target.GetVelocity();
                
                // Get attack position
                var relativeAttackPoint = AttackHelpers.GetAttackPoint(targetVel - myVel, target.GetPositionFromCanonicalTime(canonicalTime) - (reference.WorldMatrix.Translation), projectileSpeed);
                var relativeAttackDirection = Vector3D.Normalize(relativeAttackPoint);

                // Check range
                if (relativeAttackPoint.Length() > range) continue;

                // Calculate azimuth angle
                var azimuthVector = relativeAttackDirection - VectorHelpers.VectorProjection(relativeAttackDirection, Azimuth.WorldMatrix.Up);
                var azimuthAngle = VectorHelpers.VectorAngleBetween(azimuthVector, Azimuth.WorldMatrix.Backward) * Math.Sign(azimuthVector.Dot(Azimuth.WorldMatrix.Left));

                //statusBuilder.AppendLine("AZM: " + (azimuthAngle * 180 / Math.PI).ToString());

                // Check if azimuth is OK
                if (azimuthAngle > AzimuthMax || azimuthAngle < AzimuthMin) continue;

                // Calculate elevation angle
                var elevationAngle = VectorHelpers.VectorAngleBetween(azimuthVector, relativeAttackDirection) * Math.Sign(relativeAttackDirection.Dot(Azimuth.WorldMatrix.Up));
                if (elevationAngle > ElevationMax || elevationAngle < ElevationMin) continue;

                //statusBuilder.AppendLine("ELV: " + (elevationAngle * 180 / Math.PI).ToString());

                // Found best target, set target az, el, and return
                targetAzimuth = azimuthAngle;
                targetElevation = elevationAngle;
                return;
            }
        }

        public void AimAndFire()
        {
            var azimuthDiff = targetAzimuth - ((Azimuth.Angle + Math.PI) % (2 * Math.PI) - Math.PI);
            var elevationDiff = targetElevation - ((Elevation.Angle + Math.PI) % (2 * Math.PI) - Math.PI);
            if (snapAim)
            {
                Azimuth.UpperLimitRad = (float)targetAzimuth + 0.00001f;
                Azimuth.LowerLimitRad = (float)targetAzimuth - 0.00001f;
                Azimuth.TargetVelocityRPM = Math.Abs(azimuthDiff) < 0.00002 ? 0 : 30 * Math.Sign(azimuthDiff);

                Elevation.UpperLimitRad = (float)targetElevation + 0.00001f;
                Elevation.LowerLimitRad = (float)targetElevation - 0.00001f;
                Elevation.TargetVelocityRPM = Math.Abs(elevationDiff) < 0.00002 ? 0 : 30 * Math.Sign(elevationDiff);
            }
            else
            {
                if (Math.Abs(azimuthDiff) < 0.00002)
                {
                    Azimuth.TargetVelocityRPM = 0;
                }
                else
                {
                    Azimuth.UpperLimitRad = azimuthDiff > 0 ? (float)targetAzimuth : Azimuth.Angle + 0.5f * 3.1415f / 180;
                    Azimuth.LowerLimitRad = azimuthDiff < 0 ? (float)targetAzimuth : Azimuth.Angle - 0.5f * 3.1415f / 180;
                    Azimuth.TargetVelocityRPM = 30 * Math.Sign(azimuthDiff);
                }

                if (Math.Abs(elevationDiff) < 0.00002)
                {
                    Elevation.TargetVelocityRPM = 0;
                }
                else
                {
                    Elevation.UpperLimitRad = elevationDiff > 0 ? (float)targetElevation : Elevation.Angle + 0.5f * 3.1415f / 180;
                    Elevation.LowerLimitRad = elevationDiff < 0 ? (float)targetElevation : Elevation.Angle - 0.5f * 3.1415f / 180;
                    Elevation.TargetVelocityRPM = 30 * Math.Sign(elevationDiff);
                }

                //Azimuth.TargetVelocityRPM = (float)AzimuthPID.Control(azimuthDiff);
                //Elevation.TargetVelocityRPM = (float)ElevationPID.Control(elevationDiff);
            }

            if (targetAzimuth != 0 && Math.Abs(azimuthDiff) < fireTolerance && targetElevation != 0 && Math.Abs(elevationDiff) < fireTolerance)
            {
                if (fireTicks < 0)
                {
                    foreach (var weapon in Weapons)
                    {
                        weapon.Enabled = true;
                    }
                }
                fireTicks = 3;
            }
            else
            {
                fireTicks--;
                if (fireTicks < 0)
                {
                    foreach (var weapon in Weapons)
                    {
                        weapon.Enabled = false;
                    }
                }
            }
        }

        public string GetStatus()
        {
            return "";
        }

        string GetBasicStatus()
        {
            var azmStatus = Azimuth == null ? "MIS" : Azimuth.IsFunctional ? "AOK" : "DMG";
            var elvStatus = Elevation == null ? "MIS" : Elevation.IsFunctional ? "AOK" : "DMG";
            int okWpn = 0;
            foreach (var weapon in Weapons)
            {
                if (weapon.IsFunctional)
                    okWpn++;
            }
            return $"AZM:{azmStatus} ELV:{elvStatus} WPN:{okWpn}";
        }

        public const int TURRET_TIMESTEP = 5;
    }


    public class TurretSubsystem : ISubsystem
    {
        #region ISubsystem
        public UpdateFrequency UpdateFrequency => UpdateFrequency.Update1;
        
        public void Command(TimeSpan timestamp, string command, object argument)
        {
        }

        public void CommandV2(TimeSpan timestamp, CommandLine command)
        {

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

        public void Setup(ExecutionContext context, string name )
        {
            Context = context;

            ParseConfigs();
            GetParts();
        }
        
        public void Update(TimeSpan timestamp, UpdateFrequency updateFlags)
        {
            runs++;

            if (runs % RotorHingeTurret.TURRET_TIMESTEP == 0 && runs > 60)
            {
                if (Context.WCAPI == null) return;
                statusBuilder.Clear();
                var target = Context.WCAPI.GetAiFocus(Context.Reference.CubeGrid.EntityId);
                statusBuilder.AppendLine(target == null ? "NULL" : target.Value.Name);

                // Get ordered list of targets
                var intelItems = IntelProvider.GetFleetIntelligences(timestamp);
                OrderedTargets.Clear();
                EnemyToScore.Clear();

                foreach (var intelItem in intelItems)
                {
                    if (intelItem.Key.Item1 == IntelItemType.Enemy)
                    {
                        var esi = (EnemyShipIntel)intelItem.Value;
                        var dist = (esi.GetPositionFromCanonicalTime(timestamp + IntelProvider.CanonicalTimeDiff) - Context.Reference.WorldMatrix.Translation).Length();
                        if (dist > MaxEngagementDist) continue;

                        var priority = IntelProvider.GetPriority(esi.ID);
                        var size = esi.Radius;

                        if (size < MinEngagementSize && priority < 3) continue;

                        if (priority < 2) continue;

                        int score = (int)(priority * 10000 + size);

                        if (target != null && target.Value.EntityId == intelItem.Key.Item2)
                            score = int.MaxValue;

                        EnemyToScore[esi] = score;

                        for (int i = 0; i <= OrderedTargets.Count; i++)
                        {
                            if (i == OrderedTargets.Count || score > EnemyToScore[OrderedTargets[i]])
                            {
                                OrderedTargets.Insert(i, esi);
                                break;
                            }
                        }
                    }
                }

                // Each turret gets target solution
                foreach (var turret in TurretsDict.Values)
                {
                    turret.SelectTarget(OrderedTargets, timestamp);
                    turret.AimAndFire();
                    statusBuilder.AppendLine($"{turret.Azimuth.CustomName}: {turret.GetStatus()}");
                }
            }
        }
        #endregion
        const string kTurretSettingSection = "TurretSetting";
        const string kTurretSection = "Turret";
        ExecutionContext Context;

        StringBuilder statusBuilder = new StringBuilder();

        MyIni iniParser = new MyIni();
        List<MyIniKey> iniKeyScratchpad = new List<MyIniKey>();

        string TurretGroupName;
        public IIntelProvider IntelProvider;
        public int runs = 0;

        public Dictionary<IMyMotorStator, RotorHingeTurret> TurretsDict = new Dictionary<IMyMotorStator, RotorHingeTurret>();
        public List<IMyTerminalBlock> GetBlocksScratchpad = new List<IMyTerminalBlock>();
        Dictionary<long, RotorHingeTurret> TopIDsToTurret = new Dictionary<long, RotorHingeTurret>();
        Dictionary<long, RotorHingeTurret> TopTopIDsToTurret = new Dictionary<long, RotorHingeTurret>();

        List<EnemyShipIntel> OrderedTargets = new List<EnemyShipIntel>();
        Dictionary<EnemyShipIntel, int> EnemyToScore = new Dictionary<EnemyShipIntel, int>();
        int MaxEngagementDist = 0;
        int MinEngagementSize = 0;


        public TurretSubsystem(IIntelProvider intelProvider, string turretGroupName = "[LG-TURRET]")
        {
            TurretGroupName = turretGroupName;
            IntelProvider = intelProvider;
        }

        void GetParts()
        {
            GetBlocksScratchpad.Clear();
            var group = Context.Terminal.GetBlockGroupWithName(TurretGroupName);
            if (group == null) return;
            group.GetBlocksOfType<IMyTerminalBlock>(GetBlocksScratchpad);

            TopIDsToTurret.Clear();
            TopTopIDsToTurret.Clear();

            // First iteration, get turret bases
            foreach (var block in GetBlocksScratchpad)
            {
                if (block is IMyMotorStator && block.CubeGrid.EntityId == Context.Reference.CubeGrid.EntityId)
                {
                    var rotor = block as IMyMotorStator;
                    rotor.UpperLimitDeg = 360;
                    rotor.LowerLimitDeg = -360;
                    rotor.TargetVelocityRPM = 0;
                    TurretsDict[rotor] = new RotorHingeTurret();
                    TurretsDict[rotor].Azimuth = rotor;
                    TurretsDict[rotor].Host = this;

                    iniParser.Clear();
                    MyIniParseResult result;
                    if (iniParser.TryParse(rotor.CustomData, out result))
                    {
                        TurretsDict[rotor].range = iniParser.Get(kTurretSettingSection, "Range").ToInt32(1000);
                        MaxEngagementDist = Math.Max(TurretsDict[rotor].range, MaxEngagementDist);
                        TurretsDict[rotor].projectileSpeed = iniParser.Get(kTurretSettingSection, "Speed").ToInt32(1000);
                        TurretsDict[rotor].targetLarge = iniParser.Get(kTurretSettingSection, "TargetLarge").ToBoolean(true);
                        TurretsDict[rotor].targetSmall = iniParser.Get(kTurretSettingSection, "TargetSmall").ToBoolean(true);
                        TurretsDict[rotor].snapAim = iniParser.Get(kTurretSettingSection, "SnapAim").ToBoolean(true);
                        var kP = (float)iniParser.Get(kTurretSettingSection, "kP").ToDecimal(60);
                        var kI = (float)iniParser.Get(kTurretSettingSection, "kI").ToDecimal(1);
                        var kD = (float)iniParser.Get(kTurretSettingSection, "kD").ToDecimal(30);

                        TurretsDict[rotor].AzimuthMax = (float)(iniParser.Get(kTurretSettingSection, "AzimuthMax").ToInt32(175) * 180 / Math.PI);
                        TurretsDict[rotor].AzimuthMin = (float)(iniParser.Get(kTurretSettingSection, "AzimuthMin").ToInt32(-175) * 180 / Math.PI);
                        TurretsDict[rotor].ElevationMax = (float)(iniParser.Get(kTurretSettingSection, "ElevationMax").ToInt32(20) * 180 / Math.PI);
                        TurretsDict[rotor].ElevationMin = (float)(iniParser.Get(kTurretSettingSection, "ElevationMin").ToInt32(-20) * 180 / Math.PI);

                        TurretsDict[rotor].AzimuthPID = new PID(kP, kI, kD, 0.03, RotorHingeTurret.TURRET_TIMESTEP);
                        TurretsDict[rotor].ElevationPID = new PID(kP, kI, kD, 0.03, RotorHingeTurret.TURRET_TIMESTEP);
                    }

                    if (rotor.TopGrid != null)
                        TopIDsToTurret[rotor.TopGrid.EntityId] = TurretsDict[rotor];
                }
            }

            // Second iteration, get hinges
            foreach (var block in GetBlocksScratchpad)
            {
                if (block is IMyMotorAdvancedStator && TopIDsToTurret.ContainsKey(block.CubeGrid.EntityId))
                {
                    var hinge = block as IMyMotorAdvancedStator;
                    hinge.UpperLimitDeg = 90;
                    hinge.LowerLimitDeg = -90;
                    hinge.TargetVelocityRPM = 0;
                    TopIDsToTurret[block.CubeGrid.EntityId].Elevation = hinge;
                    if (hinge.TopGrid != null)
                        TopTopIDsToTurret[hinge.TopGrid.EntityId] = TopIDsToTurret[block.CubeGrid.EntityId];
                }
            }

            // Final iteration, get all other blocks
            foreach (var block in GetBlocksScratchpad)
            {
                if (TopTopIDsToTurret.ContainsKey(block.CubeGrid.EntityId) && block is IMyFunctionalBlock)
                {
                    TopTopIDsToTurret[block.CubeGrid.EntityId].Weapons.Add(block as IMyFunctionalBlock);
                }
            }
        }

        // [Turret]
        // MinEngagementSize = 1
        void ParseConfigs()
        {
            iniParser.Clear();
            MyIniParseResult result;
            if (!iniParser.TryParse(Context.Reference.CustomData, out result))
                return;

            MinEngagementSize = iniParser.Get(kTurretSection, "MinEngagementSize").ToInt32(1);
        }
    }
}
