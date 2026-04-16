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
        private const double distance_cap = OsuDifficultyHitObject.NORMALISED_DIAMETER * 1.2; // 1.25 circles distance between centers
        private const double wide_angle_multiplier = 1.10;

        /// <summary>
        /// Evaluates the difficulty of fast aiming
        /// </summary>
        public static double EvaluateDifficultyOf(DifficultyHitObject current, bool withCheesability, OsuDifficultyConstants tuning)
        {
            if (current.BaseObject is Spinner)
                return 0;

            var osuCurrObj = (OsuDifficultyHitObject)current;
            var osuPrevObj = current.Index > 0 ? (OsuDifficultyHitObject)current.Previous(0) : null;
            var osuPrevObj1 = current.Index > 1 ? (OsuDifficultyHitObject)current.Previous(1) : null;

            double currDeltaTime = osuCurrObj.AdjustedDeltaTime;

            if (withCheesability)
            {
                currDeltaTime += osuCurrObj.ExtraDeltaTime;
            }

            double strain = getStrain(osuCurrObj, osuPrevObj, withCheesability);

            if (osuCurrObj.Angle != null && osuPrevObj != null)
            {
                double prevDeltaTime = osuCurrObj.AdjustedDeltaTime;

                if (withCheesability)
                {
                    prevDeltaTime += osuCurrObj.ExtraDeltaTime;
                }

                double wideAngleBonus = SnapAimEvaluator.CalcAngleWideness(osuCurrObj.Angle.Value);
                wideAngleBonus *= DifficultyCalculationUtils.ReverseLerp(prevDeltaTime, currDeltaTime * 0.5, currDeltaTime * 0.75);

                double strainPrev = getStrain(osuPrevObj, osuPrevObj1, withCheesability);
                strain += Math.Min(strain, strainPrev) * wideAngleBonus * wide_angle_multiplier;
            }

            strain *= Math.Pow(osuCurrObj.SmallCircleBonus, 1.5);
            return strain * highBpmBonus(currDeltaTim, tuning);
        }

        private static double getStrain(OsuDifficultyHitObject osuCurrObj, OsuDifficultyHitObject? osuPrevObj, bool withCheesability)
        {
            double currDeltaTime = osuCurrObj.AdjustedDeltaTime;

            if (withCheesability)
            {
                currDeltaTime += osuCurrObj.ExtraDeltaTime;
            }

            double distance = osuCurrObj.GetDistance(true);

            double distanceScaled = Math.Min(distance, distance_cap) / distance_cap;
            return distanceScaled * 1000 / currDeltaTime;
        }

        private static double highBpmBonus(double ms, OsuDifficultyConstants tuning) => 1 / (1 - Math.Pow(tuning.AgilityHighBpmBonusBase, Math.Pow(ms / 1000, tuning.AgilityHighBpmExponent)));
    }
}
