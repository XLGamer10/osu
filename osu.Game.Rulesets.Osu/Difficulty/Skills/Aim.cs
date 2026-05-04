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

        public Aim(Mod[] mods, bool includeSliders, bool withCheesability)
            : base(mods)
        {
            IncludeSliders = includeSliders;
            WithCheesability = withCheesability;
        }

        private double inaccuraciesWhileCheesing;
        private double maxStrain;
        private double currentStrain;
        private double preservedStrain;

        private double skillMultiplierSnap => 510.0;
        private double skillMultiplierAgility => 12.0;
        private double skillMultiplierFlow => 1057.0;
        private double skillMultiplierTotal => 1.1;
        private double combinedSnapNormExponent => 1.2;

        private readonly List<double> sliderStrains = new List<double>();

        protected override double HitProbability(double skill, double difficulty)
        {
            if (difficulty <= 0) return 1;
            if (skill <= 0) return 0;

            return DifficultyCalculationUtils.Erf(skill / (Math.Sqrt(2) * difficulty));
        }

        private double strainDecay(double ms) => Math.Pow(0.2, ms / 1000);
        private double preservedStrainDecay(double ms) => Math.Pow(0.4, ms / 1000);

        protected override double StrainValueAt(DifficultyHitObject current)
        {
            if (Mods.Any(m => m is OsuModAutopilot))
                return 0;

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

            currentStrain *= decay;
            currentStrain += calculateModAdjustedDifficulty(current) * (1 - decay);

            if (current.BaseObject is Slider)
                sliderStrains.Add(currentStrain);

            inaccuraciesWhileCheesing += isInaccurateWhileCheesed(current) * currentStrain;
            if (currentStrain > maxStrain)
                maxStrain = currentStrain;

            return currentStrain;
        }

        private double calculateModAdjustedDifficulty(DifficultyHitObject current)
        {
            double snapDifficulty = SnapAimEvaluator.EvaluateDifficultyOf(current, IncludeSliders) * skillMultiplierSnap;
            double agilityDifficulty = AgilityEvaluator.EvaluateDifficultyOf(current) * skillMultiplierAgility;
            double flowDifficulty = FlowAimEvaluator.EvaluateDifficultyOf(current, IncludeSliders) * skillMultiplierFlow;

            double totalDifficulty = calculateTotalValue(snapDifficulty, agilityDifficulty, flowDifficulty);

            if (Mods.Any(m => m is OsuModMagnetised))
            {
                float magnetisedStrength = Mods.OfType<OsuModMagnetised>().First().AttractionStrength.Value;
                totalDifficulty *= 1.0 - magnetisedStrength;
            }

            return totalDifficulty;
        }

        private double calculateTotalValue(double snapDifficulty, double agilityDifficulty, double flowDifficulty)
        {
            // We compare flow to combined snap and agility because snap by itself doesn't have enough difficulty to be above flow on streams
            // Agility on the other hand is supposed to measure the rate of cursor velocity changes while snapping
            // So snapping every circle on a stream requires an enormous amount of agility at which point it's easier to flow
            double combinedSnapDifficulty = DifficultyCalculationUtils.Norm(combinedSnapNormExponent, snapDifficulty, agilityDifficulty);

            double pSnap = calculateSnapFlowProbability(flowDifficulty / combinedSnapDifficulty);
            double pFlow = 1 - pSnap;

            if (Mods.Any(m => m is OsuModTouchDevice))
            {
                // we don't adjust agility here since agility represents TD difficulty in a decent enough way
                snapDifficulty = Math.Pow(snapDifficulty, 0.89);
                combinedSnapDifficulty = DifficultyCalculationUtils.Norm(combinedSnapNormExponent, snapDifficulty, agilityDifficulty);
            }

            if (Mods.Any(m => m is OsuModRelax))
            {
                combinedSnapDifficulty *= 0.75;
                flowDifficulty *= 0.6;
            }

            double totalDifficulty = combinedSnapDifficulty * pSnap + flowDifficulty * pFlow;

            double totalStrain = totalDifficulty * skillMultiplierTotal;

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
