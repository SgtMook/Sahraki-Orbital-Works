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
    // This is a Raven class attack drone
    public class Raven
    {
        public SubsystemManager SubsystemManager;

//        int runs = 0;

        IMyRemoteControl Controller;

        List<IMyLargeTurretBase> Turrets = new List<IMyLargeTurretBase>();

        ExecutionContext Context;
        public AtmoDrive Drive;

        public Raven(IMyRemoteControl reference, MyGridProgram program)
        {
            Controller = reference;
            Context = new ExecutionContext(program, reference);

            SubsystemManager = new SubsystemManager(Context);
            Drive = new AtmoDrive(Controller);
            CombatLoaderSubsystem loaderSubsystem = new CombatLoaderSubsystem("Drone Cargo", "Drone Store");
            IntelSubsystem intelSubsystem = new IntelSubsystem();
            DockingSubsystem dockingSubsystem = new DockingSubsystem(intelSubsystem, loaderSubsystem);
            StatusIndicatorSubsystem indicatorSubsystem = new StatusIndicatorSubsystem(dockingSubsystem, intelSubsystem);
            MonitorSubsystem monitorSubsystem = new MonitorSubsystem(intelSubsystem);
            AgentSubsystem agentSubsystem = new AgentSubsystem(intelSubsystem, AgentClass.Fighter);
            UndockFirstTaskGenerator undockingTaskGenerator = new UndockFirstTaskGenerator(program, Drive, dockingSubsystem);
            ScannerNetworkSubsystem scannerSubsystem = new ScannerNetworkSubsystem(intelSubsystem);
            HornetCombatSubsystem combatSubsystem = new HornetCombatSubsystem(intelSubsystem);

            SubsystemManager.AddSubsystem("autopilot", Drive);
            SubsystemManager.AddSubsystem("docking", dockingSubsystem);
            SubsystemManager.AddSubsystem("intel", intelSubsystem);
            SubsystemManager.AddSubsystem("monitor", monitorSubsystem);
            SubsystemManager.AddSubsystem("combat", combatSubsystem);
            SubsystemManager.AddSubsystem("indicator", indicatorSubsystem);
            SubsystemManager.AddSubsystem("loader", loaderSubsystem);

            undockingTaskGenerator.AddTaskGenerator(new WaypointTaskGenerator(program, Drive));
            undockingTaskGenerator.AddTaskGenerator(new DockTaskGenerator(program, Drive, dockingSubsystem));
            undockingTaskGenerator.AddTaskGenerator(new HornetAttackTaskGenerator(program, combatSubsystem, Drive, agentSubsystem, monitorSubsystem, intelSubsystem));

            agentSubsystem.AddTaskGenerator(undockingTaskGenerator);
            agentSubsystem.AddTaskGenerator(new SetHomeTaskGenerator(program, dockingSubsystem));

            SubsystemManager.AddSubsystem("agent", agentSubsystem);
            SubsystemManager.AddSubsystem("scanner", scannerSubsystem);
        }

        public void Update(UpdateType updateSource)
        {
            SubsystemManager.UpdateTime();
            SubsystemManager.Update(updateSource);
        }

        // Vector3D linearVelocity = Vector3D.Zero;
        // Vector3D LastLinearVelocity = Vector3D.Zero;
        // Vector3D LastAcceleration = Vector3D.Zero;
        // MatrixD LastReference = MatrixD.Zero;
        // 
        // public void Update()
        // {
        //     runs++;
        //     if (runs % 5 == 0)
        //     {
        //         Drive.AimTarget = Vector3D.Zero;
        //         // TODO: Add WeaponCore targeting here
        //         foreach (var turret in Turrets)
        //         {
        //             if (turret.HasTarget)
        //             {
        //                 var target = turret.GetTargetedEntity();
        //                 var grav = Controller.GetNaturalGravity();
        //                 grav.Normalize();
        // 
        //                 var targetVel = target.Velocity;
        // 
        //                 linearVelocity = Controller.GetShipVelocities().LinearVelocity;
        // 
        //                 var Acceleration = linearVelocity - LastLinearVelocity;
        //                 if (LastAcceleration == Vector3D.Zero) LastAcceleration = Acceleration;
        //                 if (LastReference == MatrixD.Zero) LastReference = Controller.WorldMatrix;
        // 
        //                 var CurrentAccelerationPreviousFrame = Vector3D.TransformNormal(Acceleration, MatrixD.Transpose(LastReference));
        // 
        //                 var accelerationAdjust = Vector3D.TransformNormal(CurrentAccelerationPreviousFrame, Controller.WorldMatrix);
        //                 var velocityAdjust = linearVelocity + (accelerationAdjust) * 0.05;
        // 
        //                 Vector3D relativeAttackPoint = AttackHelpers.GetAttackPoint(targetVel - velocityAdjust, target.Position + targetVel * 0.05f - (Controller.WorldMatrix.Translation + velocityAdjust * 0.22), 400);
        // 
        //                 Drive.Destination = target.Position - grav * 100 + Controller.WorldMatrix.Left * 500;
        //                 Drive.AimTarget = relativeAttackPoint + Controller.GetPosition();
        // 
        //                 LastAcceleration = linearVelocity - LastLinearVelocity;
        //                 LastReference = Controller.WorldMatrix;
        //                 LastLinearVelocity = linearVelocity;
        //                 break;
        //             }
        //         }
        //     }
        //     Drive.Update(TimeSpan.Zero, UpdateFrequency.Update1);
        // }

        public string GetStatus()
        {
            return SubsystemManager.GetStatus();
        }
    }
}
