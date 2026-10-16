using System;
using System.Collections.Generic;
using UnityEngine;

namespace Landsmith
{
    internal enum EditStatus
    {
        Done,
        NothingChanged,
        Protected,
        NoBuildZone,
        NotLoaded,
        Unsupported,
        Refused,
        Failed
    }

    internal readonly struct EditOutcome
    {
        internal readonly EditStatus Status;
        internal readonly string Message;

        internal EditOutcome(EditStatus status, string message)
        {
            Status = status;
            Message = message;
        }

        internal bool Succeeded => Status == EditStatus.Done;

        internal static readonly EditOutcome Ok = new EditOutcome(EditStatus.Done, null);
    }

    // What a stroke would change: ground raised and lowered (cubic metres; a vertex shared by two zones counts
    // once, with its larger change), painted area (square metres, weighted by the soft edge) and vertices held
    // back by the 8 m limit.
    internal struct StrokeMeasure
    {
        internal bool Changes;
        internal double RaisedVolume;
        internal double LoweredVolume;
        internal double PaintedArea;
        internal int ClampedVertices;
    }

    // State of one terrain compiler before an edit, kept for undo.
    internal sealed class CompSnapshot
    {
        internal TerrainComp Comp;
        internal Heightmap Heightmap;
        internal ZDO Zdo;
        internal uint BaseRevision;
        internal uint SavedRevision;
        internal bool[] ModifiedHeight;
        internal float[] LevelDelta;
        internal float[] SmoothDelta;
        internal bool[] ModifiedPaint;
        internal Color[] PaintMask;
        internal bool PaintOnly;
    }

    internal sealed class EditRecord
    {
        internal readonly List<CompSnapshot> Comps = new List<CompSnapshot>();
        internal Vector3 Center;
        internal float Radius;
    }

    // Applies a whole brush stroke with one write per affected terrain compiler.
    // Vanilla tools send one network operation per heightmap and click; a large custom shape built from many
    // vanilla operations would recompress, resend and rebuild each heightmap once per operation.
    internal static class TerrainWriter
    {
        private const float PaintTolerance = 1f / 512f;
        private const float LocationSearchMargin = 128f;

        private sealed class Target
        {
            internal Heightmap Heightmap;
            internal TerrainComp Comp;
            internal int BaseX;
            internal int BaseZ;
            internal bool HeightChanges;
            internal bool PaintChanges;
            internal bool Prepared;
        }

        private sealed class Stroke
        {
            internal ToolKind Tool;
            internal Brush Brush;
            internal HeightGrid Grid;
            internal float[] Result;
            internal float[] Weight;
            internal float[] Rise;
            internal float[] Fall;
            internal bool[] CountedClamps;
            internal bool[] CountedCells;
            internal Color Paint;
            internal bool Paints;
            internal bool Resets;
            internal bool Shapes;
        }

        private static readonly List<Heightmap> heightmaps = new List<Heightmap>();
        private static readonly List<Target> targets = new List<Target>();

        // Read-only estimate for the cost display. Never creates, claims or writes a terrain compiler.
        internal static EditOutcome Plan(in ShapeRequest request, out StrokeMeasure measure)
        {
            measure = default;
            EditOutcome begun = Begin(request, out Stroke stroke);
            if (!begun.Succeeded) return begun;
            foreach (Target target in targets) target.Comp = ExistingComp(target.Heightmap);
            measure = Measure(stroke, write: false);
            return EditOutcome.Ok;
        }

        // approve receives the final measurement before anything is written; a failed approval cancels the stroke.
        internal static EditOutcome Apply(in ShapeRequest request, UndoHistory<EditRecord> history, Func<StrokeMeasure, EditOutcome> approve)
        {
            EditOutcome begun = Begin(request, out Stroke stroke);
            if (!begun.Succeeded) return begun;
            Vector3 center = new Vector3((float)stroke.Brush.CenterX, 0, (float)stroke.Brush.CenterZ);
            float reach = (float)stroke.Brush.Reach;

            EditOutcome blocked = CheckProtection(center, reach, flash: true);
            if (!blocked.Succeeded) return blocked;

            // Dry run on the existing data first, so untouched zones never get a terrain compiler or change owner.
            foreach (Target target in targets) target.Comp = ExistingComp(target.Heightmap);
            StrokeMeasure measure = Measure(stroke, write: false);
            if (!measure.Changes) return NothingChanged(measure);
            // Refuse before taking ownership when the stroke is already known to be unaffordable.
            EditOutcome approved = approve != null ? approve(measure) : EditOutcome.Ok;
            if (!approved.Succeeded) return approved;

            foreach (Target target in targets)
            {
                if (!target.HeightChanges && !target.PaintChanges) continue;
                EditOutcome prepared = Prepare(target);
                if (!prepared.Succeeded) return prepared;
            }

            // Preparing may have loaded newer replicated data; measure again from the state as it is now.
            Sample(stroke.Grid);
            if (!stroke.Grid.AllKnown()) return new EditOutcome(EditStatus.NotLoaded, "The terrain here is not fully loaded yet.");
            Shaper.Compute(request, stroke.Grid, stroke.Result, stroke.Weight);
            measure = Measure(stroke, write: false);
            if (!measure.Changes) return NothingChanged(measure);
            foreach (Target target in targets)
                if ((target.HeightChanges || target.PaintChanges) && !target.Prepared)
                    return new EditOutcome(EditStatus.Refused, "The terrain here was just changed by someone else. Try again.");

            approved = approve != null ? approve(measure) : EditOutcome.Ok;
            if (!approved.Succeeded) return approved;

            var record = new EditRecord { Center = center, Radius = reach };
            foreach (Target target in targets)
            {
                if (!target.HeightChanges && !target.PaintChanges) continue;
                record.Comps.Add(Snapshot(target.Comp, target.Heightmap));
            }
            Measure(stroke, write: true);
            foreach (CompSnapshot snapshot in record.Comps)
            {
                Target target = targets.Find(t => t.Comp == snapshot.Comp);
                snapshot.PaintOnly = !target.HeightChanges;
                Commit(snapshot.Comp, snapshot.Heightmap, center, reach, snapshot.PaintOnly);
                snapshot.SavedRevision = snapshot.Zdo.DataRevision;
            }
            if (ClutterSystem.instance) ClutterSystem.instance.ResetGrass(center, reach);
            history?.Push(record);
            return new EditOutcome(EditStatus.Done,
                measure.ClampedVertices > 0 ? "Part of the area reached the terrain limit (8 m from the original ground)." : null);
        }

        internal static EditOutcome Undo(UndoHistory<EditRecord> history)
        {
            if (history == null || !history.TryPop(out EditRecord record))
                return new EditOutcome(EditStatus.NothingChanged, "Nothing to undo.");
            foreach (CompSnapshot snapshot in record.Comps)
            {
                if (!snapshot.Comp || !snapshot.Heightmap)
                    return new EditOutcome(EditStatus.NotLoaded, "Cannot undo: the edited area is no longer loaded.");
                ZNetView view = GameAccess.CompView(snapshot.Comp);
                if (!view || !view.IsValid() || view.GetZDO() != snapshot.Zdo)
                    return new EditOutcome(EditStatus.NotLoaded, "Cannot undo: the edited area is no longer loaded.");
                if (snapshot.Zdo.DataRevision != snapshot.SavedRevision)
                    return new EditOutcome(EditStatus.Refused, "Cannot undo: the terrain was changed again since then.");
            }
            foreach (CompSnapshot snapshot in record.Comps)
            {
                TerrainComp comp = snapshot.Comp;
                ZNetView view = GameAccess.CompView(comp);
                if (!view.IsOwner()) view.ClaimOwnership();
                Restore(comp, snapshot);
                Commit(comp, snapshot.Heightmap, record.Center, record.Radius, snapshot.PaintOnly);
                uint restored = snapshot.Zdo.DataRevision;
                // Saving changed the revision; let the previous edit of this zone remain undoable.
                foreach (EditRecord older in history.NewestFirst())
                {
                    CompSnapshot previous = older.Comps.Find(c => c.Zdo == snapshot.Zdo);
                    if (previous == null) continue;
                    previous.SavedRevision = UndoChain.NextExpected(previous.SavedRevision, snapshot.BaseRevision, restored);
                    break;
                }
            }
            if (ClutterSystem.instance) ClutterSystem.instance.ResetGrass(record.Center, record.Radius);
            return EditOutcome.Ok;
        }

        // Wards and no-build locations block the whole stroke, not only its centre. Every enabled ward the stroke
        // reaches must grant access (wardCheck), so a player's own ward cannot open up a neighbour's.
        internal static EditOutcome CheckProtection(Vector3 center, float reach, bool flash)
        {
            if (!PrivateArea.CheckAccess(center, reach, flash, wardCheck: true))
                return new EditOutcome(EditStatus.Protected, "$msg_privatezone");
            if (TouchesNoBuildLocation(center, reach))
                return new EditOutcome(EditStatus.NoBuildZone, "$msg_nobuildzone");
            return EditOutcome.Ok;
        }

        // Locations are registered per zone; search the zones around the stroke, wide enough for large locations.
        private static bool TouchesNoBuildLocation(Vector3 center, float reach)
        {
            Vector2s zone = ZoneSystem.GetZone(center);
            int rings = Mathf.CeilToInt((reach + LocationSearchMargin) / ZoneSystem.c_ZoneSize);
            for (int dz = -rings; dz <= rings; dz++)
            {
                for (int dx = -rings; dx <= rings; dx++)
                {
                    Location location = Location.GetZoneLocation(new Vector2s(zone.x + dx, zone.y + dz));
                    if (location && location.m_noBuild && location.IsInside(center, reach, buildCheck: true)) return true;
                }
            }
            return false;
        }

        private static EditOutcome NothingChanged(StrokeMeasure measure) =>
            new EditOutcome(EditStatus.NothingChanged,
                measure.ClampedVertices > 0 ? "Terrain limit reached (8 m from the original ground)." : "Nothing to change here.");

        private static EditOutcome Begin(in ShapeRequest request, out Stroke stroke)
        {
            Brush brush = Tools.UsesEdges(request.Tool) ? request.Brush : request.Brush.WithoutEdge();
            var grid = HeightGrid.Around(brush, Shaper.MarginFor(request));
            stroke = new Stroke
            {
                Tool = request.Tool,
                Brush = brush,
                Grid = grid,
                Result = new float[grid.Length],
                Weight = new float[grid.Length],
                Rise = new float[grid.Length],
                Fall = new float[grid.Length],
                CountedClamps = new bool[grid.Length],
                CountedCells = new bool[grid.Length],
                Paint = request.Tool == ToolKind.Paved ? Heightmap.m_paintMaskPaved : Heightmap.m_paintMaskDirt,
                Paints = Tools.Paints(request.Tool),
                Resets = request.Tool == ToolKind.Reset,
                Shapes = Tools.ChangesHeight(request.Tool)
            };
            EditOutcome collected = Collect(grid);
            if (!collected.Succeeded) return collected;
            Shaper.Compute(request, grid, stroke.Result, stroke.Weight);
            return EditOutcome.Ok;
        }

        // Finds the loaded heightmaps under the grid and samples current heights on the shared vertex lattice.
        private static EditOutcome Collect(HeightGrid grid)
        {
            targets.Clear();
            heightmaps.Clear();
            float cx = (grid.MinX + grid.MaxX) * 0.5f;
            float cz = (grid.MinZ + grid.MaxZ) * 0.5f;
            float radius = Mathf.Max(grid.CountX, grid.CountZ) * 0.5f + 1f;
            Heightmap.FindHeightmap(new Vector3(cx, 0, cz), radius, heightmaps);
            foreach (Heightmap heightmap in heightmaps)
            {
                if (!heightmap) continue;
                if (!Mathf.Approximately(heightmap.m_scale, 1f) || heightmap.m_width <= 0 || heightmap.m_width % 2 != 0)
                    return new EditOutcome(EditStatus.Unsupported, "Unsupported terrain layout here.");
                Vector3 position = heightmap.transform.position;
                int half = heightmap.m_width / 2;
                float baseX = position.x - half;
                float baseZ = position.z - half;
                if (Mathf.Abs(baseX - Mathf.Round(baseX)) > 0.001f || Mathf.Abs(baseZ - Mathf.Round(baseZ)) > 0.001f)
                    return new EditOutcome(EditStatus.Unsupported, "Unsupported terrain layout here.");
                targets.Add(new Target
                {
                    Heightmap = heightmap,
                    BaseX = Mathf.RoundToInt(baseX),
                    BaseZ = Mathf.RoundToInt(baseZ)
                });
            }
            heightmaps.Clear();
            Sample(grid);
            if (!grid.AllKnown())
                return new EditOutcome(EditStatus.NotLoaded, "The terrain here is not fully loaded yet.");
            return EditOutcome.Ok;
        }

        // Reads current heights on the shared vertex lattice. Border vertices exist in both neighbouring heightmaps.
        private static void Sample(HeightGrid grid)
        {
            Array.Clear(grid.Known, 0, grid.Known.Length);
            foreach (Target target in targets)
            {
                Heightmap heightmap = target.Heightmap;
                if (!heightmap) continue;
                float originY = heightmap.transform.position.y;
                Range(target, grid.MinX, grid.MaxX, grid.MinZ, grid.MaxZ, out int x0, out int x1, out int z0, out int z1);
                for (int z = z0; z <= z1; z++)
                    for (int x = x0; x <= x1; x++)
                        grid.Set(target.BaseX + x, target.BaseZ + z, heightmap.GetHeight(x, z) + originY);
            }
        }

        // Local vertex or pixel range of a heightmap that overlaps the world range [minX, maxX] x [minZ, maxZ].
        private static void Range(Target target, int minX, int maxX, int minZ, int maxZ, out int x0, out int x1, out int z0, out int z1)
        {
            int last = target.Heightmap.m_width;
            x0 = Math.Max(0, minX - target.BaseX);
            x1 = Math.Min(last, maxX - target.BaseX);
            z0 = Math.Max(0, minZ - target.BaseZ);
            z1 = Math.Min(last, maxZ - target.BaseZ);
        }

        private static TerrainComp ExistingComp(Heightmap heightmap)
        {
            TerrainComp comp = TerrainComp.FindTerrainCompiler(heightmap.transform.position);
            if (!comp || !GameAccess.Initialized(comp) || GameAccess.CompHeightmap(comp) != heightmap) return null;
            return comp;
        }

        // Without a compiler a heightmap has no stored deltas; with write = false nothing is modified.
        private static StrokeMeasure Measure(Stroke stroke, bool write)
        {
            var measure = new StrokeMeasure();
            Array.Clear(stroke.Rise, 0, stroke.Rise.Length);
            Array.Clear(stroke.Fall, 0, stroke.Fall.Length);
            Array.Clear(stroke.CountedClamps, 0, stroke.CountedClamps.Length);
            Array.Clear(stroke.CountedCells, 0, stroke.CountedCells.Length);
            foreach (Target target in targets)
            {
                if (write && !target.HeightChanges && !target.PaintChanges) continue;
                bool heights = stroke.Shapes && MeasureHeights(stroke, target, write, ref measure);
                bool paint = (stroke.Paints || stroke.Resets) && MeasurePaint(stroke, target, write, ref measure);
                if (write) continue;
                target.HeightChanges = heights;
                target.PaintChanges = paint;
                measure.Changes |= heights || paint;
            }
            for (int g = 0; g < stroke.Rise.Length; g++)
            {
                measure.RaisedVolume += stroke.Rise[g];
                measure.LoweredVolume += stroke.Fall[g];
            }
            return measure;
        }

        private static bool MeasureHeights(Stroke stroke, Target target, bool write, ref StrokeMeasure measure)
        {
            HeightGrid grid = stroke.Grid;
            Heightmap heightmap = target.Heightmap;
            TerrainComp comp = target.Comp;
            bool[] modified = comp ? GameAccess.ModifiedHeight(comp) : null;
            float[] level = comp ? GameAccess.LevelDelta(comp) : null;
            float[] smooth = comp ? GameAccess.SmoothDelta(comp) : null;
            float originY = heightmap.transform.position.y;
            float maxDelta = Heightmap.c_LevelMaxDelta;
            int pitch = heightmap.m_width + 1;
            bool changed = false;
            Range(target, grid.MinX, grid.MaxX, grid.MinZ, grid.MaxZ, out int x0, out int x1, out int z0, out int z1);
            for (int z = z0; z <= z1; z++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    int g = grid.Index(target.BaseX + x, target.BaseZ + z);
                    if (stroke.Weight[g] <= 0) continue;
                    int i = z * pitch + x;
                    float oldLevel = level != null ? level[i] : 0;
                    float oldSmooth = smooth != null ? smooth[i] : 0;
                    float newLevel = oldLevel;
                    float newSmooth = oldSmooth;
                    if (stroke.Resets)
                    {
                        bool wasModified = modified != null && modified[i];
                        if (!wasModified && oldLevel == 0 && oldSmooth == 0) continue;
                        newLevel = 0;
                        newSmooth = 0;
                        if (write)
                        {
                            modified[i] = false;
                            level[i] = 0;
                            smooth[i] = 0;
                        }
                    }
                    else
                    {
                        // Each heightmap's own current height, so existing seams between zones are closed too.
                        float current = heightmap.GetHeight(x, z);
                        bool moved = TerrainDelta.Apply(current, stroke.Result[g] - originY, maxDelta, ref newLevel, ref newSmooth, out bool clamped);
                        if (clamped && !stroke.CountedClamps[g])
                        {
                            stroke.CountedClamps[g] = true;
                            measure.ClampedVertices++;
                        }
                        if (!moved) continue;
                        if (write)
                        {
                            modified[i] = true;
                            level[i] = newLevel;
                            smooth[i] = newSmooth;
                        }
                    }
                    changed = true;
                    float rise = (newLevel + newSmooth) - (oldLevel + oldSmooth);
                    if (rise > stroke.Rise[g]) stroke.Rise[g] = rise;
                    if (-rise > stroke.Fall[g]) stroke.Fall[g] = -rise;
                }
            }
            return changed;
        }

        // Paint mask pixel (x, z) covers the one-metre cell starting at vertex (x, z), matching the vanilla
        // half-cell offset (TerrainComp.PaintCleared with m_halfOffset).
        private static bool MeasurePaint(Stroke stroke, Target target, bool write, ref StrokeMeasure measure)
        {
            HeightGrid grid = stroke.Grid;
            Brush brush = stroke.Brush;
            Heightmap heightmap = target.Heightmap;
            TerrainComp comp = target.Comp;
            bool[] modified = comp ? GameAccess.ModifiedPaint(comp) : null;
            Color[] mask = comp ? GameAccess.PaintMask(comp) : null;
            bool soft = stroke.Paints;
            int pitch = heightmap.m_width + 1;
            bool changed = false;
            Range(target, grid.MinX - 1, grid.MaxX, grid.MinZ - 1, grid.MaxZ, out int x0, out int x1, out int z0, out int z1);
            for (int z = z0; z <= z1; z++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    int wx = target.BaseX + x;
                    int wz = target.BaseZ + z;
                    double cx = wx + 0.5;
                    double cz = wz + 0.5;
                    double w = soft ? brush.Weight(cx, cz) : (brush.IsInside(cx, cz) ? 1 : 0);
                    if (w <= 0) continue;
                    int i = z * pitch + x;
                    bool wasModified = modified != null && modified[i];
                    if (stroke.Resets)
                    {
                        if (!wasModified) continue;
                        if (write) modified[i] = false;
                        changed = true;
                        continue;
                    }
                    Color current = wasModified ? mask[i] : heightmap.GetPaintMask(x, z);
                    float t = (float)w;
                    var next = new Color(
                        Mathf.Lerp(current.r, stroke.Paint.r, t),
                        Mathf.Lerp(current.g, stroke.Paint.g, t),
                        Mathf.Lerp(current.b, stroke.Paint.b, t),
                        current.a);
                    if (Mathf.Abs(next.r - current.r) < PaintTolerance && Mathf.Abs(next.g - current.g) < PaintTolerance &&
                        Mathf.Abs(next.b - current.b) < PaintTolerance) continue;
                    if (write)
                    {
                        modified[i] = true;
                        mask[i] = next;
                    }
                    changed = true;
                    if (grid.Contains(wx, wz))
                    {
                        int g = grid.Index(wx, wz);
                        if (stroke.CountedCells[g]) continue;
                        stroke.CountedCells[g] = true;
                    }
                    measure.PaintedArea += w;
                }
            }
            return changed;
        }

        private static EditOutcome Prepare(Target target)
        {
            Heightmap heightmap = target.Heightmap;
            TerrainComp comp = heightmap.GetAndCreateTerrainCompiler();
            if (!comp || !GameAccess.Initialized(comp) || GameAccess.CompHeightmap(comp) != heightmap)
                return new EditOutcome(EditStatus.NotLoaded, "The terrain here is not ready yet.");
            ZNetView view = GameAccess.CompView(comp);
            if (!view || !view.IsValid())
                return new EditOutcome(EditStatus.NotLoaded, "The terrain here is not ready yet.");
            int length = (heightmap.m_width + 1) * (heightmap.m_width + 1);
            if (GameAccess.LevelDelta(comp).Length != length || GameAccess.PaintMask(comp).Length != length)
                return new EditOutcome(EditStatus.Unsupported, "Unsupported terrain layout here.");
            // Vanilla applies terrain operations on the compiler's owner. A Landsmith shape is not a vanilla
            // operation, so the editing client takes ownership, then loads any newer replicated data.
            if (!view.IsOwner()) view.ClaimOwnership();
            GameAccess.CheckLoad(comp);
            target.Comp = comp;
            target.Prepared = true;
            return EditOutcome.Ok;
        }

        private static void Commit(TerrainComp comp, Heightmap heightmap, Vector3 center, float reach, bool paintOnly)
        {
            GameAccess.Operations(comp)++;
            GameAccess.LastOpPoint(comp) = center;
            GameAccess.LastOpRadius(comp) = reach;
            GameAccess.Save(comp, false);
            heightmap.Poke(1, paintOnly);
        }

        private static CompSnapshot Snapshot(TerrainComp comp, Heightmap heightmap)
        {
            ZDO zdo = GameAccess.CompView(comp).GetZDO();
            return new CompSnapshot
            {
                Comp = comp,
                Heightmap = heightmap,
                Zdo = zdo,
                BaseRevision = zdo.DataRevision,
                ModifiedHeight = (bool[])GameAccess.ModifiedHeight(comp).Clone(),
                LevelDelta = (float[])GameAccess.LevelDelta(comp).Clone(),
                SmoothDelta = (float[])GameAccess.SmoothDelta(comp).Clone(),
                ModifiedPaint = (bool[])GameAccess.ModifiedPaint(comp).Clone(),
                PaintMask = (Color[])GameAccess.PaintMask(comp).Clone()
            };
        }

        private static void Restore(TerrainComp comp, CompSnapshot snapshot)
        {
            Array.Copy(snapshot.ModifiedHeight, GameAccess.ModifiedHeight(comp), snapshot.ModifiedHeight.Length);
            Array.Copy(snapshot.LevelDelta, GameAccess.LevelDelta(comp), snapshot.LevelDelta.Length);
            Array.Copy(snapshot.SmoothDelta, GameAccess.SmoothDelta(comp), snapshot.SmoothDelta.Length);
            Array.Copy(snapshot.ModifiedPaint, GameAccess.ModifiedPaint(comp), snapshot.ModifiedPaint.Length);
            Array.Copy(snapshot.PaintMask, GameAccess.PaintMask(comp), snapshot.PaintMask.Length);
        }
    }
}
