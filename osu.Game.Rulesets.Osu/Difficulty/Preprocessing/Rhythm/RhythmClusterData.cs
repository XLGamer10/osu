// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

namespace osu.Game.Rulesets.Osu.Difficulty.Preprocessing.Rhythm
{
    public readonly struct RhythmClusterData
    {
        public readonly int Index;
        public readonly int Size;
        public readonly double StartTime;
        public readonly double EndTime;

        public readonly double ParitySurprisal;
        public readonly double GapSurprisal;
        public readonly double InternalSurprisal;

        public readonly double ParityEntropy;
        public readonly double GapEntropy;
        public readonly double InternalEntropy;

        public readonly double InherentRatioComplexity;

        public RhythmClusterData(int index, int size, double startTime, double endTime,
                                 double paritySurprisal, double gapSurprisal, double internalSurprisal,
                                 double parityEntropy, double gapEntropy, double internalEntropy,
                                 double inherentComplexity)
        {
            Index = index;
            Size = size;
            StartTime = startTime;
            EndTime = endTime;
            ParitySurprisal = paritySurprisal;
            GapSurprisal = gapSurprisal;
            InternalSurprisal = internalSurprisal;
            ParityEntropy = parityEntropy;
            GapEntropy = gapEntropy;
            InternalEntropy = internalEntropy;
            InherentRatioComplexity = inherentComplexity;
        }
    }
}
