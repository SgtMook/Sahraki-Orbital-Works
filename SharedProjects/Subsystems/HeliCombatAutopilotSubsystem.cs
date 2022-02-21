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
    public class HeliCombatAutopilotSubsystem : ISubsystem
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

            return false;
        }

        public void Update(TimeSpan timestamp, UpdateFrequency updateFlags)
        {
            runs++;

            if (runs % 10 == 0)
            {
                indicatorBuilder.Clear();

                var index = Indicator.CustomName.IndexOf('|') + 1;
                if (index <= 0) return;
                string originalName = Indicator.CustomName.Substring(0, index);

                if (Context.WCAPI == null) return;
                var rotationIndicator = Vector3.NegativeInfinity;

                var movementIndicator = Vector3.NegativeInfinity;

                if (!active)
                {
                    indicatorBuilder.Append("OFF");
                }
                else
                {
                    var target = Context.WCAPI.GetAiFocus(Context.Reference.CubeGrid.EntityId);
                    if (target == null)
                        indicatorBuilder.Append("NO TGT");
                    else
                    {
                        var gravDir = IntelProvider.Controller.GetNaturalGravity();
                        var gravStr = gravDir.Normalize();
                        var velocity = IntelProvider.Controller.GetShipVelocities().LinearVelocity;

                        var relativeVector = target.Value.Position - IntelProvider.Controller.WorldMatrix.Translation;
                        var relativeVelocity = target.Value.Velocity - velocity;

                        var heightSquared = VectorHelpers.VectorProjection(relativeVector, gravDir).LengthSquared();
                        var relativeplanarDir = relativeVector - VectorHelpers.VectorProjection(relativeVector, gravDir);
                        var relativeplanarDist = relativeplanarDir.Normalize();
                        var relativePlanarVelocity = relativeVelocity - VectorHelpers.VectorProjection(relativeVelocity, gravDir);

                        // Attack dir
                        var relativeAttackPoint = AttackHelpers.GetAttackPoint(relativeVelocity, relativeVector, projectileSpeed);
                        var attackPlanarVector = relativeAttackPoint - VectorHelpers.VectorProjection(relativeAttackPoint, gravDir);

                        var attackPlanarLeft = IntelProvider.Controller.WorldMatrix.Left - VectorHelpers.VectorProjection(IntelProvider.Controller.WorldMatrix.Left, gravDir);
                        attackPlanarLeft.Normalize();

                        var attackPlanarForward = IntelProvider.Controller.WorldMatrix.Forward - VectorHelpers.VectorProjection(IntelProvider.Controller.WorldMatrix.Forward, gravDir);
                        attackPlanarForward.Normalize();

                        var spinAngle = VectorHelpers.VectorAngleBetween(attackPlanarForward, attackPlanarVector) * Math.Sign(-attackPlanarLeft.Dot(attackPlanarVector));

                        rotationIndicator.Y = (float)spinAngle;

                        // Movement
                        var myPlanarVelocity = velocity - VectorHelpers.VectorProjection(relativeAttackPoint, gravDir);
                        var targetPlanarDist = Math.Sqrt(range * range - heightSquared);
                        var targetPlanarPoint = relativeplanarDir * (relativeplanarDist - targetPlanarDist);
                        var desiredRelativeVelocity = targetPlanarPoint * 0.5 + relativePlanarVelocity - myPlanarVelocity;
                        desiredRelativeVelocity.Normalize();

                        movementIndicator.X = (float)desiredRelativeVelocity.Dot(IntelProvider.Controller.WorldMatrix.Right);
                        movementIndicator.Z = (float)desiredRelativeVelocity.Dot(IntelProvider.Controller.WorldMatrix.Backward);

                        indicatorBuilder.Append("TGT:");
                        indicatorBuilder.Append(target.Value.Name.Substring(0, 4));
                        indicatorBuilder.Append(" - DIST:");
                        indicatorBuilder.Append(relativeVector.Length().ToString("F"));
                        indicatorBuilder.Append(" - ");
                        indicatorBuilder.Append(movementIndicator.X.ToString("F"));
                        indicatorBuilder.Append(",");
                        indicatorBuilder.Append(movementIndicator.Z.ToString("F"));
                        indicatorBuilder.Append(" -- ");
                        indicatorBuilder.Append(spinAngle.ToString("F"));
                    }
                }
                Indicator.CustomName = originalName + " " + indicatorBuilder.ToString();
                Drive.RotationIndicatorOverride = rotationIndicator;
                Drive.MoveIndicatorOverride = movementIndicator;
            }
        }
        #endregion
        ExecutionContext Context;

        StringBuilder statusBuilder = new StringBuilder();
        StringBuilder indicatorBuilder = new StringBuilder();

        MyIni iniParser = new MyIni();

        int runs = 0;

        int range;
        int projectileSpeed;

        HeliDriveSubsystem Drive;
        IIntelProvider IntelProvider;
        IMyTerminalBlock Indicator;

        bool active = false;

        public HeliCombatAutopilotSubsystem(HeliDriveSubsystem drive, IIntelProvider intelProvider)
        {
            Drive = drive;
            IntelProvider = intelProvider;
        }

        // [HeliCAP]
        // range = 1800
        // projectileSpeed = 700
        void ParseConfigs()
        {
            iniParser.Clear();
            if (!iniParser.TryParse(Context.Reference.CustomData))
                return;

            range = iniParser.Get("HeliCAP", "range").ToInt32(1800);
            projectileSpeed = iniParser.Get("HeliCAP", "projectileSpeed").ToInt32(700);
        }
    }
}
