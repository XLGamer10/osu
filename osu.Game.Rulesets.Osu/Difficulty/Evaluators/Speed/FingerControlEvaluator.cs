// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Osu.Difficulty.Preprocessing;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.Osu.Difficulty.Evaluators.Speed
{
    public static class FingerControlEvaluator
    {
        private const double jerk_balancing_factor = 1.0; // Increase this value to make values more based
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
            var osuPrevPrevObj = (OsuDifficultyHitObject?)current.Previous(1);

            double doubletapness = 1.0 - osuPrevObj?.GetDoubletapness(osuCurrObj) ?? 0;
            double epsilon = current.HitWindow(HitResult.Great) * 0.15;

            // Do not calculate a jerk if there is no previous note
            if (osuPrevObj == null) return 0;

            bool applySliderPenalty = false;
            double sliderJumpDist = 0;

            if (osuPrevObj.BaseObject is Slider)
            {
                applySliderPenalty = true;
                sliderJumpDist = osuCurrObj.GetDistance(true);
            }
            else if (osuPrevPrevObj?.BaseObject is Slider)
            {
                applySliderPenalty = true;
                // We are on the second note, but we need the jump distance from the previous transition
                sliderJumpDist = osuPrevObj.GetDistance(true);
            }

            // Extract the previous and current object's speed values and several other metrics for state machine and hold time estimations
            double previousSpeed = osuPrevObj.History.BaseSpeed;
            double currentSpeed = osuCurrObj.History.BaseSpeed;

            // Save all relevant hit object information to history
            osuCurrObj.History.PushSpeed(osuCurrObj.History.BaseSpeed);
            double smallOddPatternPenalty = nerfSmallOdds(osuCurrObj, epsilon); // Immediately calculate an odd pattern penalty

            // Exponentially decay the jerk strain
            osuCurrObj.History.DecayJerk(osuCurrObj.AdjustedDeltaTime, jerk_time_constant);

            // Calculate the jerk of the required motion based off of current and previous speed difficulty, scaled by DeltaTime
            double jerkDifficulty = calculateEffectiveJerk(osuCurrObj, currentSpeed, previousSpeed, osuCurrObj.AdjustedDeltaTime, epsilon, applySliderPenalty, sliderJumpDist);
            double addedJerkStrain = Math.Abs(jerkDifficulty) * doubletapness * smallOddPatternPenalty;
            osuCurrObj.History.JerkStrain += addedJerkStrain;

            double totalFingerControl = (osuCurrObj.History.JerkStrain * jerk_balancing_factor);

            return totalFingerControl;
        }

        // Function that calculates the (compressed) derivative of two Speed values of two hit objects
        // Includes an explicit doubletap and gallop nerf, as well as a from-slider nerf dependent on lazy jump distance from sliderend to next object
        private static double calculateEffectiveJerk(OsuDifficultyHitObject osuCurrObj, double currentSpeed, double prevSpeed, double dt, double epsilon, bool fromSlider, double sliderJumpDist)
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

            // Apply a smooth penalty to bursts starting close to a slider end
            if (fromSlider)
            {
                var osuPrevObj = (OsuDifficultyHitObject)osuCurrObj.Previous(0);
                double jumpDistance = (osuPrevObj.BaseObject is Slider)
                    ? osuCurrObj.GetDistance(true)
                    : osuPrevObj.GetDistance(true);

                double penaltyScaling = Math.Exp(-jumpDistance / 300.0);
                jerk *= 1.0 - 0.5 * penaltyScaling;
            }

            return jerk;
        }

        // Function for nerfing small odd-numbered patterns (triples, 5-bursts and 7-bursts)
        // Simultaneously handles odd-numbered patterns embedded within slider rhythms (such as slider into a double -> functionally a triple)
        private static double nerfSmallOdds(OsuDifficultyHitObject osuCurrObj, double epsilon)
        {
            var phrase = new List<OsuDifficultyHitObject>();

            for (int i = 0; i < 9; i++)
            {
                var h = (OsuDifficultyHitObject?)osuCurrObj.Previous(i);
                if (h == null) break;

                phrase.Add(h);

                var prevInList = (OsuDifficultyHitObject?)osuCurrObj.Previous(i + 1);

                if (prevInList == null) continue;

                double gapBeforeH = prevInList.BaseObject is Slider ? h.LastObjectEndDeltaTime : h.DeltaTime;

                if (i > 0 && gapBeforeH > phrase[i - 1].DeltaTime * 1.8) break;
            }

            if (phrase.Count is 3 or 5 or 7)
            {
                bool isEquidistant = true;
                double internalDelta = phrase[0].DeltaTime;

                for (int i = 1; i < phrase.Count - 1; i++)
                {
                    if (!(Math.Abs(phrase[i].DeltaTime - internalDelta) > epsilon)) continue;

                    isEquidistant = false;
                    break;
                }

                // Nearly nullify the energy needed to RE-ENTER an odd pattern, though EXITING the previous still requires energy
                // This assumes that small odd patterns are played "symmetrically" by the hand
                if (isEquidistant) return 0.1;
            }

            return 1.0;
        }
    }
}
