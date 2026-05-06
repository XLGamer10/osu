// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Difficulty.Utils;
using osu.Game.Rulesets.Osu.Difficulty.Preprocessing;
using osu.Game.Rulesets.Osu.Objects;
using osuTK;

namespace osu.Game.Rulesets.Osu.Difficulty.Evaluators.Aim
{
    public static class FlowAimEvaluator
    {
        private const double velocity_change_multiplier = 0.52;

        /// <summary>
        /// Evaluates difficulty of "flow aim" - aiming pattern where player doesn't stop their cursor on every object and instead "flows" through them.
        /// </summary>
        public static double EvaluateDifficultyOf(DifficultyHitObject current, bool withSliderTravelDistance, bool withCheesability)
        {
            if (current.BaseObject is Spinner || current.Index <= 1 || current.Previous(0).BaseObject is Spinner)
                return 0;

            // We use osuNextObj for everything instead of osuCurrObj because in flow aim difficulty comes from the prev-curr-next angle
            // when in snap aim we measure the difficulty at the end of the angle so prev2-prev-curr works better

            var osuNextObj = (OsuDifficultyHitObject?)current.Next(0);
            if (osuNextObj == null)
                return 0;

            var osuCurrObj = (OsuDifficultyHitObject)current;
            var osuLastObj = (OsuDifficultyHitObject)current.Previous(0);

            double currDeltaTime = osuCurrObj.AdjustedDeltaTime;
            double nextDeltaTime = osuNextObj.AdjustedDeltaTime;

            if (withCheesability)
            {
                currDeltaTime += osuCurrObj.ExtraDeltaTime;
                nextDeltaTime += osuNextObj.ExtraDeltaTime;
            }

            double currDistance = osuCurrObj.GetDistance(withSliderTravelDistance);
            double nextDistance = osuNextObj.GetDistance(withSliderTravelDistance);

            double currVelocity = currDistance / currDeltaTime;
            double nextVelocity = nextDistance / nextDeltaTime;

            double flowDifficulty = currVelocity;

            // Apply high circle size bonus to the base velocity.
            // We use reduced CS bonus here because the bonus was made for an evaluator with a different d/t scaling
            flowDifficulty *= Math.Sqrt(osuCurrObj.SmallCircleBonus);

            // Rhythm changes are harder to flow
            flowDifficulty *= 1 + Math.Min(0.25,
                Math.Pow((Math.Max(currDeltaTime, nextDeltaTime) - Math.Min(currDeltaTime, nextDeltaTime)) / 50, 4));

            if (osuCurrObj.Angle != null && osuNextObj.Angle != null)
            {
                double angleDifference = Math.Abs(osuCurrObj.Angle.Value - osuNextObj.Angle.Value);
                double angleDifferenceAdjusted = Math.Sin(angleDifference / 2) * 180.0;
                double angularVelocity = angleDifferenceAdjusted / (currDeltaTime * 0.1);

                // Low angular velocity flow (angles are consistent) is easier to follow than erratic flow
                flowDifficulty *= 0.8 + Math.Sqrt(angularVelocity / 270.0);
            }

            double o1 = calculateOverlapFactor(osuNextObj, osuCurrObj);
            double o2 = calculateOverlapFactor(osuNextObj, osuLastObj);
            double o3 = calculateOverlapFactor(osuCurrObj, osuLastObj);

            // If all three notes are overlapping - don't reward bonuses as you don't have to do additional movement
            double overlappedNotesWeight = 1 - o1 * o2 * o3;

            if (osuNextObj.Angle != null)
            {
                // Acute angles are also hard to flow
                flowDifficulty += currVelocity *
                                  SnapAimEvaluator.CalcAngleAcuteness(osuNextObj.Angle.Value) *
                                  overlappedNotesWeight;
            }

            if (Math.Max(nextVelocity, currVelocity) != 0)
            {
                // Scale with ratio of difference compared to 0.5 * max dist.
                double distRatio = DifficultyCalculationUtils.Smoothstep(Math.Abs(nextVelocity - currVelocity) / Math.Max(nextVelocity, currVelocity), 0, 1);

                // Reward for % distance up to 125 / strainTime for overlaps where velocity is still changing.
                double overlapVelocityBuff = Math.Min(OsuDifficultyHitObject.NORMALISED_DIAMETER * 1.25 / Math.Min(currDeltaTime, nextDeltaTime),
                    Math.Abs(nextVelocity - currVelocity));

                flowDifficulty += overlapVelocityBuff *
                                  distRatio *
                                  overlappedNotesWeight *
                                  velocity_change_multiplier;
            }

            if (osuCurrObj.BaseObject is Slider && withSliderTravelDistance)
            {
                // Include slider velocity to make velocity more consistent with snap
                flowDifficulty += osuCurrObj.SliderBonusDistance / osuCurrObj.SliderTravelTime;
            }

            // Final velocity is being raised to a power because flow difficulty scales harder with both high distance and time, and we want to account for that
            flowDifficulty = Math.Pow(flowDifficulty, 1.45);

            // Reduce difficulty for low spacing since spacing below radius is always to be flowed
            return flowDifficulty * DifficultyCalculationUtils.Smootherstep(currDistance, 0, OsuDifficultyHitObject.NORMALISED_RADIUS);
        }

        private static double calculateOverlapFactor(OsuDifficultyHitObject first, OsuDifficultyHitObject second)
        {
            var firstBase = (OsuHitObject)first.BaseObject;
            var secondBase = (OsuHitObject)second.BaseObject;
            double objectRadius = firstBase.Radius;

            double distance = Vector2.Distance(firstBase.StackedPosition, secondBase.StackedPosition);
            return Math.Clamp(1 - Math.Pow(Math.Max(distance - objectRadius, 0) / objectRadius, 2), 0, 1);
        }
    }
}
