using System;
using System.Collections.Generic;
using System.IO;
using Landsmith;

internal static class Program
{
    private static int Main()
    {
        var scenarios = new List<(string Name, Action Run)>
        {
            ("CircleWeightsFallOffAcrossTheEdgeBand", CircleWeightsFallOffAcrossTheEdgeBand),
            ("RotatedSquareUsesBrushSpace", RotatedSquareUsesBrushSpace),
            ("GridCoversEveryAffectedVertex", GridCoversEveryAffectedVertex),
            ("LevelReachesTargetAndBlendsEdges", LevelReachesTargetAndBlendsEdges),
            ("OffsetMovesTheExistingProfile", OffsetMovesTheExistingProfile),
            ("SlopeFollowsAngleAndDirection", SlopeFollowsAngleAndDirection),
            ("SlopeStrokesShareOneWorldPlane", SlopeStrokesShareOneWorldPlane),
            ("SmoothFlattensBumpsAndKeepsFlatGround", SmoothFlattensBumpsAndKeepsFlatGround),
            ("ResetIgnoresTheEdgeBand", ResetIgnoresTheEdgeBand),
            ("PaintToolsLeaveHeightsAlone", PaintToolsLeaveHeightsAlone),
            ("UnknownVerticesAreNeverWritten", UnknownVerticesAreNeverWritten),
            ("DeltaMirrorsVanillaLevelSemantics", DeltaMirrorsVanillaLevelSemantics),
            ("StoneFollowsRaisedVolumeAndPavedArea", StoneFollowsRaisedVolumeAndPavedArea),
            ("UndoChainKeepsOlderEditsUndoable", UndoChainKeepsOlderEditsUndoable),
            ("StepsSnapClampAndWrap", StepsSnapClampAndWrap),
            ("ScrollInputBecomesWholeSteps", ScrollInputBecomesWholeSteps),
            ("SlopeAngleListIsValidated", SlopeAngleListIsValidated),
            ("RatioLabelsMatchBuildingAngles", RatioLabelsMatchBuildingAngles),
            ("UndoHistoryIsBounded", UndoHistoryIsBounded),
            ("InvalidNumbersAreContained", InvalidNumbersAreContained),
            ("EmbeddedIconsDecode", EmbeddedIconsDecode),
            ("DecoderRejectsOtherFormats", DecoderRejectsOtherFormats),
        };
        int failed = 0;
        foreach (var (name, run) in scenarios)
        {
            try { run(); Console.WriteLine("PASS " + name); }
            catch (Exception error) { failed++; Console.WriteLine("FAIL " + name + ": " + error.Message); }
        }
        Console.WriteLine(scenarios.Count + " Landsmith scenario(s), " + failed + " failed. No game runtime was exercised.");
        return failed == 0 ? 0 : 1;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static bool Near(double a, double b, double tolerance = 1e-4) => Math.Abs(a - b) <= tolerance;

    private static HeightGrid Flat(Brush brush, float height, int margin = 0)
    {
        var grid = HeightGrid.Around(brush, margin);
        for (int i = 0; i < grid.Length; i++)
        {
            grid.Heights[i] = height;
            grid.Known[i] = true;
        }
        return grid;
    }

    private static (float[] Result, float[] Weight) Run(ShapeRequest request, HeightGrid grid)
    {
        var result = new float[grid.Length];
        var weight = new float[grid.Length];
        Shaper.Compute(request, grid, result, weight);
        return (result, weight);
    }

    private static ShapeRequest Request(ToolKind tool, Brush brush, double level = 0, double offset = 0,
        SlopePlane plane = default, double strength = 0.5, int passes = 2) =>
        new ShapeRequest(tool, brush, level, offset, plane, strength, passes);

    private static void CircleWeightsFallOffAcrossTheEdgeBand()
    {
        var brush = new Brush(BrushShape.Circle, 0, 0, 4, 0, 2);
        Check(brush.Weight(0, 0) == 1 && brush.Weight(4, 0) == 1, "Inside or border vertex not fully affected.");
        Check(brush.Weight(4.5, 0) < 1 && brush.Weight(4.5, 0) > brush.Weight(5, 0) && brush.Weight(5, 0) > brush.Weight(5.5, 0), "Band is not monotonic.");
        Check(Near(brush.Weight(5, 0), 0.5), "Band midpoint should be half weight.");
        Check(brush.Weight(6, 0) == 0 && brush.Weight(9, 0) == 0, "Vertex beyond the band affected.");
        Check(new Brush(BrushShape.Circle, 0, 0, 4, 0, 0).Weight(4.01, 0) == 0, "Hard edge leaked.");
        Check(Near(brush.Reach, 6) && Near(brush.Area, Math.PI * 16), "Circle reach or area wrong.");
    }

    private static void RotatedSquareUsesBrushSpace()
    {
        var square = new Brush(BrushShape.Square, 10, 20, 2, 45, 0);
        Check(square.IsInside(10, 20 + 2.8), "Rotated corner direction excluded.");
        Check(!square.IsInside(12, 22), "Unrotated corner included.");
        square.ToLocal(13, 17, out double lx, out double lz);
        square.ToWorld(lx, lz, out double x, out double z);
        Check(Near(x, 13) && Near(z, 17), "Local/world conversion does not round-trip.");
        var axis = new Brush(BrushShape.Square, 0, 0, 2, 0, 0);
        Check(axis.IsInside(2, 2) && !axis.IsInside(2.01, 0), "Axis-aligned square border wrong.");
        Check(Near(axis.Area, 16) && Near(axis.Reach, 2 * Math.Sqrt(2)), "Square area or reach wrong.");
        Check(Near(square.HalfExtent, 2 * Math.Sqrt(2)), "Rotated bounding box wrong.");
    }

    private static void GridCoversEveryAffectedVertex()
    {
        foreach (var brush in new[]
        {
            new Brush(BrushShape.Circle, 3.3, -7.6, 5.5, 0, 3),
            new Brush(BrushShape.Square, -12.5, 4.2, 4, 30, 2),
            new Brush(BrushShape.Square, 0, 0, 20, 15, 10),
        })
        {
            var grid = HeightGrid.Around(brush, 0);
            int reach = (int)Math.Ceiling(brush.Reach) + 2;
            for (int z = (int)brush.CenterZ - reach; z <= (int)brush.CenterZ + reach; z++)
                for (int x = (int)brush.CenterX - reach; x <= (int)brush.CenterX + reach; x++)
                    if (brush.Weight(x, z) > 0) Check(grid.Contains(x, z), $"Affected vertex {x},{z} outside the grid.");
            Check(grid.Length <= (2 * (int)Math.Ceiling(brush.HalfExtent) + 3) * (2 * (int)Math.Ceiling(brush.HalfExtent) + 3), "Grid larger than needed.");
        }
    }

    private static void LevelReachesTargetAndBlendsEdges()
    {
        var brush = new Brush(BrushShape.Circle, 0, 0, 3, 0, 2);
        var grid = Flat(brush, 10);
        var (result, weight) = Run(Request(ToolKind.Level, brush, level: 12), grid);
        Check(Near(result[grid.Index(0, 0)], 12) && Near(result[grid.Index(3, 0)], 12), "Selection not levelled.");
        float band = result[grid.Index(4, 0)];
        Check(band > 10 && band < 12, "Edge band not blended.");
        Check(weight[grid.Index(5, 0)] == 0 && Near(result[grid.Index(5, 0)], 10), "Vertex beyond the band changed.");
    }

    private static void OffsetMovesTheExistingProfile()
    {
        var brush = new Brush(BrushShape.Square, 0, 0, 2, 0, 0);
        var grid = Flat(brush, 0);
        for (int i = 0; i < grid.Length; i++) grid.Heights[i] = grid.WorldX(i) * 0.5f;
        var (result, _) = Run(Request(ToolKind.Offset, brush, offset: -1.5), grid);
        for (int i = 0; i < grid.Length; i++)
            Check(Near(result[i], grid.Heights[i] - 1.5), "Offset changed the profile shape.");
    }

    private static void SlopeFollowsAngleAndDirection()
    {
        var plane = new SlopePlane(0, 30, 0, 0, Math.Atan(0.5) * 180 / Math.PI);
        Check(Near(plane.HeightAt(0, 2), 31) && Near(plane.HeightAt(0, -4), 28), "Gradient along +z wrong.");
        Check(Near(plane.HeightAt(5, 0), 30), "Height changed across the slope.");
        var east = plane.WithDirection(90);
        Check(Near(east.HeightAt(2, 0), 31) && Near(east.HeightAt(0, 2), 30), "Direction rotation wrong.");
        Check(Near(east.HeightAt(0, 0), 30), "Rotation moved the anchor.");
        Check(Near(new SlopePlane(0, 0, 0, 45, 45).HeightAt(1, 1), Math.Sqrt(2)), "Diagonal 45 degree slope wrong.");
        Check(Near(plane.WithHeight(31).HeightAt(0, 0), 31), "Height adjustment wrong.");
        var brush = new Brush(BrushShape.Square, 0, 0, 3, 0, 0);
        var grid = Flat(brush, 30);
        var (result, _) = Run(Request(ToolKind.Slope, brush, plane: plane), grid);
        Check(Near(result[grid.Index(0, 2)], 31) && Near(result[grid.Index(3, -2)], 29), "Stroke does not follow the plane.");
    }

    private static void SlopeStrokesShareOneWorldPlane()
    {
        var plane = new SlopePlane(0, 10, 0, 30, 26.565);
        var first = new Brush(BrushShape.Circle, 0, 0, 3, 0, 0);
        var second = first.WithCenter(2, 4);
        var a = Flat(first, 0);
        var b = Flat(second, 7);
        var ra = Run(Request(ToolKind.Slope, first, plane: plane), a).Result;
        var rb = Run(Request(ToolKind.Slope, second, plane: plane), b).Result;
        Check(first.IsInside(1, 2) && second.IsInside(1, 2), "Test vertex not shared.");
        double expected = plane.HeightAt(1, 2);
        Check(Near(ra[a.Index(1, 2)], expected) && Near(rb[b.Index(1, 2)], expected), "Strokes from different ground do not meet on the plane.");
        Check(Near(rb[b.Index(4, 6)], plane.HeightAt(4, 6)), "Second stroke not on the plane.");
    }

    private static void SmoothFlattensBumpsAndKeepsFlatGround()
    {
        var brush = new Brush(BrushShape.Circle, 0, 0, 4, 0, 0);
        var flat = Flat(brush, 5, 2);
        var flatResult = Run(Request(ToolKind.Smooth, brush, strength: 1, passes: 2), flat).Result;
        foreach (float h in flatResult) Check(Near(h, 5), "Flat ground changed by smoothing.");
        var bumpy = Flat(brush, 5, 2);
        bumpy.Heights[bumpy.Index(0, 0)] = 9;
        var (result, _) = Run(Request(ToolKind.Smooth, brush, strength: 1, passes: 2), bumpy);
        Check(result[bumpy.Index(0, 0)] < 9 && result[bumpy.Index(0, 0)] > 5, "Spike not reduced.");
        Check(result[bumpy.Index(1, 0)] > 5, "Neighbour did not receive material.");
        var none = Run(Request(ToolKind.Smooth, brush, strength: 0, passes: 3), bumpy).Result;
        Check(Near(none[bumpy.Index(0, 0)], 9), "Zero strength changed terrain.");
    }

    private static void ResetIgnoresTheEdgeBand()
    {
        var brush = new Brush(BrushShape.Circle, 0, 0, 2, 0, 3);
        var grid = Flat(brush, 1);
        var (_, weight) = Run(Request(ToolKind.Reset, brush), grid);
        Check(weight[grid.Index(2, 0)] == 1, "Reset excluded the selection border.");
        Check(weight[grid.Index(3, 0)] == 0 && weight[grid.Index(4, 0)] == 0, "Reset used the edge band.");
    }

    private static void PaintToolsLeaveHeightsAlone()
    {
        var brush = new Brush(BrushShape.Circle, 0, 0, 3, 0, 1);
        var grid = Flat(brush, 2);
        foreach (var tool in new[] { ToolKind.Path, ToolKind.Paved })
        {
            var (result, weight) = Run(Request(tool, brush, level: 50), grid);
            for (int i = 0; i < grid.Length; i++) Check(weight[i] == 0 && result[i] == 2, $"{tool} changed a height.");
            Check(Tools.Paints(tool) && !Tools.ChangesHeight(tool), $"{tool} classified wrongly.");
        }
    }

    private static void UnknownVerticesAreNeverWritten()
    {
        var brush = new Brush(BrushShape.Circle, 0, 0, 2, 0, 0);
        var grid = Flat(brush, 3);
        int hole = grid.Index(0, 0);
        grid.Known[hole] = false;
        Check(!grid.AllKnown(), "Missing vertex not reported.");
        var (result, weight) = Run(Request(ToolKind.Level, brush, level: 8), grid);
        Check(weight[hole] == 0 && result[hole] == 3, "Unknown vertex written.");
    }

    private static void DeltaMirrorsVanillaLevelSemantics()
    {
        float level = 0, smooth = 0;
        Check(TerrainDelta.Apply(10, 12, 8, ref level, ref smooth, out bool clamped) && Near(level, 2) && !clamped, "Simple raise wrong.");
        level = 1; smooth = 0.5f;
        Check(TerrainDelta.Apply(11.5f, 12, 8, ref level, ref smooth, out _) && Near(level, 2) && smooth == 0, "Smooth delta not folded into level.");
        level = 7; smooth = 0;
        Check(TerrainDelta.Apply(17, 30, 8, ref level, ref smooth, out clamped) && Near(level, 8) && clamped, "Limit not applied.");
        level = -8; smooth = 0;
        Check(!TerrainDelta.Apply(2, -5, 8, ref level, ref smooth, out clamped) && clamped, "Clamped no-op reported as change.");
        level = 0.5f; smooth = 0;
        Check(!TerrainDelta.Apply(4.5f, 4.5004f, 8, ref level, ref smooth, out _) && Near(level, 0.5), "Tolerance ignored.");
    }

    private static void StoneFollowsRaisedVolumeAndPavedArea()
    {
        // One vanilla Raise ground click lifts about 8.3 m3 for 2 stone; one Paved road click paints about 15.2 m2 for 1 stone.
        Check(Costs.Stone(8.27, 0.25) == 2 && Costs.Stone(15.2, 0.065) == 1, "Vanilla-sized strokes must cost the vanilla amount.");
        Check(Costs.Stone(1257 * 8, 0.25) == 2514 && Costs.Stone(400, 0.065) == 26, "Large stroke cost wrong.");
        Check(Costs.Stone(0.005, 0.25) == 0, "Rounding noise charged.");
        Check(Costs.Stone(0.5, 0.25) == 1, "A small real change must cost at least one stone.");
        Check(Costs.Stone(100, 0) == 0 && Costs.Stone(-3, 0.25) == 0, "Free rate or negative volume charged.");
        Check(Costs.Stone(double.NaN, 0.25) == 0 && Costs.Stone(10, double.PositiveInfinity) == 0, "Invalid numbers charged.");
        Check(Costs.Stone(1e12, 10) == Costs.MaximumStone, "Cost not capped.");
        foreach (var tool in new[] { ToolKind.Level, ToolKind.Offset, ToolKind.Slope, ToolKind.Reset, ToolKind.Smooth })
            Check(Tools.ChargesForRaising(tool) && !Tools.ChargesForPaving(tool), $"{tool} must charge for raised ground.");
        Check(Tools.ChargesNetVolume(ToolKind.Smooth) && !Tools.ChargesNetVolume(ToolKind.Level), "Net volume classification wrong.");
        Check(!Tools.ChargesForRaising(ToolKind.Path) && !Tools.ChargesForPaving(ToolKind.Path), "Path charged.");
        Check(Tools.ChargesForPaving(ToolKind.Paved) && !Tools.ChargesForRaising(ToolKind.Paved), "Paving cost classification wrong.");
    }

    private static void UndoChainKeepsOlderEditsUndoable()
    {
        // Edit 1 saved revision 10, edit 2 started from 10 and saved 11, undoing edit 2 saved 12.
        Check(UndoChain.NextExpected(10, 10, 12) == 12, "Directly preceding edit not relinked.");
        // Someone else saved 11 between the two edits (edit 2 started from 11): edit 1 must stay blocked.
        Check(UndoChain.NextExpected(10, 11, 12) == 10, "Foreign change ignored.");
        var history = new UndoHistory<string>(5);
        history.Push("a"); history.Push("b"); history.Push("c");
        var order = new List<string>(history.NewestFirst());
        Check(order.Count == 3 && order[0] == "c" && order[2] == "a", "History order wrong.");
    }

    private static void StepsSnapClampAndWrap()
    {
        Check(Near(Steps.Adjust(3.0, 1, 0.5, 0.5, 20), 3.5) && Near(Steps.Adjust(19.9, 3, 0.5, 0.5, 20), 20), "Adjust or clamp wrong.");
        Check(Near(Steps.Adjust(0.5, -4, 0.5, 0.5, 20), 0.5), "Lower bound ignored.");
        Check(Near(Steps.Adjust(31.37, 1, 0.25, -1000, 1000), 31.5), "Value not kept on the step grid.");
        Check(Near(Steps.Snap(-2.6, 0.25), -2.5) && Near(Steps.Snap(0.1 + 0.2, 0.1), 0.3), "Snap wrong.");
        Check(Near(Steps.NormalizeDegrees(-15), 345) && Near(Steps.NormalizeDegrees(720), 0) && Near(Steps.NormalizeDegrees(359.9999999), 0), "Wrap wrong.");
    }

    private static void ScrollInputBecomesWholeSteps()
    {
        double acc = 0;
        Check(Steps.ConsumeScroll(ref acc, 0.05, 0.1) == 0, "Partial scroll applied.");
        Check(Steps.ConsumeScroll(ref acc, 0.06, 0.1) == 1 && acc == 0, "Accumulated scroll lost.");
        Check(Steps.ConsumeScroll(ref acc, -0.35, 0.1) == -1, "Reverse scroll wrong.");
        Check(Steps.ConsumeScroll(ref acc, 120, 0.1) == 1, "Raw notch scale produced more than one step.");
        Check(Steps.ConsumeScroll(ref acc, double.NaN, 0.1) == 0, "Invalid scroll accepted.");
    }

    private static void SlopeAngleListIsValidated()
    {
        var fallback = new[] { 26.57, 45.0 };
        var parsed = Steps.ParseAngles("14.04, 26.57;45 x 90 0 45", fallback);
        Check(parsed.Length == 3 && parsed[0] == 14.04 && parsed[2] == 45, "Angle list parsed wrongly.");
        Check(Steps.ParseAngles("", fallback).Length == 2 && Steps.ParseAngles("abc", fallback)[1] == 45, "Fallback not used.");
    }

    private static void RatioLabelsMatchBuildingAngles()
    {
        Check(Steps.RatioLabel(26.565) == "1:2" && Steps.RatioLabel(45) == "1:1", "Roof ratios wrong.");
        Check(Steps.RatioLabel(63.435) == "2:1" && Steps.RatioLabel(14.036) == "1:4", "Steep or shallow ratio wrong.");
    }

    private static void UndoHistoryIsBounded()
    {
        var history = new UndoHistory<int>(2);
        history.Push(1); history.Push(2); history.Push(3);
        Check(history.Count == 2 && history.TryPop(out int last) && last == 3 && history.TryPop(out int prev) && prev == 2, "Oldest entry not dropped.");
        Check(!history.TryPop(out _), "Empty history returned an entry.");
        var disabled = new UndoHistory<int>(0);
        disabled.Push(1);
        Check(disabled.Count == 0, "Disabled history stored an entry.");
        history.Push(4); history.Push(5); history.Capacity = 1;
        Check(history.Count == 1 && history.TryPop(out int kept) && kept == 5, "Shrinking capacity kept the wrong entry.");
    }

    private static void InvalidNumbersAreContained()
    {
        var brush = new Brush(BrushShape.Circle, 0, 0, double.NaN, double.PositiveInfinity, -3);
        Check(brush.Size == 0 && brush.Edge == 0 && brush.RotationDegrees == 0, "Invalid brush numbers kept.");
        var plane = new SlopePlane(0, 0, 0, double.NaN, 500);
        Check(plane.AngleDegrees == SlopePlane.MaxAngle && plane.DirectionDegrees == 0, "Invalid plane numbers kept.");
        var real = new Brush(BrushShape.Circle, 0, 0, 2, 0, 0);
        var grid = Flat(real, 1);
        var (result, weight) = Run(Request(ToolKind.Level, real, level: double.NaN), grid);
        for (int i = 0; i < grid.Length; i++) Check(weight[i] == 0 && result[i] == 1, "NaN target written.");
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AGENTS.md"))) dir = dir.Parent;
        if (dir == null) throw new InvalidOperationException("Repository root not found.");
        return dir.FullName;
    }

    private static void EmbeddedIconsDecode()
    {
        string folder = Path.Combine(RepoRoot(), "src", "Landsmith", "assets", "icons");
        int count = 0;
        foreach (string file in Directory.GetFiles(folder, "*.png"))
        {
            byte[] rgba = PngDecoder.DecodeRgba(File.ReadAllBytes(file), out int width, out int height);
            Check(width == 128 && height == 128 && rgba.Length == width * height * 4, $"{Path.GetFileName(file)} has an unexpected size.");
            bool opaque = false;
            for (int i = 3; i < rgba.Length; i += 4) opaque |= rgba[i] == 255;
            Check(opaque && rgba[3] == 0, $"{Path.GetFileName(file)} decoded without transparent border or opaque content.");
            count++;
        }
        foreach (ToolKind tool in Tools.All)
            Check(File.Exists(Path.Combine(folder, IconName(tool))), $"Icon for {tool} missing.");
        Check(count == Tools.All.Length, "Unexpected number of icons.");
    }

    private static string IconName(ToolKind tool) => tool.ToString().ToLowerInvariant() + ".png";

    private static void DecoderRejectsOtherFormats()
    {
        foreach (var bad in new[] { new byte[0], new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 }, new byte[] { 137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 13 } })
        {
            bool rejected = false;
            try { PngDecoder.DecodeRgba(bad, out _, out _); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected, "Invalid PNG accepted.");
        }
    }
}
