// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Osu.Difficulty.Preprocessing;
using osu.Game.Rulesets.Osu.Objects;

namespace osu.Game.Rulesets.Osu.Difficulty.Evaluators
{
    /// <summary>
    /// Compute the local CTW surprise and associated entropy at node.
    /// </summary>
    public static class RhythmEvaluator
    {
        private const double surprisal_factor = 1.0;
        private const double entropy_factor = 0.2;
        private const int window_size = 8; // Pull this from OsuRhythmDifficultyPreprocessor constants later...

        public static double EvaluateDifficultyOf(DifficultyHitObject current)
        {
            if (current.BaseObject is Spinner)
                return 0;

            var osuCurr = (OsuDifficultyHitObject)current;
            double totalWeightedComplexity = 0;

            for (int i = 0; i < window_size; i++)
            {
                var obj = i == 0 ? current : current.Index >= i ? current.Previous(i - 1) : null;
                if (obj == null || obj.BaseObject is Spinner) continue;

                var osuObj = (OsuDifficultyHitObject)obj;

                foreach (var cluster in osuObj.RhythmClusters)
                {
                    // Check note position relative to the cluster
                    bool isFirstNote = Math.Abs(osuObj.StartTime - cluster.StartTime) < 1e-7;
                    bool isLastNote = Math.Abs(osuObj.StartTime - cluster.EndTime) < 1e-7;

                    // Inter-cluster (gap): entry shock of the transition
                    double gapScore = isFirstNote ? cluster.GapSurprisal : 0;

                    // Intra-cluster (internal): the sustained difficulty of maintaining the internal rhythm, spread throughout the cluster
                    double internalScore = cluster.InternalSurprisal / cluster.Size;

                    // Parity (resolution): exit shock of the transition
                    double parityScore = isLastNote ? cluster.ParitySurprisal : 0;

                    // We are deliberately not normalizing surprisal by alphabet size (lod2) due to parity often having an outsized effect
                    double normSurprisal = gapScore + internalScore + parityScore;

                    // Amortize entropy at node across the cluster size
                    double normNodalEntropy = (cluster.ParityEntropy
                                               + cluster.GapEntropy
                                               + cluster.InternalEntropy) / cluster.Size;

                    // Combine using tunable weights
                    double combined = surprisal_factor * normSurprisal
                                      + entropy_factor * normNodalEntropy;

                    // Apply time scaling
                    double timeScale = 1000.0 / Math.Max(current.DeltaTime, 1.0);

                    totalWeightedComplexity += combined * timeScale;
                }
            }

            // Divide by window size, missing context is treated as simple (0 surprise)
            return totalWeightedComplexity / window_size;
        }
    }
}
