// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Framework.Extensions.ObjectExtensions;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Difficulty.Utils;
using osu.Game.Rulesets.Osu.Difficulty.Preprocessing;
using osu.Game.Rulesets.Osu.Objects;

namespace osu.Game.Rulesets.Osu.Difficulty.Evaluators.Aim
{
    public static class SnapAimEvaluator
    {
        private const double wide_angle_multiplier = 1.05;
        private const double acute_angle_multiplier = 1.95;
        private const double slider_multiplier = 0.25;
        private const double maximum_repetition_nerf = 0.15;
        private const double maximum_vector_influence = 0.5;

        /// <summary>
        /// Evaluates the difficulty of aiming the current object, based on:
        /// <list type="bullet">
        /// <item><description>cursor velocity to the current object,</description></item>
        /// <item><description>angle difficulty,</description></item>
        /// <item><description>sharp velocity increases,</description></item>
        /// <item><description>and slider difficulty.</description></item>
        /// </list>
        /// </summary>
        public static double EvaluateDifficultyOf(DifficultyHitObject current, bool withSliderTravelDistance, bool withCheesability)
        {
            if (current.BaseObject is Spinner || current.Index <= 1 || current.Previous(0).BaseObject is Spinner)
                return 0;

            var osuCurrObj = (OsuDifficultyHitObject)current;
            var osuLastObj = (OsuDifficultyHitObject)current.Previous(0);

            double currDeltaTime = osuCurrObj.AdjustedDeltaTime;
            double lastDeltaTime = osuLastObj.AdjustedDeltaTime;

            if (withCheesability)
            {
                currDeltaTime += osuCurrObj.ExtraDeltaTime;
                lastDeltaTime += osuLastObj.ExtraDeltaTime;
            }

            // Calculate the velocity to the current hitobject, which starts with a base distance / time assuming the last object is a hitcircle.
            double currDistance = withSliderTravelDistance ? osuCurrObj.LazyJumpDistance : osuCurrObj.JumpDistance;
            double currVelocity = currDistance / currDeltaTime;

            // But if the last object is a slider, then we extend the travel velocity through the slider into the current object.
            if (osuLastObj.BaseObject is Slider && withSliderTravelDistance)
            {
                double sliderDistance = osuLastObj.LazyTravelDistance + osuCurrObj.LazyJumpDistance;
                currVelocity = Math.Max(currVelocity, sliderDistance / lastDeltaTime);
            }

            double wideAngleBonus = 0;
            double acuteAngleBonus = 0;
            double sliderBonus = 0;
            double velocityChangeBonus = 0;
            double wiggleBonus = 0;

            double aimStrain = currVelocity; // Start strain with regular velocity.

            if (osuCurrObj.BaseObject is Slider)
            {
                // Reward sliders based on velocity.
                sliderBonus = osuCurrObj.TravelDistance / osuCurrObj.TravelTime;
            }

            // Penalize angle repetition.
            aimStrain *= vectorAngleRepetition(osuCurrObj, osuLastObj);

            // Add in acute angle bonus or wide angle bonus, whichever is larger.
            aimStrain += Math.Max(acuteAngleBonus * acute_angle_multiplier, wideAngleBonus * wide_angle_multiplier);

            // Add in additional slider velocity bonus.
            if (withSliderTravelDistance)
                aimStrain += (sliderBonus < 1 ? sliderBonus : Math.Pow(sliderBonus, 0.75)) * slider_multiplier;

            // Apply high circle size bonus
            aimStrain *= osuCurrObj.SmallCircleBonus;

            return aimStrain;
        }

        private static double vectorAngleRepetition(OsuDifficultyHitObject current, OsuDifficultyHitObject previous)
        {
            if (current.Angle == null || previous.Angle == null)
                return 1;

            const double note_limit = 6;

            double constantAngleCount = 0;

            for (int index = 0; index < note_limit; index++)
            {
                var loopObj = (OsuDifficultyHitObject)current.Previous(index);

                if (loopObj.IsNull())
                    break;

                // Only consider vectors in the same jump section, stopping to change rhythm ruins momentum
                if (Math.Max(current.AdjustedDeltaTime, loopObj.AdjustedDeltaTime) > 1.1 * Math.Min(current.AdjustedDeltaTime, loopObj.AdjustedDeltaTime))
                    break;

                if (loopObj.NormalisedVectorAngle.IsNotNull() && current.NormalisedVectorAngle.IsNotNull())
                {
                    double angleDifference = Math.Abs(current.NormalisedVectorAngle.Value - loopObj.NormalisedVectorAngle.Value);
                    // Refer to this desmos for tuning, constants need to be precise so that values stay within the range of 0 and 1.
                    // https://www.desmos.com/calculator/a8jesv5sv2
                    constantAngleCount += Math.Cos(8 * Math.Min(double.DegreesToRadians(11.25), angleDifference));
                }
            }

            double vectorRepetition = Math.Pow(Math.Min(0.5 / constantAngleCount, 1), 2);

            double stackFactor = DifficultyCalculationUtils.Smootherstep(current.LazyJumpDistance, 0, OsuDifficultyHitObject.NORMALISED_DIAMETER);

            double currAngle = current.Angle.Value;
            double lastAngle = previous.Angle.Value;

            double angleDifferenceAdjusted = Math.Cos(2 * Math.Min(double.DegreesToRadians(45), Math.Abs(currAngle - lastAngle) * stackFactor));

            double baseNerf = 1 - maximum_repetition_nerf * CalcAcuteAngleBonus(lastAngle) * angleDifferenceAdjusted;

            return Math.Pow(baseNerf + (1 - baseNerf) * vectorRepetition * maximum_vector_influence * stackFactor, 2);
        }

        public static double CalcAcuteAngleBonus(double angle) => DifficultyCalculationUtils.Smoothstep(angle, double.DegreesToRadians(140), double.DegreesToRadians(40));
    }
}
