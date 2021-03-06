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
    public class HornetAttackTaskGenerator : ITaskGenerator
    {
        #region ITaskGenerator
        public TaskType AcceptedTypes => TaskType.Attack | TaskType.Picket;

        public ITask GenerateTask(TaskType type, MyTuple<IntelItemType, long> intelKey, Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence> IntelItems, TimeSpan canonicalTime, long myID)
        {
            if (MonitorSubsystem != null &&
                (MonitorSubsystem.GetPercentage(MonitorOptions.Hydrogen) < 0.3 ||
                MonitorSubsystem.GetPercentage(MonitorOptions.Cargo) < 0.1 ||
                MonitorSubsystem.GetPercentage(MonitorOptions.Power) < 0.1))
            {
                return new NullTask();
            }
            if (type != TaskType.Attack && type != TaskType.Picket) return new NullTask();
            HornetAttackTask.Reset(intelKey, type);
            return HornetAttackTask;
        }
        #endregion

        MyGridProgram Program;
        HornetCombatSubsystem CombatSystem;
        IAutopilot Autopilot;
        IAgentSubsystem AgentSubsystem;
        IMonitorSubsystem MonitorSubsystem;
        IIntelProvider IntelProvider;

        public HornetAttackTask HornetAttackTask;

        public HornetAttackTaskGenerator(MyGridProgram program, HornetCombatSubsystem combatSystem, IAutopilot autopilot, IAgentSubsystem agentSubsystem, IMonitorSubsystem monitorSubsystem, IIntelProvider intelProvider)
        {
            Program = program;
            CombatSystem = combatSystem;
            Autopilot = autopilot;
            AgentSubsystem = agentSubsystem;
            MonitorSubsystem = monitorSubsystem;
            IntelProvider = intelProvider;

            HornetAttackTask = new HornetAttackTask(Program, CombatSystem, Autopilot, AgentSubsystem, MonitorSubsystem, IntelProvider);
            HornetAttackTask.Do(new Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence>(), TimeSpan.Zero, null);
        }
    }


    public class HornetAttackTask : ITask
    {
        #region ITask
        public TaskStatus Status { get; set; }

        public void Do(Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence> IntelItems, TimeSpan canonicalTime, Profiler profiler)
        {
            if (canonicalTime == TimeSpan.Zero) return;

            runs++;

            if (MonitorSubsystem != null && 
                (MonitorSubsystem.GetPercentage(MonitorOptions.Hydrogen) < 0.2 ||
                MonitorSubsystem.GetPercentage(MonitorOptions.Cargo) < 0.02 ||
                MonitorSubsystem.GetPercentage(MonitorOptions.Power) < 0.1))
            {
                GoHome(canonicalTime);
                return;
            }

            IMyShipController controller = Autopilot.Controller;
            Vector3D linearVelocity = controller.GetShipVelocities().LinearVelocity;
            var currentPosition = controller.WorldMatrix.Translation;
            LeadTask.Destination.MaxSpeed = Autopilot.CruiseSpeed;

            if (!TargetPositionSet)
            {
                if (IntelKey.Item1 == IntelItemType.Waypoint && IntelItems.ContainsKey(IntelKey))
                {
                    TargetPosition = IntelItems[IntelKey].GetPositionFromCanonicalTime(canonicalTime);
                    PatrolMaxSpeed = ((Waypoint)IntelItems[IntelKey]).MaxSpeed;
                }
                TargetPositionSet = true;
            }

            EnemyShipIntel orbitIntel = null;
            EnemyShipIntel shootIntel = null;
            double closestIntelDist = CombatSystem.AlertDist;
            foreach (var intel in IntelItems)
            {
                if (intel.Key.Item1 != IntelItemType.Enemy) 
                    continue;

                var enemyIntel = (EnemyShipIntel)intel.Value;
                if (!EnemyShipIntel.PrioritizeTarget(enemyIntel)) 
                    continue;
                if (IntelProvider.GetPriority(enemyIntel.ID) < 2) 
                    continue;

                double dist = (enemyIntel.GetPositionFromCanonicalTime(canonicalTime) - controller.WorldMatrix.Translation).Length();

                if (IntelKey.Item1 == IntelItemType.Enemy && enemyIntel.ID != IntelKey.Item2 && FocusedTarget) continue;

                if (enemyIntel.CubeSize == MyCubeSize.Small) 
                    dist -= 300;
                if (IntelProvider.GetPriority(enemyIntel.ID) == 3) 
                    dist -= 600;
                if (IntelProvider.GetPriority(enemyIntel.ID) == 4) 
                    dist -= 1200;
                if (dist < closestIntelDist)
                {
                    closestIntelDist = dist;
                    shootIntel = enemyIntel;
                    if (enemyIntel.Radius > 30) 
                        orbitIntel = enemyIntel;
                }
            }

            if (shootIntel == null && CombatSystem.TargetIntel != null && IntelProvider.GetPriority(CombatSystem.TargetIntel.ID) >= 2) 
                shootIntel = CombatSystem.TargetIntel;

            if (orbitIntel == null) 
                orbitIntel = shootIntel;

            var gravdir = Autopilot.Controller.GetNaturalGravity();
            if (gravdir != Vector3D.Zero)
            {
                gravdir.Normalize();
            }

            // No Target
            if (orbitIntel == null)
            {
                // Plot Intercept
                if (IntelKey.Item1 == IntelItemType.Enemy && IntelItems.ContainsKey(IntelKey) && EnemyShipIntel.PrioritizeTarget((EnemyShipIntel)IntelItems[IntelKey]) && IntelProvider.GetPriority(IntelKey.Item2) >= 2)
                {
                    var target = IntelItems[IntelKey];
                    LeadTask.Destination.Position = currentPosition + AttackHelpers.GetAttackPoint(target.GetVelocity(), target.GetPositionFromCanonicalTime(canonicalTime) + target.GetVelocity() * 0.08 - currentPosition, Autopilot.CruiseSpeed);
                }
                // Go to Position ( Scramble )
                else if (TargetPosition != Vector3.Zero)
                {
                    LeadTask.Destination.MaxSpeed = PatrolMaxSpeed;
                    LeadTask.Destination.Position = TargetPosition;
                }
                else
                {
                    GoHome(canonicalTime);
                    return;
                }

                Vector3D toTarget = LeadTask.Destination.Position - Program.Me.WorldMatrix.Translation;
                if (toTarget.LengthSquared() > 400)
                {
                    if (gravdir == Vector3D.Zero)
                    {
                        LeadTask.Destination.Direction = toTarget;
                    }
                    else
                    {
                        LeadTask.Destination.Direction = Vector3D.Zero;
                    }
                }
                else 
                    LeadTask.Destination.Direction = Vector3D.Zero;

                LastAcceleration = Vector3D.Zero;
                LastReference = MatrixD.Zero;
                LastEnemyVelocity = Vector3D.Zero;
                LastEnemyPosition = Vector3D.Zero;
                LastRelativeAttackPoint = Vector3D.Zero;
//                targetLastPoweredRun = 0;
            }

            // Orbiting a Target
            else
            {
                CombatSystem.MarkEngaged();
                LeadTask.Destination.MaxSpeed = Autopilot.CombatSpeed;
                Vector3D targetPosition = orbitIntel.GetPositionFromCanonicalTime(canonicalTime);
                var periodicFactor = runs * Math.PI * CombatSystem.EngageTheta / 4;
                Vector3D periodicDirection = Math.Sin(periodicFactor) * controller.WorldMatrix.Left + Math.Cos(periodicFactor) * controller.WorldMatrix.Up;

                var Acceleration = linearVelocity - LastLinearVelocity;
                if (LastAcceleration == Vector3D.Zero) 
                    LastAcceleration = Acceleration;
                if (LastReference == MatrixD.Zero) 
                    LastReference = controller.WorldMatrix;
                if (LastEnemyVelocity == Vector3D.Zero) 
                    LastEnemyVelocity = shootIntel.GetVelocity();
                if (LastEnemyPosition == Vector3D.Zero)
                    LastEnemyPosition = shootIntel.GetPositionFromCanonicalTime(canonicalTime);

                var enemyAcceleration = shootIntel.GetVelocity() - LastEnemyVelocity;

                var enemyVelocityAdjust = shootIntel.GetVelocity() * 2 - LastEnemyVelocity;

                var CurrentAccelerationPreviousFrame = Vector3D.TransformNormal(Acceleration, MatrixD.Transpose(LastReference));

                var accelerationAdjust = Vector3D.TransformNormal(CurrentAccelerationPreviousFrame, controller.WorldMatrix);
                var velocityAdjust = linearVelocity + accelerationAdjust * 0.4;

                velocityAdjust *= CombatSystem.OwnSpeedMultiplier;

                Vector3D relativeAttackPoint = AttackHelpers.GetAttackPoint(enemyVelocityAdjust - velocityAdjust, shootIntel.GetPositionFromCanonicalTime(canonicalTime) + enemyVelocityAdjust * 0.25 - controller.WorldMatrix.Translation - velocityAdjust * 0.25, CombatSystem.ProjectileSpeed);

                relativeAttackPoint.Normalize();

                if (LastRelativeAttackPoint == Vector3D.Zero)
                    LastRelativeAttackPoint = relativeAttackPoint;

                LastAcceleration = linearVelocity - LastLinearVelocity;
                LeadTask.Destination.Direction = relativeAttackPoint;

                if ((controller.WorldMatrix.Translation - targetPosition).Length() < CombatSystem.FireDist && 
                    VectorHelpers.VectorAngleBetween(LeadTask.Destination.Direction, controller.WorldMatrix.Forward) < CombatSystem.FireTolerance) 
                    CombatSystem.Fire();

                Vector3D dirTargetToMe = controller.WorldMatrix.Translation - targetPosition;
                var distTargetToMe = dirTargetToMe.Length();
                dirTargetToMe.Normalize();

                if (Mode == 0)
                {
                    Vector3D dirTargetToOrbitTarget = Vector3D.Cross(dirTargetToMe, controller.WorldMatrix.Up);
                    dirTargetToOrbitTarget.Normalize();

                    if (gravdir == Vector3D.Zero)
                    {
                        LeadTask.Destination.DirectionUp = Math.Sin(CombatSystem.EngageTheta) * controller.WorldMatrix.Right + Math.Cos(CombatSystem.EngageTheta) * controller.WorldMatrix.Up;
                        LeadTask.Destination.Position = targetPosition + orbitIntel.GetVelocity() + dirTargetToMe * CombatSystem.EngageDist + dirTargetToOrbitTarget * 200;
                    }
                    else
                    {
                        var flatDirTargetToMe = dirTargetToMe - VectorHelpers.VectorProjection(dirTargetToMe, gravdir);
                        flatDirTargetToMe.Normalize();
                        LeadTask.Destination.Position = targetPosition + orbitIntel.GetVelocity() + flatDirTargetToMe * CombatSystem.EngageDist * 0.9 - gravdir * 300 + periodicDirection * 100;
                    }
                    LeadTask.Destination.Velocity = orbitIntel.GetVelocity() * 0.5;
                }
                else if (Mode == 2)
                {
                    LeadTask.Destination.Position = targetPosition + dirTargetToMe * (CombatSystem.EngageDist + (CombatSystem.EngageDist - distTargetToMe));
                    LeadTask.Destination.Velocity = orbitIntel.GetVelocity();
                }

                LastReference = controller.WorldMatrix;
                LastEnemyVelocity = shootIntel.GetVelocity();
                LastEnemyPosition = shootIntel.GetPositionFromCanonicalTime(canonicalTime);
                LastRelativeAttackPoint = relativeAttackPoint;

                //var n = VectorHelpers.VectorProjection(enemyAcceleration, gravdir).Length() / enemyAcceleration.Length();
                //
                //if (targetLastPoweredRun == 0 || n < 0.6)
                //    targetLastPoweredRun = runs;
                //
                //if (runs - targetLastPoweredRun > 20)
                //    IntelProvider.SetPriority(shootIntel.ID, 1);
            }

            if (!Attack)
            {
                Vector3D toTarget = TargetPosition - Program.Me.WorldMatrix.Translation;
                if (toTarget.LengthSquared() > 100) 
                    LeadTask.Destination.Position = TargetPosition;
                else 
                    LeadTask.Destination.Position = Vector3D.Zero;
                LeadTask.Destination.Velocity = Vector3D.Zero;
            }

            if (Mode == 1)
            {
                //TODO: Add support for disabling dampeners in this mode - let pilot have full control aside from aiming
                LeadTask.Destination.Position = controller.GetPosition(); 
                LeadTask.Destination.Velocity = Vector3D.Zero;

                if (shootIntel == null)
                {
                    LeadTask.Destination.Direction = Vector3D.Zero;
                    LeadTask.Destination.DirectionUp = Vector3D.Zero;
                }
            }

            LastLinearVelocity = linearVelocity;
            if (LeadTask.Status == TaskStatus.Incomplete) 
                LeadTask.Do(IntelItems, canonicalTime, profiler);
        }

        void GoHome(TimeSpan canonicalTime)
        {
            AgentSubsystem.AddTask(TaskType.Dock, MyTuple.Create(IntelItemType.NONE, (long)0), CommandType.Enqueue, 0, canonicalTime);
            Autopilot.Clear();
            Status = TaskStatus.Complete;
        }

        public string Name => "HornetTask";
        #endregion

        WaypointTask LeadTask;
        MyGridProgram Program;

        HornetCombatSubsystem CombatSystem;
        IAutopilot Autopilot;
        IAgentSubsystem AgentSubsystem;
        IMonitorSubsystem MonitorSubsystem;
        IIntelProvider IntelProvider;

        MyTuple<IntelItemType, long> IntelKey;
        Vector3D TargetPosition;
        bool TargetPositionSet = false;

        Vector3D LastEnemyVelocity = Vector3D.Zero;
        Vector3D LastEnemyPosition = Vector3D.Zero;
        Vector3D LastRelativeAttackPoint = Vector3D.Zero;
        Vector3D LastLinearVelocity = Vector3D.Zero;
        Vector3D LastAcceleration = Vector3D.Zero;
        MatrixD LastReference = MatrixD.Zero;

        float PatrolMaxSpeed = 98;

        public bool Attack = true;

        public int Mode = 0; // 0 - standard, 1 - aim only, 2 - kite
        public bool FocusedTarget = false;

        int runs = 0;
//        int targetLastPoweredRun = 0;

        public HornetAttackTask(MyGridProgram program, HornetCombatSubsystem combatSystem, IAutopilot autopilot, IAgentSubsystem agentSubsystem, IMonitorSubsystem monitorSubsystem, IIntelProvider intelProvider)
        {
            Program = program;
            CombatSystem = combatSystem;
            Autopilot = autopilot;
            AgentSubsystem = agentSubsystem;
            MonitorSubsystem = monitorSubsystem;
            IntelProvider = intelProvider;

            Status = TaskStatus.Incomplete;

            LeadTask = new WaypointTask(Program, Autopilot, new Waypoint(), WaypointTask.AvoidObstacleMode.Avoid);
        }

        public void Reset(MyTuple<IntelItemType, long> intelKey, TaskType taskType)
        {
            Status = TaskStatus.Incomplete;

            IntelKey = intelKey;

            TargetPositionSet = false;

            LastLinearVelocity = Vector3D.Zero;
            LastAcceleration = Vector3D.Zero;
            LastReference = MatrixD.Zero;

            Attack = taskType == TaskType.Attack;
        }
    }
}
