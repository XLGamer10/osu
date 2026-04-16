// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Difficulty.Skills;
using osu.Game.Rulesets.Difficulty.Utils;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.Osu.Difficulty.Evaluators.Aim;
using osu.Game.Rulesets.Osu.Difficulty.Preprocessing;
using osu.Game.Rulesets.Osu.Mods;
using osu.Game.Rulesets.Osu.Objects;

namespace osu.Game.Rulesets.Osu.Difficulty.Skills
{
    /// <summary>
    /// Represents the skill required to correctly aim at every object in the map with a uniform CircleSize and normalized distances.
    /// </summary>
    public class Aim : TimeSkill
    {
        public readonly bool IncludeSliders;
        public readonly bool WithCheesability;
        private readonly OsuDifficultyConstants tuning;

        public Aim(Mod[] mods, OsuDifficultyConstants tuning, bool includeSliders, bool withCheesability)
            : base(mods)
        {
            this.tuning = tuning;
            IncludeSliders = includeSliders;
            WithCheesability = withCheesability;
        }

        protected override double TimeThresholdMinutes => tuning.AimTimeThresholdMinutes;
        protected override double MaxDeltaTime => tuning.AimMaxDeltaTime;
        protected override double RetryCooldownTime => tuning.AimRetryCooldownTime;

        private double inaccuraciesWhileCheesing;
        private double maxStrain;
        private double currentStrain;
        private double preservedStrain;

        private readonly List<double> sliderStrains = new List<double>();

        protected override double HitProbability(double skill, double difficulty)
        {
            if (difficulty <= 0) return 1;
            if (skill <= 0) return 0;

            return DifficultyCalculationUtils.Erf(skill / (Math.Sqrt(2) * difficulty));
        }

        private double strainDecay(double ms) => Math.Pow(tuning.AimStrainDecayBase, ms / 1000);
        private double preservedStrainDecay(double ms) => Math.Pow(tuning.AimPreservedStrainDecayBase, ms / 1000);

        protected override double StrainValueAt(DifficultyHitObject current)
        {
            double decay = strainDecay(((OsuDifficultyHitObject)current).AdjustedDeltaTime);
            double preservedDecay = preservedStrainDecay(((OsuDifficultyHitObject)current).AdjustedDeltaTime);

            double deltaDifferenceEpsilon = ((OsuDifficultyHitObject)current).HitWindow(HitResult.Great) * 0.3;

            // if we speed up - aim strain should get preserved until we slow back down
            if (current.DeltaTime - (current.Previous(0)?.DeltaTime ?? current.DeltaTime) < -deltaDifferenceEpsilon)
            {
                preservedStrain = Math.Max(currentStrain, preservedStrain * preservedDecay);
            }

            // we slow back down, so add back the preserved strain and reset preserved strain
            if (current.DeltaTime - (current.Previous(0)?.DeltaTime ?? current.DeltaTime) > deltaDifferenceEpsilon)
            {
                currentStrain = Math.Max(currentStrain, preservedStrain * preservedDecay);
                preservedStrain = 0;
            }

            double snapDifficulty = SnapAimEvaluator.EvaluateDifficultyOf(current, IncludeSliders, WithCheesability, tuning) * tuning.AimSkillMultiplierSnap;
            double agilityDifficulty = AgilityEvaluator.EvaluateDifficultyOf(current, WithCheesability, tuning) * tuning.AimSkillMultiplierAgility;
            double flowDifficulty = FlowAimEvaluator.EvaluateDifficultyOf(current, IncludeSliders, WithCheesability, tuning) * tuning.AimSkillMultiplierFlow;

            double totalDifficulty = calculateTotalValue(snapDifficulty, agilityDifficulty, flowDifficulty);

            currentStrain *= decay;
            currentStrain += totalDifficulty * (1 - decay);

            if (current.BaseObject is Slider)
                sliderStrains.Add(currentStrain);

            inaccuraciesWhileCheesing += isInaccurateWhileCheesed(current) * currentStrain;
            if (currentStrain > maxStrain)
                maxStrain = currentStrain;

            return currentStrain;
        }

        private double calculateTotalValue(double snapDifficulty, double agilityDifficulty, double flowDifficulty)
        {
            // We compare flow to combined snap and agility because snap by itself doesn't have enough difficulty to be above flow on streams
            // Agility on the other hand is supposed to measure the rate of cursor velocity changes while snapping
            // So snapping every circle on a stream requires an enormous amount of agility at which point it's easier to flow
            double combinedSnapDifficulty = DifficultyCalculationUtils.Norm(tuning.AimCombinedSnapNormExponent, snapDifficulty, agilityDifficulty);

            double pSnap = calculateSnapFlowProbability(flowDifficulty / combinedSnapDifficulty);
            double pFlow = 1 - pSnap;

            if (Mods.Any(m => m is OsuModTouchDevice))
            {
                // we don't adjust agility here since agility represents TD difficulty in a decent enough way
                snapDifficulty = Math.Pow(snapDifficulty, 0.89);
                combinedSnapDifficulty = DifficultyCalculationUtils.Norm(tuning.AimCombinedSnapNormExponent, snapDifficulty, agilityDifficulty);
            }

            if (Mods.Any(m => m is OsuModRelax))
            {
                combinedSnapDifficulty *= 0.75;
                flowDifficulty *= 0.6;
            }

            double totalDifficulty = combinedSnapDifficulty * pSnap + flowDifficulty * pFlow;

            double totalStrain = totalDifficulty * tuning.AimSkillMultiplierTotal;

            return totalStrain;
        }

        // A function that turns the ratio of snap : flow into the probability of snapping/flowing
        // It has the constraints:
        // P(snap) + P(flow) = 1 (the object is always either snapped or flowed)
        // P(snap) = f(snap/flow), P(flow) = f(flow/snap) (ie snap and flow are symmetric and reversible)
        // Therefore: f(x) + f(1/x) = 1
        // 0 <= f(x) <= 1 (cannot have negative or greater than 100% probability of snapping or flowing)
        // This logistic function is a solution, which fits nicely with the general idea of interpolation and provides a tunable constant
        private static double calculateSnapFlowProbability(double ratio)
        {
            const double k = 7.27;

            if (ratio == 0)
                return 0;

            if (double.IsNaN(ratio))
                return 1;

            return DifficultyCalculationUtils.Logistic(-k * Math.Log(ratio));
        }

        public double GetDifficultSliders()
        {
            if (sliderStrains.Count == 0)
                return 0;

            double maxSliderStrain = sliderStrains.Max();

            if (maxSliderStrain == 0)
                return 0;

            return sliderStrains.Sum(strain => 1.0 / (1.0 + Math.Exp(-(strain / maxSliderStrain * 12.0 - 6.0))));
        }

        public double CountTopWeightedSliders(double difficultyValue)
        {
            if (sliderStrains.Count == 0)
                return 0;

            double consistentTopStrain = difficultyValue * (1 - 0.9); // What would the top strain be if all strain values were identical

            if (consistentTopStrain == 0)
                return 0;
            // Use a weighted sum of all strains. Constants are arbitrary and give nice values
            return sliderStrains.Sum(s => DifficultyCalculationUtils.Logistic(s / consistentTopStrain, 0.88, 10, 1.1));
        }

        public double GetInaccuraciesWithCheesing() => maxStrain > 0 ? inaccuraciesWhileCheesing / maxStrain : 0;

        // Check if cheesing the current object still results in a great.
        private static int isInaccurateWhileCheesed(DifficultyHitObject current)
        {
            var osuCurrObj = (OsuDifficultyHitObject)current;

            // Assume even on Lazer that cheesing does not happen on sliders
            if (osuCurrObj.BaseObject is Slider)
                return 0;

            return osuCurrObj.ExtraDeltaTime > osuCurrObj.HitWindow(HitResult.Great) ? 1 : 0;
        }
    }
}
