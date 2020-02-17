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
            return new HornetAttackTask(Program, CombatSystem, Autopilot, intelKey);
        }
        #endregion

        MyGridProgram Program;
        HornetCombatSubsystem CombatSystem;
        IAutopilot Autopilot;

        public HornetAttackTaskGenerator(MyGridProgram program, HornetCombatSubsystem combatSystem, IAutopilot autopilot)
        {
            Program = program;
            CombatSystem = combatSystem;
            Autopilot = autopilot;
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

            if (!TargetPositionSet)
            {
                if (IntelKey.Item1 == IntelItemType.Waypoint && IntelItems.ContainsKey(IntelKey))
                    TargetPosition = IntelItems[IntelKey].GetPositionFromCanonicalTime(canonicalTime);
                TargetPositionSet = true;
            }

            if (CombatSystem.TargetIntel == null)
            {
                if (IntelItems.ContainsKey(IntelKey))
                    LeadTask.Destination.Position = IntelItems[IntelKey].GetPositionFromCanonicalTime(canonicalTime);
                else if (TargetPosition != Vector3.Zero)
                    LeadTask.Destination.Position = TargetPosition;

                Vector3D toTarget = LeadTask.Destination.Position - Program.Me.WorldMatrix.Translation;
                LeadTask.Destination.Direction = toTarget;

                LastAcceleration = Vector3D.Zero;
                LastReference = MatrixD.Zero;
                // LocalAttackDirD = Vector3D.Zero;
                // LocalAttackDirI = Vector3D.Zero;
            }
            else
            {
                EnemyShipIntel targetIntel = CombatSystem.TargetIntel;
                Vector3D targetPosition = targetIntel.GetPositionFromCanonicalTime(canonicalTime);

                var Acceleration = linearVelocity - LastLinearVelocity;
                if (LastAcceleration == Vector3D.Zero) LastAcceleration = Acceleration;
                if (LastReference == MatrixD.Zero) LastReference = controller.WorldMatrix;

                var CurrentAccelerationPreviousFrame = Vector3D.TransformNormal(Acceleration, MatrixD.Transpose(LastReference));

                //var localAcceleration = Vector3D.TransformNormal(Acceleration, MatrixD.Transpose(controller.WorldMatrix));
                //var localLastAcceleration = Vector3D.TransformNormal(LastAcceleration, MatrixD.Transpose(controller.WorldMatrix));

                var accelerationAdjust = Vector3D.TransformNormal(CurrentAccelerationPreviousFrame, controller.WorldMatrix);

                // Magic constant
                var kMagic = 0.57;
                Vector3D relativeAttackPoint = AttackHelpers.GetAttackPoint(targetIntel.GetVelocity() - (linearVelocity + (accelerationAdjust + Acceleration) * 0.5) * kMagic, targetPosition - controller.WorldMatrix.Translation, 400);

                LastAcceleration = linearVelocity - LastLinearVelocity;
                //relativeAttackPoint.Normalize();
                //
                //var localAttackDir = Vector3D.TransformNormal(relativeAttackPoint, MatrixD.Transpose(controller.WorldMatrix));
                //var localAttackDirError = localAttackDir - MatrixD.Identity.Forward;
                //
                //if (LocalAttackDirD == Vector3D.Zero) LocalAttackDirD = localAttackDirError;
                //
                //var localAttackPointAdjust = localAttackDir + LocalAttackDirD * 0 + LocalAttackDirI * 0.2;
                //
                //LocalAttackDirI += localAttackDirError;
                //LocalAttackDirD = localAttackDirError;
                //
                LeadTask.Destination.Direction = relativeAttackPoint;
                //LastLocalAcceleration = localAcceleration;

                if (VectorHelpers.VectorAngleBetween(LeadTask.Destination.Direction, controller.WorldMatrix.Forward) < 0.05) CombatSystem.Fire();

                Vector3D dirTargetToMe = controller.WorldMatrix.Translation - targetPosition;
                Vector3D dirTargetToOrbitTarget = Vector3D.Cross(dirTargetToMe, controller.WorldMatrix.Up);
                dirTargetToOrbitTarget.Normalize();
                LeadTask.Destination.Position = targetPosition + targetIntel.GetVelocity() * 6 + dirTargetToOrbitTarget * HornetCombatSubsystem.kEngageRange * 2;
            }

            LastLinearVelocity = linearVelocity;
            LeadTask.Do(IntelItems, canonicalTime);
        }
        #endregion

        WaypointTask LeadTask;
        MyGridProgram Program;
        HornetCombatSubsystem CombatSystem;
        IAutopilot Autopilot;
        MyTuple<IntelItemType, long> IntelKey;
        Vector3D TargetPosition;
        bool TargetPositionSet = false;

        Vector3D LastLinearVelocity = Vector3D.Zero;
        Vector3D LastAcceleration = Vector3D.Zero;
        MatrixD LastReference = MatrixD.Zero;

        // Vector3D LocalAttackDirD = Vector3D.Zero;
        // Vector3D LocalAttackDirI = Vector3D.Zero;

        public HornetAttackTask(MyGridProgram program, HornetCombatSubsystem combatSystem, IAutopilot autopilot, MyTuple<IntelItemType, long> intelKey)
        {
            Program = program;
            CombatSystem = combatSystem;
            Autopilot = autopilot;
            IntelKey = intelKey;

            Status = TaskStatus.Incomplete;

            LeadTask = new WaypointTask(Program, Autopilot, new Waypoint(), WaypointTask.AvoidObstacleMode.Avoid);
        }
    }
}
