using System;

namespace Landsmith
{
    // Terrain heights on the world vertex lattice (one vertex per metre at integer x/z coordinates).
    internal sealed class HeightGrid
    {
        internal readonly int MinX;
        internal readonly int MinZ;
        internal readonly int CountX;
        internal readonly int CountZ;
        internal readonly float[] Heights;
        internal readonly bool[] Known;

        internal HeightGrid(int minX, int minZ, int countX, int countZ)
        {
            if (countX <= 0 || countZ <= 0) throw new ArgumentOutOfRangeException(nameof(countX));
            MinX = minX;
            MinZ = minZ;
            CountX = countX;
            CountZ = countZ;
            Heights = new float[countX * countZ];
            Known = new bool[countX * countZ];
        }

        internal int Length => Heights.Length;
        internal int MaxX => MinX + CountX - 1;
        internal int MaxZ => MinZ + CountZ - 1;

        // The lattice box covering the brush, its transition band and an extra margin of vertices.
        internal static HeightGrid Around(Brush brush, int margin)
        {
            double extent = brush.HalfExtent;
            int minX = (int)Math.Floor(brush.CenterX - extent) - margin;
            int maxX = (int)Math.Ceiling(brush.CenterX + extent) + margin;
            int minZ = (int)Math.Floor(brush.CenterZ - extent) - margin;
            int maxZ = (int)Math.Ceiling(brush.CenterZ + extent) + margin;
            return new HeightGrid(minX, minZ, maxX - minX + 1, maxZ - minZ + 1);
        }

        internal bool Contains(int x, int z) => x >= MinX && z >= MinZ && x < MinX + CountX && z < MinZ + CountZ;

        internal int Index(int x, int z) => (z - MinZ) * CountX + (x - MinX);

        internal int WorldX(int index) => MinX + index % CountX;

        internal int WorldZ(int index) => MinZ + index / CountX;

        internal void Set(int x, int z, float height)
        {
            if (!Contains(x, z)) return;
            int i = Index(x, z);
            Heights[i] = height;
            Known[i] = true;
        }

        internal bool AllKnown()
        {
            foreach (bool known in Known)
                if (!known) return false;
            return true;
        }
    }
}
