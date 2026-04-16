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
using osu.Game.Rulesets.Osu.Difficulty.Evaluators;
using osu.Game.Rulesets.Osu.Mods;

namespace osu.Game.Rulesets.Osu.Difficulty.Skills
{
    public class Reading : HarmonicSkill
    {
        private readonly List<DifficultyHitObject> objectList = new List<DifficultyHitObject>();

        private readonly bool hasHiddenMod;
        private readonly OsuDifficultyConstants tuning;

        public Reading(Mod[] mods, OsuDifficultyConstants tuning)
            : base(mods)
        {
            hasHiddenMod = mods.OfType<OsuModHidden>().Any(m => !m.OnlyFadeApproachCircles.Value);
            this.tuning = tuning;
        }

        private double currentDifficulty;

        private double strainDecay(double ms) => Math.Pow(tuning.ReadingStrainDecayBase, ms / 1000);

        protected override double ObjectDifficultyOf(DifficultyHitObject current)
        {
            objectList.Add(current);

            double decay = strainDecay(current.DeltaTime);

            currentDifficulty *= decay;

            currentDifficulty += ReadingEvaluator.EvaluateDifficultyOf(current, hasHiddenMod, tuning) * (1 - decay) * tuning.ReadingSkillMultiplier;

            return currentDifficulty;
        }

        protected override void ApplyDifficultyTransformation(double[] difficulties)
        {
            int reducedNoteCount = calculateReducedNoteCount();

            for (int i = 0; i < Math.Min(difficulties.Length, reducedNoteCount); i++)
            {
                double scale = Math.Log10(Interpolation.Lerp(1, 10, Math.Clamp((double)i / reducedNoteCount, 0, 1)));
                difficulties[i] *= Interpolation.Lerp(tuning.ReadingReducedDifficultyBaseLine, 1.0, scale);
            }
        }

        private int calculateReducedNoteCount()
        {
            if (objectList.Count == 0)
                return 0;

            double reducedDuration = objectList.First().StartTime + tuning.ReadingReducedDifficultyDuration;

            int reducedNoteCount = 0;

            foreach (var hitObject in objectList)
            {
                if (hitObject.StartTime > reducedDuration)
                    break;

                reducedNoteCount++;
            }

            return reducedNoteCount;
        }

        public override double CountTopWeightedObjectDifficulties(double difficultyValue)
        {
            if (ObjectDifficulties.Count == 0)
                return 0.0;

            if (NoteWeightSum == 0)
                return 0.0;

            double consistentTopNote = difficultyValue / NoteWeightSum; // What would the top difficulty be if all object difficulties were identical

            if (consistentTopNote == 0)
                return 0;

            return ObjectDifficulties.Sum(d => DifficultyCalculationUtils.Logistic(d / consistentTopNote, 1.15, 5, 1.1));
        }
    }
}
