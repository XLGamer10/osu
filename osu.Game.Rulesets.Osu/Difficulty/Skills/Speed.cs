// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Difficulty.Skills;
using osu.Game.Rulesets.Difficulty.Utils;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu.Difficulty.Evaluators.Speed;
using osu.Game.Rulesets.Osu.Difficulty.Preprocessing;
using osu.Game.Rulesets.Osu.Mods;
using osu.Game.Rulesets.Osu.Objects;

namespace osu.Game.Rulesets.Osu.Difficulty.Skills
{
    /// <summary>
    /// Represents the skill required to press keys with regards to keeping up with the speed at which objects need to be hit.
    /// </summary>
    public class Speed : TimeSkill
    {
        private double skillMultiplier => 7.16;

        private readonly List<double> sliderStrains = new List<double>();

        private double currentStrain;

        private double strainDecayBase => 0.3;

        public Speed(Mod[] mods)
            : base(mods)
        {
        }

        protected override double HitProbability(double skill, double difficulty)
        {
            if (difficulty <= 0) return 1;
            if (skill <= 0) return 0;

            return DifficultyCalculationUtils.Erf(skill / (Math.Sqrt(2) * difficulty));
        }

        private double strainDecay(double ms) => Math.Pow(strainDecayBase, ms / 1000);

        protected override double StrainValueAt(DifficultyHitObject current)
        {
            if (Mods.Any(m => m is OsuModRelax))
                return 0;

            double decay = strainDecay(((OsuDifficultyHitObject)current).AdjustedDeltaTime);

            currentStrain *= decay;
            currentStrain += calculateModAdjustedDifficulty(current) * (1 - decay) * skillMultiplier;

            double currentRhythm = RhythmEvaluator.EvaluateDifficultyOf(current);

            double totalStrain = currentStrain * currentRhythm;

            if (current.BaseObject is Slider)
                sliderStrains.Add(totalStrain);

            return totalStrain;
        }

        private double calculateModAdjustedDifficulty(DifficultyHitObject current)
        {
            double difficulty = SpeedEvaluator.EvaluateDifficultyOf(current);

            if (Mods.Any(m => m is OsuModAutopilot))
                difficulty *= 0.5;

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

        public double CountTopWeightedSliders(double difficultyValue)
        {
            if (sliderStrains.Count == 0)
                return 0;

            double consistentTopNote = difficultyValue / 0.1; // What would the top note be if all note values were identical

            if (consistentTopNote == 0)
                return 0;

            // Use a weighted sum of all notes. Constants are arbitrary and give nice values
            return sliderStrains.Sum(s => DifficultyCalculationUtils.Logistic(s / consistentTopNote, 0.88, 10, 1.1));
        }

        public double CountTopWeightedObjectDifficulties(double difficultyValue)
        {
            if (sliderStrains.Count == 0)
                return 0;

            double consistentTopNote = difficultyValue / 0.1; // What would the top note be if all note values were identical

            if (consistentTopNote == 0)
                return 0;

            // Use a weighted sum of all notes. Constants are arbitrary and give nice values
            return sliderStrains.Sum(s => DifficultyCalculationUtils.Logistic(s / consistentTopNote, 0.88, 10, 1.1));
        }
    }
}
