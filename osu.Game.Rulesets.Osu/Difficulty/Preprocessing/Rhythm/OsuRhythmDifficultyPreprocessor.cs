// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.Osu.Difficulty.Preprocessing.Rhythm
{
    public static class OsuRhythmDifficultyPreprocessor
    {
        private const int ctw_max_depth = 8;
        private const double ctw_epsilon_factor = 0.3;

        private readonly struct RhythmEvent
        {
            public readonly double Time;
            public readonly double Delta;
            public readonly double HitWindow;
            public readonly OsuDifficultyHitObject? Source;

            public RhythmEvent(double time, double delta, double hitWindow, OsuDifficultyHitObject? source)
            {
                Time = time;
                Delta = delta;
                HitWindow = hitWindow;
                Source = source;
            }
        }

        public static void ProcessAndAssign(List<DifficultyHitObject> objects)
        {
            var events = collectEvents(objects);

            if (events.Count == 0)
                return;

            buildAndScoreClusters(events);
        }

        private static List<RhythmEvent> collectEvents(List<DifficultyHitObject> objects)
        {
            var events = new List<RhythmEvent>();
            double prevTime = 0;

            for (int i = 0; i < objects.Count; i++)
            {
                var obj = (OsuDifficultyHitObject)objects[i];

                if (obj.BaseObject is Spinner)
                    continue;

                double hitTime = obj.StartTime;
                double hitWindow = obj.HitWindow(HitResult.Great);

                events.Add(new RhythmEvent(hitTime, hitTime - prevTime, hitWindow, obj));
                prevTime = hitTime;

                if (obj.BaseObject is Slider slider)
                {
                    double releaseTime = Math.Max(
                        slider.StartTime + slider.Duration + SliderEventGenerator.TAIL_LENIENCY,
                        slider.StartTime + slider.Duration / 2);

                    if (releaseTime > hitTime)
                    {
                        double tailTime = obj.EndTime;
                        events.Add(new RhythmEvent(tailTime, tailTime - prevTime, hitWindow, null));
                        prevTime = tailTime;
                    }
                }
            }

            return events;
        }

        private static void buildAndScoreClusters(List<RhythmEvent> events)
        {
            var clusters = new List<List<RhythmEvent>>();

            if (events.Count == 0)
                return;

            int lastCovered = -1;

            for (int i = 1; i < events.Count;)
            {
                if (events[i].Source == null)
                {
                    i++;
                    continue;
                }

                double delta = Math.Max(events[i].Delta, 1e-7);
                double epsilon = events[i].HitWindow * ctw_epsilon_factor;

                int end = i;

                while (end + 1 < events.Count && events[end + 1].Source != null && Math.Abs(Math.Max(events[end + 1].Delta, 1e-7) - delta) < epsilon)
                    end++;

                if (end > i)
                {
                    for (int k = Math.Max(lastCovered + 1, 0); k < i - 1; k++)
                        clusters.Add(new List<RhythmEvent> { events[k] });

                    var cluster = new List<RhythmEvent>();

                    for (int j = i - 1; j <= end; j++)
                        cluster.Add(events[j]);

                    clusters.Add(cluster);
                    lastCovered = end;
                }

                i = end + 1;
            }

            for (int k = Math.Max(lastCovered + 1, 0); k < events.Count; k++)
                clusters.Add(new List<RhythmEvent> { events[k] });

            mergeDoubles(clusters);

            constructAndEvaluateTrees(clusters);
        }

        private static void constructAndEvaluateTrees(List<List<RhythmEvent>> clusters)
        {
            // Initialize the parity, gap and internal CTW instances
            var parityCtw = new ContextTreeWeighting(ctw_max_depth, 2);
            var gapCtw = new ContextTreeWeighting(ctw_max_depth, RhythmSymbolQuantizer.RATIO_BIN_COUNT);
            var internalCtw = new ContextTreeWeighting(ctw_max_depth, RhythmSymbolQuantizer.RATIO_BIN_COUNT);

            // First pass - construction of trees
            // Track the slider tail decisions (which are based on the sequentially obtained surprisal values)
            // This ensures that the upcoming second pass is consistent
            int[] assignStarts = new int[clusters.Count];
            constructTrees(clusters, parityCtw, gapCtw, internalCtw, assignStarts);

            // Finalize the trees, i.e. lock the global probabilities and resets the context buffers internally
            parityCtw.FinalizeTree();
            gapCtw.FinalizeTree();
            internalCtw.FinalizeTree();

            // Second pass - evaluation of trees
            // Calculate the actual difficulty metrics using the "frozen" tree
            evaluateTrees(clusters, parityCtw, gapCtw, internalCtw, assignStarts);
        }

        private static void constructTrees(List<List<RhythmEvent>> clusters,
                                           ContextTreeWeighting parityCtw,
                                           ContextTreeWeighting gapCtw,
                                           ContextTreeWeighting internalCtw,
                                           int[] assignStarts)
        {
            double prevGap = 0;
            double prevInternalDelta = 0;

            for (int i = 0; i < clusters.Count; i++)
            {
                var cluster = clusters[i];
                bool tailLeading = cluster.Count > 1 && cluster[0].Source == null;

                int assignStart = 0;

                if (tailLeading)
                {
                    var withTail = scoreCandidateConstruction(cluster, 0, parityCtw, gapCtw, internalCtw, prevGap, prevInternalDelta);
                    var withoutTail = scoreCandidateConstruction(cluster, 1, parityCtw, gapCtw, internalCtw, prevGap, prevInternalDelta);

                    assignStart = withoutTail.Surprise <= withTail.Surprise ? 1 : 0;
                    var chosen = assignStart == 1 ? withoutTail : withTail;

                    parityCtw.ConstructTreeNode(chosen.ParitySymbol);
                    gapCtw.ConstructTreeNode(chosen.GapSymbol);
                    internalCtw.ConstructTreeNode(chosen.InternalSymbol);

                    prevGap = chosen.PrevGap;
                    prevInternalDelta = chosen.PrevInternalDelta;
                }
                else
                {
                    int paritySymbol = cluster.Count % 2;
                    double gapDelta = Math.Max(cluster[0].Delta, 1e-7);
                    double epsilon = cluster[0].HitWindow * ctw_epsilon_factor;
                    int gapSymbol = RhythmSymbolQuantizer.QuantizeRatio(gapDelta, prevGap > 0 ? prevGap : gapDelta, epsilon);

                    double internalDelta = cluster.Count > 1 ? averageInternalDelta(cluster, 0) : 0;
                    int internalSymbol = cluster.Count <= 1 || prevInternalDelta <= 0
                        ? RhythmSymbolQuantizer.RATIO_BIN_COUNT / 2
                        : RhythmSymbolQuantizer.QuantizeRatio(internalDelta, prevInternalDelta, epsilon);

                    parityCtw.ConstructTreeNode(paritySymbol);
                    gapCtw.ConstructTreeNode(gapSymbol);
                    internalCtw.ConstructTreeNode(internalSymbol);

                    prevGap = gapDelta;
                    prevInternalDelta = cluster.Count > 1 ? internalDelta : prevInternalDelta;
                }

                assignStarts[i] = assignStart;
            }
        }

        private static void evaluateTrees(List<List<RhythmEvent>> clusters,
                                          ContextTreeWeighting parityCtw,
                                          ContextTreeWeighting gapCtw,
                                          ContextTreeWeighting internalCtw,
                                          int[] assignStarts)
        {
            double prevGap = 0;
            double prevInternalDelta = 0;

            for (int i = 0; i < clusters.Count; i++)
            {
                var cluster = clusters[i];
                int startOffset = assignStarts[i];
                int count = cluster.Count - startOffset;

                int paritySymbol = count % 2;

                double gapDelta = Math.Max(cluster[startOffset].Delta, 1e-7);
                double epsilon = cluster[startOffset].HitWindow * ctw_epsilon_factor;
                int gapSymbol = RhythmSymbolQuantizer.QuantizeRatio(gapDelta, prevGap > 0 ? prevGap : gapDelta, epsilon);

                double internalDelta = count > 1 ? averageInternalDelta(cluster, startOffset) : 0;
                int internalSymbol = count <= 1 || prevInternalDelta <= 0
                    ? RhythmSymbolQuantizer.RATIO_BIN_COUNT / 2
                    : RhythmSymbolQuantizer.QuantizeRatio(internalDelta, prevInternalDelta, epsilon);

                var parityResult = parityCtw.EvaluateTreeNode(paritySymbol);
                var gapResult = gapCtw.EvaluateTreeNode(gapSymbol);
                var internalResult = internalCtw.EvaluateTreeNode(internalSymbol);

                double inherent = RhythmSymbolQuantizer.GetInherentRatioComplexity(gapDelta, prevGap, epsilon);

                var data = new RhythmClusterData(
                    i,
                    count,
                    cluster[startOffset].Time,
                    cluster[^1].Time,
                    parityResult.Surprisal,
                    gapResult.Surprisal,
                    internalResult.Surprisal,
                    parityResult.Entropy,
                    gapResult.Entropy,
                    internalResult.Entropy,
                    inherent
                );

                for (int j = startOffset; j < cluster.Count; j++)
                    cluster[j].Source?.RhythmClusters.Add(data);

                prevGap = gapDelta;
                prevInternalDelta = count > 1 ? internalDelta : prevInternalDelta;
            }

            // Remove singlet entries from notes that also belong to larger clusters
            foreach (var cluster in clusters)
            {
                foreach (var evt in cluster)
                {
                    if (evt.Source != null && evt.Source.RhythmClusters.Count > 1)
                        evt.Source.RhythmClusters.RemoveAll(c => c.Size == 1);
                }
            }
        }

        private static ConstructionResult scoreCandidateConstruction(
            List<RhythmEvent> cluster, int startOffset,
            ContextTreeWeighting parityCtw, ContextTreeWeighting gapCtw, ContextTreeWeighting internalCtw,
            double prevGap, double prevInternalDelta)
        {
            var parityCtwCloned = parityCtw.Clone();
            var gapCtwCloned = gapCtw.Clone();
            var internalCtwCloned = internalCtw.Clone();

            int count = cluster.Count - startOffset;
            int pSym = count % 2;
            double paritySurprisal = parityCtwCloned.UpdateTreeNode(pSym);

            double gapDelta = Math.Max(cluster[startOffset].Delta, 1e-7);
            double epsilon = cluster[startOffset].HitWindow * ctw_epsilon_factor;
            int gapSymbol = RhythmSymbolQuantizer.QuantizeRatio(gapDelta, prevGap > 0 ? prevGap : gapDelta, epsilon);
            double gapSurprisal = gapCtwCloned.UpdateTreeNode(gapSymbol);

            double internalDelta = count > 1 ? averageInternalDelta(cluster, startOffset) : 0;
            int internalSymbol = (count <= 1 || prevInternalDelta <= 0)
                ? RhythmSymbolQuantizer.RATIO_BIN_COUNT / 2
                : RhythmSymbolQuantizer.QuantizeRatio(internalDelta, prevInternalDelta, epsilon);
            double internalSurprisal = internalCtwCloned.UpdateTreeNode(internalSymbol);

            return new ConstructionResult
            {
                Surprise = paritySurprisal + gapSurprisal + internalSurprisal,
                ParitySymbol = pSym, GapSymbol = gapSymbol, InternalSymbol = internalSymbol,
                PrevGap = gapDelta, PrevInternalDelta = count > 1 ? internalDelta : prevInternalDelta
            };
        }

        private static double averageInternalDelta(List<RhythmEvent> cluster, int startOffset = 0)
        {
            double sum = 0;
            int pairs = 0;

            for (int i = startOffset + 1; i < cluster.Count; i++)
            {
                sum += Math.Max(cluster[i].Delta, 1e-7);
                pairs++;
            }

            return pairs > 0 ? sum / pairs : 0;
        }

        private struct ConstructionResult
        {
            public double Surprise;
            public int ParitySymbol;
            public int GapSymbol;
            public int InternalSymbol;
            public double PrevGap;
            public double PrevInternalDelta;
        }

        private static void mergeDoubles(List<List<RhythmEvent>> clusters)
        {
            for (int i = 1; i < clusters.Count; i++)
            {
                double epsilon = clusters[i][0].HitWindow;
                double prev = clusters[i - 1][^1].Delta;
                double curr = clusters[i][0].Delta;

                double next;

                if (clusters[i].Count > 1)
                {
                    next = clusters[i][1].Delta;
                }
                else
                {
                    int nextIndex = i + 1;

                    if (nextIndex < clusters.Count && clusters[nextIndex][0].Source == null)
                        nextIndex++;

                    if (nextIndex >= clusters.Count)
                        continue;

                    next = clusters[nextIndex][0].Time - clusters[i][0].Time;
                }

                if (prev > curr + epsilon && next > curr + epsilon)
                {
                    var doublePair = new List<RhythmEvent> { clusters[i - 1][^1], clusters[i][0] };
                    int prevIdx = i - 1;

                    if (clusters[i].Count == 1)
                        clusters[i] = doublePair;
                    else
                    {
                        clusters.Insert(i, doublePair);
                        i++;
                    }

                    if (clusters[prevIdx].Count == 1)
                    {
                        clusters.RemoveAt(prevIdx);
                        i--;
                    }
                }
            }
        }
    }
}
