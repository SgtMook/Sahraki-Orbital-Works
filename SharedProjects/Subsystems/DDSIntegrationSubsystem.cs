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
    public class DDSIntegrationSubsystem : ISubsystem
    {
        const string IGC_MSG_TARGET_DATALINK = "IGCMSG_TR_DL";

        #region ISubsystem
        public UpdateFrequency UpdateFrequency => UpdateFrequency.Update1;

        public void Command(TimeSpan timestamp, string command, object argument)
        {
        }

        public void DeserializeSubsystem(string serialized)
        {
        }

        public string GetStatus()
        {
            return string.Empty;
        }

        public string SerializeSubsystem()
        {
            return string.Empty;
        }

        public void Setup(MyGridProgram program, string name)
        {
            Program = program;
        }

        public void Update(TimeSpan timestamp, UpdateFrequency updateFlags)
        {
        }
        #endregion

        MyGridProgram Program;

        //void ProcessCommsMessage(string arguments)
        //{
        //    while (igcTargetTracksListener.HasPendingMessage)
        //    {
        //        object data = igcTargetTracksListener.AcceptMessage().Data;
        //        if (data is MyTuple<long, long, Vector3D, Vector3D, double>)
        //        {
        //            MyTuple<long, long, Vector3D, Vector3D, double> targetTracksData = (MyTuple<long, long, Vector3D, Vector3D, double>)data;
        //            if (!targetManager.TargetExists(targetTracksData.Item2) || clock - targetManager.GetTarget(targetTracksData.Item2).LastDetectedClock >= targetSlippedTicks)
        //            {
        //                TargetData targetData = new TargetData();
        //                targetData.EntityId = targetTracksData.Item2;
        //                targetData.Position = targetTracksData.Item3;
        //                targetData.Velocity = targetTracksData.Item4;
        //                targetManager.UpdateTarget(targetData, clock - 1, false);
        //
        //                if (targetTracksData.Item5 > 0)
        //                {
        //                    PDCTarget target = targetManager.GetTarget(targetData.EntityId);
        //                    if (target != null && target.TargetSizeSq == 0)
        //                    {
        //                        target.TargetSizeSq = targetTracksData.Item5;
        //                    }
        //                }
        //            }
        //        }
        //    }
        //}
        //
        //double ComputePriority(double shipRadius, Vector3D shipPosition, Vector3D shipVelocity, PDCTarget target)
        //{
        //    Vector3D rangeVector = target.Position - shipPosition;
        //    double priorityValue = rangeVector.Length();
        //
        //    PlaneD plane;
        //    if (shipVelocity.LengthSquared() < 0.01)
        //    {
        //        plane = new PlaneD(shipPosition, Vector3D.Normalize(rangeVector));
        //    }
        //    else
        //    {
        //        plane = new PlaneD(shipPosition, shipPosition + shipVelocity, shipPosition + rangeVector.Cross(shipVelocity));
        //    }
        //    Vector3D intersectPoint = plane.Intersection(ref target.Position, ref target.Velocity);
        //    Vector3D targetTravelVector = intersectPoint - target.Position;
        //
        //    if (targetTravelVector.Dot(ref target.Velocity) < 0)
        //    {
        //        priorityValue += priorityDowngradeConstant * 4;
        //    }
        //    else
        //    {
        //        double t = Math.Sqrt(targetTravelVector.LengthSquared() / target.Velocity.LengthSquared());
        //        if ((intersectPoint - (shipPosition + (shipVelocity * t))).LengthSquared() > shipRadius * shipRadius)
        //        {
        //            priorityValue += priorityDowngradeConstant * 2;
        //        }
        //        else if (target.TargetSizeSq <= 0)
        //        {
        //            priorityValue += priorityDowngradeConstant;
        //        }
        //        else if (target.TargetSizeSq < minTargetSizePriority * minTargetSizePriority)
        //        {
        //            priorityValue += priorityDowngradeConstant * 3;
        //        }
        //    }
        //
        //    if (target.CheckTargetSizeSq > target.TargetSizeSq)
        //    {
        //        priorityValue += priorityDowngradeConstant * Math.Max(target.PDCTargetedCount, 1);
        //    }
        //    else
        //    {
        //        priorityValue += priorityDowngradeConstant * Math.Min(target.PDCTargetedCount, 1);
        //    }
        //
        //    return priorityValue;
        //}

    }
}
