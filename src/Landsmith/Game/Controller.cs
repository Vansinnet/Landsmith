using System;
using System.Globalization;
using System.Text;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;

namespace Landsmith
{
    // Per-frame tool state for the local player: brush input, anchors, planned cost, preview and the info panel.
    internal sealed class Controller
    {
        private const double MinSize = 0.5;
        private const double ReanchorDistance = 40;
        private const float RefreshSeconds = 0.3f;
        private const float PlanSeconds = 0.2f;
        private const string Degree = "°";

        private readonly Settings settings;
        private readonly ManualLogSource log;
        private readonly Preview preview;
        private readonly UndoHistory<EditRecord> history;
        private readonly StringBuilder text = new StringBuilder(512);

        private ToolDefinition active;
        private BrushShape shape;
        private double size;
        private double rotation;
        private bool edges;
        private double levelHeight;
        private bool levelSet;
        private double levelAnchorX;
        private double levelAnchorZ;
        private double offset;
        private SlopePlane plane;
        private bool planeSet;
        private double[] angles;
        private int angleIndex;
        private double smoothStrength;
        private double scroll;
        private bool checkAnchors;
        private bool halted;
        private int version;
        private int strokeVersion = -1;
        private Vector3 strokeCenter;
        private float strokeTime;
        private int plannedStone = -1;
        private bool planPending;
        private float planTime;
        private bool blocked;
        private bool planWarned;
        private int panelVersion = -1;
        private GUIContent panel = new GUIContent(string.Empty);
        private Vector2 panelSize;
        private GUIStyle panelStyle;
        private float panelScale;
        private ZNet session;

        internal bool ConsumesScroll { get; private set; }

        internal Controller(Settings settings, ManualLogSource log)
        {
            this.settings = settings;
            this.log = log;
            preview = new Preview();
            if (!preview.Available) log.LogWarning("No line shader found; the ground preview is disabled. The tools still work.");
            history = new UndoHistory<EditRecord>(settings.UndoSteps.Value);
            shape = settings.DefaultShape.Value;
            size = Steps.Clamp(settings.DefaultSize.Value, MinSize, settings.MaximumSize.Value);
            edges = settings.DefaultEdges.Value;
            offset = settings.DefaultOffset.Value;
            smoothStrength = settings.SmoothStrength.Value;
            angles = Steps.ParseAngles(settings.SlopeAngles.Value, Settings.DefaultAngles);
            angleIndex = Math.Min(1, angles.Length - 1);
            settings.UndoSteps.SettingChanged += (_, __) => history.Capacity = settings.UndoSteps.Value;
            settings.SlopeAngles.SettingChanged += (_, __) =>
            {
                angles = Steps.ParseAngles(settings.SlopeAngles.Value, Settings.DefaultAngles);
                angleIndex = Math.Min(angleIndex, angles.Length - 1);
                if (planeSet) plane = plane.WithAngle(angles[angleIndex]);
                version++;
            };
            settings.StonePerCubicMetre.SettingChanged += (_, __) => version++;
            settings.StonePerSquareMetre.SettingChanged += (_, __) => version++;
            settings.EdgeWidth.SettingChanged += (_, __) => version++;
            settings.SnapToGrid.SettingChanged += (_, __) => version++;
            settings.MaximumSize.SettingChanged += (_, __) =>
            {
                size = Steps.Clamp(size, MinSize, settings.MaximumSize.Value);
                version++;
            };
        }

        // After an unexpected error the tools stay selectable but refuse to edit.
        internal void Halt()
        {
            halted = true;
            ConsumesScroll = false;
            preview.Hide();
        }

        internal void Destroy()
        {
            preview.Destroy();
            history.Clear();
        }

        internal void Update()
        {
            ConsumesScroll = false;
            Player player = Player.m_localPlayer;
            if (session != ZNet.instance)
            {
                // A new world or connection: snapshots and anchors of the previous session are meaningless.
                session = ZNet.instance;
                history.Clear();
                levelSet = planeSet = false;
            }
            if (!player || !GameAccess.Ready || !ToolRegistry.Ready)
            {
                Deactivate();
                return;
            }
            Piece selected = player.InPlaceMode() ? player.GetSelectedPiece() : null;
            if (!ToolRegistry.TryGet(selected, out ToolDefinition definition))
            {
                Deactivate();
                return;
            }
            if (definition != active) Activate(definition);

            bool aiming = TryGetAim(player, out Vector3 aim);
            if (InputAllowed(player)) HandleInput(player, aiming, aim);
            if (aiming && checkAnchors) CheckAnchorsAfterSelection(player, aim);
            UpdateStroke(player, aiming, aim);
        }

        // Replaces TryPlacePiece for a Landsmith piece. Returns false when the original must be skipped.
        internal bool TryPlace(Player player, ToolDefinition definition, Piece piece, ref bool placed)
        {
            placed = false;
            if (halted)
            {
                player.Message(MessageHud.MessageType.Center, "Landsmith stopped after an error. See BepInEx/LogOutput.log.");
                return false;
            }
            // Vanilla placement rules first (wards at the centre, dungeons, no ray hit), with the vanilla messages.
            GameAccess.UpdatePlacementGhost(player, true);
            GameObject ghost = GameAccess.PlacementGhost(player);
            Player.PlacementStatus status = player.GetPlacementStatus();
            if (status != Player.PlacementStatus.Valid || !ghost)
            {
                player.Message(MessageHud.MessageType.Center, StatusMessage(status));
                return false;
            }
            if (active != definition) Activate(definition);
            Vector3 position = ghost.transform.position;
            Quaternion rotationValue = ghost.transform.rotation;
            EnsureAnchors(position);

            Brush brush = CurrentBrush(position.x, position.z);
            ShapeRequest request = Request(definition.Kind, brush);
            EditOutcome outcome;
            try
            {
                outcome = TerrainWriter.Apply(request, history, measure => Approve(player, definition, piece, measure));
            }
            catch (Exception error)
            {
                log.LogError($"Terrain edit failed: {error}");
                outcome = new EditOutcome(EditStatus.Failed, "Landsmith could not edit the terrain here. See BepInEx/LogOutput.log.");
            }
            version++;
            if (!outcome.Succeeded)
            {
                ToolRegistry.SetStone(definition, 0);
                if (!string.IsNullOrEmpty(outcome.Message)) player.Message(MessageHud.MessageType.Center, outcome.Message);
                return false;
            }
            placed = true;
            if (!string.IsNullOrEmpty(outcome.Message)) player.Message(MessageHud.MessageType.TopLeft, outcome.Message);
            // The feedback vanilla PlacePiece gives for a placed piece.
            player.FaceLookDirection();
            ItemDrop.ItemData tool = GameAccess.RightItem != null ? GameAccess.RightItem(player) : null;
            ZSyncAnimation animator = GameAccess.Animator != null ? GameAccess.Animator(player) : null;
            if (tool != null && animator) animator.SetTrigger(tool.m_shared.m_attack.m_attackAnimation);
            piece.m_placeEffect.Create(position, rotationValue, null, 1f, -1, player.GetZDOID());
            player.AddNoise(50f);
            return false;
        }

        // Sets the exact stone amount of this stroke; vanilla consumes it after TryPlacePiece returns true.
        private EditOutcome Approve(Player player, ToolDefinition definition, Piece piece, StrokeMeasure measure)
        {
            int stone = StoneFor(definition.Kind, measure);
            ToolRegistry.SetStone(definition, stone);
            if (stone > 0 && definition.Stone != null && !player.NoCostCheat() && !player.HaveRequirements(piece, Player.RequirementMode.CanBuild))
                return new EditOutcome(EditStatus.Refused, $"This stroke needs {stone} stone.");
            return EditOutcome.Ok;
        }

        // The same messages vanilla TryPlacePiece shows for each placement status.
        private static string StatusMessage(Player.PlacementStatus status)
        {
            switch (status)
            {
                case Player.PlacementStatus.NoBuildZone: return "$msg_nobuildzone";
                case Player.PlacementStatus.BlockedbyPlayer: return "$msg_blocked";
                case Player.PlacementStatus.PrivateZone: return "$msg_privatezone";
                case Player.PlacementStatus.MoreSpace: return "$msg_needspace";
                case Player.PlacementStatus.NoTeleportArea: return "$msg_noteleportarea";
                case Player.PlacementStatus.ExtensionMissingStation: return "$msg_extensionmissingstation";
                case Player.PlacementStatus.WrongBiome: return "$msg_wrongbiome";
                case Player.PlacementStatus.NeedCultivated: return "$msg_needcultivated";
                case Player.PlacementStatus.NeedDirt: return "$msg_needdirt";
                case Player.PlacementStatus.NotInDungeon: return "$msg_notindungeon";
                case Player.PlacementStatus.DeepSnow: return "$msg_snowtoodeep";
                case Player.PlacementStatus.NoSnow: return "$msg_nosnow";
                default: return "$msg_invalidplacement";
            }
        }

        private int StoneFor(ToolKind tool, StrokeMeasure measure)
        {
            if (Tools.ChargesNetVolume(tool))
                return Costs.Stone(measure.RaisedVolume - measure.LoweredVolume, settings.StonePerCubicMetre.Value);
            if (Tools.ChargesForRaising(tool)) return Costs.Stone(measure.RaisedVolume, settings.StonePerCubicMetre.Value);
            if (Tools.ChargesForPaving(tool)) return Costs.Stone(measure.PaintedArea, settings.StonePerSquareMetre.Value);
            return 0;
        }

        internal void OnGUI()
        {
            if (active == null || !settings.ShowPanel.Value || !Player.m_localPlayer || Hud.IsUserHidden() ||
                Menu.IsVisible() || InventoryGui.IsVisible() || Minimap.IsOpen() || Hud.IsPieceSelectionVisible()) return;
            float scale = settings.PanelScale.Value * Mathf.Max(1f, Screen.height / 1080f);
            if (panelStyle == null || !Mathf.Approximately(panelScale, scale))
            {
                panelScale = scale;
                panelStyle = new GUIStyle(GUI.skin.box)
                {
                    alignment = TextAnchor.UpperLeft,
                    richText = true,
                    wordWrap = false,
                    fontSize = Mathf.RoundToInt(14 * scale),
                    padding = new RectOffset(Mathf.RoundToInt(10 * scale), Mathf.RoundToInt(10 * scale), Mathf.RoundToInt(8 * scale), Mathf.RoundToInt(8 * scale))
                };
                panelStyle.normal.textColor = new Color(1f, 0.95f, 0.85f);
                panelVersion = -1;
            }
            if (panelVersion != version)
            {
                panelVersion = version;
                panel = new GUIContent(BuildPanel());
                panelSize = panelStyle.CalcSize(panel);
            }
            var area = new Rect(Mathf.Round(20 * scale), Mathf.Round(Screen.height * 0.4f), panelSize.x, panelSize.y);
            GUI.Box(area, panel, panelStyle);
        }

        private void Activate(ToolDefinition definition)
        {
            active = definition;
            checkAnchors = true;
            scroll = 0;
            plannedStone = -1;
            strokeVersion = -1;
            planPending = true;
            ToolRegistry.ResetStone();
            version++;
        }

        // A stale plan must not keep showing (or, with "hide unavailable pieces", hide) a tool in the build menu.
        private void Deactivate()
        {
            if (active != null)
            {
                active = null;
                ToolRegistry.ResetStone();
                version++;
            }
            preview.Hide();
        }

        private static bool TryGetAim(Player player, out Vector3 aim)
        {
            aim = Vector3.zero;
            GameObject ghost = GameAccess.PlacementGhost(player);
            if (!ghost || !ghost.activeInHierarchy || player.GetPlacementStatus() == Player.PlacementStatus.NoRayHits) return false;
            aim = ghost.transform.position;
            return true;
        }

        private static bool InputAllowed(Player player) =>
            !player.IsDead() && !Hud.IsPieceSelectionVisible() && !Hud.InRadial() && !global::Console.IsVisible() &&
            !(Chat.instance && Chat.instance.HasFocus()) && !Menu.IsVisible() && !InventoryGui.IsVisible() &&
            !Minimap.IsOpen() && !TextInput.IsVisible() && !StoreGui.IsVisible() && !GameCamera.InFreeFly() &&
            !(Hud.instance && Hud.instance.m_buildUi && Hud.instance.m_buildUi.SearchFieldFocused);

        private void HandleInput(Player player, bool aiming, Vector3 aim)
        {
            bool shift = Key(KeyCode.LeftShift) || Key(KeyCode.RightShift);
            bool ctrl = Key(KeyCode.LeftControl) || Key(KeyCode.RightControl);
            bool alt = Key(KeyCode.LeftAlt) || Key(KeyCode.RightAlt) || Key(KeyCode.AltGr);
            // GameCamera keeps zooming while the aim hits nothing, so the wheel is only taken while aiming at ground.
            ConsumesScroll = aiming && (shift || ctrl || alt);
            if (ConsumesScroll)
            {
                int steps = Steps.ConsumeScroll(ref scroll, ZInput.GetMouseScrollWheel(), player.m_scrollAmountThreshold);
                if (steps != 0)
                {
                    if (shift && !ctrl && !alt) size = Steps.Adjust(size, steps, settings.SizeStep.Value, MinSize, settings.MaximumSize.Value);
                    else if (ctrl && !shift && !alt) AdjustValue(steps);
                    else if (alt && !shift && !ctrl) Rotate(steps);
                    version++;
                }
            }
            else
            {
                scroll = 0;
            }

            if (Pressed(settings.ToggleShape.Value))
            {
                shape = shape == BrushShape.Circle ? BrushShape.Square : BrushShape.Circle;
                player.Message(MessageHud.MessageType.TopLeft, shape == BrushShape.Circle ? "Circle brush" : "Square brush");
                version++;
            }
            if (Pressed(settings.ToggleEdges.Value))
            {
                edges = !edges;
                player.Message(MessageHud.MessageType.TopLeft, edges ? "Soft edges on" : "Soft edges off");
                version++;
            }
            if (Pressed(settings.Resample.Value)) Resample(player, aiming, aim);
            if (Pressed(settings.NextSlopeAngle.Value) && active.Kind == ToolKind.Slope)
            {
                angleIndex = (angleIndex + 1) % angles.Length;
                if (planeSet) plane = plane.WithAngle(angles[angleIndex]);
                player.Message(MessageHud.MessageType.TopLeft, "Slope " + AngleText(angles[angleIndex]));
                version++;
            }
            if (Pressed(settings.Undo.Value))
            {
                EditOutcome outcome;
                try
                {
                    outcome = halted ? new EditOutcome(EditStatus.Failed, "Landsmith stopped after an error.") : TerrainWriter.Undo(history);
                }
                catch (Exception error)
                {
                    log.LogError($"Undo failed: {error}");
                    outcome = new EditOutcome(EditStatus.Failed, "Undo failed. See BepInEx/LogOutput.log.");
                }
                player.Message(outcome.Succeeded ? MessageHud.MessageType.TopLeft : MessageHud.MessageType.Center,
                    outcome.Succeeded ? "Undone" : outcome.Message);
                version++;
            }
        }

        private void AdjustValue(int steps)
        {
            double step = settings.HeightStep.Value;
            switch (active.Kind)
            {
                case ToolKind.Level:
                    if (levelSet) levelHeight = Math.Round(levelHeight + steps * step, 4);
                    break;
                case ToolKind.Offset:
                    offset = Steps.Adjust(offset, steps, step, -Heightmap.c_LevelMaxDelta, Heightmap.c_LevelMaxDelta);
                    break;
                case ToolKind.Slope:
                    if (planeSet) plane = plane.WithHeight(Math.Round(plane.AnchorY + steps * step, 4));
                    break;
                case ToolKind.Smooth:
                    smoothStrength = Steps.Adjust(smoothStrength, steps, 0.1, 0.1, 1);
                    break;
            }
        }

        private void Rotate(int steps)
        {
            double step = settings.RotationStep.Value;
            if (active.Kind == ToolKind.Slope)
            {
                if (planeSet) plane = plane.WithDirection(plane.DirectionDegrees + steps * step);
                return;
            }
            rotation = Steps.NormalizeDegrees(rotation + steps * step);
        }

        private void Resample(Player player, bool aiming, Vector3 aim)
        {
            switch (active.Kind)
            {
                case ToolKind.Offset:
                    offset = settings.DefaultOffset.Value;
                    player.Message(MessageHud.MessageType.TopLeft, "Offset " + Metres(offset, true));
                    break;
                case ToolKind.Level:
                    if (!aiming) return;
                    SetLevel(aim);
                    player.Message(MessageHud.MessageType.TopLeft, "Level height " + Metres(levelHeight, false));
                    break;
                case ToolKind.Slope:
                    if (!aiming) return;
                    SetPlane(aim);
                    player.Message(MessageHud.MessageType.TopLeft, "New slope " + AngleText(plane.AngleDegrees));
                    break;
                default:
                    return;
            }
            version++;
        }

        // Level height and slope plane stay fixed while working, so long plateaus and ramps stay continuous.
        // Only when the tool is selected again far away from them are they taken anew, with a message.
        private void CheckAnchorsAfterSelection(Player player, Vector3 aim)
        {
            checkAnchors = false;
            if (active.Kind == ToolKind.Level && levelSet && Far(levelAnchorX, levelAnchorZ, aim))
            {
                SetLevel(aim);
                player.Message(MessageHud.MessageType.TopLeft, "Level height " + Metres(levelHeight, false) + " taken here");
                version++;
            }
            if (active.Kind == ToolKind.Slope && planeSet && Far(plane.AnchorX, plane.AnchorZ, aim))
            {
                SetPlane(aim);
                player.Message(MessageHud.MessageType.TopLeft, "New slope started here");
                version++;
            }
            EnsureAnchors(aim);
        }

        private void EnsureAnchors(Vector3 aim)
        {
            if (active == null) return;
            if (active.Kind == ToolKind.Level && !levelSet)
            {
                SetLevel(aim);
                version++;
            }
            if (active.Kind == ToolKind.Slope && !planeSet)
            {
                SetPlane(aim);
                version++;
            }
        }

        private static bool Far(double x, double z, Vector3 aim)
        {
            double dx = aim.x - x;
            double dz = aim.z - z;
            return dx * dx + dz * dz > ReanchorDistance * ReanchorDistance;
        }

        private void SetLevel(Vector3 aim)
        {
            levelHeight = Math.Round(aim.y, 2);
            levelAnchorX = aim.x;
            levelAnchorZ = aim.z;
            levelSet = true;
        }

        private void SetPlane(Vector3 aim)
        {
            double step = settings.RotationStep.Value;
            double yaw = GameCamera.instance ? GameCamera.instance.transform.eulerAngles.y : 0;
            double direction = Steps.NormalizeDegrees(Math.Round(yaw / step) * step);
            plane = new SlopePlane(Snap(aim.x), Math.Round(aim.y, 2), Snap(aim.z), direction, angles[angleIndex]);
            planeSet = true;
        }

        private double Snap(double value) => settings.SnapToGrid.Value ? Math.Floor(value + 0.5) : value;

        private Brush CurrentBrush(double x, double z)
        {
            bool slope = active != null && active.Kind == ToolKind.Slope && planeSet;
            double edge = edges && (active == null || Tools.UsesEdges(active.Kind)) ? settings.EdgeWidth.Value : 0;
            return new Brush(shape, Snap(x), Snap(z), size, slope ? plane.DirectionDegrees : rotation, edge);
        }

        private ShapeRequest Request(ToolKind tool, Brush brush) =>
            new ShapeRequest(tool, brush, levelHeight, offset, plane, smoothStrength, settings.SmoothPasses.Value);

        // Planned cost and preview. With grid snapping the stroke only changes when the brush moves to another
        // vertex, so most frames reuse the previous result; a periodic refresh picks up edits by other players.
        // The cost plan (a read-only pass over the brush area) runs at most every PlanSeconds.
        private void UpdateStroke(Player player, bool aiming, Vector3 aim)
        {
            if (!aiming)
            {
                preview.Hide();
                return;
            }
            Brush brush = CurrentBrush(aim.x, aim.z);
            var center = new Vector3((float)brush.CenterX, 0, (float)brush.CenterZ);
            float now = Time.time;
            bool changed = strokeVersion != version || now - strokeTime >= RefreshSeconds || (strokeCenter - center).sqrMagnitude >= 0.0001f;
            if (changed)
            {
                strokeVersion = version;
                strokeTime = now;
                strokeCenter = center;
                planPending = true;
            }
            bool planned = false;
            if (planPending && now - planTime >= PlanSeconds)
            {
                planPending = false;
                planTime = now;
                UpdatePlannedCost(player, brush);
                planned = true;
            }
            if (!changed && !planned) return;
            if (!settings.ShowPreview.Value || !preview.Available)
            {
                preview.Hide();
                return;
            }
            Func<double, double, double> surface = null;
            SlopePlane? arrow = null;
            switch (active.Kind)
            {
                case ToolKind.Level:
                    if (levelSet)
                    {
                        double height = levelHeight;
                        surface = (x, z) => height;
                    }
                    break;
                case ToolKind.Offset:
                    double lift = offset;
                    surface = (x, z) => preview.TrySampleGround((float)x, (float)z, out float h) ? h + lift : double.NaN;
                    break;
                case ToolKind.Slope:
                    if (planeSet)
                    {
                        SlopePlane current = plane;
                        surface = current.HeightAt;
                        arrow = current;
                    }
                    break;
            }
            preview.Show(brush, blocked, Tools.UsesEdges(active.Kind), surface, arrow);
        }

        private void UpdatePlannedCost(Player player, Brush brush)
        {
            var ground = new Vector3((float)brush.CenterX, 0, (float)brush.CenterZ);
            blocked = player.GetPlacementStatus() != Player.PlacementStatus.Valid ||
                !TerrainWriter.CheckProtection(ground, (float)brush.Reach, flash: false).Succeeded;
            int stone = 0;
            if (active.Stone != null)
            {
                stone = -1;
                try
                {
                    if (TerrainWriter.Plan(Request(active.Kind, brush), out StrokeMeasure measure).Succeeded)
                        stone = StoneFor(active.Kind, measure);
                }
                catch (Exception error)
                {
                    if (!planWarned) log.LogWarning($"Cost estimate failed: {error}");
                    planWarned = true;
                }
            }
            ToolRegistry.SetStone(active, Math.Max(0, stone));
            if (stone != plannedStone)
            {
                plannedStone = stone;
                panelVersion = -1;
            }
        }

        private string BuildPanel()
        {
            text.Length = 0;
            if (active == null) return string.Empty;
            text.Append("<b>").Append(active.Name).Append("</b>\n");
            switch (active.Kind)
            {
                case ToolKind.Level:
                    text.Append("Height ").Append(levelSet ? Metres(levelHeight, false) : "-").Append("   Ctrl+Wheel, ").Append(KeyName(settings.Resample.Value)).Append(" sample\n");
                    break;
                case ToolKind.Offset:
                    text.Append("Offset ").Append(Metres(offset, true)).Append(" per click   Ctrl+Wheel, ").Append(KeyName(settings.Resample.Value)).Append(" reset\n");
                    break;
                case ToolKind.Smooth:
                    text.Append("Strength ").Append(smoothStrength.ToString("0.0", CultureInfo.InvariantCulture)).Append("   Ctrl+Wheel\n");
                    break;
                case ToolKind.Slope:
                    text.Append("Angle ").Append(AngleText(planeSet ? plane.AngleDegrees : angles[angleIndex])).Append("   ").Append(KeyName(settings.NextSlopeAngle.Value)).Append(" next\n");
                    if (planeSet)
                    {
                        text.Append("Uphill ").Append(plane.DirectionDegrees.ToString("0", CultureInfo.InvariantCulture)).Append(Degree).Append("   Alt+Wheel\n");
                        text.Append("Anchor height ").Append(Metres(plane.AnchorY, false)).Append("   Ctrl+Wheel\n");
                    }
                    text.Append(KeyName(settings.Resample.Value)).Append(" new slope at the aimed point\n");
                    break;
            }
            text.Append(shape == BrushShape.Circle ? "Circle, radius " : "Square, half-width ")
                .Append(Metres(size, false)).Append("   Shift+Wheel, ").Append(KeyName(settings.ToggleShape.Value)).Append(" shape\n");
            if (shape == BrushShape.Square && active.Kind != ToolKind.Slope)
                text.Append("Rotation ").Append(rotation.ToString("0", CultureInfo.InvariantCulture)).Append(Degree).Append("   Alt+Wheel\n");
            if (Tools.UsesEdges(active.Kind))
                text.Append("Soft edges ").Append(edges ? Metres(settings.EdgeWidth.Value, false) : "off").Append("   ").Append(KeyName(settings.ToggleEdges.Value)).Append('\n');
            if (active.Stone != null)
                text.Append("Stone here: ").Append(plannedStone < 0 ? "-" : plannedStone.ToString(CultureInfo.InvariantCulture)).Append('\n');
            int steps = history.Count;
            text.Append(KeyName(settings.Undo.Value)).Append(" undo").Append(steps > 0 ? " (" + steps + ")" : string.Empty);
            return text.ToString();
        }

        private static string AngleText(double angle) =>
            angle.ToString("0.#", CultureInfo.InvariantCulture) + Degree + " (" + Steps.RatioLabel(angle) + ")";

        private static string Metres(double value, bool signed) =>
            (signed && value > 0 ? "+" : string.Empty) + value.ToString("0.00", CultureInfo.InvariantCulture) + " m";

        private static string KeyName(KeyboardShortcut shortcut) =>
            shortcut.MainKey == KeyCode.None ? "(unbound)" : shortcut.ToString();

        // Keys go through the game's input layer. A key without a counterpart there counts as not pressed.
        private static bool Key(KeyCode key)
        {
            try
            {
                return ZInput.GetKey(key, false);
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        private static readonly KeyCode[] ModifierKeys =
        {
            KeyCode.LeftShift, KeyCode.RightShift, KeyCode.LeftControl, KeyCode.RightControl, KeyCode.LeftAlt, KeyCode.RightAlt, KeyCode.AltGr
        };

        // Exact combination, like BepInEx KeyboardShortcut: the main key went down this frame, every listed
        // modifier is held and no other Shift, Ctrl or Alt key is.
        private static bool Pressed(KeyboardShortcut shortcut)
        {
            if (shortcut.MainKey == KeyCode.None) return false;
            try
            {
                if (!ZInput.GetKeyDown(shortcut.MainKey, false)) return false;
            }
            catch (ArgumentException)
            {
                return false;
            }
            foreach (KeyCode modifier in shortcut.Modifiers)
                if (!Key(modifier)) return false;
            foreach (KeyCode modifier in ModifierKeys)
            {
                if (modifier == shortcut.MainKey || !Key(modifier)) continue;
                bool listed = false;
                foreach (KeyCode wanted in shortcut.Modifiers) listed |= wanted == modifier;
                if (!listed) return false;
            }
            return true;
        }
    }
}
