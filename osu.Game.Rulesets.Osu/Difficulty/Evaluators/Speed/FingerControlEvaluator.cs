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
        private const double jerk_balancing_factor = 0.9; // Increase this value to make values more based
        private const double jerk_time_constant = 400.0; // 400ms - Time constant for the "strain" decay of the jerk
        private const double compression_exponent = 0.5; // Reflects the nonlinear perception of finger control effort, more relevant at higher BPMs
        private const double gallop_midpoint = 37.0; // 37ms - arbitrary midpoint where a player is considered to be galloping

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

            double doubletapness = 1.0 - osuPrevObj?.GetDoubletapness(osuCurrObj) ?? 0;
            double epsilon = current.HitWindow(HitResult.Great) * 0.4;

            // Do not calculate a jerk if there is no previous note
            if (osuPrevObj == null) return 0;

            // Extract the previous and current object's speed values
            double previousSpeed = osuPrevObj.History.BaseSpeed;
            double currentSpeed = osuCurrObj.History.BaseSpeed;

            // Exponentially decay the jerk strain
            osuCurrObj.History.DecayJerk(osuCurrObj.AdjustedDeltaTime, jerk_time_constant);

            // Calculate the jerk of the required motion based off of current and previous speed difficulty, scaled by DeltaTime
            double jerkDifficulty = calculateJerk(currentSpeed, previousSpeed, osuCurrObj.AdjustedDeltaTime, epsilon, osuPrevObj.BaseObject is Slider);

            double addedJerkStrain = Math.Abs(jerkDifficulty) * doubletapness;
            osuCurrObj.History.JerkStrain += addedJerkStrain;

            // Save the current hit object's speed to history
            osuCurrObj.History.Push(osuCurrObj.History.BaseSpeed);

            double totalFingerControl = (osuCurrObj.History.JerkStrain * jerk_balancing_factor);

            return totalFingerControl;
        }

        // Function that calculates the (compressed) difference between two Speed values of two hit objects
        private static double calculateJerk(double currentSpeed, double prevSpeed, double dt, double epsilon, bool fromSlider)
        {
            // If the pattern is unambiguously doubletappable, skip the jerk calculation
            if (dt < epsilon) return 0;

            // Smooth gallop factor: 1.0 if the notes are very fast (mashing), 0.0 if slow
            // This behaves slightly differently from GetDoubletapness but produces decent results in simplified finger control
            double gallopFactor = 1.0 / (1.0 + Math.Exp(0.5 * (dt - gallop_midpoint)));

            double rawRateOfChange = (currentSpeed - prevSpeed) / ((dt + epsilon) / 1000.0); // Smooth with epsilon
            double sign = Math.Sign(rawRateOfChange);

            // Compress the value to avoid double-counting at high BPM
            double jerk = sign * Math.Pow(Math.Abs(rawRateOfChange), compression_exponent);
            jerk *= 1.0 - gallopFactor;

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
