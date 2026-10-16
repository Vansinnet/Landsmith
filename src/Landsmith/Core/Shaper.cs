using System;

namespace Landsmith
{
    internal enum ToolKind
    {
        Level,
        Offset,
        Smooth,
        Slope,
        Path,
        Paved,
        Reset
    }

    internal static class Tools
    {
        internal static readonly ToolKind[] All =
        {
            ToolKind.Level, ToolKind.Offset, ToolKind.Smooth, ToolKind.Slope, ToolKind.Path, ToolKind.Paved, ToolKind.Reset
        };

        internal static bool ChangesHeight(ToolKind tool) =>
            tool == ToolKind.Level || tool == ToolKind.Offset || tool == ToolKind.Smooth || tool == ToolKind.Slope || tool == ToolKind.Reset;

        internal static bool Paints(ToolKind tool) => tool == ToolKind.Path || tool == ToolKind.Paved;

        // Reset restores exactly the selection; a transition band would leave partial edits behind.
        internal static bool UsesEdges(ToolKind tool) => tool != ToolKind.Reset;

        // Raising ground costs stone per cubic metre.
        internal static bool ChargesForRaising(ToolKind tool) =>
            tool == ToolKind.Level || tool == ToolKind.Offset || tool == ToolKind.Slope || tool == ToolKind.Reset || tool == ToolKind.Smooth;

        // Smooth moves ground around inside the brush; only what it adds overall (raised minus lowered) is charged.
        internal static bool ChargesNetVolume(ToolKind tool) => tool == ToolKind.Smooth;

        internal static bool ChargesForPaving(ToolKind tool) => tool == ToolKind.Paved;
    }

    // A plane rising along a horizontal direction. Direction follows Unity yaw (degrees clockwise from +z).
    internal readonly struct SlopePlane
    {
        internal const double MinAngle = 1;
        internal const double MaxAngle = 80;

        internal readonly double AnchorX;
        internal readonly double AnchorY;
        internal readonly double AnchorZ;
        internal readonly double DirectionDegrees;
        internal readonly double AngleDegrees;
        private readonly double gradient;
        private readonly double dirX;
        private readonly double dirZ;

        internal SlopePlane(double anchorX, double anchorY, double anchorZ, double directionDegrees, double angleDegrees)
        {
            AnchorX = anchorX;
            AnchorY = anchorY;
            AnchorZ = anchorZ;
            DirectionDegrees = Steps.NormalizeDegrees(directionDegrees);
            AngleDegrees = Math.Min(MaxAngle, Math.Max(MinAngle, Brush.Finite(angleDegrees) ? angleDegrees : MinAngle));
            gradient = Math.Tan(AngleDegrees * Math.PI / 180);
            double radians = DirectionDegrees * Math.PI / 180;
            dirX = Math.Sin(radians);
            dirZ = Math.Cos(radians);
        }

        internal double Gradient => gradient;

        internal double HeightAt(double x, double z) => AnchorY + gradient * ((x - AnchorX) * dirX + (z - AnchorZ) * dirZ);

        internal SlopePlane WithHeight(double anchorY) => new SlopePlane(AnchorX, anchorY, AnchorZ, DirectionDegrees, AngleDegrees);

        internal SlopePlane WithAngle(double angle) => new SlopePlane(AnchorX, AnchorY, AnchorZ, DirectionDegrees, angle);

        // Rotates around the anchor, so the plane keeps passing through it.
        internal SlopePlane WithDirection(double directionDegrees) => new SlopePlane(AnchorX, AnchorY, AnchorZ, directionDegrees, AngleDegrees);
    }

    internal readonly struct ShapeRequest
    {
        internal readonly ToolKind Tool;
        internal readonly Brush Brush;
        internal readonly double LevelHeight;
        internal readonly double Offset;
        internal readonly SlopePlane Plane;
        internal readonly double SmoothStrength;
        internal readonly int SmoothPasses;

        internal ShapeRequest(ToolKind tool, Brush brush, double levelHeight, double offset, SlopePlane plane,
            double smoothStrength, int smoothPasses)
        {
            Tool = tool;
            Brush = brush;
            LevelHeight = levelHeight;
            Offset = offset;
            Plane = plane;
            SmoothStrength = Math.Min(1, Math.Max(0, Brush.Finite(smoothStrength) ? smoothStrength : 0));
            SmoothPasses = Math.Min(Shaper.MaxSmoothPasses, Math.Max(1, smoothPasses));
        }
    }

    // Computes the complete result of one brush stroke in a single pass over the vertex lattice.
    internal static class Shaper
    {
        internal const int MaxSmoothPasses = 8;

        // Extra lattice vertices needed around the brush so smoothing sees real neighbours.
        internal static int MarginFor(in ShapeRequest request) => request.Tool == ToolKind.Smooth ? request.SmoothPasses : 0;

        // result: the new height of every grid vertex; weight: its influence from 0 (untouched) to 1.
        internal static void Compute(in ShapeRequest request, HeightGrid grid, float[] result, float[] weight)
        {
            if (result.Length != grid.Length || weight.Length != grid.Length) throw new ArgumentException("Output size mismatch.");
            float[] smoothed = request.Tool == ToolKind.Smooth ? Smooth(grid, request.SmoothStrength, request.SmoothPasses) : null;
            Brush brush = Tools.UsesEdges(request.Tool) ? request.Brush : request.Brush.WithoutEdge();
            bool shapes = Tools.ChangesHeight(request.Tool);
            for (int i = 0; i < grid.Length; i++)
            {
                float height = grid.Heights[i];
                result[i] = height;
                weight[i] = 0;
                if (!shapes || !grid.Known[i]) continue;
                int x = grid.WorldX(i);
                int z = grid.WorldZ(i);
                double w = brush.Weight(x, z);
                if (w <= 0) continue;
                double target;
                switch (request.Tool)
                {
                    case ToolKind.Level: target = request.LevelHeight; break;
                    case ToolKind.Offset: target = height + request.Offset; break;
                    case ToolKind.Slope: target = request.Plane.HeightAt(x, z); break;
                    case ToolKind.Smooth: target = smoothed[i]; break;
                    default: target = height; break;
                }
                if (!Brush.Finite(target)) continue;
                result[i] = (float)(height + w * (target - height));
                weight[i] = (float)w;
            }
        }

        // Repeated 3x3 binomial blur over known vertices. The grid border uses the vertices that exist.
        internal static float[] Smooth(HeightGrid grid, double strength, int passes)
        {
            var current = (float[])grid.Heights.Clone();
            var next = new float[current.Length];
            for (int pass = 0; pass < passes; pass++)
            {
                for (int cz = 0; cz < grid.CountZ; cz++)
                {
                    for (int cx = 0; cx < grid.CountX; cx++)
                    {
                        int center = cz * grid.CountX + cx;
                        if (!grid.Known[center])
                        {
                            next[center] = current[center];
                            continue;
                        }
                        double sum = 0;
                        double total = 0;
                        for (int dz = -1; dz <= 1; dz++)
                        {
                            int z = cz + dz;
                            if (z < 0 || z >= grid.CountZ) continue;
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                int x = cx + dx;
                                if (x < 0 || x >= grid.CountX) continue;
                                int i = z * grid.CountX + x;
                                if (!grid.Known[i]) continue;
                                double k = (dx == 0 ? 2 : 1) * (dz == 0 ? 2 : 1);
                                sum += current[i] * k;
                                total += k;
                            }
                        }
                        double average = sum / total;
                        next[center] = (float)(current[center] + strength * (average - current[center]));
                    }
                }
                var swap = current;
                current = next;
                next = swap;
            }
            return current;
        }
    }

    // Mirrors the stored-delta semantics of a vanilla level operation: afterwards the vertex sits at the target,
    // unless the per-vertex limit (8 m from the generated ground in vanilla) clamps it.
    internal static class TerrainDelta
    {
        internal const float ChangeTolerance = 0.001f;

        internal static bool Apply(float current, float target, float maxDelta, ref float level, ref float smooth, out bool clamped)
        {
            float wanted = level + smooth + (target - current);
            float stored = Math.Min(maxDelta, Math.Max(-maxDelta, wanted));
            clamped = Math.Abs(stored - wanted) > ChangeTolerance;
            bool changed = Math.Abs(stored - (level + smooth)) > ChangeTolerance;
            if (!changed) return false;
            level = stored;
            smooth = 0;
            return true;
        }
    }

    internal static class Costs
    {
        // Ignores rounding noise from vertices that barely move.
        internal const double MinimumQuantity = 0.01;
        internal const int MaximumStone = 99999;

        // Stone for a stroke: raised volume (cubic metres) or paved area (square metres) times the configured rate,
        // rounded to the nearest stone and at least one for any real change.
        internal static int Stone(double quantity, double perUnit)
        {
            if (!Brush.Finite(quantity) || !Brush.Finite(perUnit) || !(perUnit > 0) || !(quantity > MinimumQuantity)) return 0;
            double stone = Math.Round(quantity * perUnit, MidpointRounding.AwayFromZero);
            return (int)Math.Min(MaximumStone, Math.Max(1, stone));
        }
    }

    // After an edit is undone the terrain holds the data the previous edit of the same zone saved. If that previous
    // edit was directly followed by the undone one, it may be undone next: its expected revision becomes the new one.
    internal static class UndoChain
    {
        internal static uint NextExpected(uint olderSaved, uint undoneBase, uint restored) =>
            olderSaved == undoneBase ? restored : olderSaved;
    }
}
