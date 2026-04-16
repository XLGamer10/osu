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

        private double skillMultiplierAgility => 6.7;
        private double skillMultiplierFlow => 12.5;
        private double skillMultiplierTotal => 5.12;
        private double strainDecayDenominator => 1000;
        private double flowDecayDenominator => 10;

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

        private double strainDecay(double ms, double denominator) => Math.Pow(0.3, ms / denominator);

        protected override double CalculateInitialStrain(double time, DifficultyHitObject current) =>
            currentStrain * strainDecay(time - current.Previous(0).StartTime, strainDecayDenominator);

        protected override double StrainValueAt(DifficultyHitObject current)
        {
            double agilityDifficulty = AgilityEvaluator.EvaluateDifficultyOf(current, IncludeSliders) * skillMultiplierAgility;
            double flowDifficulty = FlowAimEvaluator.EvaluateDifficultyOf(current, IncludeSliders) * skillMultiplierFlow;

            if (Mods.Any(m => m is OsuModTouchDevice))
            {
                agilityDifficulty = Math.Pow(agilityDifficulty, 0.89);
                // we don't adjust agility here since agility represents TD difficulty in a decent enough way
                flowDifficulty = Math.Pow(flowDifficulty, 1.1);
            }

            if (Mods.Any(m => m is OsuModRelax))
            {
                agilityDifficulty *= 0.3;
            }

            double totalDifficulty = (agilityDifficulty + flowDifficulty) * skillMultiplierTotal;

            double flowDecayMult = strainDecay(flowDifficulty, flowDecayDenominator);
            double decay = strainDecay(((OsuDifficultyHitObject)current).AdjustedDeltaTime, strainDecayDenominator) * (1 - flowDecayMult);

            currentStrain *= decay;
            currentStrain += Math.Sqrt(agilityDifficulty) * (1 - decay);

            return totalDifficulty * (1 + currentStrain);
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

            double consistentTopStrain = difficultyValue * (1 - DecayWeight); // What would the top strain be if all strain values were identical

            if (consistentTopStrain == 0)
                return 0;

            // Use a weighted sum of all strains. Constants are arbitrary and give nice values
            return sliderStrains.Sum(s => DifficultyCalculationUtils.Logistic(s / consistentTopStrain, 0.88, 10, 1.1));
        }

        public override double DifficultyValue()
        {
            double difficulty = 0;
            double time = 0;

            var strains = getReducedStrainPeaks();

            // Difficulty is a continuous weighted sum of the sorted strains
            foreach (StrainPeak strain in strains)
            {
                /* Weighting function can be thought of as:
                        b
                        ∫ DecayWeight^x dx
                        a
                    where a = startTime and b = endTime

                    Technically, the function below has been slightly modified from the equation above.
                    The real function would be
                        double weight = Math.Pow(DecayWeight, startTime) - Math.Pow(DecayWeight, endTime);
                        ...
                        return difficulty / Math.Log(1 / DecayWeight);
                    E.g. for a DecayWeight of 0.9, we're multiplying by 10 instead of 9.49122...

                    This change makes it so that a map composed solely of MaxSectionLength chunks will have the exact same value when summed in this class and StrainSkill.
                    Doing this ensures the relationship between strain values and difficulty values remains the same between the two classes.
                */
                double startTime = time;
                double endTime = time + strain.SectionLength / MaxSectionLength;

                double weight = Math.Pow(DecayWeight, startTime) - Math.Pow(DecayWeight, endTime);

                difficulty += strain.Value * weight;
                time = endTime;
            }

            return difficulty / (1 - DecayWeight);
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
