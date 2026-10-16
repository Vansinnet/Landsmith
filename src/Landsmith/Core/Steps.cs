using System;
using System.Collections.Generic;
using System.Globalization;

namespace Landsmith
{
    internal static class Steps
    {
        // Moves a value by whole steps, keeps it on the step grid and inside the range.
        internal static double Adjust(double value, int steps, double step, double min, double max)
        {
            if (!(step > 0) || !Brush.Finite(value)) return Clamp(value, min, max);
            double moved = Snap(value + steps * step, step);
            return Clamp(moved, min, max);
        }

        internal static double Snap(double value, double step)
        {
            if (!(step > 0) || !Brush.Finite(value)) return value;
            return Math.Round(Math.Round(value / step) * step, 6);
        }

        internal static double Clamp(double value, double min, double max)
        {
            if (double.IsNaN(value)) return min;
            return Math.Min(max, Math.Max(min, value));
        }

        internal static double NormalizeDegrees(double degrees)
        {
            if (!Brush.Finite(degrees)) return 0;
            double result = degrees % 360;
            if (result < 0) result += 360;
            return Math.Round(result, 6) >= 360 ? 0 : result;
        }

        // Mirrors the vanilla piece-rotation scroll handling: input accumulates until it passes the threshold,
        // then exactly one step is taken and the accumulator restarts. Independent of the device's scroll scale.
        internal static int ConsumeScroll(ref double accumulated, double delta, double threshold)
        {
            if (!Brush.Finite(delta) || !(threshold > 0)) return 0;
            accumulated += delta;
            if (accumulated > threshold)
            {
                accumulated = 0;
                return 1;
            }
            if (accumulated < -threshold)
            {
                accumulated = 0;
                return -1;
            }
            return 0;
        }

        // Parses a comma-separated list of slope angles in degrees. Invalid entries are skipped.
        internal static double[] ParseAngles(string text, double[] fallback)
        {
            var result = new List<double>();
            if (!string.IsNullOrEmpty(text))
            {
                foreach (string part in text.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (!double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out double angle)) continue;
                    if (angle < SlopePlane.MinAngle || angle > SlopePlane.MaxAngle) continue;
                    if (!result.Contains(angle)) result.Add(angle);
                }
            }
            return result.Count > 0 ? result.ToArray() : (double[])fallback.Clone();
        }

        // Rise over run as a short ratio label, for example 26.57 degrees -> "1:2".
        internal static string RatioLabel(double angleDegrees)
        {
            double gradient = Math.Tan(angleDegrees * Math.PI / 180);
            if (!(gradient > 0) || !Brush.Finite(gradient)) return "-";
            if (gradient >= 1)
            {
                double rise = gradient;
                return Math.Abs(rise - Math.Round(rise)) < 0.02 ? $"{Math.Round(rise):0}:1" : $"{rise.ToString("0.##", CultureInfo.InvariantCulture)}:1";
            }
            double run = 1 / gradient;
            return Math.Abs(run - Math.Round(run)) < 0.02 ? $"1:{Math.Round(run):0}" : $"1:{run.ToString("0.##", CultureInfo.InvariantCulture)}";
        }
    }

    // Bounded last-in-first-out history. The oldest entry is dropped when the capacity is exceeded.
    internal sealed class UndoHistory<T>
    {
        private readonly LinkedList<T> entries = new LinkedList<T>();
        private int capacity;

        internal UndoHistory(int capacity)
        {
            this.capacity = Math.Max(0, capacity);
        }

        internal int Count => entries.Count;

        internal int Capacity
        {
            get => capacity;
            set
            {
                capacity = Math.Max(0, value);
                while (entries.Count > capacity) entries.RemoveFirst();
            }
        }

        internal void Push(T entry)
        {
            if (capacity == 0) return;
            entries.AddLast(entry);
            while (entries.Count > capacity) entries.RemoveFirst();
        }

        internal bool TryPop(out T entry)
        {
            if (entries.Count == 0)
            {
                entry = default;
                return false;
            }
            entry = entries.Last.Value;
            entries.RemoveLast();
            return true;
        }

        internal void Clear() => entries.Clear();

        internal IEnumerable<T> NewestFirst()
        {
            for (LinkedListNode<T> node = entries.Last; node != null; node = node.Previous)
                yield return node.Value;
        }
    }
}
