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

    public class HummingbirdControlSubsystem : ISubsystem
    {
        StringBuilder statusbuilder = new StringBuilder();
        // StringBuilder debugBuilder = new StringBuilder();

        Hummingbird MyHummingbird;

        #region ISubsystem
        public void Command(TimeSpan timestamp, string command, object argument)
        {
            if (command == "SetTarget") SetTarget(ParseGPS((string)argument));
            if (command == "SetDest") SetDest(ParseGPS((string)argument));
        }

        public void Setup(MyGridProgram program, string name)
        {
            Program = program;

            UpdateFrequency = UpdateFrequency.Update1;

            MyHummingbird = Hummingbird.GetHummingbird(Program.Me, Program.GridTerminalSystem.GetBlockGroupWithName(Hummingbird.GroupName));

        }

        public void Update(TimeSpan timestamp, UpdateFrequency updateFlags)
        {
            MyHummingbird.Update();
        }

        public string GetStatus()
        {
            return MyHummingbird.Status;
        }

        public MyGridProgram Program { get; private set; }
        public UpdateFrequency UpdateFrequency { get; set; }

        public string SerializeSubsystem()
        {
            return string.Empty;
        }

        public void DeserializeSubsystem(string serialized)
        {
        }

        void SetTarget(Vector3D argument)
        {
            MyHummingbird.SetTarget(argument);
        }
        void SetDest(Vector3D argument)
        {
            MyHummingbird.SetDest(argument);
        }
        Vector3D ParseGPS(string s)
        {
            var split = s.Split(':');
            return new Vector3(float.Parse(split[2]), float.Parse(split[3]), float.Parse(split[4]));
        }

        #endregion
    }
}