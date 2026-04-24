// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Difficulty.Skills;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu.Difficulty.Evaluators;

namespace osu.Game.Rulesets.Osu.Difficulty.Skills
{
    /// <summary>
    /// Measures rhythmic complexity as the entropy rate of the CTW model.
    /// Final difficulty is the mean of all positive per-object entropy rates.
    /// </summary>
    public class Rhythm : Skill
    {
        public Rhythm(Mod[] mods)
            : base(mods)
        {
        }

        protected override double ProcessInternal(DifficultyHitObject current)
            => RhythmEvaluator.EvaluateDifficultyOf(current);

        public override double DifficultyValue()
        {
            var positive = ObjectDifficulties.Where(d => d > 0).ToList();
            if (positive.Count == 0) return 0;

            const double p = 2.0;

            double powerSum = positive.Sum(d => Math.Pow(d, p));
            double powerMean = Math.Pow(powerSum / positive.Count, 1.0 / p);

            return 15.0 * powerMean;
        }

        public static double DifficultyToPerformance(double difficulty) => 4.0 * Math.Pow(difficulty, 3.0);
    }
}
