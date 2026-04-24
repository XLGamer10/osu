// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Osu.Difficulty.Preprocessing;
using osu.Game.Rulesets.Osu.Objects;

namespace osu.Game.Rulesets.Osu.Difficulty.Evaluators
{
    /// <summary>
    /// Computes the local entropy rate: the mean CTW surprise over the model's context window.
    /// This measures sustained rhythmic complexity rather than single-note surprise.
    /// </summary>
    public static class RhythmEvaluator
    {
        private const double surprisal_factor = 25.0;
        private const double entropy_factor = 5.0;
        private const int window_size = 8; // Pull this from OsuRhythmDifficultyPreprocessor constants

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
                    // Normal surprisals by log2 of alphabet size (for 31, this is ~4.95) to put them on a 0-1 scale
                    double normSurprisal = cluster.ParitySurprisal / 1.0
                                           + cluster.GapSurprisal / 4.95
                                           + cluster.InternalSurprisal / 4.95;

                    // Same for entropies
                    double normEntropy = cluster.ParityEntropy / 1.0
                                         + cluster.GapEntropy / 4.95
                                         + cluster.InternalEntropy / 4.95;

                    // Combine using tunable weights
                    double combined = surprisal_factor * normSurprisal
                                      + entropy_factor * normEntropy;

                    // Apply time scaling (there has to be a better way...)
                    double timeScale = 1000.0 / Math.Max(current.DeltaTime, 1.0);

                    totalWeightedComplexity += combined * timeScale / Math.Max(1, cluster.Size);
                }
            }

            return totalWeightedComplexity / window_size;
        }
    }
}
