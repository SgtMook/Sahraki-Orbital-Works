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
    public class HornetAttackTaskGenerator : ITaskGenerator
    {
        #region ITaskGenerator
        public TaskType AcceptedTypes => TaskType.Attack;

        public ITask GenerateTask(TaskType type, MyTuple<IntelItemType, long> intelKey, Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence> IntelItems, TimeSpan canonicalTime, long myID)
        {
            if (type != TaskType.Attack) return new NullTask();
            return new HornetAttackTask(Program, CombatSystem, Autopilot, AgentSubsystem, intelKey);
        }
        #endregion

        MyGridProgram Program;
        HornetCombatSubsystem CombatSystem;
        IAutopilot Autopilot;
        IAgentSubsystem AgentSubsystem;

        public HornetAttackTaskGenerator(MyGridProgram program, HornetCombatSubsystem combatSystem, IAutopilot autopilot, IAgentSubsystem agentSubsystem)
        {
            Program = program;
            CombatSystem = combatSystem;
            Autopilot = autopilot;
            AgentSubsystem = agentSubsystem;
        }
    }


    public class HornetAttackTask : ITask
    {
        #region ITask
        public TaskStatus Status { get; private set; }

        public void Do(Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence> IntelItems, TimeSpan canonicalTime)
        {
            IMyShipController controller = Autopilot.Controller;
            Vector3D linearVelocity = controller.GetShipVelocities().LinearVelocity;
            var currentPosition = controller.WorldMatrix.Translation;

            if (!TargetPositionSet)
            {
                if (IntelKey.Item1 == IntelItemType.Waypoint && IntelItems.ContainsKey(IntelKey))
                    TargetPosition = IntelItems[IntelKey].GetPositionFromCanonicalTime(canonicalTime);
                TargetPositionSet = true;
            }

            EnemyShipIntel combatIntel = null;
            double closestIntelDist = 1500;
            foreach (var intel in IntelItems)
            {
                if (intel.Key.Item1 != IntelItemType.Enemy) continue;
                var enemyIntel = (EnemyShipIntel)intel.Value;

                if (!PrioritizeTarget(enemyIntel)) continue;

                double dist = (enemyIntel.GetPositionFromCanonicalTime(canonicalTime) - controller.WorldMatrix.Translation).Length();
                if (dist < closestIntelDist)
                {
                    closestIntelDist = dist;
                    combatIntel = enemyIntel;
                }
            }

            if (combatIntel == null)
            {
                foreach (var intel in IntelItems)
                {
                    if (intel.Key.Item1 != IntelItemType.Enemy) continue;
                    var enemyIntel = (EnemyShipIntel)intel.Value;

                    double dist = (enemyIntel.GetPositionFromCanonicalTime(canonicalTime) - controller.WorldMatrix.Translation).Length();
                    if (dist < closestIntelDist)
                    {
                        closestIntelDist = dist;
                        combatIntel = enemyIntel;
                    }
                }
            }

            if (combatIntel == null) combatIntel = CombatSystem.TargetIntel;

            if (combatIntel == null)
            {
                if (IntelKey.Item1 == IntelItemType.Enemy && IntelItems.ContainsKey(IntelKey) && PrioritizeTarget((EnemyShipIntel)IntelItems[IntelKey]))
                {
                    var target = IntelItems[IntelKey];
                    LeadTask.Destination.Position = currentPosition + AttackHelpers.GetAttackPoint(target.GetVelocity(), target.GetPositionFromCanonicalTime(canonicalTime) + target.GetVelocity() * 0.08 - currentPosition, 98);
                }
                else if (TargetPosition != Vector3.Zero && (currentPosition - TargetPosition).Length() > 500)
                {
                    LeadTask.Destination.Position = TargetPosition;
                }
                else
                {
                    AgentSubsystem.AddTask(TaskType.Dock, MyTuple.Create(IntelItemType.NONE, (long)0), CommandType.Enqueue, 0, canonicalTime);
                    Status = TaskStatus.Complete;
                }

                Vector3D toTarget = LeadTask.Destination.Position - Program.Me.WorldMatrix.Translation;
                LeadTask.Destination.Direction = toTarget;

                LastAcceleration = Vector3D.Zero;
                LastReference = MatrixD.Zero;

                //LastSwapTime = TimeSpan.Zero;
                //NextSwapTime = TimeSpan.Zero;
            }
            else
            {
                Vector3D targetPosition = combatIntel.GetPositionFromCanonicalTime(canonicalTime);

                var Acceleration = linearVelocity - LastLinearVelocity;
                if (LastAcceleration == Vector3D.Zero) LastAcceleration = Acceleration;
                if (LastReference == MatrixD.Zero) LastReference = controller.WorldMatrix;

                var CurrentAccelerationPreviousFrame = Vector3D.TransformNormal(Acceleration, MatrixD.Transpose(LastReference));

                var accelerationAdjust = Vector3D.TransformNormal(CurrentAccelerationPreviousFrame, controller.WorldMatrix);
                var velocityAdjust = linearVelocity + (accelerationAdjust + Acceleration) * 0.5;

                Vector3D relativeAttackPoint = AttackHelpers.GetAttackPoint(combatIntel.GetVelocity() - velocityAdjust, targetPosition + combatIntel.GetVelocity() * 0.08 - (controller.WorldMatrix.Translation + velocityAdjust * 0.08), 400);

                LastAcceleration = linearVelocity - LastLinearVelocity;
                LeadTask.Destination.Direction = relativeAttackPoint;
                if ((controller.WorldMatrix.Translation - targetPosition).Length() < 800 && VectorHelpers.VectorAngleBetween(LeadTask.Destination.Direction, controller.WorldMatrix.Forward) < 0.05) CombatSystem.Fire();

                Vector3D dirTargetToMe = controller.WorldMatrix.Translation - targetPosition;
                Vector3D dirTargetToOrbitTarget = Vector3D.Cross(dirTargetToMe, controller.WorldMatrix.Up);
                dirTargetToOrbitTarget.Normalize();
                dirTargetToMe.Normalize();
                LeadTask.Destination.DirectionUp = Math.Sin(kRotateTheta) * controller.WorldMatrix.Right + Math.Cos(kRotateTheta) * controller.WorldMatrix.Up;
                LeadTask.Destination.Position = targetPosition + combatIntel.GetVelocity() * 2 + dirTargetToMe * HornetCombatSubsystem.kEngageRange + dirTargetToOrbitTarget * 200;

                //if (NextSwapTime < canonicalTime)
                //{
                //    NextSwapTime = canonicalTime + kSwapTimeMin + TimeSpan.FromSeconds(random.Next(0, kSwapTimeDelta));
                //    kRotateTheta *= -1;
                //}
            }

            LastLinearVelocity = linearVelocity;
            LeadTask.Do(IntelItems, canonicalTime);
        }
        #endregion

        WaypointTask LeadTask;
        MyGridProgram Program;
        HornetCombatSubsystem CombatSystem;
        IAutopilot Autopilot;
        IAgentSubsystem AgentSubsystem;
        MyTuple<IntelItemType, long> IntelKey;
        Vector3D TargetPosition;
        bool TargetPositionSet = false;

        Vector3D LastLinearVelocity = Vector3D.Zero;
        Vector3D LastAcceleration = Vector3D.Zero;
        MatrixD LastReference = MatrixD.Zero;

        double kRotateTheta = 0.1;

        TimeSpan kSwapTimeMin = TimeSpan.FromSeconds(15);
        int kSwapTimeDelta = 30;
        TimeSpan LastSwapTime;
        TimeSpan NextSwapTime;
        Random random = new Random();

        public HornetAttackTask(MyGridProgram program, HornetCombatSubsystem combatSystem, IAutopilot autopilot, IAgentSubsystem agentSubsystem, MyTuple<IntelItemType, long> intelKey)
        {
            Program = program;
            CombatSystem = combatSystem;
            Autopilot = autopilot;
            IntelKey = intelKey;
            AgentSubsystem = agentSubsystem;

            Status = TaskStatus.Incomplete;

            LeadTask = new WaypointTask(Program, Autopilot, new Waypoint(), WaypointTask.AvoidObstacleMode.Avoid);

            if (random.Next(2) == 1) kRotateTheta *= -1;
        }

        private bool PrioritizeTarget(EnemyShipIntel target)
        {
            if (target.CubeSize == MyCubeSize.Small && target.Radius < 4) return false;
            if (target.CubeSize == MyCubeSize.Large && target.Radius < 12) return false;
            return true;
        }
    }
}
