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
    partial class Program : MyGridProgram
    {
        IMyBroadcastListener Listener;
        public Program()
        {
            Listener = IGC.RegisterBroadcastListener("ECMPROJECT");
            Listener.SetMessageCallback("ECMPROJECT");
            Runtime.UpdateFrequency = UpdateFrequency.Update10;
        }

        public void Save()
        {
        }

        public void Main(string argument, UpdateType updateSource)
        {
            int count = 0;
            while (Listener.HasPendingMessage)
            {
                object data = Listener.AcceptMessage().Data;
                if (data is MyTuple<Vector3D, Vector3D>)
                {
                    count++;
                    MyTuple<Vector3D, Vector3D> targetTracksData = (MyTuple<Vector3D, Vector3D>)data;
                    IGC.SendBroadcastMessage("IGCMSG_TR_TK", MyTuple.Create((long)0, (long)count, targetTracksData.Item1, targetTracksData.Item2, (double)500));
                }
            }


            Random rand = new Random();
            //IGC.SendBroadcastMessage("IGCMSG_TR_TK", MyTuple.Create((long)0, (long)1, Me.WorldMatrix.Translation + new Vector3D(400, 0, 0), Vector3D.Zero, (double)500));
            //IGC.SendBroadcastMessage("IGCMSG_TR_TK", MyTuple.Create((long)0, (long)2, Me.WorldMatrix.Translation + new Vector3D(-400, 0, 0), Vector3D.Zero, (double)500));
            //IGC.SendBroadcastMessage("IGCMSG_TR_TK", MyTuple.Create((long)0, (long)3, Me.WorldMatrix.Translation + new Vector3D(0, 400, 0), Vector3D.Zero, (double)500));
            //IGC.SendBroadcastMessage("IGCMSG_TR_TK", MyTuple.Create((long)0, (long)4, Me.WorldMatrix.Translation + new Vector3D(0, -400, 0), Vector3D.Zero, (double)500));
            //IGC.SendBroadcastMessage("IGCMSG_TR_TK", MyTuple.Create((long)0, (long)5, Me.WorldMatrix.Translation + new Vector3D(0, 0, 400), Vector3D.Zero, (double)500));
            //IGC.SendBroadcastMessage("IGCMSG_TR_TK", MyTuple.Create((long)0, (long)6, Me.WorldMatrix.Translation + new Vector3D(0, 0, -400), Vector3D.Zero, (double)500));
            IGC.SendBroadcastMessage("IGCMSG_TR_TK", MyTuple.Create((long)0, (long)Me.CubeGrid.EntityId, Me.WorldMatrix.Translation, Vector3D.Zero, (double)1));
        }
    }
}
