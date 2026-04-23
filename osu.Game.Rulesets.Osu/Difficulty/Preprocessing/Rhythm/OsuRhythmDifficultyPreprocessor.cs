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
        private const double ctw_epsilon_factor = 0.5;

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

            scoreClusters(clusters);
        }

        private static void scoreClusters(List<List<RhythmEvent>> clusters)
        {
            var parityCtw = new ContextTreeWeighting(ctw_max_depth, 2);
            var gapCtw = new ContextTreeWeighting(ctw_max_depth, RhythmSymbolQuantizer.RATIO_BIN_COUNT);
            var internalCtw = new ContextTreeWeighting(ctw_max_depth, RhythmSymbolQuantizer.RATIO_BIN_COUNT);

            double prevGap = 0;
            double prevInternalDelta = 0;

            var scoredStartTimes = new List<double>();
            var scoredLeadingDeltas = new List<double>();

            for (int i = 0; i < clusters.Count; i++)
            {
                var cluster = clusters[i];
                bool tailLeading = cluster.Count > 1 && cluster[0].Source == null;

                ScoredCluster scored;
                int assignStart = 0;

                if (tailLeading)
                {
                    var withTail = scoreCandidate(cluster, 0, parityCtw, gapCtw, internalCtw, prevGap, prevInternalDelta);
                    var withoutTail = scoreCandidate(cluster, 1, parityCtw, gapCtw, internalCtw, prevGap, prevInternalDelta);

                    scored = withoutTail.Surprise <= withTail.Surprise ? withoutTail : withTail;
                    assignStart = withoutTail.Surprise <= withTail.Surprise ? 1 : 0;
                }
                else
                {
                    scored = scoreCandidate(cluster, 0, parityCtw, gapCtw, internalCtw, prevGap, prevInternalDelta);
                }

                parityCtw = scored.ParityCtw;
                gapCtw = scored.GapCtw;
                internalCtw = scored.InternalCtw;
                prevGap = scored.PrevGap;
                prevInternalDelta = scored.PrevInternalDelta;

                scoredStartTimes.Add(scored.StartTime);
                scoredLeadingDeltas.Add(Math.Max(cluster[0].Delta, 1.0));

                int contextIdx = Math.Max(0, scoredStartTimes.Count - ctw_max_depth);
                double contextTime = scored.EndTime - scoredStartTimes[contextIdx] + scoredLeadingDeltas[contextIdx];
                double timeScale = 1000.0 / Math.Max(contextTime, 1.0);

                // Suppress clusters before the CTW context window is full.
                if (scoredStartTimes.Count <= ctw_max_depth)
                    timeScale = 0;

                var data = new RhythmClusterData(i, scored.Count, scored.StartTime, scored.EndTime,
                    scored.ParitySurprise * timeScale, scored.GapSurprise * timeScale, scored.InternalSurprise * timeScale);

                for (int j = assignStart; j < cluster.Count; j++)
                    cluster[j].Source?.RhythmClusters.Add(data);
            }

            // Remove singlet entries from notes that also belong to larger clusters.
            foreach (var cluster in clusters)
            {
                foreach (var evt in cluster)
                {
                    if (evt.Source != null && evt.Source.RhythmClusters.Count > 1)
                        evt.Source.RhythmClusters.RemoveAll(c => c.Size == 1);
                }
            }
        }

        private static ScoredCluster scoreCandidate(
            List<RhythmEvent> cluster, int startOffset,
            ContextTreeWeighting parityCtw, ContextTreeWeighting gapCtw, ContextTreeWeighting internalCtw,
            double prevGap, double prevInternalDelta)
        {
            var parity = parityCtw.Clone();
            var gap = gapCtw.Clone();
            var intern = internalCtw.Clone();

            int count = cluster.Count - startOffset;
            double startTime = cluster[startOffset].Time;
            double endTime = cluster[^1].Time;

            double paritySurprise = parity.Update(count % 2);

            double gapDelta = Math.Max(cluster[startOffset].Delta, 1e-7);
            double epsilon = cluster[startOffset].HitWindow * ctw_epsilon_factor;
            double gapSurprise = gap.Update(RhythmSymbolQuantizer.QuantizeRatio(gapDelta, prevGap > 0 ? prevGap : gapDelta, epsilon));
            double newPrevGap = gapDelta;

            double internalDelta = count > 1 ? averageInternalDelta(cluster, startOffset) : 0;
            int internalSym = count <= 1 || prevInternalDelta <= 0
                ? RhythmSymbolQuantizer.RATIO_BIN_COUNT / 2
                : RhythmSymbolQuantizer.QuantizeRatio(internalDelta, prevInternalDelta, epsilon);
            double internalSurprise = intern.Update(internalSym);

            double newPrevInternalDelta = count > 1 ? internalDelta : prevInternalDelta;

            return new ScoredCluster
            {
                Count = count,
                StartTime = startTime,
                EndTime = endTime,
                ParitySurprise = paritySurprise,
                GapSurprise = gapSurprise,
                InternalSurprise = internalSurprise,
                Surprise = paritySurprise + gapSurprise + internalSurprise,
                ParityCtw = parity,
                GapCtw = gap,
                InternalCtw = intern,
                PrevGap = newPrevGap,
                PrevInternalDelta = newPrevInternalDelta,
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

        private struct ScoredCluster
        {
            public int Count;
            public double StartTime;
            public double EndTime;
            public double ParitySurprise;
            public double GapSurprise;
            public double InternalSurprise;
            public double Surprise;
            public ContextTreeWeighting ParityCtw;
            public ContextTreeWeighting GapCtw;
            public ContextTreeWeighting InternalCtw;
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
