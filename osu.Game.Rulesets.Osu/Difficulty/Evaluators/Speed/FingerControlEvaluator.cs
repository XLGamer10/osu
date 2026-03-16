// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Osu.Difficulty.Preprocessing;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.Osu.Difficulty.Evaluators.Speed
{
    public static class FingerControlEvaluator
    {
        private const double jerk_balancing_factor = 0.92; // Increase this value to make values more based
        private const double jerk_time_constant = 400.0; // 400ms - Time constant for the "strain" decay of the jerk
        private const double compression_exponent = 0.5; // Reflects the nonlinear perception of finger control effort, more relevant at higher BPMs

        /// <summary>
        /// Calculates a finger control value for the difficulty of the tap associated with historic speed data of the current <see cref="OsuDifficultyHitObject"/>.
        /// Uses jerk (derivative of acceleration/force) to model muscle confusion, which is defined here as the derivative of Speed values for the object.
        /// Note that this specifically does NOT model cognitive difficulty and is very much only a measurement of physical finger control.
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

            // Do not calculate a jerk if there is no previous note
            if (osuPrevObj == null) return 0;

            // Extract the previous and current object's speed values
            double previousSpeed = osuPrevObj.History.BaseSpeed;
            double currentSpeed = osuCurrObj.History.BaseSpeed;

            // Exponentially decay the jerk strain, and decay the rhythmic strain by a fixed amount
            osuCurrObj.History.DecayJerk(osuCurrObj.AdjustedDeltaTime, jerk_time_constant);

            // Calculate the jerk of the required motion based off of current and previous speed difficulty, scaled by DeltaTime
            double jerkDifficulty = calculateJerk(currentSpeed, previousSpeed, osuCurrObj.AdjustedDeltaTime, epsilon, osuPrevObj.BaseObject is Slider);

            double addedJerkStrain = Math.Abs(jerkDifficulty) * doubletapness;
            osuCurrObj.History.JerkStrain += addedJerkStrain;

            // Save the current hit object's speed (power) to history
            osuCurrObj.History.Push(osuCurrObj.History.BaseSpeed);

            double totalFingerControl = (osuCurrObj.History.JerkStrain * jerk_balancing_factor);

            return totalFingerControl;
        }

        // Function that calculates the (compressed) difference between two Speed values (or power) of two hit objects
        private static double calculateJerk(double currentPower, double prevPower, double dt, double epsilon, bool fromSlider)
        {
            // If the pattern is unambiguously doubletappable, assume its jerk to be 0
            if (dt < epsilon) return 0;

            double rawRateOfChange = (currentPower - prevPower) / ((dt + epsilon) / 1000.0); // Smooth with epsilon
            double sign = Math.Sign(rawRateOfChange);

            // Compress the value to avoid double-counting at high BPM
            double jerk = sign * Math.Pow(Math.Abs(rawRateOfChange), compression_exponent);

            if (fromSlider)
            {
                // If the last object was a slider, this is typically a much easier transition
                return jerk * 0.5;
            }

            // TODO: add some more empirical rules akin to the old rhythm evaluator

            return jerk;
        }
    }
}
