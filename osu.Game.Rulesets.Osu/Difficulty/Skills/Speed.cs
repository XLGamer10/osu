// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu.Difficulty.Preprocessing;
using osu.Game.Rulesets.Osu.Objects;
using System.Linq;
using osu.Game.Rulesets.Difficulty.Skills;
using osu.Game.Rulesets.Difficulty.Utils;
using osu.Game.Rulesets.Osu.Difficulty.Evaluators.Speed;

namespace osu.Game.Rulesets.Osu.Difficulty.Skills
{
    /// <summary>
    /// Represents the skill required to press keys with regards to keeping up with the speed at which objects need to be hit.
    /// </summary>
    public class Speed : HarmonicSkill
    {
        private double currentBurstStrain;
        private double currentStreamStrain;
        private double currentStaminaStrain;
        private double currentFingerControl;

        private readonly OsuDifficultyConstants tuning;
        private readonly List<double> sliderStrains = new List<double>();
        public readonly bool WithoutStamina;

        public Speed(Mod[] mods, OsuDifficultyConstants tuning, bool withoutStamina)
            : base(mods)
        {
            this.tuning = tuning;
            WithoutStamina = withoutStamina;
        }

        protected override double HarmonicScale => tuning.SpeedHarmonicScale;
        protected override double DecayExponent => tuning.SpeedDecayExponent;

        private double strainDecayBurst(double ms) => Math.Pow(tuning.SpeedStrainDecayBurstBase, ms / 1000);
        private double strainDecayStream(double ms) => Math.Pow(tuning.SpeedStrainDecayStreamBase, Math.Pow(ms / 1000, tuning.SpeedStrainDecayStreamExp));

        private double strainDecayStamina(double ms, double staminaValue)
        {
            double changeFactor = currentStaminaStrain > 0 ? 1 + Math.Pow(currentStaminaStrain / (staminaValue + currentStaminaStrain), 25.0) : 1.0;
            return Math.Pow(0.05, Math.Pow(ms * changeFactor / 1000, 3.5));
        }

        protected override double ObjectDifficultyOf(DifficultyHitObject current)
        {
            currentBurstStrain *= strainDecayBurst(((OsuDifficultyHitObject)current).AdjustedDeltaTime);
            currentFingerControl = FingerControlEvaluator.EvaluateDifficultyOf(current, tuning);
            currentBurstStrain += SpeedEvaluator.EvaluateDifficultyOf(current, tuning) * tuning.SpeedBurstMultiplier;

            double totalBurstStrain = DifficultyCalculationUtils.Norm(tuning.SpeedFControlNorm, [currentBurstStrain, currentFingerControl]);

            if (WithoutStamina)
            {
                double totalWithoutStamina = totalBurstStrain * tuning.SpeedTotalMultiplier;

                if (current.BaseObject is Slider)
                    sliderStrains.Add(totalWithoutStamina);

                return totalWithoutStamina;
            }

            double staminaValue = StaminaEvaluator.EvaluateDifficultyOf(current, tuning);

            currentStreamStrain *= strainDecayStream(((OsuDifficultyHitObject)current).AdjustedDeltaTime);
            currentStreamStrain += staminaValue * tuning.SpeedStreamMultiplier;

            currentStaminaStrain *= strainDecayStamina(((OsuDifficultyHitObject)current).AdjustedDeltaTime, staminaValue * tuning.SpeedStaminaMultiplier);
            currentStaminaStrain += staminaValue * tuning.SpeedStaminaMultiplier;

            double totalValue = DifficultyCalculationUtils.Norm(tuning.SpeedMeanExponent,
                totalBurstStrain,
                currentStreamStrain,
                currentStaminaStrain) * tuning.SpeedTotalMultiplier;

            if (current.BaseObject is Slider)
                sliderStrains.Add(totalValue);

            return totalValue;
        }

        public double RelevantNoteCount()
        {
            if (ObjectDifficulties.Count == 0)
                return 0;

            double maxStrain = ObjectDifficulties.Max();
            if (maxStrain == 0)
                return 0;

            return ObjectDifficulties.Sum(strain => strain / maxStrain);
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
    }
}
