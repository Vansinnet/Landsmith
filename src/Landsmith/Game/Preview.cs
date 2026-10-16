using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Landsmith
{
    // Outline preview drawn with a few line renderers. Cost depends on the outline length, not the brush area,
    // and points are only recomputed when the brush, the aim or the terrain sample changes.
    internal sealed class Preview
    {
        private const float Lift = 0.12f;
        private const double Spacing = 0.5;
        private const int MaxPoints = 400;

        private static readonly Color OutlineColor = new Color(1f, 0.93f, 0.7f, 0.95f);
        private static readonly Color BlockedColor = new Color(1f, 0.35f, 0.3f, 0.95f);
        private static readonly Color BandColor = new Color(1f, 1f, 1f, 0.35f);
        private static readonly Color SurfaceColor = new Color(0.45f, 0.85f, 1f, 0.95f);
        private static readonly Color ArrowColor = new Color(1f, 0.72f, 0.2f, 1f);

        private readonly GameObject root;
        private readonly LineRenderer outline;
        private readonly LineRenderer band;
        private readonly LineRenderer surface;
        private readonly LineRenderer arrow;
        private readonly Material material;
        private readonly Vector3[] buffer = new Vector3[MaxPoints + 1];
        private Heightmap lastHeightmap;

        internal bool Available => material;

        internal Preview()
        {
            root = new GameObject("Landsmith_Preview");
            UnityEngine.Object.DontDestroyOnLoad(root);
            material = CreateMaterial();
            outline = CreateLine("Outline", 0.09f, true);
            band = CreateLine("Edge", 0.05f, true);
            surface = CreateLine("Surface", 0.07f, true);
            arrow = CreateLine("Arrow", 0.12f, false);
            Hide();
        }

        internal void Destroy()
        {
            if (root) UnityEngine.Object.Destroy(root);
            if (material) UnityEngine.Object.Destroy(material);
        }

        internal void Hide()
        {
            if (root && root.activeSelf) root.SetActive(false);
        }

        // surfaceHeight: planned height at a point, or null to skip the planned-surface line.
        internal void Show(Brush brush, bool blocked, bool showBand, Func<double, double, double> surfaceHeight, SlopePlane? plane)
        {
            if (!root || !material) return;
            if (!root.activeSelf) root.SetActive(true);
            Color main = blocked ? BlockedColor : OutlineColor;
            int count = BuildOutline(brush, 0, null);
            Apply(outline, count, main);

            if (showBand && brush.Edge > 0)
                Apply(band, BuildOutline(brush.WithoutEdge(), brush.Edge, null), BandColor);
            else
                band.positionCount = 0;

            if (surfaceHeight != null)
                Apply(surface, BuildOutline(brush.WithoutEdge(), 0, surfaceHeight), SurfaceColor);
            else
                surface.positionCount = 0;

            if (plane.HasValue)
                Apply(arrow, BuildArrow(brush, plane.Value), ArrowColor);
            else
                arrow.positionCount = 0;
        }

        private void Apply(LineRenderer line, int count, Color color)
        {
            line.startColor = color;
            line.endColor = color;
            line.positionCount = count;
            for (int i = 0; i < count; i++) line.SetPosition(i, buffer[i]);
        }

        // Points along the selection border, optionally pushed outwards by `grow` (rounded square corners).
        private int BuildOutline(Brush brush, double grow, Func<double, double, double> height)
        {
            int count = 0;
            if (brush.Shape == BrushShape.Circle)
            {
                double radius = brush.Size + grow;
                int n = Clamp((int)Math.Ceiling(2 * Math.PI * radius / Spacing), 24, MaxPoints);
                for (int i = 0; i < n; i++)
                {
                    double a = i * 2 * Math.PI / n;
                    count = Add(count, brush.CenterX + Math.Sin(a) * radius, brush.CenterZ + Math.Cos(a) * radius, height);
                }
                return count;
            }
            double s = brush.Size;
            int perSide = Clamp((int)Math.Ceiling((2 * s) / Spacing), 2, MaxPoints / 8);
            int perCorner = grow > 0 ? Clamp((int)Math.Ceiling(Math.PI * grow / 2 / Spacing), 2, MaxPoints / 8) : 0;
            // Corners counter-clockwise in brush space; each side lies along the outward normal at its corner's start angle.
            double[] cornerX = { s, -s, -s, s };
            double[] cornerZ = { s, s, -s, -s };
            for (int c = 0; c < 4; c++)
            {
                int previous = (c + 3) % 4;
                double start = c * Math.PI / 2;
                double nx = Math.Cos(start) * grow;
                double nz = Math.Sin(start) * grow;
                for (int k = 0; k < perSide; k++)
                {
                    double t = (double)k / perSide;
                    double lx = cornerX[previous] + nx + (cornerX[c] - cornerX[previous]) * t;
                    double lz = cornerZ[previous] + nz + (cornerZ[c] - cornerZ[previous]) * t;
                    brush.ToWorld(lx, lz, out double wx, out double wz);
                    count = Add(count, wx, wz, height);
                }
                for (int k = 0; k <= perCorner && grow > 0; k++)
                {
                    double a = start + (Math.PI / 2) * k / perCorner;
                    brush.ToWorld(cornerX[c] + Math.Cos(a) * grow, cornerZ[c] + Math.Sin(a) * grow, out double wx, out double wz);
                    count = Add(count, wx, wz, height);
                }
            }
            return count;
        }

        private int BuildArrow(Brush brush, SlopePlane plane)
        {
            double length = Math.Max(1.5, Math.Min(brush.Size, 4));
            double radians = plane.DirectionDegrees * Math.PI / 180;
            double dx = Math.Sin(radians);
            double dz = Math.Cos(radians);
            double tipX = brush.CenterX + dx * length * 0.5;
            double tipZ = brush.CenterZ + dz * length * 0.5;
            double tailX = brush.CenterX - dx * length * 0.5;
            double tailZ = brush.CenterZ - dz * length * 0.5;
            double wing = length * 0.3;
            double leftX = tipX - dx * wing - dz * wing * 0.6;
            double leftZ = tipZ - dz * wing + dx * wing * 0.6;
            double rightX = tipX - dx * wing + dz * wing * 0.6;
            double rightZ = tipZ - dz * wing - dx * wing * 0.6;
            Func<double, double, double> height = plane.HeightAt;
            int count = 0;
            count = Add(count, tailX, tailZ, height);
            count = Add(count, tipX, tipZ, height);
            count = Add(count, leftX, leftZ, height);
            count = Add(count, tipX, tipZ, height);
            count = Add(count, rightX, rightZ, height);
            return count;
        }

        private int Add(int count, double x, double z, Func<double, double, double> height)
        {
            if (count >= MaxPoints) return count;
            float y;
            if (height != null)
            {
                double planned = height(x, z);
                if (!Brush.Finite(planned)) return count;
                y = (float)planned + Lift * 0.5f;
            }
            else
            {
                if (!TrySampleGround((float)x, (float)z, out y)) return count;
                y += Lift;
            }
            buffer[count] = new Vector3((float)x, y, (float)z);
            return count + 1;
        }

        // Bilinear height between the four surrounding terrain vertices.
        internal bool TrySampleGround(float x, float z, out float height)
        {
            var point = new Vector3(x, 0, z);
            Heightmap heightmap = lastHeightmap && lastHeightmap.IsPointInside(point) ? lastHeightmap : Heightmap.FindHeightmap(point);
            height = 0;
            if (!heightmap) return false;
            lastHeightmap = heightmap;
            Vector3 origin = heightmap.transform.position;
            int width = heightmap.m_width;
            float scale = heightmap.m_scale;
            float lx = (x - origin.x) / scale + width / 2;
            float lz = (z - origin.z) / scale + width / 2;
            int ix = Mathf.Clamp(Mathf.FloorToInt(lx), 0, width - 1);
            int iz = Mathf.Clamp(Mathf.FloorToInt(lz), 0, width - 1);
            float fx = Mathf.Clamp01(lx - ix);
            float fz = Mathf.Clamp01(lz - iz);
            float h00 = heightmap.GetHeight(ix, iz);
            float h10 = heightmap.GetHeight(ix + 1, iz);
            float h01 = heightmap.GetHeight(ix, iz + 1);
            float h11 = heightmap.GetHeight(ix + 1, iz + 1);
            height = Mathf.Lerp(Mathf.Lerp(h00, h10, fx), Mathf.Lerp(h01, h11, fx), fz) + origin.y;
            return true;
        }

        private LineRenderer CreateLine(string name, float width, bool loop)
        {
            var child = new GameObject(name);
            child.transform.SetParent(root.transform, false);
            LineRenderer line = child.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.loop = loop;
            line.widthMultiplier = width;
            line.numCornerVertices = 0;
            line.numCapVertices = 0;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.positionCount = 0;
            if (material) line.sharedMaterial = material;
            return line;
        }

        private static Material CreateMaterial()
        {
            foreach (string name in new[] { "Sprites/Default", "Hidden/Internal-Colored", "UI/Default" })
            {
                Shader shader = Shader.Find(name);
                if (!shader) continue;
                return new Material(shader) { name = "Landsmith_Preview" };
            }
            return null;
        }

        private static int Clamp(int value, int min, int max) => Math.Min(max, Math.Max(min, value));
    }
}
