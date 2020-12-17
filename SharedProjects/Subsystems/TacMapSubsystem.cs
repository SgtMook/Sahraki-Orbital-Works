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
            if (command == "togglenorth") AlignWithNorth = !AlignWithNorth;
        }

        public void DeserializeSubsystem(string serialized)
        {
        }

        public string GetStatus()
        {
            return DebugBuilder.ToString();
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
                panel.Value.ScriptBackgroundColor = new Color(1, 0, 0, 0);
            }

            MapScale = MapOnScreenSizeMeters / MapSize;
        }

        public void Update(TimeSpan timestamp, UpdateFrequency updateFlags)
        {
            runs++;

            var a = Turret.Azimuth;
            var b = Turret.Elevation;
            var TurretAimRay = Math.Sin(a) * Math.Cos(b) * Turret.WorldMatrix.Left + Math.Cos(a) * Math.Cos(b) * Turret.WorldMatrix.Forward + Math.Sin(b) * Turret.WorldMatrix.Up;

            var WallPlane = new Plane(Panels[Vector2I.Zero].GetPosition() - Panels[Vector2I.Zero].WorldMatrix.Backward, Panels[Vector2I.Zero].WorldMatrix.Backward);
            Vector3 Intersection;
            TrigHelpers.PlaneIntersection(WallPlane, Turret.GetPosition() + Turret.WorldMatrix.Up * 0.579 + Turret.WorldMatrix.Backward * 0.068, TurretAimRay, out Intersection);

            var CursorDirection = Intersection - Panels[Vector2I.Zero].GetPosition() + Panels[Vector2I.Zero].WorldMatrix.Backward * 1.25 + Panels[Vector2I.Zero].WorldMatrix.Right * 1.25;
            var CursorDist = CursorDirection.Length();
            CursorDirection.Normalize();

            // LCD wall configuration:
            // |  |    |
            // |  |    |
            // |  |    |

            // Cursor position, originating in the middle of the LCD wall, with unit in meters
            var CursorPos = Vector3D.TransformNormal(CursorDirection, MatrixD.Transpose(Panels[Vector2I.Zero].WorldMatrix)) * CursorDist; 
            var CursorPos2D = new Vector2((float)CursorPos.X, (float)CursorPos.Y);
            
            if (runs % 10 == 0)
            {
                DebugBuilder.Clear();
                SinViewDegree = Math.Sin(ViewAngleDegrees * Math.PI / 180);
                CosViewDegree = Math.Cos(ViewAngleDegrees * Math.PI / 180);

                SpriteScratchpad.Clear();

                AddSprite("CircleHollow", new Vector2(0, 0), new Vector2(5, 5 * (float)CosViewDegree));
                for (int i = 10000; i <= MapSize; i += 10000)
                {
                    var CircleSize = MapOnScreenSizeMeters * PixelsPerMeter * i / MapSize;
                    AddSprite("CircleHollow", new Vector2(0, 0), new Vector2(CircleSize, CircleSize * (float)CosViewDegree), new Color(1f, 0.4f, 0f, 0.005f));
                }

                var gravDir = Controller.GetNaturalGravity();

                // Create map matrix
                if (gravDir != Vector3D.Zero)
                {
                    // Within gravity, align with gravity = down
                    // Either use controller's forward direction or north as forward
                    gravDir.Normalize();
                    var flatNorthDir = new Vector3D(0, 1, 0) - VectorHelpers.VectorProjection(new Vector3D(0, 1, 0), gravDir);
                    flatNorthDir.Normalize();
                    var flatForwardDir = AlignWithNorth ? flatNorthDir : (Controller.WorldMatrix.Forward - VectorHelpers.VectorProjection(AlignWithNorth ? new Vector3D(0, 1, 0) : Controller.WorldMatrix.Forward, gravDir));
                    flatForwardDir.Normalize();
                    var flatLeftDir = Vector3D.Cross(flatForwardDir, gravDir);

                    MapMatrix.Up = -gravDir;
                    MapMatrix.Forward = flatForwardDir;
                    MapMatrix.Left = flatLeftDir;

                    var localNorthDir = Vector3D.TransformNormal(flatNorthDir, MatrixD.Transpose(MapMatrix));
                    var localWestDir = Vector3D.Cross(localNorthDir, new Vector3D(0, -1, 0));

                    AddTextSprite("N", LocalCoordsToMapPosition(localNorthDir * MapSize * 0.5, true), 4);
                    AddTextSprite("S", LocalCoordsToMapPosition(-localNorthDir * MapSize * 0.5, true), 4);
                    AddTextSprite("W", LocalCoordsToMapPosition(localWestDir * MapSize * 0.5, true), 4);
                    AddTextSprite("E", LocalCoordsToMapPosition(-localWestDir * MapSize * 0.5, true), 4);
                }
                else
                {
                    // Else... idk just align with world axis?
                    MapMatrix = MatrixD.Identity;
                }

                var intelItems = IntelProvider.GetFleetIntelligences(timestamp);
                DebugBuilder.AppendLine(intelItems.Count.ToString());
                foreach (var kvp in intelItems)
                {
                    Vector3D localCoords;
                    Vector2 flatPosition, altitudePosition;
                    GetMapCoordsFromIntel(kvp.Value, timestamp, out localCoords, out flatPosition, out altitudePosition);



                    AddFleetIntelToMap(kvp.Value, timestamp, localCoords, flatPosition, altitudePosition);
                }

                // var cursor = new MySprite(SpriteType.TEXTURE, "White screen", size: new Vector2(60f, 60f), color: Turret.IsShooting ? Color.Green : Color.Red);
                // cursor.Position = CursorPos2D;
                // SpriteScratchpad.Add(cursor);

                // AddSprite("White screen", new Vector2(1, 1), new Vector2(60, 60));
                // AddSprite("Circle", new Vector2(1, 0), new Vector2(60, 60));
                // AddSprite("CircleHollow", new Vector2(1, -1), new Vector2(60, 30));

                var text = Turret.IsShooting ? "[><]" : "><";
                AddTextSprite(text, CursorPos2D, 2, "Debug");

                foreach (var kvp in Panels)
                {
                    DrawSpritesForPanel(kvp.Key);
                }
            }
        }

        Vector2 AddFleetIntelToMap(IFleetIntelligence intel, TimeSpan localTime, Vector3D localCoords, Vector2 flatPosition, Vector2 altitudePosition)
        {
            var color = Color.White;
            if (intel.IntelItemType == IntelItemType.Friendly)
            {
                if ((((FriendlyShipIntel)intel).AgentStatus & AgentStatus.DockedAtHome) != 0)
                    return Vector2.PositiveInfinity;
                color = Color.Blue;
            }
            else if (intel.IntelItemType == IntelItemType.Enemy)
            {
                color = Color.Red;
                var lastDetectedTime = localTime + IntelProvider.CanonicalTimeDiff - ((EnemyShipIntel)intel).LastValidatedCanonicalTime;
                if (lastDetectedTime > TimeSpan.FromSeconds(4))
                    color = new Color(1f, 0f, 0f, 0.002f);
                else if (lastDetectedTime > TimeSpan.FromSeconds(3))
                    color = new Color(1f, 0f, 0f, 0.005f);
                else if (lastDetectedTime > TimeSpan.FromSeconds(2))
                    color = new Color(1f, 0f, 0f, 0.007f);
            }
            else if (intel.IntelItemType == IntelItemType.Waypoint)
            {

            }
            else
            {
                return Vector2.PositiveInfinity;
            }

            var middlePosition = (altitudePosition + flatPosition) * 0.5f;
            var lineLength = MapScale * (float)localCoords.Y * (float)SinViewDegree;

            if (intel.ID != Controller.CubeGrid.EntityId)
            {
                AddSprite("CircleHollow", flatPosition, new Vector2(30, 30 * (float)CosViewDegree), color);
                AddSprite("CircleHollow", altitudePosition, new Vector2(20, 20), color);
                AddSprite("SquareSimple", middlePosition, new Vector2(2, lineLength * PixelsPerMeter), color);
            }

            if (intel.IntelItemType == IntelItemType.Friendly)
            {
                AddSprite("CircleHollow", altitudePosition, new Vector2(2 * ScannerRange * MapScale * PixelsPerMeter), color);

                if (Math.Abs((float)localCoords.Y) < ScannerRange)
                {
                    AddSprite("CircleHollow", flatPosition, new Vector2(2 * (float)Math.Sqrt(ScannerRange * ScannerRange - localCoords.Y * localCoords.Y) * MapScale * PixelsPerMeter, 2 * (float)(Math.Sqrt(ScannerRange * ScannerRange - localCoords.Y * localCoords.Y) * MapScale * PixelsPerMeter * CosViewDegree)), color);
                }
            }

            return altitudePosition;
        }

        void GetMapCoordsFromIntel(IFleetIntelligence intel, TimeSpan localTime, out Vector3D localCoords, out Vector2 flatPosition, out Vector2 altitudePosition)
        {
            var worldCoords = intel.GetPositionFromCanonicalTime(localTime + IntelProvider.CanonicalTimeDiff);

            localCoords = WorldCoordsToLocalCoords(worldCoords);
            flatPosition = LocalCoordsToMapPosition(localCoords, true);
            altitudePosition = LocalCoordsToMapPosition(localCoords);
        }

        Vector3D WorldCoordsToLocalCoords(Vector3D worldCoords)
        {
            var worldDir = worldCoords - Controller.GetPosition();
            var worldDist = worldDir.Length();
            worldDir.Normalize();
            var localCoords = Vector3D.TransformNormal(worldDir, Matrix.Transpose(MapMatrix));
            localCoords *= worldDist;
            return localCoords;
        }

        private Vector2 LocalCoordsToMapPosition(Vector3D localCoords, bool flat = false)
        {
            return new Vector2(MapScale * (float)localCoords.X, -MapScale * (float)localCoords.Z * (float)CosViewDegree + (flat ? 0 : (MapScale * (float)localCoords.Y * (float)SinViewDegree)));
        }

        void AddSprite(string spriteType, Vector2 wallPosition, Vector2 size, Color? color = null)
        {
            var sprite = MySprite.CreateSprite(spriteType, wallPosition, size);
            sprite.Color = color == null ? Color.White : color;
            SpriteScratchpad.Add(sprite);
        }

        void AddTextSprite(string text, Vector2 position, int fontSize = 1, string font = "Debug")
        {
            TextMeasureBuilder.Clear();
            TextMeasureBuilder.Append(text);
            var sprite = MySprite.CreateText(text, font, Color.White, fontSize);
            sprite.Size = Panels[Vector2I.Zero].MeasureStringInPixels(TextMeasureBuilder, sprite.FontId, sprite.RotationOrScale);
            position.Y += sprite.Size.Value.Y * 0.5f / PixelsPerMeter;
            sprite.Position = position;
            SpriteScratchpad.Add(sprite);
        }

        Vector2 WallPositionToScreenPosition(Vector2I screenIndex, Vector2 wallPositionMeters)
        {
            var screenCenterMeters = ((Vector2)screenIndex) * 2.5f;
            if (Panels[screenIndex].SurfaceSize.X == 1024)
                screenCenterMeters.X += 1.25f;
            var screenPosition = (wallPositionMeters - screenCenterMeters) * PixelsPerMeter;
            screenPosition.Y *= -1; // Invert Y, because that's how the screen renders sprites
            screenPosition += Panels[screenIndex].SurfaceSize * 0.5f;
            return screenPosition;
        }

        void DrawSpritesForPanel(Vector2I screenIndex)
        {
            using (var frame = Panels[screenIndex].DrawFrame())
            {
                for (int i = 0; i < SpriteScratchpad.Count; i++)
                {
                    var spr = SpriteScratchpad[i];

                    var originalPos = spr.Position.Value;

                    spr.Position = WallPositionToScreenPosition(screenIndex, originalPos);

                    if (spr.Position.Value.X + spr.Size.Value.X > 0 &&
                        spr.Position.Value.X - spr.Size.Value.X < Panels[screenIndex].SurfaceSize.X &&
                        spr.Position.Value.Y + spr.Size.Value.Y > 0 &&
                        spr.Position.Value.Y - spr.Size.Value.Y < Panels[screenIndex].SurfaceSize.Y)
                        frame.Add(spr);

                    spr.Position = originalPos;
                }
            }
        }

        MyGridProgram Program;
        StringBuilder DebugBuilder = new StringBuilder();
        StringBuilder TextMeasureBuilder = new StringBuilder();
        IMyTerminalBlock ProgramReference;

        int MapSize = 30000; // In meters
        int PixelsPerMeter = 205; // 512 / 2.5
        int ViewAngleDegrees = 60;
        float MapOnScreenSizeMeters = 6.5f;
        int ScannerRange = 1000;
        float MapScale = 0;

        int runs = 0;

        // Required Subsystems
        IIntelProvider IntelProvider;

        public TacMapSubsystem(IIntelProvider intelProvider)
        {
            IntelProvider = intelProvider;
        }

        List<MySprite> SpriteScratchpad = new List<MySprite>();
        MatrixD MapMatrix = new MatrixD();
        bool AlignWithNorth = false;
        double SinViewDegree;
        double CosViewDegree;

        // Components
        IMyLargeTurretBase Turret;
        IMyShipController Controller;
        Dictionary<Vector2I, IMyTextPanel> Panels = new Dictionary<Vector2I, IMyTextPanel>();

        void GetParts()
        {
            Program.GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(null, CollectParts);
        }

        bool CollectParts(IMyTerminalBlock block)
        {
            if (block.CubeGrid.EntityId != ProgramReference.CubeGrid.EntityId) return false;
            if (block is IMyLargeTurretBase) Turret = (IMyLargeTurretBase)block;
            if (block is IMyShipController) Controller = (IMyShipController)block;
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
