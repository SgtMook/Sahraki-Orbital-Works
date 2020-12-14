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
    public class TacMapSubsystem : ISubsystem
    {
        public UpdateFrequency UpdateFrequency => UpdateFrequency.Update1;

        public void Command(TimeSpan timestamp, string command, object argument)
        {
        }

        public void DeserializeSubsystem(string serialized)
        {
        }

        public string GetStatus()
        {
            return "";
        }

        public string SerializeSubsystem()
        {
            return "";
        }

        public void Setup(MyGridProgram program, string name, IMyTerminalBlock reference = null)
        {
            ProgramReference = reference;
            if (ProgramReference == null) ProgramReference = program.Me;
            Program = program;
            GetParts();

            Turret.EnableIdleRotation = false;
            Turret.SyncEnableIdleRotation();

            foreach (var panel in Panels)
            {
                panel.Value.ContentType = ContentType.SCRIPT;
            }
        }

        public void Update(TimeSpan timestamp, UpdateFrequency updateFlags)
        {
            DebugBuilder.Clear();
            DebugBuilder.AppendLine(Turret.Azimuth.ToString());
            DebugBuilder.AppendLine(Turret.Elevation.ToString());
            DebugBuilder.AppendLine(Turret.IsShooting.ToString());

            var a = Turret.Azimuth;
            var b = Turret.Elevation;
            var TurretAimRay = Math.Sin(a) * Math.Cos(b) * Turret.WorldMatrix.Left + Math.Cos(a) * Math.Cos(b) * Turret.WorldMatrix.Forward + Math.Sin(b) * Turret.WorldMatrix.Up;

            var WallPlane = new Plane(Panels[Vector2I.Zero].GetPosition() - Panels[Vector2I.Zero].WorldMatrix.Backward, Panels[Vector2I.Zero].WorldMatrix.Backward);
            Vector3 Intersection;
            TrigHelpers.PlaneIntersection(WallPlane, Turret.GetPosition() + Turret.WorldMatrix.Up * 0.579 + Turret.WorldMatrix.Backward * 0.068, TurretAimRay, out Intersection);
            DebugBuilder.AppendLine((Intersection - Panels[Vector2I.Zero].GetPosition() + Panels[Vector2I.Zero].WorldMatrix.Backward * 1.25).Length().ToString());

            var CursorDirection = Intersection - Panels[Vector2I.Zero].GetPosition() + Panels[Vector2I.Zero].WorldMatrix.Backward * 1.25;
            var CursorDist = CursorDirection.Length();
            CursorDirection.Normalize();

            var CursorPos = Vector3D.TransformNormal(CursorDirection, MatrixD.Transpose(Panels[Vector2I.Zero].WorldMatrix)) * CursorDist;

            DebugBuilder.AppendLine(CursorPos.ToString());

            var sprite = new MySprite(SpriteType.TEXTURE, "Cross", size: new Vector2(60f, 60f), color: Turret.IsShooting ? Color.Green : Color.Red);
            sprite.Position = new Vector2((float)(CursorPos.X * 204.8 + 512), (float)(-CursorPos.Y * 204.8 + 256));
            SpriteScratchpad.Clear();
            SpriteScratchpad.Add(sprite);

            foreach (var kvp in Panels)
            {
                using (var frame = kvp.Value.DrawFrame())
                {
                    foreach (var spr in SpriteScratchpad)
                    {
                        frame.Add(spr);
                    }
                }
            }
        }

        MyGridProgram Program;
        StringBuilder DebugBuilder = new StringBuilder();
        IMyTerminalBlock ProgramReference;

        List<MySprite> SpriteScratchpad = new List<MySprite>();

        // Components
        IMyLargeTurretBase Turret;
        Dictionary<Vector2I, IMyTextPanel> Panels = new Dictionary<Vector2I, IMyTextPanel>();

        void GetParts()
        {
            Program.GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(null, CollectParts);
        }

        bool CollectParts(IMyTerminalBlock block)
        {
            if (block.CubeGrid.EntityId != ProgramReference.CubeGrid.EntityId) return false;
            if (block is IMyLargeTurretBase) Turret = (IMyLargeTurretBase)block;
            if (block is IMyTextPanel)
            {
                var startIndex = block.CustomName.IndexOf("[");
                var endIndex = block.CustomName.IndexOf("]");
                if (startIndex < endIndex && startIndex > -1)
                {
                    var tagStrings = block.CustomName.Substring(startIndex + 1, endIndex - startIndex - 1).Split(',');
                    var coords = new Vector2I(int.Parse(tagStrings[0]), int.Parse(tagStrings[1]));
                    Panels.Add(coords, (IMyTextPanel)block);
                }
            }

            return false;
        }
    }
}
