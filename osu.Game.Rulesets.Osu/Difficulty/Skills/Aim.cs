// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
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
    public class Aim : HarmonicSkill
    {
        public readonly bool IncludeSliders;

        public Aim(Mod[] mods, bool includeSliders)
            : base(mods)
        {
            IncludeSliders = includeSliders;
        }

        private double currentStrain;

        protected override double HarmonicScale => 35;
        protected override double DecayExponent => 0.90;

        private double skillMultiplierSnap => 77.7;
        private double skillMultiplierAgility => 3.85;
        private double skillMultiplierFlow => 307.0;
        private double skillMultiplierTotal => 1.12;
        private double combinedSnapNormExponent => 1.2;
        private double maxDeltaTime => 5000;
        private double timeWeightSize => 200;
        private double startTimeInfluence => 500000;
        private double weightExponent => 0.4;

        private readonly List<double> sliderStrains = new List<double>();
        private readonly List<double> deltaTimesList = new List<double>();
        private readonly List<double> startTimesList = new List<double>();

        private double strainDecay(double ms) => Math.Pow(0.2, ms / 1000);

        protected override double ObjectDifficultyOf(DifficultyHitObject current)
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

            if (currentStrain <= 0) return currentStrain;

            deltaTimesList.Add(Math.Min(((OsuDifficultyHitObject)current).AdjustedDeltaTime, maxDeltaTime));
            startTimesList.Add(current.StartTime);

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

            // Notes with 0 difficulty are excluded to avoid worst-case time complexity of the following sort (e.g. /b/2351871).
            // These notes will not contribute to the difficulty.
            double[] difficulties = ObjectDifficulties.Where(p => p > 0).ToArray();
            double[] difficultiesCopy = difficulties; // Created because .Sort() only accepts one extra array to sort while we need to sort two
            double[] deltaTimes = deltaTimesList.Select(p => p / timeWeightSize).ToArray(); // Changes how much time fits within a section of the weighting function
            double[] startTimes = startTimesList.ToArray();

            if (difficulties.Length == 0)
                return 0;

            double mapLength = startTimes[^1] / 1000;

            ApplyDifficultyTransformation(difficulties);

            Array.Sort(difficulties, deltaTimes); // Sorts the difficulties and deltaTimes arrays according to difficulties
            Array.Sort(difficultiesCopy, startTimes); // Sorts startTimes in the same way as the above
            Array.Reverse(difficulties); // Descending order
            Array.Reverse(deltaTimes);
            Array.Reverse(startTimes);

            double difficulty = 0;
            double time = 0;
            int index = 0;

            foreach (double note in difficulties)
            {
                // Use a harmonic sum that considers each note of the map according to a predefined weight.
                double weight = (1 + HarmonicScale / (1 + time)) / (Math.Pow(time, DecayExponent) + 1 + HarmonicScale / (1 + time))
                                * deltaTimes[index] // To ensure that multiple fast notes are weighted the same as a slow note
                                * Math.Log(startTimes[index] + startTimeInfluence, startTimeInfluence) // Buff difficult notes later on in the map
                                * (1 + 1 / (1 + Math.Pow(mapLength, 0.5))); // Buff short maps since they miss out on a lot of the high value weights;

                NoteWeightSum += weight;
                difficulty += note * weight;
                time += deltaTimes[index];
                index += 1;
            }

            return difficulty;
        }

        protected override void ApplyDifficultyTransformation(double[] difficulties)
        {
            if (weightExponent <= 0) return; // Just in case someone puts in a negative number

            double peakDifficulty = difficulties.Max();

            // Reduce the difficulty of notes that are easier than the most difficult object
            for (int i = 0; i < difficulties.Length; i++)
            {
                difficulties[i] *= Math.Pow(difficulties[i], weightExponent) / Math.Pow(peakDifficulty, weightExponent);
            }
        }
    }
}
