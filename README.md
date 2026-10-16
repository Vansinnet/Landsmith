# Landsmith

Version **0.1.0** for Valheim **1.0.20** with BepInEx 5.

Fast terrain shaping for the Hoe: level, raise or lower, smooth, straight slopes for ramps and stairs, paths, paved roads and reset, with circle or square brushes, soft edges, a live outline preview and undo.

**Status: Runtime-pending.** The mod compiles against Valheim 1.0.20 and its shaping logic is covered by automated tests, but it has not yet been observed in a live game. Use it on a backed-up world.

**Download:** [GitHub releases](https://github.com/Vansinnet/Landsmith/releases). Take the newest release (marked *Pre-release* while the mod is Runtime-pending), download `Landsmith-<version>.zip` under *Assets* and extract it into `BepInEx/plugins/Landsmith/`.

## Why It Stays Fast

Valheim's terrain only knows a few operations (level to one height, raise, smooth, paint). A shape such as a straight ramp built from those would need hundreds of operations per click, and every operation recompresses and resends the terrain data of each 64 m zone and rebuilds its mesh.

Landsmith calculates the whole stroke at once and writes each affected zone **once per click**: one save, one network update and one mesh rebuild per zone, whatever the brush size or shape. The ground preview only draws the outline and is recalculated when the brush, the aim or the terrain changes, not every frame over the whole area.

## Tools

All tools are added to the Hoe's build menu as **Landsmith: ...** pieces, next to the vanilla tools.

| Tool | What it does |
|---|---|
| Level | Sets the ground to the target height. The height is taken from the first point you aim at and stays until you sample again (F6), so neighbouring strokes form one plateau. |
| Raise or lower | Moves the ground up or down by the offset (default +0.5 m) and keeps its bumps and dips. Each click applies the offset again. |
| Smooth | Softens bumps and steps. Repeat for stronger smoothing. |
| Slope | Shapes a straight incline. The slope is a fixed plane in the world: click along it to continue the same ramp. The arrow points uphill. Angles: 14&deg; (1:4), 26.6&deg; (1:2, matches the 26&deg; building pieces), 45&deg; (1:1) and 63.4&deg; (2:1). |
| Path | Paints dirt without changing the height. |
| Paved road | Paints paving without changing the height. |
| Reset | Restores the original height and ground cover inside the brush. |

### Costs

Adding ground costs stone for the volume actually added: 0.25 stone per cubic metre by default, the rate of the vanilla Raise ground (2 stone for about 8 cubic metres per click). This applies to Level, Raise or lower, Slope, Reset (when it refills dug ground) and Smooth (only for what it adds overall, raised minus lowered). Lowering ground and Path are free. Paved road costs 0.065 stone per square metre, the rate of the vanilla tool (1 stone for about 15 square metres). Costs are rounded to the nearest stone, with at least one stone for any change.

The panel shows the stone the stroke under the cursor needs, and the build panel lists it as the requirement. A click that changes nothing, is blocked or cannot be paid costs nothing. Stamina and Hoe durability are used once per successful click, as with the vanilla Hoe. Free building costs nothing.

Raise or lower and Slope need a workbench nearby and Paved road a stonecutter, like the vanilla tools they are based on; Level, Smooth, Path and Reset need no station, like vanilla Level ground. The cost rates are settings of the player who uses the tools: in multiplayer they are not enforced by the server.

## Controls

| Input | Effect |
|---|---|
| Left click | Apply the selected tool |
| Shift + mouse wheel | Brush size (radius, or half the side of a square) |
| Ctrl + mouse wheel | Level: target height. Raise or lower: offset. Slope: height of the slope. Smooth: strength |
| Alt + mouse wheel | Square rotation; for Slope the uphill direction (the square follows it) |
| F8 | Circle or square brush |
| Y | Soft edges on or off: a transition band outside the brush that blends into the surrounding ground |
| F6 | Level: take the aimed height. Raise or lower: reset the offset. Slope: start a new slope at the aimed point, facing away from the camera |
| F10 | Next slope angle |
| Backspace | Undo your last Landsmith edit |

The mouse wheel only zooms the camera when no modifier key is held. Ctrl is also the crouch key, so Ctrl + wheel makes your character crouch. The keys only act while a Landsmith tool is selected, and only when pressed without other Shift, Ctrl or Alt keys. The defaults avoid the vanilla keys and SeneaL UI (F7, Z, X, C, H) and QuickStack (F3). All keys can be changed in the configuration file. A panel on the left shows the current tool settings, the stone needed and the keys.

The preview shows the brush outline on the ground, the soft edge band (faint), the planned surface (blue) for Level, Raise or lower and Slope, and the uphill arrow for Slope. A red outline means the stroke would be refused (for example inside someone else's ward).

## Limits

- Valheim limits terrain edits to 8 m above or below the original generated ground. Landsmith reports when part of a stroke reaches the limit.
- The terrain grid is one metre. Very fine details and perfectly sharp edges are not possible.
- The whole stroke, including soft edges, must be on loaded terrain, outside wards you have no access to and outside no-build areas.
- Undo works for your own edits in the current session, newest first. It refuses an edit if that area was changed again afterwards (by anyone, also with the vanilla Hoe) or has been unloaded.
- Level height and the slope plane stay fixed while you work. If you select the tool again more than 40 m away, they are taken anew at the aimed point and a message says so.
- Keyboard and mouse only.

## Multiplayer

Only the player who uses the tools needs Landsmith. Edits are stored as ordinary Valheim terrain data, so other players (including unmodded and console players) and the server see them without installing anything, and they remain if the mod is removed.

To write a shape Valheim has no operation for, Landsmith takes ownership of the affected 64 m terrain zones before writing; afterwards the game moves ownership between nearby players as usual. If another player edits exactly the same zone with the vanilla Hoe in the same moment, one of the two edits can be lost, and the two players may see different terrain there until that zone is edited again.

## Installation

Put `Landsmith.dll` in `BepInEx/plugins/Landsmith/`, or install the package with a mod manager. Install side: **client**.

If you also use another terrain tool mod, such as ValheimTerrainTools or SmarterHoe, disable it or change its keys: they also use F8 and the mouse wheel.

## Configuration

`BepInEx/config/se.junge.valheim.landsmith.cfg`. Most changes apply immediately; `DefaultSize`, `DefaultShape`, `SoftEdges`, `RaiseOffset` and `SmoothStrength` are starting values used at the next game start.

- `[General] Enabled` turns the mod on or off (restart required).
- `[Brush]` `DefaultSize`, `MaximumSize` (up to 30 m), `SizeStep`, `HeightStep`, `RotationStep`, `DefaultShape`, `SoftEdges`, `EdgeWidth`, `SnapToGrid`.
- `[Tools]` `RaiseOffset`, `SlopeAngles` (comma-separated degrees), `SmoothStrength`, `SmoothPasses`, `UndoSteps`.
- `[Costs]` `StonePerCubicMetre`, `StonePerSquareMetre` (0 makes that kind of edit free).
- `[Interface]` `ShowPanel`, `ShowPreview`, `PanelScale`.
- `[Keys]` `ToggleSoftEdges`, `ToggleShape`, `SampleHeight`, `NextSlopeAngle`, `Undo`.

## Compatibility

Harmony patches: `ObjectDB.Awake`, `ObjectDB.CopyOtherDB`, `Player.TryPlacePiece` and `Player.PlacePiece` (only for Landsmith pieces) and `Player.CanRotatePiece` (only while a Landsmith modifier key is held). Mods that replace terrain storage, change the 8 m limit or patch Hoe placement have not been tested together with Landsmith.

## License

MIT. See `LICENSE`. The tool icons and the mod icon are original artwork made for this mod.
