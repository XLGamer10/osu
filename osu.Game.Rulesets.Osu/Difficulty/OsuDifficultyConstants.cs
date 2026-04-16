// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Game.Rulesets.Osu.Difficulty.Preprocessing;

namespace osu.Game.Rulesets.Osu.Difficulty
{
    public record OsuDifficultyConstants
    {
        public static OsuDifficultyConstants Default { get; } = new OsuDifficultyConstants();

        // Aim skill constants
        public double AimSkillMultiplierSnap { get; init; } = 510;
        public double AimSkillMultiplierAgility { get; init; } = 12.0;
        public double AimSkillMultiplierFlow { get; init; } = 1060.0;
        public double AimSkillMultiplierTotal { get; init; } = 1.1;
        public double AimCombinedSnapNormExponent { get; init; } = 1.2;
        public double AimStrainDecayBase { get; init; } = 0.2;
        public double AimPreservedStrainDecayBase { get; set; } = 0.4;
        public double AimTimeThresholdMinutes { get; set; } = 24;
        public double AimMaxDeltaTime { get; set; } = 5000;
        public double AimRetryCooldownTime { get; set; } = 60000;

        // Speed skill constants
        public double SpeedHarmonicScale { get; init; } = 20;
        public double SpeedDecayExponent { get; init; } = 0.9;
        public double SpeedStrainDecayBurstBase { get; set; } = 0.1;
        public double SpeedStrainDecayStreamBase { get; set; } = 0.01;
        public double SpeedStrainDecayStreamExp { get; set; } = 1.6;
        public double SpeedBurstMultiplier { get; set; } = 2.45;
        public double SpeedStreamMultiplier { get; set; } = 0.2;
        public double SpeedStaminaMultiplier { get; set; } = 0.03;
        public double SpeedTotalMultiplier { get; set; } = 0.80;
        public double SpeedFControlNorm { get; set; } = 1.5;
        public double SpeedMeanExponent { get; set; } = 1.25;

        // Reading skill constants
        public double ReadingSkillMultiplier { get; init; } = 2.5;
        public double ReadingStrainDecayBase { get; init; } = 0.8;
        public double ReadingReducedDifficultyBaseLine { get; init; } = 0.0;
        public double ReadingReducedDifficultyDuration { get; init; } = 60000;

        // Flashlight skill constants
        public double FlashlightSkillMultiplier { get; init; } = 0.056;
        public double FlashlightStrainDecayBase { get; init; } = 0.15;

        // SnapAimEvaluator constants
        public double SnapAimWideAngleScale { get; init; } = 0.3;
        public double SnapAimAcuteAngleScale { get; init; } = 2.13;
        public double SnapAimSliderScale { get; init; } = 0.9;
        public double SnapAimVelocityChangeScale { get; init; } = 0.95;
        public double SnapAimMaxRepetitionNerf { get; init; } = 0.15;
        public double SnapAimMaxVectorInfluence { get; init; } = 0.5;

        // AgilityEvaluator constants
        public double AgilityWideAngleMultiplier { get; set; } = 1.1;
        public double AgilityHighBpmBonusBase { get; init; } = 0.3;
        public double AgilityHighBpmExponent { get; init; } = 0.9;

        // FlowAimEvaluator constants
        public double FlowVelocityChangeScale { get; init; } = 0.52;

        // FlashlightEvaluator constants
        public double FlashlightMaxOpacityBonusScale { get; init; } = 0.4;
        public double FlashlightHiddenBonusScale { get; init; } = 0.2;
        public double FlashlightMinVelocityScale { get; init; } = 0.5;
        public double FlashlightSliderBonusScale { get; init; } = 1.3;
        public double FlashlightMinAngleScale { get; init; } = 0.2;

        // FingerControlEvaluator constants
        public double FingerControlJerkTimeConstant { get; set; } = 400.0;
        public double FingerControlJerkBalancingFactor { get; set; } = 0.9;
        public double FingerControlGallopMidpoint { get; set; } = 37.0;
        public double FingerControlCompressionExponent { get; set; } = 0.5;

        // SpeedEvaluator constants
        public double SpeedMinBonusBpm { get; init; } = 200;
        public double SpeedBalancingFactor { get; init; } = 45;

        //public double SpeedHighBpmBonusBase { get; init; } = 0.3;
        //public double SpeedHighBpmExponent { get; init; } = 1.0;

        // StaminaEvaluator constants
        public double StaminaBaseBpm { get; set; } = 240;
        public double StaminaSpeedBalanceFactor { get; set; } = 16.5;
        public double StaminaSpeedExponent { get; set; } = 1.1;

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

        // Performance calculator constants
        public double TotalPerformanceScale { get; init; } = 1.0;
        public double AccuracyPerformanceMult { get; set; } = 120;
        public double AccuracyPerformanceBase { get; set; } = 7.5;
        public double AccuracyPerformanceExponent { get; set; } = 2;
    }
}
