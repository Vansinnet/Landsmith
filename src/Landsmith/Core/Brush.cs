using System;

namespace Landsmith
{
    internal enum BrushShape
    {
        Circle,
        Square
    }

    // Game-independent brush geometry on the horizontal x/z plane, in metres.
    // Rotation follows Unity yaw: degrees clockwise from +z when seen from above.
    internal readonly struct Brush
    {
        private const double InsideTolerance = 1e-6;

        internal readonly BrushShape Shape;
        internal readonly double CenterX;
        internal readonly double CenterZ;
        // Circle radius, or half the side length of the square.
        internal readonly double Size;
        internal readonly double RotationDegrees;
        // Width of the soft transition band outside the selection; 0 gives a hard edge.
        internal readonly double Edge;
        private readonly double cos;
        private readonly double sin;

        internal Brush(BrushShape shape, double centerX, double centerZ, double size, double rotationDegrees, double edge)
        {
            Shape = shape;
            CenterX = centerX;
            CenterZ = centerZ;
            Size = Finite(size) ? Math.Max(0, size) : 0;
            Edge = Finite(edge) ? Math.Max(0, edge) : 0;
            RotationDegrees = Finite(rotationDegrees) ? rotationDegrees : 0;
            double radians = RotationDegrees * Math.PI / 180;
            cos = Math.Cos(radians);
            sin = Math.Sin(radians);
        }

        // Selection area without the transition band, in square metres.
        internal double Area => Shape == BrushShape.Circle ? Math.PI * Size * Size : 4 * Size * Size;

        // Largest horizontal distance from the centre that can be affected, including the transition band.
        internal double Reach => (Shape == BrushShape.Circle ? Size : Size * Math.Sqrt(2)) + Edge;

        // Half extent of the axis-aligned bounding box, including the transition band.
        internal double HalfExtent => (Shape == BrushShape.Circle ? Size : Size * (Math.Abs(cos) + Math.Abs(sin))) + Edge;

        internal Brush WithCenter(double x, double z) => new Brush(Shape, x, z, Size, RotationDegrees, Edge);

        internal Brush WithoutEdge() => new Brush(Shape, CenterX, CenterZ, Size, RotationDegrees, 0);

        // Brush-local coordinates: x to the right, z forwards along the rotation.
        internal void ToLocal(double x, double z, out double localX, out double localZ)
        {
            double dx = x - CenterX;
            double dz = z - CenterZ;
            localX = dx * cos - dz * sin;
            localZ = dx * sin + dz * cos;
        }

        internal void ToWorld(double localX, double localZ, out double x, out double z)
        {
            x = CenterX + localX * cos + localZ * sin;
            z = CenterZ - localX * sin + localZ * cos;
        }

        // Distance outside the selection; 0 inside or on the border.
        internal double OutsideDistance(double x, double z)
        {
            if (Shape == BrushShape.Circle)
            {
                double dx = x - CenterX;
                double dz = z - CenterZ;
                return Math.Max(0, Math.Sqrt(dx * dx + dz * dz) - Size);
            }
            ToLocal(x, z, out double lx, out double lz);
            double ox = Math.Max(0, Math.Abs(lx) - Size);
            double oz = Math.Max(0, Math.Abs(lz) - Size);
            return Math.Sqrt(ox * ox + oz * oz);
        }

        internal bool IsInside(double x, double z) => OutsideDistance(x, z) <= InsideTolerance;

        // 1 inside the selection, a smooth falloff across the transition band and 0 beyond it.
        internal double Weight(double x, double z)
        {
            double distance = OutsideDistance(x, z);
            if (distance <= InsideTolerance) return 1;
            if (Edge <= 0 || distance >= Edge) return 0;
            double t = 1 - distance / Edge;
            return t * t * (3 - 2 * t);
        }

        internal static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
