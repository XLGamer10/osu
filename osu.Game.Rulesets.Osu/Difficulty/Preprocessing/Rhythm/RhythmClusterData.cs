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

        public readonly double ParitySurprise;
        public readonly double GapSurprise;
        public readonly double InternalSurprise;

        public readonly double ParityEntropy;
        public readonly double GapEntropy;
        public readonly double InternalEntropy;

        public readonly double InherentRatioComplexity;

        public RhythmClusterData(int index, int size, double startTime, double endTime,
                                 double paritySurprise, double gapSurprise, double internalSurprise,
                                 double parityEntropy, double gapEntropy, double internalEntropy,
                                 double inherentComplexity)
        {
            Index = index;
            Size = size;
            StartTime = startTime;
            EndTime = endTime;
            ParitySurprise = paritySurprise;
            GapSurprise = gapSurprise;
            InternalSurprise = internalSurprise;
            ParityEntropy = parityEntropy;
            GapEntropy = gapEntropy;
            InternalEntropy = internalEntropy;
            InherentRatioComplexity = inherentComplexity;
        }
    }
}
