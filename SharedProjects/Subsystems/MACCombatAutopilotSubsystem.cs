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
    public class MACCombatAutopilotSubsystem : ISubsystem
    {
        #region ISubsystem
        public UpdateFrequency UpdateFrequency => UpdateFrequency.Update1;
        
        public void Command(TimeSpan timestamp, string command, object argument)
        {
        }

        public void CommandV2(TimeSpan timestamp, CommandLine command)
        {
            if (command.Argument(0) == "toggle")
            {
                active = !active;
            }
        }

        public void DeserializeSubsystem(string serialized)
        {
        }
        
        public string GetStatus()
        {
            return statusBuilder.ToString();
        }
        
        public string SerializeSubsystem()
        {
            return string.Empty;
        }

        public void Setup(ExecutionContext context, string name)
        {
            Context = context;

            ParseConfigs();

            GetParts();
        }

        void GetParts()
        {
            Context.Terminal.GetBlocksOfType<IMyTerminalBlock>(null, CollectParts);
        }

        bool CollectParts(IMyTerminalBlock block)
        {
            if (block.CubeGrid.EntityId != Context.Reference.CubeGrid.EntityId) return false;
            if (block.CustomName.Contains("[HCAP] |")) Indicator = block;
            if (block.CustomName.Contains("[MAC]")) Gun = block;

            return false;
        }

        public void Update(TimeSpan timestamp, UpdateFrequency updateFlags)
        {
            runs++;

            if (runs % 5 == 0)
            {
                statusBuilder.Clear();
                indicatorBuilder.Clear();

                bool firing = false;

                var index = Indicator.CustomName.IndexOf('|') + 1;
                if (index <= 0) return;
                string originalName = Indicator.CustomName.Substring(0, index);

                if (Context.WCAPI == null) return;

                var gravDir = IntelProvider.Controller.GetNaturalGravity();
                var gravStr = gravDir.Normalize();

                if (!active)
                {
                    indicatorBuilder.Append("OFF");

                    NeutralControl(gravDir);
                }
                else
                {
                    var target = Context.WCAPI.GetAiFocus(Context.Reference.CubeGrid.EntityId);
                    if (!target.HasValue || target.Value.IsEmpty())
                    {
                        indicatorBuilder.Append("NO TGT");
                        NeutralControl(gravDir);
                    }
                    else
                    {
                        var velocity = IntelProvider.Controller.GetShipVelocities().LinearVelocity;
                        var relativeVector = target.Value.Position - IntelProvider.Controller.WorldMatrix.Translation;
                        var relativeVelocity = target.Value.Velocity - velocity;

                        var planarVector = relativeVector - VectorHelpers.VectorProjection(relativeVector, gravDir);
                        planarVector.Normalize();

                        var targetPos = target.Value.Position - gravDir * range + planarVector * 50;

                        statusBuilder.AppendLine((gravDir * range).ToString());

                        Drive.Move(targetPos);
                        if ((targetPos - IntelProvider.Controller.WorldMatrix.Translation).Length() < 100)
                            Drive.Move(Vector3D.Zero);
                        Drive.Drift(target.Value.Velocity);

                        var VerticalAngleToTarget = VectorHelpers.VectorAngleBetween(gravDir, relativeVector);

                        planarVector -= gravDir * Math.Tan(VerticalAngleToTarget > 20 * Math.PI / 180 ? 0 : VerticalAngleToTarget);

                        Drive.Turn(planarVector);
                        Drive.Spin(-gravDir);

                        indicatorBuilder.Append("TGT:");
                        indicatorBuilder.Append(target.Value.Name.Length > 4 ? target.Value.Name.Substring(0, 4) : target.Value.Name);
                        indicatorBuilder.Append(" - DIST:");
                        indicatorBuilder.Append(relativeVector.Length().ToString("F"));
                        indicatorBuilder.Append(" - STUS:");

                        // Fire control

                        if (VerticalAngleToTarget > 20 * Math.PI / 180)
                        {
                            indicatorBuilder.Append(" APPROACH");
                        }
                        else
                        {
                            indicatorBuilder.Append(" AIM ");
                            var angleToTargetDegrees = VectorHelpers.VectorAngleBetween(Gun.WorldMatrix.Forward, relativeVector) * 180 / Math.PI;
                            indicatorBuilder.Append(angleToTargetDegrees.ToString("F"));
                            
                            if (angleToTargetDegrees < 0.5 && Context.WCAPI.IsWeaponReadyToFire(Gun))
                            {
                                indicatorBuilder.Clear();
                                indicatorBuilder.Append("FIRING!");
                                firing = true;
                            }
                        }
                    }
                }
                Indicator.CustomName = originalName + " " + indicatorBuilder.ToString();
                Context.WCAPI.ToggleWeaponFire(Gun, firing, true);
            }
        }

        private void NeutralControl(Vector3D gravDir)
        {
            Drive.Move(Vector3D.Zero);
            Vector3D turnTarget = Drive.Controller.WorldMatrix.Forward + Drive.Controller.RotationIndicator.Y * Drive.Controller.WorldMatrix.Right * 0.2;
            turnTarget = turnTarget - VectorHelpers.VectorProjection(turnTarget, gravDir);
            Drive.Turn(turnTarget);
            Drive.Drift(Vector3D.Zero);
            Drive.Spin(-gravDir);
        }
        #endregion
        ExecutionContext Context;

        StringBuilder statusBuilder = new StringBuilder();
        StringBuilder indicatorBuilder = new StringBuilder();

        MyIni iniParser = new MyIni();

        int runs = 0;

        int range;
        int maxRange;
        int projectileSpeed;

        IAutopilot Drive;
        IIntelProvider IntelProvider;
        IMyTerminalBlock Indicator;
        IMyTerminalBlock Gun;

        bool active = false;

        public MACCombatAutopilotSubsystem(IAutopilot drive, IIntelProvider intelProvider)
        {
            Drive = drive;
            IntelProvider = intelProvider;
        }

        // [MACCAP]
        // range = 2550
        // maxRange = 2700
        void ParseConfigs()
        {
            iniParser.Clear();
            MyIniParseResult result;
            if (!iniParser.TryParse(Context.Reference.CustomData, out result))
                return;

            range = iniParser.Get("MACCAP", "range").ToInt32(2550);
            maxRange = iniParser.Get("MACCAP", "maxRange").ToInt32(2700);
        }
    }
}
