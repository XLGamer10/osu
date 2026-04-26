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

        public readonly double ParityCrossEntropy;
        public readonly double GapCrossEntropy;
        public readonly double InternalCrossEntropy;

        public RhythmClusterData(int index, int size, double startTime, double endTime,
                                 double paritySurprisal, double gapSurprisal, double internalSurprisal,
                                 double parityCrossEntropy, double gapCrossEntropy, double internalCrossEntropy)
        {
            Index = index;
            Size = size;
            StartTime = startTime;
            EndTime = endTime;
            ParitySurprisal = paritySurprisal;
            GapSurprisal = gapSurprisal;
            InternalSurprisal = internalSurprisal;
            ParityCrossEntropy = parityCrossEntropy;
            GapCrossEntropy = gapCrossEntropy;
            InternalCrossEntropy = internalCrossEntropy;
        }
    }
}
