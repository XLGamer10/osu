// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Osu.Difficulty.Preprocessing;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.Osu.Difficulty.Evaluators.Speed
{
    public static class RhythmEvaluator
    {
        private const double jerk_balancing_factor = 0.220; // Increase this value to make values more based
        private const double jerk_time_constant = 400.0; // 400ms - Time constant for the "strain" decay of the jerk
        private const double periodicity_sharpness = 0.7; // Determines how generally aggressive periodicity nerfs are, higher values are more aggressive
        private const double compression_exponent = 0.6; // Reflects the nonlinear perception of finger control effort, more relevant at higher BPMs

        /// <summary>
        /// Calculates a rhythm multiplier for the difficulty of the tap associated with historic rhythmic interval data of the current <see cref="OsuDifficultyHitObject"/>.
        /// Uses jerk (derivative of acceleration/force) to model muscle confusion, which is defined here as the derivative of Speed values for the object.
        /// Note that this specifically does NOT model cognitive difficulty and is a very "1st-order" measurement of physical finger control.
        /// </summary>
        public static double EvaluateDifficultyOf(DifficultyHitObject current)
        {
            if (current.BaseObject is Spinner)
                return 0;

            var osuCurrObj = (OsuDifficultyHitObject)current;
            var osuPrevObj = (OsuDifficultyHitObject?)current.Previous(0);

            double nextDoubletapness = osuCurrObj.GetDoubletapness((OsuDifficultyHitObject?)osuCurrObj.Next(0));
            double prevDoubletapness = osuPrevObj?.GetDoubletapness(osuCurrObj) ?? 0;
            double doubletapness = 1.0 - Math.Max(nextDoubletapness, prevDoubletapness); // This is done because for doubletaps, extreme jerk appears BEFORE the note with high doubletapness
            double epsilon = current.HitWindow(HitResult.Great) * 0.4;

            // Exponentially decay the jerk strain, and decay the rhythmic strain by a fixed amount
            osuCurrObj.History.Decay(osuCurrObj.AdjustedDeltaTime, jerk_time_constant);

            // Do not calculate a jerk if there is no previous note
            if (osuPrevObj == null) return 0;

            // Calculate the current rhythmic ratio
            double currentRatio = osuCurrObj.AdjustedDeltaTime / Math.Max(osuPrevObj.AdjustedDeltaTime, 1.0);

            // Catch-all nerf for repetitive patterns which players can optimize, minimizing the jerk of the required motion
            double periodicityMultiplier = periodicityNerf(osuCurrObj, currentRatio);

            // Calculate the jerk of the required motion based off of current and previous speed difficulty, scaled by DeltaTime and a rhythmic interval factor
            double rawJerk = calculateJerk(osuCurrObj.History.BasePower, osuPrevObj.History.BasePower, osuCurrObj.AdjustedDeltaTime, epsilon, currentRatio, osuPrevObj.BaseObject is Slider);

            // Accumulate jerk difficulty with all added multipliers
            double jerkDifficulty = rawJerk * periodicityMultiplier;
            osuCurrObj.History.JerkStrain += jerkDifficulty * doubletapness;

            // Save the current ratio and hit object's speed (power) to history
            osuCurrObj.History.Push(currentRatio, osuCurrObj.History.BasePower);

            return osuCurrObj.History.JerkStrain * jerk_balancing_factor * doubletapness;
        }

        // Function that calculates the (compressed) difference between two Speed values (or "power") of two hit objects.
        private static double calculateJerk(double currentPower, double prevPower, double dt, double epsilon, double currentRatio, bool fromSlider)
        {
            // If the pattern is unambiguously doubletappable, assume its jerk to be 0
            if (dt < epsilon) return 0;

            double deltaPower = currentPower - prevPower;
            double compressedDelta = Math.Sign(deltaPower) * Math.Pow(Math.Abs(deltaPower), compression_exponent); // Compress the value to avoid double-counting at high BPM
            double jerk = compressedDelta / ((dt + epsilon) / 1000.0); // Smooth with epsilon

            if (jerk < 0)
            {
                // Slowdowns are mildly harder to execute than speedups (subject to debate)
                return Math.Abs(jerk) * 1.25;
            }

            if (fromSlider)
            {
                // If the last object was a slider, this is typically a much easier transition
                return Math.Abs(jerk) * 0.6;
            }

            return Math.Abs(jerk);
        }

        // Function that calculates how "resonant" a pattern is by penalizing same rhythmic intervals occurring at smaller prime note intervals
        // e.g. since triples are period-4 (smallest prime factor is 2), they will likely be nerfed more than weirder periods (e.g. period-7)
        // Outputs a value [0.0, 1.0]
        private static double periodicityNerf(OsuDifficultyHitObject current, double currentRatio)
        {
            const double ratio_tolerance = 0.10; // 10% - relative tolerance for ratios to be considered "the same"
            const double recency_decay = 0.95; // 5% - when calculating periodicity, longer periods are weighed less: 0.95 ^ (p - 1)
            const double gallop_midpoint = 37.0; // 37 ms - arbitrary midpoint where a player is considered to be galloping

            double totalPeriodicity = 0;

            // Smooth gallop factor: 1.0 if the notes are very fast (mashing), 0.0 if slow
            double gallopFactor = 1.0 / (1.0 + Math.Exp(0.25 * (current.AdjustedDeltaTime - gallop_midpoint)));

            for (int p = 1; p <= 16; p++)
            {
                double historicalRatio = current.History.GetRatio(p);

                bool isRhythmicMatch = Math.Abs(currentRatio - historicalRatio) < currentRatio * ratio_tolerance;

                // Gallop match: if we are clicking fast enough to be "mashing",
                // we treat the first few notes of history as a match regardless of the exact ratio
                bool isGallopMatch = gallopFactor > 0.1 && p < 4;

                if (!isRhythmicMatch && !isGallopMatch) continue;

                double weight = Math.Pow(recency_decay, p - 1);

                // If it's a gallop match, we scale the weight by how fast the mashing is
                if (!isRhythmicMatch && isGallopMatch)
                    weight *= (Math.Pow(gallop_midpoint / current.AdjustedDeltaTime, 2) * gallopFactor);

                totalPeriodicity += weight;
            }

            return 1.0 / (1.0 + totalPeriodicity * periodicity_sharpness);
        }
    }
}
