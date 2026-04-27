// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Osu.Difficulty.Preprocessing;
using osu.Game.Rulesets.Osu.Objects;

namespace osu.Game.Rulesets.Osu.Difficulty.Evaluators
{
    /// <summary>
    /// Compute the local CTW surprise and cross-entropy with respect to an "ideal" prior.
    /// </summary>
    public static class RhythmEvaluator
    {
        private const double overall_multiplier = 1.0;
        private const double surprisal_to_cross_entropy_ratio = 0.5; // Lower value will make this more in favor of cross entropy
        private const int window_size = 4; // Pull this from OsuRhythmDifficultyPreprocessor constants later...

        public static double EvaluateDifficultyOf(DifficultyHitObject current)
        {
            if (current.BaseObject is Spinner)
                return 0;

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
                    double gapSurprisal = isFirstNote ? cluster.GapSurprisal : 0;

                    // Intra-cluster (internal): the sustained difficulty of maintaining the internal rhythm, spread throughout the cluster
                    double internalSurprisal = cluster.InternalSurprisal / cluster.Size;

                    // Parity (resolution): exit shock of the transition
                    double paritySurprisal = isLastNote ? cluster.ParitySurprisal : 0;

                    // We are deliberately not normalizing surprisal by alphabet size (log2) due to parity often having an outsized effect
                    double totalSurprisal = gapSurprisal + internalSurprisal + paritySurprisal;

                    // Likewise for cross-entropy
                    double gapCrossEntropy = isFirstNote ? cluster.GapCrossEntropy : 0;
                    double internalCrossEntropy = cluster.InternalCrossEntropy / cluster.Size;
                    double parityCrossEntropy = isLastNote ? cluster.ParityCrossEntropy : 0;

                    double totalCrossEntropy = gapCrossEntropy + internalCrossEntropy + parityCrossEntropy;

                    // Combine using tunable weights
                    const double surprisal_factor = 1 - 1 / (surprisal_to_cross_entropy_ratio + 1);
                    const double cross_entropy_factor = 1 / (surprisal_to_cross_entropy_ratio + 1);
                    double combined = surprisal_factor * totalSurprisal
                                      + cross_entropy_factor * totalCrossEntropy;

                    // Apply time scaling
                    double timeScale = 1000.0 / Math.Max(current.DeltaTime, 1.0);

                    // Due to the above time scaling, doubletapness must also be used
                    var osuObjNext = (OsuDifficultyHitObject)osuObj.Next(0);
                    double doubletapness = 1 - osuObj.GetDoubletapness(osuObjNext);

                    totalWeightedComplexity += overall_multiplier * combined * timeScale * doubletapness;
                }
            }

            // Divide by window size, missing context is treated as simple (0 surprise)
            return totalWeightedComplexity / window_size;
        }
    }
}
