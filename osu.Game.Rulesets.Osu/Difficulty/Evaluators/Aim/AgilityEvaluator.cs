// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Difficulty.Utils;
using osu.Game.Rulesets.Osu.Difficulty.Preprocessing;
using osu.Game.Rulesets.Osu.Objects;
using static osu.Game.Rulesets.Osu.Difficulty.Evaluators.Aim.FlowAimEvaluator;

namespace osu.Game.Rulesets.Osu.Difficulty.Evaluators.Aim
{
    public static class AgilityEvaluator
    {
        private const double stop_exponent = 1; //In case we want to scale jerk difficulty of snapping at a different rate than d/t^2
        private const double start_exponent = 2; //In case we want to scale jerk difficulty of accelerating at a different rate than d/t^2
        private const double jerk_change_cap = 2; //
        private const double tangential_mult = 1;
        private const double normal_mult = 4;

        /// <summary>
        /// Evaluates the difficulty of changing your velocity
        /// </summary>
        public static double EvaluateDifficultyOf(DifficultyHitObject current, bool withSliderTravelDistance) // NOT FINISHED!!!!!
        {
            if (current.BaseObject is Spinner || current.Index <= 1 || current.Previous(0).BaseObject is Spinner)
                return 0;

            var osuNextObj = (OsuDifficultyHitObject)current.Next(0);
            if (osuNextObj == null)
                return 0;

            var osuCurrObj = (OsuDifficultyHitObject)current;
            var osuLastObj = (OsuDifficultyHitObject)current.Previous(0);
            var osuLastLastObj = (OsuDifficultyHitObject)current.Previous(1);

            // Calculate the velocity to the current hitobject, which starts with a base distance / time assuming the last object is a hitcircle.
            double currDistance = withSliderTravelDistance ? osuCurrObj.LazyJumpDistance : osuCurrObj.JumpDistance;
            double nextDistance = withSliderTravelDistance ? osuNextObj.LazyJumpDistance : osuNextObj.JumpDistance;

            double currVelocity = currDistance / osuCurrObj.AdjustedDeltaTime;

            // But if the last object is a slider, then we extend the travel velocity through the slider into the current object.
            if (osuLastObj.BaseObject is Slider && withSliderTravelDistance)
            {
                double sliderDistance = osuLastObj.LazyTravelDistance + osuCurrObj.LazyJumpDistance;
                currVelocity = Math.Max(currVelocity, sliderDistance / osuCurrObj.AdjustedDeltaTime);
            }

            double nextVelocity = nextDistance / osuNextObj.AdjustedDeltaTime;

            if (osuCurrObj.BaseObject is Slider && withSliderTravelDistance)
            {
                // If the last object is a slider, then we extend the travel velocity through the slider into the current object.
                double sliderDistance = osuCurrObj.LazyTravelDistance + osuNextObj.LazyJumpDistance;
                nextVelocity = Math.Max(nextVelocity, sliderDistance / osuNextObj.AdjustedDeltaTime);
            }

            double stopDifficulty = currVelocity * Math.Pow(highBpmBonus(osuCurrObj.AdjustedDeltaTime, osuCurrObj.LazyJumpDistance), stop_exponent);
            double startDifficulty = 0;

            if (osuCurrObj.Angle != null && osuNextObj.Angle != null)
            {
                double currAngleAt = osuNextObj.Angle.Value;

                double tangentialVectorInfluence = Math.Cos(currAngleAt);
                double normalVectorInfluence = Math.Sin(currAngleAt);

                tangentialVectorInfluence *= tangentialVectorInfluence < 0 ? -2 : 1; // Buff tangential jerk direction switch after snap (linear jump buff)
                double tangentialNextVelocity = nextVelocity * tangentialVectorInfluence;
                double tangentialDifficulty = Math.Abs(tangentialNextVelocity) * tangential_mult;
                if (tangentialNextVelocity != 0) tangentialDifficulty *= Math.Min(Math.Abs(currVelocity / tangentialNextVelocity), jerk_change_cap);

                double normalNextVelocity = nextVelocity * normalVectorInfluence;
                double normalDifficulty = normalNextVelocity * normal_mult;

                startDifficulty = Math.Sqrt(normalDifficulty * normalDifficulty + tangentialDifficulty * tangentialDifficulty)
                                  * -(Math.Cos(currAngleAt) / 4 - 1.5)
                                  * Math.Pow(highBpmBonus(osuNextObj.AdjustedDeltaTime, osuNextObj.LazyJumpDistance), start_exponent);
            }

            // If all three notes are overlapping - don't reward bonuses as you don't have to do additional movement
            double overlappedNotesWeight = 1;

            if (current.Index > 2)
            {
                double o1 = CalculateOverlapFactor(osuCurrObj, osuLastObj);
                double o2 = CalculateOverlapFactor(osuCurrObj, osuLastLastObj);
                double o3 = CalculateOverlapFactor(osuLastObj, osuLastLastObj);

                overlappedNotesWeight = 1 - o1 * o2 * o3;
            }

            return (stopDifficulty + startDifficulty) * overlappedNotesWeight / 1000;
        }

        private static double highBpmBonus(double ms, double distance) => 1 / (1 - Math.Pow(0.3, ms / 1000))
                                                                          * DifficultyCalculationUtils.Smootherstep(distance, 0, OsuDifficultyHitObject.NORMALISED_RADIUS);
    }
}
