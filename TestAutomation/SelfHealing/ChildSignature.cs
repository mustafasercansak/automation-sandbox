using System;
using System.Collections.Generic;
using System.Linq;

namespace SelfHealing
{
    // Multiset-Jaccard over two "Type:count|Type:count" child-ControlType signatures
    // (see UiModel.UiElementSnapshot.ComputeChildControlTypeSignature). Both empty -> 1.0
    // (two leaves match); exactly one empty -> 0.0 (a container versus a leaf is a
    // mismatch); otherwise the intersection over the union of the two ControlType multisets.
    internal static class ChildSignature
    {
        public static double Similarity(string? a, string? b)
        {
            var left = Parse(a);
            var right = Parse(b);
            if (left.Count == 0 && right.Count == 0)
            {
                return 1.0;
            }

            if (left.Count == 0 || right.Count == 0)
            {
                return 0.0;
            }

            long intersection = 0;
            long union = 0;
            foreach (var key in left.Keys.Union(right.Keys))
            {
                var x = left.TryGetValue(key, out var xv) ? xv : 0;
                var y = right.TryGetValue(key, out var yv) ? yv : 0;
                intersection += Math.Min(x, y);
                union += Math.Max(x, y);
            }

            return union == 0 ? 1.0 : (double)intersection / union;
        }

        private static Dictionary<string, int> Parse(string? signature)
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(signature))
            {
                return counts;
            }

            foreach (var part in signature!.Split('|'))
            {
                var separator = part.LastIndexOf(':');
                if (separator <= 0)
                {
                    continue;
                }

                var type = part.Substring(0, separator);
                if (int.TryParse(part.Substring(separator + 1), out var n) && n > 0)
                {
                    counts[type] = counts.TryGetValue(type, out var existing) ? existing + n : n;
                }
            }

            return counts;
        }
    }
}
