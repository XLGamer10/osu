// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Difficulty.Utils;
using osu.Game.Rulesets.Osu.Difficulty.Preprocessing;
using osu.Game.Rulesets.Osu.Objects;

namespace osu.Game.Rulesets.Osu.Difficulty.Evaluators.Aim
{
    public static class AgilityEvaluator
    {
        private const double distance_cap = OsuDifficultyHitObject.NORMALISED_RADIUS; // 1.25 circles distance between centers
        private const double stop_exponent = 1; //In case we want to scale jerk difficulty of snapping at a different rate than d/t^2
        private const double start_exponent = 1; //In case we want to scale jerk difficulty of accelerating at a different rate than d/t^2
        private const double jerk_change_cap = 2; //

        /// <summary>
        /// Evaluates the difficulty of fast aiming
        /// </summary>
        public static double EvaluateDifficultyOf(DifficultyHitObject current)
        {
            if (current.BaseObject is Spinner)
                return 0;

            var osuCurrObj = (OsuDifficultyHitObject)current;
            var osuPrevObj = current.Index > 0 ? (OsuDifficultyHitObject)current.Previous(0) : null;

            double travelDistance = osuPrevObj?.LazyTravelDistance ?? 0;
            double distance = travelDistance + osuCurrObj.LazyJumpDistance;

            double distanceScaled = Math.Min(distance, distance_cap) / distance_cap;

            double strain = distanceScaled * 1000 / osuCurrObj.AdjustedDeltaTime;

            strain *= Math.Pow(osuCurrObj.SmallCircleBonus, 1.5);

            strain *= highBpmBonus(osuCurrObj.AdjustedDeltaTime, osuCurrObj.LazyJumpDistance);

            return strain;
        }

        public static double EvaluateJerkingOfSnap(DifficultyHitObject current, bool withSliderTravelDistance) // NOT FINISHED!!!!!
        {
            if (current.BaseObject is Spinner || current.Index <= 1 || current.Previous(0).BaseObject is Spinner)
                return 0;

            var osuCurrObj = (OsuDifficultyHitObject)current;
            var osuLastObj = (OsuDifficultyHitObject)current.Previous(0);
            var osuNextObj = (OsuDifficultyHitObject)current.Next(0);

            // Calculate the velocity to the current hitobject, which starts with a base distance / time assuming the last object is a hitcircle.
            double currDistance = withSliderTravelDistance ? osuCurrObj.LazyJumpDistance : osuCurrObj.JumpDistance;
            double currVelocity = currDistance / osuCurrObj.AdjustedDeltaTime;

            // But if the last object is a slider, then we extend the travel velocity through the slider into the current object.
            if (osuLastObj.BaseObject is Slider && withSliderTravelDistance)
            {
                double sliderDistance = osuLastObj.LazyTravelDistance + osuCurrObj.LazyJumpDistance;
                currVelocity = Math.Max(currVelocity, sliderDistance / osuCurrObj.AdjustedDeltaTime);
            }

            double nextDistance = withSliderTravelDistance ? osuNextObj.LazyJumpDistance : osuNextObj.JumpDistance;
            double nextVelocity = nextDistance / osuNextObj.AdjustedDeltaTime;

            if (osuCurrObj.BaseObject is Slider && withSliderTravelDistance)
            {
                double sliderDistance = osuCurrObj.LazyTravelDistance + osuNextObj.LazyJumpDistance;
                nextVelocity = Math.Max(nextVelocity, sliderDistance / osuNextObj.AdjustedDeltaTime);
            }

            double stopDifficulty = currVelocity * Math.Pow(highBpmBonus(osuCurrObj.AdjustedDeltaTime, osuCurrObj.LazyJumpDistance), stop_exponent);
            double startDifficulty = 0;

            if (osuCurrObj.Angle != null && osuNextObj.Angle != null)
            {
                double currAngleAt = double.DegreesToRadians(osuNextObj.Angle.Value);
                double tangentialVectorInfluence = Math.Cos(currAngleAt);
                double tangentialNextVelocity = nextVelocity * tangentialVectorInfluence;
                //tangentialVectorInfluence *= tangentialVectorInfluence < 0 ? -2 : 1; // Buff tangential jerk direction switch after snap (linear jump buff)
                double tangentialDifficulty = tangentialNextVelocity;
                tangentialDifficulty *= Math.Min(currVelocity / tangentialNextVelocity, jerk_change_cap);
            }

            return stopDifficulty + startDifficulty;
        }

        private static double highBpmBonus(double ms, double distance) => 1 / (1 - Math.Pow(0.2, ms / 1000))
                                                                          * DifficultyCalculationUtils.Smootherstep(distance, 0, OsuDifficultyHitObject.NORMALISED_RADIUS);
    }
}
