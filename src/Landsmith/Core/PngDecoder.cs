using System;
using System.IO;
using System.IO.Compression;

namespace Landsmith
{
    // Minimal decoder for the mod's own embedded icons: 8-bit RGBA, non-interlaced PNG.
    // Avoids UnityEngine.ImageConversionModule, whose Unity 6 build needs netstandard 2.1 types at compile time.
    internal static class PngDecoder
    {
        private static readonly byte[] Signature = { 137, 80, 78, 71, 13, 10, 26, 10 };
        private const int MaxSide = 1024;

        // Returns RGBA bytes, top row first.
        internal static byte[] DecodeRgba(byte[] png, out int width, out int height)
        {
            width = height = 0;
            if (png == null || png.Length < Signature.Length) throw new InvalidDataException("Not a PNG file.");
            for (int i = 0; i < Signature.Length; i++)
                if (png[i] != Signature[i]) throw new InvalidDataException("Not a PNG file.");

            var compressed = new MemoryStream();
            int position = Signature.Length;
            bool header = false;
            while (position + 8 <= png.Length)
            {
                int length = ReadInt(png, position);
                string type = new string(new[] { (char)png[position + 4], (char)png[position + 5], (char)png[position + 6], (char)png[position + 7] });
                int data = position + 8;
                if (length < 0 || data + length + 4 > png.Length) throw new InvalidDataException("Truncated PNG chunk.");
                if (type == "IHDR")
                {
                    width = ReadInt(png, data);
                    height = ReadInt(png, data + 4);
                    byte depth = png[data + 8], color = png[data + 9], interlace = png[data + 12];
                    if (depth != 8 || color != 6 || interlace != 0) throw new InvalidDataException("Only 8-bit RGBA non-interlaced PNG is supported.");
                    if (width <= 0 || height <= 0 || width > MaxSide || height > MaxSide) throw new InvalidDataException("Unsupported PNG size.");
                    header = true;
                }
                else if (type == "IDAT")
                {
                    compressed.Write(png, data, length);
                }
                else if (type == "IEND")
                {
                    break;
                }
                position = data + length + 4;
            }
            if (!header || compressed.Length < 2) throw new InvalidDataException("PNG without image data.");

            int stride = width * 4;
            var raw = new byte[(stride + 1) * height];
            compressed.Position = 2; // zlib header; the trailing Adler-32 checksum is ignored
            using (var inflater = new DeflateStream(compressed, CompressionMode.Decompress))
            {
                int read = 0;
                while (read < raw.Length)
                {
                    int n = inflater.Read(raw, read, raw.Length - read);
                    if (n <= 0) throw new InvalidDataException("Truncated PNG image data.");
                    read += n;
                }
            }

            var pixels = new byte[stride * height];
            for (int y = 0; y < height; y++)
            {
                int filter = raw[y * (stride + 1)];
                int source = y * (stride + 1) + 1;
                int target = y * stride;
                for (int x = 0; x < stride; x++)
                {
                    int a = x >= 4 ? pixels[target + x - 4] : 0;
                    int b = y > 0 ? pixels[target - stride + x] : 0;
                    int c = x >= 4 && y > 0 ? pixels[target - stride + x - 4] : 0;
                    int value = raw[source + x];
                    switch (filter)
                    {
                        case 0: break;
                        case 1: value += a; break;
                        case 2: value += b; break;
                        case 3: value += (a + b) / 2; break;
                        case 4: value += Paeth(a, b, c); break;
                        default: throw new InvalidDataException("Unknown PNG filter.");
                    }
                    pixels[target + x] = (byte)value;
                }
            }
            return pixels;
        }

        private static int Paeth(int a, int b, int c)
        {
            int p = a + b - c;
            int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
            if (pa <= pb && pa <= pc) return a;
            return pb <= pc ? b : c;
        }

        private static int ReadInt(byte[] data, int offset) =>
            (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];
    }
}
