# Changelog

## 0.1.0 - 2026-10-16

- Initial version: Hoe tools Level, Raise or lower, Smooth, Slope, Path, Paved road and Reset.
- Circle and rotatable square brushes up to 30 m, soft edges, grid snapping, outline preview, info panel and undo.
- Each stroke writes every affected 64 m terrain zone once per click; the preview cost depends on the outline, not the area.
- Added ground costs stone per cubic metre and paving per square metre, at the vanilla tools' rates; failed, empty or unaffordable strokes cost nothing.
- Wards and no-build locations are checked across the whole stroke.
- Client-side only; edits are vanilla terrain data visible to unmodded players and servers.
- Built and source-verified against Valheim 1.0.20 and BepInEx 5.4.23.5. Runtime-pending.
