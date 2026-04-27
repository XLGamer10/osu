// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu.Objects;
using System.Linq;
using osu.Game.Rulesets.Difficulty.Skills;
using osu.Game.Rulesets.Difficulty.Utils;
using osu.Game.Rulesets.Osu.Difficulty.Evaluators.Speed;
using osu.Game.Rulesets.Osu.Difficulty.Preprocessing;

namespace osu.Game.Rulesets.Osu.Difficulty.Skills
{
    /// <summary>
    /// Represents the skill required to press keys with regards to keeping up with the speed at which objects need to be hit.
    /// </summary>
    public class Speed : HarmonicSkill
    {
        private double skillMultiplier => 1.30;

        private readonly List<double> sliderStrains = new List<double>();
        private readonly List<double> deltaTimesList = new List<double>();
        private readonly List<double> startTimesList = new List<double>();

        private double currentDifficulty;

        private double strainDecayBase => 0.3;
        private double maxDeltaTime => 5000;
        private double timeWeightSize => 200;
        private double startTimeInfluence => 500000;
        private double weightExponent => 0.0;

        protected override double HarmonicScale => 20;
        protected override double DecayExponent => 0.9;

        public Speed(Mod[] mods)
            : base(mods)
        {
        }

        private double strainDecay(double ms) => Math.Pow(strainDecayBase, ms / 1000);

        protected override double ObjectDifficultyOf(DifficultyHitObject current)
        {
            double decay = strainDecay(((OsuDifficultyHitObject)current).AdjustedDeltaTime);

            currentDifficulty *= decay;
            currentDifficulty += SpeedEvaluator.EvaluateDifficultyOf(current) * (1 - decay) * skillMultiplier;

            double currentRhythm = RhythmEvaluator.EvaluateDifficultyOf(current);

            double totalDifficulty = currentDifficulty * currentRhythm;

            if (current.BaseObject is Slider)
                sliderStrains.Add(totalDifficulty);

            if (totalDifficulty <= 0) return totalDifficulty;

            deltaTimesList.Add(Math.Min(((OsuDifficultyHitObject)current).AdjustedDeltaTime, maxDeltaTime));
            startTimesList.Add(current.StartTime);

            return totalDifficulty;
        }

        public double RelevantNoteCount()
        {
            if (ObjectDifficulties.Count == 0)
                return 0;

            double maxStrain = ObjectDifficulties.Max();

            if (maxStrain == 0)
                return 0;

            return ObjectDifficulties.Sum(strain => 1.0 / (1.0 + Math.Exp(-(strain / maxStrain * 12.0 - 6.0))));
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
            double[] deltaTimes = deltaTimesList.ToArray();
            double[] startTimes = startTimesList.ToArray();

            if (difficulties.Length == 0)
                return 0;

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
                                * deltaTimes[index] / timeWeightSize // To ensure that multiple fast notes are weighted the same as a slow note
                                * Math.Log(startTimes[index] + startTimeInfluence, startTimeInfluence); // Buff difficult notes later on in the map

                NoteWeightSum += weight;
                difficulty += note * weight;
                time += deltaTimes[index] / timeWeightSize;
                index += 1;
            }

            return difficulty;
        }

        protected override void ApplyDifficultyTransformation(double[] difficulties)
        {
            if (weightExponent <= 0) return; // just in case someone puts in a negative number

            double peakDifficulty = difficulties.Max();

            // Reduce the difficulty of notes that are easier than the most difficult object
            for (int i = 0; i < difficulties.Length; i++)
            {
                difficulties[i] *= Math.Pow(difficulties[i], weightExponent) / Math.Pow(peakDifficulty, weightExponent);
            }
        }
    }
}
