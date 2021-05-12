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

        public void Setup(ExecutionContext context, string name)
        {
            Context = context;

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

            OnRelease = OnRelease || LastFrameMouseDown & !Turret.IsShooting;

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
            CursorPos = Vector3D.TransformNormal(CursorDirection, MatrixD.Transpose(Panels[Vector2I.Zero].WorldMatrix)) * CursorDist; 
            CursorPos2D = new Vector2((float)CursorPos.X, (float)CursorPos.Y);
            
            if (runs % 10 == 0)
            {
                DebugBuilder.Clear();
                SinViewDegree = Math.Sin(ViewAngleDegrees * Math.PI / 180);
                CosViewDegree = Math.Cos(ViewAngleDegrees * Math.PI / 180);

                SpriteScratchpad.Clear();
                int i;
                AddSprite("CircleHollow", new Vector2(0, 0), new Vector2(5, 5 * (float)CosViewDegree));
                for (i = 10000; i <= MapSize; i += 10000)
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

                SelectionCandidates.Clear();

                ClosestItemKey = MyTuple.Create(IntelItemType.NONE, (long)0);
                ClosestItemDist = 0.3f;

                LocalCoordsScratchpad.Clear();
                ScreenCoordsScratchpad.Clear();

                if (ActionMode == ActionSelectPosition)
                {
                    AddTextSprite("><", CursorPos2D, 1, "Debug");
                    if (OnRelease)
                    {
                        if (SelectFlatPos == Vector2.PositiveInfinity)
                        {
                            SelectFlatPos = CursorPos2D;
                        }
                        else
                        {
                            SelectedPosition = FlatPosToGlobalPos(SelectFlatPos, CursorPos2D);

                            SelectPositionCallback(timestamp);

                            SelectFlatPos = Vector2.PositiveInfinity;
                            ActionMode = ActionNone;
                        }
                        OnRelease = false;
                    }

                    if (SelectFlatPos != Vector2.PositiveInfinity)
                    {
                        var selectAltitudePos = SelectFlatPos;
                        selectAltitudePos.Y = CursorPos2D.Y;
                        var midPosition = (selectAltitudePos + SelectFlatPos) * 0.5f;
                        var lineLength = Math.Abs(selectAltitudePos.Y - SelectFlatPos.Y);
                        AddSprite("CircleHollow", selectAltitudePos, new Vector2(20, 20));

                        AddSprite("CircleHollow", SelectFlatPos, new Vector2(30, 30 * (float)CosViewDegree));
                        AddSprite("SquareSimple", midPosition, new Vector2(2, lineLength * PixelsPerMeter));
                    }
                }

                foreach (var kvp in intelItems)
                {
                    Vector3D localCoords;
                    Vector2 flatPosition, altitudePosition;

                    GetMapCoordsFromIntel(kvp.Value, timestamp, out localCoords, out flatPosition, out altitudePosition);

                    ScreenCoordsScratchpad.Add(flatPosition);
                    ScreenCoordsScratchpad.Add(altitudePosition);
                    LocalCoordsScratchpad.Add(localCoords);

                    DebugBuilder.AppendLine(localCoords.ToString());

                    var distToItem = (altitudePosition - CursorPos2D).Length();
                    if (distToItem < ClosestItemDist)
                    {
                        ClosestItemDist = distToItem;
                        ClosestItemKey = kvp.Key;
                    }
                }

                DrawContextMenu();

                if (ActionMode == ActionNone)
                {
                    SelectionCandidates.Add(ClosestItemKey);
                    if (OnRelease)
                    {
                        if (ClosestItemKey.Item1 == IntelItemType.NONE)
                        {
                            SelectedItems.Clear();
                        }
                        else if (ClosestItemKey.Item1 != IntelItemType.Waypoint)
                        {
                            if (SelectedItems.Count > 0 && SelectedItems[0].Item2 != ClosestItemKey.Item2)
                            {
                                SelectedItems.Clear();
                            }
                            SelectedItems.Add(ClosestItemKey);
                        }
                    }
                }

                i = 0;
                foreach (var kvp in intelItems)
                {
                    AddFleetIntelToMap(kvp.Value, timestamp, LocalCoordsScratchpad[i], ScreenCoordsScratchpad[2*i], ScreenCoordsScratchpad[2*i + 1]);
                    i++;
                }

                // var text = Turret.IsShooting ? "[><]" : "><";
                // AddTextSprite(text, CursorPos2D, 2, "Debug");

                foreach (var kvp in Panels)
                {
                    DrawSpritesForPanel(kvp.Key);
                }

                OnRelease = false;
            }

            LastFrameMouseDown = Turret.IsShooting;
        }

        void AddFleetIntelToMap(IFleetIntelligence intel, TimeSpan localTime, Vector3D localCoords, Vector2 flatPosition, Vector2 altitudePosition)
        {
            if (localCoords.Length() > MapSize) 
                return;

            var intelKey = MyTuple.Create(intel.Type, intel.ID);
            var color = Color.White;
            if (intel.Type == IntelItemType.Friendly)
            {
                if ((((FriendlyShipIntel)intel).AgentStatus & AgentStatus.DockedAtHome) != 0)
                    return;
                color = Color.Blue;

                if (SelectionCandidates.Contains(intelKey))
                    color = Color.LightSkyBlue;
                else if (SelectedItems.Contains(intelKey))
                    color = Color.Teal;
            }
            else if (intel.Type == IntelItemType.Enemy)
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
            else if (intel.Type == IntelItemType.Waypoint)
            {
                color = Color.Green;
            }
            else
            {
                return;
            }

            var middlePosition = (altitudePosition + flatPosition) * 0.5f;
            var lineLength = MapScale * Math.Abs((float)localCoords.Y) * (float)SinViewDegree;

            AddSprite("CircleHollow", altitudePosition, new Vector2(20, 20), color);

            if (intel.ID != Controller.CubeGrid.EntityId)
            {
                AddSprite("CircleHollow", flatPosition, new Vector2(30, 30 * (float)CosViewDegree), color);
                AddSprite("SquareSimple", middlePosition, new Vector2(2, lineLength * PixelsPerMeter), color);
            }

            if (intel.Type == IntelItemType.Friendly)
            {
                AddSprite("CircleHollow", altitudePosition, new Vector2(2 * ScannerRange * MapScale * PixelsPerMeter), color);

                if (Math.Abs((float)localCoords.Y) < ScannerRange)
                {
                    AddSprite("CircleHollow", flatPosition, new Vector2(2 * (float)Math.Sqrt(ScannerRange * ScannerRange - localCoords.Y * localCoords.Y) * MapScale * PixelsPerMeter, 2 * (float)(Math.Sqrt(ScannerRange * ScannerRange - localCoords.Y * localCoords.Y) * MapScale * PixelsPerMeter * CosViewDegree)), color);
                }
            }

            return;
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

        Vector2 LocalCoordsToMapPosition(Vector3D localCoords, bool flat = false)
        {
            return new Vector2(MapScale * (float)localCoords.X, -MapScale * (float)localCoords.Z * (float)CosViewDegree + (flat ? 0 : (MapScale * (float)localCoords.Y * (float)SinViewDegree)));
        }

        Vector3D FlatPosToGlobalPos(Vector2 flatCoords, Vector2 cursorCoords)
        {
            var result = new Vector3D();
            result += flatCoords.X * MapMatrix.Right;
            result += (flatCoords.Y / CosViewDegree) * MapMatrix.Forward;
            result += ((cursorCoords.Y - flatCoords.Y) / SinViewDegree) * MapMatrix.Up;
            result /= MapScale;
            result += Controller.GetPosition();
            return result;
        }

        MySprite AddSprite(string spriteType, Vector2 wallPosition, Vector2 size, Color? color = null)
        {
            var sprite = MySprite.CreateSprite(spriteType, wallPosition, size);
            sprite.Color = color == null ? Color.White : color;
            SpriteScratchpad.Add(sprite);
            return sprite;
        }

        MySprite AddTextSprite(string text, Vector2 position, int fontSize = 1, string font = "Debug", Color? color = null)
        {
            var sprite = MySprite.CreateText(text, font, color == null ? Color.White : color.Value, fontSize);
            sprite.Size = MeasureSize(text, fontSize, font);
            position.Y += sprite.Size.Value.Y * 0.5f / PixelsPerMeter;
            sprite.Position = position;
            SpriteScratchpad.Add(sprite);
            return sprite;
        }

        Vector2 MeasureSize(string text, int fontSize, string font, string addition = "")
        {
            TextMeasureBuilder.Clear();
            TextMeasureBuilder.Append(text);
            TextMeasureBuilder.Append(addition);
            return Panels[Vector2I.Zero].MeasureStringInPixels(TextMeasureBuilder, font, fontSize);
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

        void DrawContextMenu()
        {
            int index = 0;

            MenuItems.Clear();
            DrawMenuItem(index, ActionPing);
            index += 1;
            if (SelectedItems.Count > 0)
            {
                if (SelectedItems[0].Item1 == IntelItemType.Friendly)
                {
                    DrawMenuItem(index, ActionMove);
                }
            }
        }

        void DrawMenuItem(int index, string text)
        {
            var size = MeasureSize(text, 3, "Monospace") / PixelsPerMeter;
            float posX = -ScreenSizeMeters * 0.5f + ActionBarMargin + size.X * 0.5f;
            float posY = ScreenSizeMeters * 0.5f - ActionBarMargin * 0.5f - (ActionBarMargin + size.Y) * (0.5f + index);

            var rect = new RectangleF(posX - size.X * 0.5f, posY - size.Y * 0.5f, size.X, size.Y);

            var color = Color.White;
            if (rect.Contains(CursorPos2D))
            {
                color = Color.Teal;
                if (OnRelease && MenuItemBindings.ContainsKey(text))
                {
                    MenuItemBindings[text]();
                    OnRelease = false;
                }
            }

            AddTextSprite(text, new Vector2(posX, posY), 3, "Monospace", color);
        }

        void MenuCallbackPing()
        {
            ActionMode = ActionSelectPosition;
            SelectPositionCallback = SelectPositionCallbackPing;
        }

        void SelectPositionCallbackPing(TimeSpan timestamp)
        {
            var waypoint = new Waypoint();
            waypoint.Position = SelectedPosition;
            waypoint.Name = "Ping";

            IntelProvider.ReportFleetIntelligence(waypoint, timestamp);
        }

        void MenuCallbackMove()
        {
            ActionMode = ActionSelectPosition;
            SelectPositionCallback = SelectPositionCallbackMove;
        }

        private void SelectPositionCallbackMove(TimeSpan timestamp)
        {
            IFleetIntelligence selected;
            var intels = IntelProvider.GetFleetIntelligences(timestamp);

            if (intels.TryGetValue(SelectedItems[0], out selected))
            {
                var waypoint = new Waypoint();
                waypoint.Position = SelectedPosition;
                IntelProvider.ReportFleetIntelligence(waypoint, timestamp);

                IntelProvider.ReportCommand((FriendlyShipIntel)selected, TaskType.Move, MyTuple.Create(IntelItemType.Waypoint, waypoint.ID), timestamp);
            }
        }

        ExecutionContext Context;

        StringBuilder DebugBuilder = new StringBuilder();
        StringBuilder TextMeasureBuilder = new StringBuilder();

        List<MyTuple<IntelItemType, long>> SelectedItems = new List<MyTuple<IntelItemType, long>>();
        List<MyTuple<IntelItemType, long>> SelectionCandidates = new List<MyTuple<IntelItemType, long>>();
        Dictionary<RectangleF, Action> MenuItems = new Dictionary<RectangleF, Action>();
        Dictionary<string, Action> MenuItemBindings = new Dictionary<string, Action>();

        Vector3D CursorPos;
        Vector2 CursorPos2D;

        int MapSize = 30000; // In meters
        int PixelsPerMeter = 205; // 512 / 2.5
        int ViewAngleDegrees = 60;
        float MapOnScreenSizeMeters = 6.5f;
        float ScreenSizeMeters = 7.5f;
        int ScannerRange = 1000;
        float MapScale = 0;

//        float ActionBarHeight = 1.25f;
        float ActionBarMargin = 0.1f;

        bool LastFrameMouseDown = false;
        bool OnRelease = false;

        int runs = 0;

        string ActionMode = "NONE"; // This should be an enum, but minifier doesn't like enums

        const string ActionNone = "NONE";
        const string ActionPing = "PING";
        const string ActionMove = "MOVE";
        const string ActionSelectPosition = "SP";
        Vector2 SelectFlatPos = Vector2.PositiveInfinity;
        Vector3D SelectedPosition = Vector3D.Zero;

        Action<TimeSpan> SelectPositionCallback;

        // Required Subsystems
        IIntelProvider IntelProvider;

        public TacMapSubsystem(IIntelProvider intelProvider)
        {
            IntelProvider = intelProvider;

            MenuItemBindings.Add(ActionPing, MenuCallbackPing);
            MenuItemBindings.Add(ActionMove, MenuCallbackMove);
        }

        List<MySprite> SpriteScratchpad = new List<MySprite>();
        List<Vector2> ScreenCoordsScratchpad = new List<Vector2>();
        List<Vector3D> LocalCoordsScratchpad = new List<Vector3D>();
        MatrixD MapMatrix = new MatrixD();
        bool AlignWithNorth = false;
        double SinViewDegree;
        double CosViewDegree;
        float ClosestItemDist;
        MyTuple<IntelItemType, long> ClosestItemKey;

        // Components
        IMyLargeTurretBase Turret;
        IMyShipController Controller;
        Dictionary<Vector2I, IMyTextPanel> Panels = new Dictionary<Vector2I, IMyTextPanel>();

        void GetParts()
        {
            Context.Terminal.GetBlocksOfType<IMyTerminalBlock>(null, CollectParts);
        }

        bool CollectParts(IMyTerminalBlock block)
        {
            if (block.CubeGrid.EntityId != Context.Reference.CubeGrid.EntityId) return false;
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
