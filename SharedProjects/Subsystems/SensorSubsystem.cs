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

namespace SharedProjects.Subsystems
{
    public class SensorSubsystem : ISubsystem
    {
        public int UpdateFrequency { get; private set; }

        IMyShipController controller;

        IMyMotorStator yaw;
        IMyMotorStator pitch;

        MyGridProgram Program;

        IMyTextPanel panelLeft;
        IMyTextPanel panelRight;
        IMyTextPanel panelMiddle;

        public void Command(string command, object argument)
        {
        }

        public void DeserializeSubsystem(string serialized)
        {
        }

        public string GetStatus()
        {
            return (yaw != null && pitch != null && controller != null) ? "AOK" : "ERR";
        }

        public string SerializeSubsystem()
        {
            return string.Empty;
        }

        public void Setup(MyGridProgram program, SubsystemManager manager)
        {
            Program = program;
            GetParts();
            UpdateFrequency = 1;
        }

        void GetParts()
        {
            Program.GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(null, CollectParts);
        }

        private bool CollectParts(IMyTerminalBlock block)
        {
            if (!Program.Me.IsSameConstructAs(block)) return false;

            if (block is IMyShipController)
                controller = (IMyShipController)block;

            if (block is IMyMotorStator)
            {
                var rotor = (IMyMotorStator)block;
                if (rotor.CustomName.StartsWith("[S-Y]"))
                    yaw = rotor;
                else if (rotor.CustomName.StartsWith("[S-P]"))
                    pitch = rotor;
            }

            if (block is IMyTextPanel && block.CustomName.StartsWith("[S-SM]"))
            {
                panelMiddle = (IMyTextPanel)block;
            }

            return false;
        }

        public void Update()
        {
            pitch.TargetVelocityRPM = controller.RotationIndicator[0]*0.3f;
            yaw.TargetVelocityRPM = controller.RotationIndicator[1]*0.3f;

            using (var frame = panelMiddle.DrawFrame())
            {
                var crosshairs = new MySprite(SpriteType.TEXTURE, "Cross", size: new Vector2(20f, 20f), color: new Color(1,1,1,0.1f));
                //crosshairs.Position = panelMiddle.TextureSize / 2f + (new Vector2(50f) / 2f);
                panelMiddle.ScriptBackgroundColor = new Color(1, 0, 0, 0);
                frame.Add(crosshairs);
            }
        }
    }
}
