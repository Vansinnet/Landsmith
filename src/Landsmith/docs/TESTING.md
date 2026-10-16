# Landsmith: Runtime Acceptance

Status: **Runtime-pending**. No Landsmith game session has been observed. General method, environments and log locations: `docs/testing.md`.

Record for every session: date, role (single player, listen host, client), Valheim and BepInEx version, plugin SHA-256, world (always a disposable copy), other installed mods (especially other terrain mods), frame rate observations and relevant log lines. Add the result to the observation log at the end of this file.

## Smallest Required Test

1. Disable other terrain tool mods (ValheimTerrainTools, SmarterHoe) in Vortex; they also react to F8 and the mouse wheel. Deploy with `scripts/Deploy-Mod.ps1 -Mod Landsmith` and start the game. Confirm `Landsmith 0.1.0 loaded` and `Added 7 Landsmith tools to the Hoe` in `BepInEx/LogOutput.log`, with no `[Error` lines from Landsmith.
2. On a copy of a world, equip the Hoe and open its build menu. Confirm seven "Landsmith: ..." pieces with their own icons next to the vanilla tools.
3. Select **Landsmith: Slope**, set a 10 m square brush (Shift + wheel, F8), turn soft edges on (Y) and keep a frame rate counter visible (for example Steam's in-game FPS counter). Aim at the ground for 30 seconds while moving the mouse: the frame rate must stay at the level it has with the vanilla Hoe. Click several times along the ramp: each click may cause at most one short hitch, never a sustained drop.
4. Check that the ramp follows the arrow, that consecutive clicks continue the same plane, that Backspace twice restores the ground of two strokes, and that the stone shown in the panel is what is charged, only for clicks that changed the terrain.

## Full Matrix

- Every tool in single player: Level (F6 sample, Ctrl + wheel height), Raise or lower (positive and negative offset, no stone charged while lowering), Smooth (strength), Slope (each angle from F10, Alt + wheel direction, Ctrl + wheel height, F6 new slope), Path, Paved road, Reset (height and paint). Circle and rotated square brushes, soft edges on and off, smallest and largest brush.
- Strokes across a zone border (64 m heightmap edge) and across a zone corner: no seams, steps or paint gaps; undo restores all zones.
- The 8 m limit: Level far above and below the original ground; the clamping message appears and nothing beyond the limit is charged.
- Protection: inside and at the edge of another player's active ward (stroke reach overlapping the ward), also while standing in your own ward next to it; a stroke whose reach overlaps a no-build location (for example a boss altar or the trader) without the centre being inside; in a dungeon. Edits are refused without cost.
- Partially loaded terrain: edit at the edge of the loaded area, after teleporting, and immediately after login.
- Multiplayer on a listen host and on a dedicated server: the editing client has Landsmith, the other client and the server do not. The other client sees every edit, including grass removal and paint, without reconnecting. Repeat with the other player as the previous owner of the terrain (they edited the area first with the vanilla Hoe). Verify the edit survives a server restart.
- Two players editing the same zone at the same moment (Landsmith and the vanilla Hoe): record whether any edit is lost.
- Undo: several strokes on the same zone undone in a row; after another player changed the same zone (refused); after walking away until the zone unloads (refused); after reconnecting (history cleared).
- Zone seams: an existing step between two zones is closed by a Level stroke across it.
- Plan without writes: aiming with a tool over untouched ground and clicking Reset there creates no terrain data (`Nothing to change here`).
- Costs: workbench needed for Raise or lower and Slope, stonecutter for Paved road; the planned stone matches the charged stone for Level, Raise, Slope, Reset (refilling), Smooth (net added ground) and Paved road; one vanilla-sized raise costs 2 stone; lowering and Path are free; a stroke needing more stone than carried is refused without cost; `StonePerCubicMetre = 0` and free build charge nothing; stamina and Hoe durability are used once per successful click; no build statistics are added.
- Input: Shift/Ctrl/Alt + wheel never zooms the camera while aiming at the ground with a Landsmith tool; aiming at the sky, the wheel only zooms; plain wheel still zooms; keys do nothing while chat, console, inventory, map or the build menu is open. Remapped and unbound (`None`) shortcuts.
- Interface: panel and preview at 1080p and 4K, `PanelScale`, `ShowPanel = false`, `ShowPreview = false`, hidden HUD (Ctrl + F3).
- Performance: compare frame time while aiming with Landsmith Slope (20 m brush, soft edges 10 m) against the vanilla Hoe; record the click hitch duration if measurable.
- Compatibility: together with ValheimTerrainTools, TerrainShaperPlus, SmarterHoe and Valheim Performance Overhaul (record separately; not established by vanilla acceptance).
- Dedicated server with Landsmith installed on the server only: loads without errors and does nothing.
- Remove the mod: edits remain, the Hoe shows only its vanilla pieces, no errors.

Until these observations exist, any explicitly requested release must remain a prerelease and cannot be marked stable/latest.

## Observation Log

| Date | Role | Valheim / BepInEx | Plugin SHA-256 | Result | Notes |
|---|---|---|---|---|---|
| 2026-10-07 | Client (role not recorded) | 1.0.17 / 5.4.23.5 | B829B7338E5E9D6497AF0B0C9438FEB01BEC9979C37F2A889216B64B8C3B08FE | Pass (user report) | First session after the key change (Y, Backspace). User reported the tools worked without lag. LogOutput.log: load line and "Added 7 Landsmith tools to the Hoe", no errors or exceptions. Other mods: Jotunn, SeneaL UI, QuickStack, Craft From Containers, MassFarming; SmarterHoe and ValheimTerrainTools disabled. Individual matrix rows, multiplayer and dedicated server not observed. |
| 2026-10-16 | Client (role not recorded) | 1.0.20 / 5.4.23.5 | `B829B7338E5E9D6497AF0B0C9438FEB01BEC9979C37F2A889216B64B8C3B08FE` (dev build of 2026-10-07, not the release) | Pass (user report) | User reported that all three mods work fine in game. The installed build was not the 0.1.0 release DLL (`6AA64A37...`), so the 0.1.0 release build itself has not been observed in game. At the user's explicit request the GitHub release v0.1.0 was changed from prerelease to a normal release marked latest, although the full matrix above is not itemized; `runtimeStatus` stays `Runtime-pending`. |
