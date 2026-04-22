// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Utils;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Difficulty.Skills;
using osu.Game.Rulesets.Difficulty.Utils;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu.Difficulty.Evaluators.Aim;
using osu.Game.Rulesets.Osu.Difficulty.Preprocessing;
using osu.Game.Rulesets.Osu.Mods;
using osu.Game.Rulesets.Osu.Objects;

namespace osu.Game.Rulesets.Osu.Difficulty.Skills
{
    /// <summary>
    /// Represents the skill required to correctly aim at every object in the map with a uniform CircleSize and normalized distances.
    /// </summary>
    public class Aim : VariableLengthStrainSkill
    {
        public readonly bool IncludeSliders;

        public Aim(Mod[] mods, bool includeSliders)
            : base(mods)
        {
            IncludeSliders = includeSliders;
        }

        private double currentStrain;

        protected override double HarmonicScale => 15;
        protected override double DecayExponent => 0.9;

        private double skillMultiplierSnap => 78.9;
        private double skillMultiplierAgility => 2.0;
        private double skillMultiplierFlow => 230.0;
        private double skillMultiplierTotal => 0.9;
        private double combinedSnapNormExponent => 1.2;

        /// <summary>
        /// The number of sections with the highest strains, which the peak strain reductions will apply to.
        /// This is done in order to decrease their impact on the overall difficulty of the map for this skill.
        /// </summary>
        private int reducedSectionTime => 4000;

        /// <summary>
        /// The baseline multiplier applied to the section with the biggest strain.
        /// </summary>
        private double reducedStrainBaseline => 0.727;

        private readonly List<double> sliderStrains = new List<double>();

        private double strainDecay(double ms) => Math.Pow(0.2, ms / 1000);

        protected override double CalculateInitialStrain(double time, DifficultyHitObject current) =>
            currentStrain * strainDecay(time - current.Previous(0).StartTime);

        protected override double StrainValueAt(DifficultyHitObject current)
        {
            double decay = strainDecay(((OsuDifficultyHitObject)current).AdjustedDeltaTime);

            double snapDifficulty = SnapAimEvaluator.EvaluateDifficultyOf(current, IncludeSliders) * skillMultiplierSnap;
            double agilityDifficulty = AgilityEvaluator.EvaluateDifficultyOf(current) * skillMultiplierAgility;
            double flowDifficulty = FlowAimEvaluator.EvaluateDifficultyOf(current, IncludeSliders) * skillMultiplierFlow;

            double totalDifficulty = calculateTotalValue(snapDifficulty, agilityDifficulty, flowDifficulty);

            currentStrain *= decay;
            currentStrain += totalDifficulty * (1 - decay);

            if (current.BaseObject is Slider)
                sliderStrains.Add(currentStrain);

            return currentStrain;
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
        // This logistic function is a solution, which fits nicely with the general idea of interpolation and provides a tuneable constant
        private static double calculateSnapFlowProbability(double ratio)
        {
            const double k = 7.27;

            if (ratio == 0)
                return 0;

            if (double.IsNaN(ratio))
                return 1;

            return DifficultyCalculationUtils.Logistic(-k * Math.Log(ratio));
        }

        public override double CountTopWeightedStrains(double difficultyValue)
        {
            if (ObjectDifficulties.Count == 0)
                return 0.0;

            if (NoteWeightSum == 0)
                return 0.0;

            double consistentTopStrain = difficultyValue / NoteWeightSum; // What would the top strain be if all strain values were identical

            if (consistentTopStrain == 0)
                return ObjectDifficulties.Count;

            // Use a weighted sum of all strains. Constants are arbitrary and give nice values
            return ObjectDifficulties.Sum(s => 1.1 / (1 + Math.Exp(-10 * (s / consistentTopStrain - 0.88))));
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

            if (NoteWeightSum == 0)
                return 0.0;

            double consistentTopNote = difficultyValue / NoteWeightSum; // What would the top note be if all note values were identical

            if (consistentTopNote == 0)
                return 0;

            // Use a weighted sum of all notes. Constants are arbitrary and give nice values
            return sliderStrains.Sum(s => DifficultyCalculationUtils.Logistic(s / consistentTopNote, 0.88, 10, 1.1));
        }

        public override double DifficultyValue()
        {
            if (ObjectDifficulties.Count == 0)
                return 0;

            double difficulty = 0;
            double index = 0;

            var strains = getReducedStrainPeaks();

            foreach (StrainPeak strain in strains)
            {
                // Use a harmonic sum that considers each note of the map according to a predefined weight.
                double weight = (1 + (HarmonicScale / (1 + index))) / (Math.Pow(index, DecayExponent) + 1 + (HarmonicScale / (1 + index)));

                NoteWeightSum += weight;

                difficulty += strain.Value * weight;
                index += 1;
            }

            return difficulty;
        }

        /// <summary>
        /// Returns a sorted enumerable of strain peaks with the highest values reduced.
        /// </summary>
        /// <returns></returns>
        private IEnumerable<StrainPeak> getReducedStrainPeaks()
        {
            // Sections with 0 strain are excluded to avoid worst-case time complexity of the following sort (e.g. /b/2351871).
            // These sections will not contribute to the difficulty.
            var peaks = GetCurrentStrainPeaks().Where(p => p.Value > 0);

            List<StrainPeak> strains = peaks.OrderByDescending(p => p.Value).ToList();

            const int chunk_size = 20;
            double time = 0;
            int strainsToRemove = 0; // All strains are removed at the end for optimization purposes

            // We are reducing the highest strains first to account for extreme difficulty spikes
            // Strains are split into 20ms chunks to try to mitigate inconsistencies caused by reducing strains
            while (strains.Count > strainsToRemove && time < reducedSectionTime)
            {
                StrainPeak strain = strains[strainsToRemove];

                for (double addedTime = 0; addedTime < strain.SectionLength; addedTime += chunk_size)
                {
                    double scale = Math.Log10(Interpolation.Lerp(1, 10, Math.Clamp((time + addedTime) / reducedSectionTime, 0, 1)));

                    strains.Add(new StrainPeak(
                        strain.Value * Interpolation.Lerp(reducedStrainBaseline, 1.0, scale),
                        Math.Min(chunk_size, strain.SectionLength - addedTime)
                    ));
                }

                time += strain.SectionLength;
                strainsToRemove++;
            }

            strains.RemoveRange(0, strainsToRemove);

            return strains.OrderByDescending(p => p.Value);
        }
    }
}
