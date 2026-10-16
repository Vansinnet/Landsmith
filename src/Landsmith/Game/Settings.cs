using BepInEx.Configuration;
using UnityEngine;

namespace Landsmith
{
    // Local client settings. They only affect the player who uses the tools; every edit is ordinary
    // vanilla terrain data afterwards.
    internal sealed class Settings
    {
        internal static readonly double[] DefaultAngles = { 14.04, 26.57, 45, 63.43 };

        internal readonly ConfigEntry<float> DefaultSize;
        internal readonly ConfigEntry<float> MaximumSize;
        internal readonly ConfigEntry<float> SizeStep;
        internal readonly ConfigEntry<float> HeightStep;
        internal readonly ConfigEntry<float> RotationStep;
        internal readonly ConfigEntry<BrushShape> DefaultShape;
        internal readonly ConfigEntry<bool> DefaultEdges;
        internal readonly ConfigEntry<float> EdgeWidth;
        internal readonly ConfigEntry<bool> SnapToGrid;
        internal readonly ConfigEntry<float> DefaultOffset;
        internal readonly ConfigEntry<string> SlopeAngles;
        internal readonly ConfigEntry<float> SmoothStrength;
        internal readonly ConfigEntry<int> SmoothPasses;
        internal readonly ConfigEntry<float> StonePerCubicMetre;
        internal readonly ConfigEntry<float> StonePerSquareMetre;
        internal readonly ConfigEntry<int> UndoSteps;
        internal readonly ConfigEntry<bool> ShowPanel;
        internal readonly ConfigEntry<bool> ShowPreview;
        internal readonly ConfigEntry<float> PanelScale;
        internal readonly ConfigEntry<KeyboardShortcut> ToggleEdges;
        internal readonly ConfigEntry<KeyboardShortcut> ToggleShape;
        internal readonly ConfigEntry<KeyboardShortcut> Resample;
        internal readonly ConfigEntry<KeyboardShortcut> NextSlopeAngle;
        internal readonly ConfigEntry<KeyboardShortcut> Undo;
        // Keys only act while a Landsmith tool is selected. Defaults avoid vanilla bindings and common UI mods
        // (SeneaL UI uses F7 and Z, QuickStack F3); F9 changes the vanilla gamepad layout.

        internal Settings(ConfigFile config)
        {
            DefaultSize = config.Bind("Brush", "DefaultSize", 3f,
                new ConfigDescription("Brush radius, or half the side of a square, in metres when the game starts.", new AcceptableValueRange<float>(0.5f, 30f)));
            MaximumSize = config.Bind("Brush", "MaximumSize", 20f,
                new ConfigDescription("Largest brush radius or square half-width in metres. Large brushes still write each 64 m terrain zone only once per click.", new AcceptableValueRange<float>(1f, 30f)));
            SizeStep = config.Bind("Brush", "SizeStep", 0.5f,
                new ConfigDescription("Metres per Shift + mouse wheel step.", new AcceptableValueRange<float>(0.1f, 5f)));
            HeightStep = config.Bind("Brush", "HeightStep", 0.25f,
                new ConfigDescription("Metres per Ctrl + mouse wheel step for the level height, the raise/lower offset and the slope height.", new AcceptableValueRange<float>(0.01f, 2f)));
            RotationStep = config.Bind("Brush", "RotationStep", 15f,
                new ConfigDescription("Degrees per Alt + mouse wheel step for square brushes and the slope direction.", new AcceptableValueRange<float>(1f, 90f)));
            DefaultShape = config.Bind("Brush", "DefaultShape", BrushShape.Circle, "Brush shape when the game starts.");
            DefaultEdges = config.Bind("Brush", "SoftEdges", false, "Soft edges when the game starts: a transition band outside the brush that blends into the surrounding ground.");
            EdgeWidth = config.Bind("Brush", "EdgeWidth", 3f,
                new ConfigDescription("Width of the soft edge band in metres.", new AcceptableValueRange<float>(0.5f, 10f)));
            SnapToGrid = config.Bind("Brush", "SnapToGrid", true, "Centre the brush on the one-metre terrain grid so neighbouring strokes line up.");
            DefaultOffset = config.Bind("Tools", "RaiseOffset", 0.5f,
                new ConfigDescription("Offset of Raise or lower in metres per click when the game starts and after the sample key. Negative values lower the ground.", new AcceptableValueRange<float>(-8f, 8f)));
            SlopeAngles = config.Bind("Tools", "SlopeAngles", "14.04, 26.57, 45, 63.43",
                "Slope angles in degrees to cycle through. 26.57 and 45 match the 26 and 45 degree building pieces (1:2 and 1:1).");
            SmoothStrength = config.Bind("Tools", "SmoothStrength", 0.6f,
                new ConfigDescription("Blending per smoothing pass when the game starts. Ctrl + mouse wheel changes it in game.", new AcceptableValueRange<float>(0.1f, 1f)));
            SmoothPasses = config.Bind("Tools", "SmoothPasses", 3,
                new ConfigDescription("Smoothing passes per click.", new AcceptableValueRange<int>(1, Shaper.MaxSmoothPasses)));
            StonePerCubicMetre = config.Bind("Costs", "StonePerCubicMetre", 0.25f,
                new ConfigDescription("Stone per cubic metre of ground added by Level, Raise or lower, Slope, Reset (refilling) and Smooth (net). The default matches the vanilla Raise ground tool (2 stone for about 8 cubic metres). Lowering is free. 0 makes raising free. This is a local setting of the player using the tools.", new AcceptableValueRange<float>(0f, 10f)));
            StonePerSquareMetre = config.Bind("Costs", "StonePerSquareMetre", 0.065f,
                new ConfigDescription("Stone per square metre of Paved road. The default matches the vanilla Paved road (1 stone for about 15 square metres). 0 makes paving free.", new AcceptableValueRange<float>(0f, 10f)));
            UndoSteps = config.Bind("Tools", "UndoSteps", 10,
                new ConfigDescription("How many of your own edits can be undone. 0 disables undo.", new AcceptableValueRange<int>(0, 50)));
            ShowPanel = config.Bind("Interface", "ShowPanel", true, "Show the tool panel with the current settings and keys.");
            ShowPreview = config.Bind("Interface", "ShowPreview", true, "Draw the brush outline, soft edge and planned surface on the ground.");
            PanelScale = config.Bind("Interface", "PanelScale", 1f,
                new ConfigDescription("Size of the tool panel.", new AcceptableValueRange<float>(0.75f, 2f)));
            ToggleEdges = config.Bind("Keys", "ToggleSoftEdges", new KeyboardShortcut(KeyCode.Y), "Turn the soft edge band on or off.");
            ToggleShape = config.Bind("Keys", "ToggleShape", new KeyboardShortcut(KeyCode.F8), "Switch between circle and square brushes.");
            Resample = config.Bind("Keys", "SampleHeight", new KeyboardShortcut(KeyCode.F6),
                "Level: take the aimed ground height. Raise or lower: reset the offset. Slope: start a new slope at the aimed point, facing away from the camera.");
            NextSlopeAngle = config.Bind("Keys", "NextSlopeAngle", new KeyboardShortcut(KeyCode.F10), "Cycle through the slope angles.");
            Undo = config.Bind("Keys", "Undo", new KeyboardShortcut(KeyCode.Backspace), "Undo your last Landsmith edit.");
        }
    }
}
