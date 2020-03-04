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
    public class LocustAttackTaskGenerator : ITaskGenerator
    {
        #region ITaskGenerator
        public TaskType AcceptedTypes => TaskType.Attack;

        public ITask GenerateTask(TaskType type, MyTuple<IntelItemType, long> intelKey, Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence> IntelItems, TimeSpan canonicalTime, long myID)
        {
            if (type != TaskType.Attack) return new NullTask();
            return new LocustAttackTask(Program, CombatSystem, Autopilot, AgentSubsystem, intelKey);
        }
        #endregion

        MyGridProgram Program;
        LocustCombatSystem CombatSystem;
        IAutopilot Autopilot;
        IAgentSubsystem AgentSubsystem;

        public LocustAttackTaskGenerator(MyGridProgram program, LocustCombatSystem combatSystem, IAutopilot autopilot, IAgentSubsystem agentSubsystem)
        {
            Program = program;
            CombatSystem = combatSystem;
            Autopilot = autopilot;
            AgentSubsystem = agentSubsystem;
        }
    }


    public class LocustAttackTask : ITask
    {
        #region ITask
        public TaskStatus Status { get; private set; }

        public void Do(Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence> IntelItems, TimeSpan canonicalTime)
        {
            IMyShipController controller = Autopilot.Controller;
            var currentPosition = controller.WorldMatrix.Translation;
            Vector3D linearVelocity = controller.GetShipVelocities().LinearVelocity;

            if (!TargetPositionSet)
            {
                if (IntelKey.Item1 == IntelItemType.Waypoint && IntelItems.ContainsKey(IntelKey))
                    TargetPosition = IntelItems[IntelKey].GetPositionFromCanonicalTime(canonicalTime);
                TargetPositionSet = true;
            }

            Vector3D worldAttackPoint;

            if (TargetPositionSet && TargetPosition != Vector3D.Zero)
            {
                worldAttackPoint = TargetPosition;
            }
            else
            {
                if (IntelKey.Item1 != IntelItemType.Enemy || !IntelItems.ContainsKey(IntelKey))
                {
                    AgentSubsystem.AddTask(TaskType.Dock, MyTuple.Create(IntelItemType.NONE, (long)0), CommandType.Enqueue, 0, canonicalTime);
                    Status = TaskStatus.Aborted;
                    return;
                }

                var target = (EnemyShipIntel)IntelItems[IntelKey];
                worldAttackPoint = currentPosition + AttackHelpers.GetAttackPoint(target.GetVelocity(), target.GetPositionFromCanonicalTime(canonicalTime) + target.GetVelocity() * 0.08 - currentPosition, 98);
                Autopilot.SetStatus($"{worldAttackPoint.ToString()} {currentPosition.ToString()} {AttackHelpers.GetAttackPoint(target.GetVelocity(), target.GetPositionFromCanonicalTime(canonicalTime) + target.GetVelocity() * 0.08 - currentPosition, 98)}");
            }

            Vector3D dirToTarget = worldAttackPoint - currentPosition;

            if (dirToTarget.Length() < LocustCombatSystem.kEngageRange && deployTime == TimeSpan.Zero)
            {
                CombatSystem.Deploy();
                deployTime = canonicalTime;
            }

            dirToTarget.Normalize();
            LeadTask.Destination.Direction = dirToTarget;
            LeadTask.Destination.Position = worldAttackPoint + dirToTarget * 400;

            if (deployTime != TimeSpan.Zero)
            {
                if (deployTime + TimeSpan.FromSeconds(2) < canonicalTime)
                {
                    AgentSubsystem.AddTask(TaskType.Dock, MyTuple.Create(IntelItemType.NONE, (long)0), CommandType.Enqueue, 0, canonicalTime);
                    Status = TaskStatus.Aborted;
                    return;
                }
                else
                {
                    LeadTask.Destination.DirectionUp = Math.Sin(kRotateTheta) * controller.WorldMatrix.Right + Math.Cos(kRotateTheta) * controller.WorldMatrix.Up;
                }
            }

            LeadTask.Do(IntelItems, canonicalTime);
        }
        #endregion

        WaypointTask LeadTask;
        MyGridProgram Program;
        LocustCombatSystem CombatSystem;
        IAutopilot Autopilot;
        IAgentSubsystem AgentSubsystem;
        MyTuple<IntelItemType, long> IntelKey;
        Vector3D TargetPosition;
        bool TargetPositionSet = false;

        TimeSpan deployTime = TimeSpan.Zero;

        Vector3D LastLinearVelocity = Vector3D.Zero;
        Vector3D LastAcceleration = Vector3D.Zero;
        MatrixD LastReference = MatrixD.Zero;

        double kRotateTheta = 0;

        public LocustAttackTask(MyGridProgram program, LocustCombatSystem combatSystem, IAutopilot autopilot, IAgentSubsystem agentSubsystem, MyTuple<IntelItemType, long> intelKey)
        {
            Program = program;
            CombatSystem = combatSystem;
            Autopilot = autopilot;
            AgentSubsystem = agentSubsystem;
            IntelKey = intelKey;

            Status = TaskStatus.Incomplete;

            LeadTask = new WaypointTask(Program, Autopilot, new Waypoint(), WaypointTask.AvoidObstacleMode.Avoid);
        }
    }
}
