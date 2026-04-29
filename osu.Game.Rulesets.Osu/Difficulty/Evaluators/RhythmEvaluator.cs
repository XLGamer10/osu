// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Difficulty.Utils;
using osu.Game.Rulesets.Osu.Difficulty.Preprocessing;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.Scoring;

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
        private const double min_bpm_threshold = 210.0;
        private const double rhythm_ratio_multiplier = 12.0;

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
                var osuObjPrev = osuObj.Index > 0 ? (OsuDifficultyHitObject)osuObj.Previous(0) : null;

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

                    // Apply time scaling with additional penalty for low speed rhythm (brute-forceable)
                    double timeScale = 1000.0 / Math.Max(current.DeltaTime, 1.0);

                    double strainThreshold = DifficultyCalculationUtils.BPMToMilliseconds(min_bpm_threshold);

                    double lowEndSuppression = Math.Pow(Math.Min(1.0, strainThreshold / current.DeltaTime), 2.0);

                    // Apply explicit bonus for BPM changes regardless of how likely/unlikely they are in the CTW
                    double currDelta = Math.Max(osuObj.DeltaTime, 1e-7);
                    double prevDelta = osuObjPrev != null ? Math.Max(osuObjPrev.DeltaTime, 1e-7) : currDelta;
                    double deltaDifferenceEpsilon = osuObj.HitWindow(HitResult.Great) * 0.3;

                    double deltaDifference = Math.Max(prevDelta, currDelta) / Math.Min(prevDelta, currDelta);
                    double differenceMultiplier = Math.Clamp(2.0 - deltaDifference / 8.0, 0.0, 1.0);
                    double windowPenalty = osuObjPrev != null
                        ? Math.Min(1, Math.Max(0, Math.Abs(prevDelta - currDelta) - deltaDifferenceEpsilon) / deltaDifferenceEpsilon)
                        : 0;
                    double effectiveRatio = getEffectiveRatio(deltaDifference) * windowPenalty * differenceMultiplier;

                    // Due to the above time scaling, doubletapness must also be used to prevent value explosions for small deltas
                    var osuObjNext = (OsuDifficultyHitObject)osuObj.Next(0);
                    double doubletapness = 1 - osuObj.GetDoubletapness(osuObjNext);

                    totalWeightedComplexity += overall_multiplier * (combined + effectiveRatio) * timeScale * lowEndSuppression * doubletapness;
                }
            }

            // Divide by window size, missing context is treated as simple (0 surprise)
            return totalWeightedComplexity / window_size;
        }

        private static double getEffectiveRatio(double deltaDifference)
        {
            // Take only the fractional part of the value since we're only interested in punishing multiples
            double deltaDifferenceFraction = deltaDifference - Math.Truncate(deltaDifference);

            return 1.0 + rhythm_ratio_multiplier * Math.Min(0.5, DifficultyCalculationUtils.SmoothstepBellCurve(deltaDifferenceFraction));
        }
    }
}
