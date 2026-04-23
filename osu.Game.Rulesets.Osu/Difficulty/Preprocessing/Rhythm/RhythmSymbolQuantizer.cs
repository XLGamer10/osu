// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;

namespace osu.Game.Rulesets.Osu.Difficulty.Preprocessing.Rhythm
{
    public static class RhythmSymbolQuantizer
    {
        // 31 ratio bins evenly spaced in log-space from ln(1/16) to ln(16/1).
        // Covers all beat snap divisor ratios {1..9, 12, 16}.
        public const int RATIO_BIN_COUNT = 31;
        private static readonly double log_ratio_min = Math.Log(1.0 / 16.0);
        private static readonly double log_ratio_max = Math.Log(16.0);
        private static readonly double bin_width = (log_ratio_max - log_ratio_min) / RATIO_BIN_COUNT;

        // Center bin index (ratios of around 1.0)
        private const int center_bin = RATIO_BIN_COUNT / 2;

        public static int QuantizeRatio(double currDelta, double prevDelta, double epsilon)
        {
            // Deltas within the OD hit window are indistinguishable, snap to center
            if (Math.Abs(currDelta - prevDelta) < epsilon)
                return center_bin;

            double ratio = currDelta / prevDelta;
            double logRatio = Math.Log(ratio);

            int bin = (int)Math.Floor((logRatio - log_ratio_min) / bin_width);
            return Math.Clamp(bin, 0, RATIO_BIN_COUNT - 1);
        }

        public static double GetInherentRatioComplexity(double currDelta, double prevDelta, double epsilon)
        {
            // Simplify ratio to irreducible p/q (within epsilon)
            (int p, int q) = FindIrreducibleRatio(currDelta / prevDelta, epsilon);

            // Calculate Euler's Gradus Suavitatis
            int inherentRatioComplexity = calculateEulerGradus(p * q);

            // Scale the result to bring further in line with typical surprisal/entropy values
            return Math.Sqrt(Math.Max(0, inherentRatioComplexity - 1));
        }

        public static (int p, int q) FindIrreducibleRatio(double ratio, double epsilon)
        {
            if (Math.Abs(ratio - 1.0) < epsilon) return (1, 1);

            double x = ratio;
            int n = (int)Math.Floor(x);
            int h1 = 1, h2 = n;
            int k1 = 0, k2 = 1;
            const int max_q = 16;

            while (Math.Abs(x - n) > epsilon && k2 < max_q)
            {
                x = 1.0 / (x - n);
                n = (int)Math.Floor(x);

                int h = n * h2 + h1;
                int k = n * k2 + k1;

                if (k > max_q) break;

                h1 = h2;
                h2 = h;
                k1 = k2;
                k2 = k;
            }

            return (h2, k2);
        }

        private static int calculateEulerGradus(int n)
        {
            int g = 1;

            for (int i = 2; i * i <= n; i++)
            {
                while (n % i == 0)
                {
                    g += i - 1;
                    n /= i;
                }
            }

            if (n > 1) g += n - 1;
            return g;
        }
    }
}
