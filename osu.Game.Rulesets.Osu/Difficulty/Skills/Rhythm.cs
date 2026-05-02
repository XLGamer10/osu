// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Mods;
using System.Linq;
using osu.Game.Rulesets.Difficulty.Skills;
using osu.Game.Rulesets.Osu.Difficulty.Evaluators;
using osu.Game.Rulesets.Osu.Difficulty.Preprocessing;

namespace osu.Game.Rulesets.Osu.Difficulty.Skills
{
    /// <summary>
    /// Represents the skill required to continuously interpret rhythms on the cognitive level.
    /// </summary>
    public class Rhythm : HarmonicSkill
    {
        private double skillMultiplier => 12.2;

        private double currentDifficulty;

        private double strainDecayBase => 0.2;

        public double RhythmNormalizedVariance { get; private set; }

        protected override double HarmonicScale => 35;
        protected override double DecayExponent => 0.95;

        public Rhythm(Mod[] mods)
            : base(mods)
        {
        }

        private double strainDecay(double ms) => Math.Pow(strainDecayBase, ms / 1000);

        protected override double ObjectDifficultyOf(DifficultyHitObject current)
        {
            double decay = strainDecay(((OsuDifficultyHitObject)current).AdjustedDeltaTime);

            currentDifficulty *= decay;
            currentDifficulty += RhythmEvaluator.EvaluateDifficultyOf(current) * (1 - decay) * skillMultiplier;

            return currentDifficulty;
        }

        public override double DifficultyValue()
        {
            if (ObjectDifficulties.Count == 0)
                return 0;

            // Notes with 0 difficulty are excluded to avoid worst-case time complexity of the following sort (e.g. /b/2351871).
            // These notes will not contribute to the difficulty.
            double[] difficulties = ObjectDifficulties.Where(p => p > 0).ToArray();

            if (difficulties.Length == 0)
                return 0;

            ApplyDifficultyTransformation(difficulties);

            double difficulty = 0;
            int index = 0;

            foreach (double note in difficulties.OrderDescending())
            {
                // Use a harmonic sum that considers each note of the map according to a predefined weight.
                double weight = (1 + (HarmonicScale / (1 + index))) / (Math.Pow(index, DecayExponent) + 1 + (HarmonicScale / (1 + index)));

                NoteWeightSum += weight;

                difficulty += note * weight;
                index += 1;
            }

            var list = ObjectDifficulties.Where(d => d > 0).ToList();

            if (list.Count > 0)
            {
                double mean = list.Average();
                double variance = list.Select(d => Math.Pow(d - mean, 2)).Average();
                double stdDev = Math.Sqrt(variance);

                RhythmNormalizedVariance = mean > 0 ? stdDev / mean : 0;
            }

            return difficulty;
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
    }
}
