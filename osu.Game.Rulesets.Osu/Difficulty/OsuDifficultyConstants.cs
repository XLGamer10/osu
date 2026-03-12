// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Game.Rulesets.Osu.Difficulty.Preprocessing;

namespace osu.Game.Rulesets.Osu.Difficulty
{
    public record OsuDifficultyConstants
    {
        public static OsuDifficultyConstants Default { get; } = new OsuDifficultyConstants();

        // Aim skill constants
        public double AimSkillMultiplierSnap { get; init; } = 71.0;
        public double AimSkillMultiplierAgility { get; init; } = 2.0;
        public double AimSkillMultiplierFlow { get; init; } = 244.0;
        public double AimSkillMultiplierTotal { get; init; } = 1.1;
        public double AimMeanExponent { get; init; } = 1.2;
        public double AimStrainDecayBase { get; init; } = 0.15;
        public int AimReducedSectionCount { get; init; } = 10;
        public double AimReducedStrainBaseline { get; init; } = 0.75;

        // Speed skill constants
        public double SpeedSkillMultiplier { get; init; } = 1.15;
        public double SpeedStrainDecayBase { get; init; } = 0.3;
        public double SpeedHarmonicScale { get; init; } = 20;
        public double SpeedDecayExponent { get; init; } = 0.9;

        // Reading skill constants
        public double ReadingSkillMultiplier { get; init; } = 2.5;
        public double ReadingStrainDecayBase { get; init; } = 0.8;

        // Flashlight skill constants
        public double FlashlightSkillMultiplier { get; init; } = 0.056;
        public double FlashlightStrainDecayBase { get; init; } = 0.15;

        // SnapAimEvaluator constants
        public double SnapAimWideAngleScale { get; init; } = 1.05;
        public double SnapAimAcuteAngleScale { get; init; } = 2.41;
        public double SnapAimSliderScale { get; init; } = 1.5;
        public double SnapAimVelocityChangeScale { get; init; } = 0.9;
        /// <summary>
        /// WARNING: Increasing beyond 1.02 reduces difficulty as distance increases.
        /// </summary>
        public double SnapAimWiggleScale { get; init; } = 1.02;
        public double SnapAimHighBpmBonusBase { get; init; } = 0.03;
        public double SnapAimHighBpmExponent { get; init; } = 0.65;
        public double SnapAimMaxRepetitionNerf { get; init; } = 0.15;
        public double SnapAimMaxVectorInfluence { get; init; } = 0.5;

        // AgilityEvaluator constants
        public double AgilityHighBpmBonusBase { get; init; } = 0.3;
        public double AgilityHighBpmExponent { get; init; } = 0.9;

        // FlowAimEvaluator constants
        public double FlowVelocityChangeScale { get; init; } = 2.0;

        // FlashlightEvaluator constants
        public double FlashlightMaxOpacityBonusScale { get; init; } = 0.4;
        public double FlashlightHiddenBonusScale { get; init; } = 0.2;
        public double FlashlightMinVelocityScale { get; init; } = 0.5;
        public double FlashlightSliderBonusScale { get; init; } = 1.3;
        public double FlashlightMinAngleScale { get; init; } = 0.2;

        // RhythmEvaluator constants
        public int RhythmHistoryTimeMax { get; init; } = 5 * 1000; // 5 seconds
        public int RhythmHistoryObjectsMax { get; init; } = 32;
        public double RhythmOverallScale { get; init; } = 0.8;
        public double RhythmRatioScale { get; init; } = 32.0;
        public double RhythmSliderIntoNerf { get; init; } = 0.5;
        public double RhythmRepeatPolarityNerf { get; init; } = 0.5;
        public double RhythmSpeedupConsecutiveNerf { get; init; } = 0.125;
        public double RhythmRepeatIslandSizeNerf { get; init; } = 0.5;
        public double RhythmDoubletapScale { get; init; } = 0.75;
        public double RhythmSpeedupSliderIntoNerf { get; init; } = 0.6;
        public double RhythmSpeedupSliderFromNerf { get; init; } = 0.6;

        // SpeedEvaluator constants
        public double SpeedMinBonusBpm { get; init; } = 200;
        public double SpeedBalancingFactor { get; init; } = 40;
        public double SpeedHighBpmBonusBase { get; init; } = 0.3;
        public double SpeedHighBpmExponent { get; init; } = 1.0;

        // ReadingEvaluator constants
        public double ReadingWindowSize { get; init; } = 3000;
        public double ReadingDistanceInfluenceThreshold { get; init; } = OsuDifficultyHitObject.NORMALISED_DIAMETER * 1.5;
        public double ReadingHiddenMultiplier { get; init; } = 0.28;
        public double ReadingDensityMultiplier { get; init; } = 2.4;
        public double ReadingDensityDifficultyBase { get; init; } = 2.5;
        public double ReadingPreemptBalancingFactor { get; init; } = 140000;
        public double ReadingPreemptStartingPoint { get; init; } = 500;
        public double ReadingMinimumAngleRelevancyTime { get; init; } = 2000;
        public double ReadingMaximumAngleRelevancyTime { get; init; } = 200;
        public double ReadingReducedDifficultyBaseLine { get; init; } = 0.0;
        public double ReadingReducedDifficultyDuration { get; init; } = 60 * 1000;

        // Performance calculator constants
        public double AccuracyPerformanceScale { get; init; } = 1.0;
        public double TotalPerformanceScale { get; init; } = 1.0;
    }
}
