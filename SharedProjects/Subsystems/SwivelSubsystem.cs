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
    public class SwivelSubsystem : ISubsystem
    {
        #region ISubsystem
        public UpdateFrequency UpdateFrequency { get; set; }

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
            GetParts();
        }

        public void Update(TimeSpan timestamp, UpdateFrequency updateFlags)
        {
            if (controlIntercepter.InterceptControls)
            {
                UpdateFrequency = UpdateFrequency.Update1;
                pitch.TargetVelocityRPM = controlIntercepter.Controller.RotationIndicator[0] * 0.3f;
                yaw.TargetVelocityRPM = controlIntercepter.Controller.RotationIndicator[1] * 0.3f;
            }
            else
            {
                UpdateFrequency = UpdateFrequency.Update10;
                pitch.TargetVelocityRPM = 0;
                yaw.TargetVelocityRPM = 0;
            }
        }
        #endregion

        public SwivelSubsystem(string tag, IControlIntercepter controlIntercepter)
        {
            this.tag = tag;
            this.controlIntercepter = controlIntercepter;
            UpdateFrequency = UpdateFrequency.Update10;
        }

        private string tag;
        private IControlIntercepter controlIntercepter;

        MyGridProgram Program;

        IMyMotorStator yaw;
        IMyMotorStator pitch;

        void GetParts()
        {
            Program.GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(null, CollectParts);
        }

        private bool CollectParts(IMyTerminalBlock block)
        {
            if (!Program.Me.IsSameConstructAs(block)) return false;
            if (block is IMyMotorStator)
            {
                var rotor = (IMyMotorStator)block;
                if (rotor.CustomName.StartsWith($"{tag} Yaw"))
                    yaw = rotor;
                else if (rotor.CustomName.StartsWith($"{tag} Pitch"))
                    pitch = rotor;
            }

            return false;
        }
    }
}
