// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;

namespace osu.Game.Rulesets.Osu.Difficulty.Preprocessing.Rhythm
{
    public class CtwNode
    {
        private readonly int alphabetSize;
        private readonly int[] counts;
        private double logProbKT;
        private double logProbWeighted;
        private CtwNode?[]? children;

        private int totalCount;

        public CtwNode(int alphabetSize)
        {
            this.alphabetSize = alphabetSize;
            counts = new int[alphabetSize];
        }

        private CtwNode(CtwNode other)
        {
            alphabetSize = other.alphabetSize;
            counts = (int[])other.counts.Clone();
            logProbKT = other.logProbKT;
            logProbWeighted = other.logProbWeighted;
            totalCount = other.totalCount;

            if (other.children != null)
            {
                children = new CtwNode?[alphabetSize];

                for (int i = 0; i < alphabetSize; i++)
                    children[i] = other.children[i]?.Clone();
            }
        }

        public CtwNode Clone() => new CtwNode(this);

        /// <summary>
        /// Returns the KT-estimated log-probability of the symbol before updating counts.
        /// KT estimator: P(s) = (n_s + 0.5) / (n + K/2)
        /// </summary>
        public double UpdateKT(int symbol)
        {
            double prob = (counts[symbol] + 0.5) / (totalCount + alphabetSize / 2.0);
            double logProb = Math.Log(prob);

            logProbKT += logProb;
            counts[symbol]++;
            totalCount++;

            return logProb;
        }

        public CtwNode GetOrCreateChild(int symbol)
        {
            children ??= new CtwNode[alphabetSize];
            return children[symbol] ??= new CtwNode(alphabetSize);
        }

        // Recomputes weighted probability mixing KT estimate with children's predictions.
        // At leaf depth the KT estimate is used directly; at internal nodes we average
        // the KT estimate with the product of children's weighted probabilities.
        public void RecomputeWeighted(bool isLeaf)
        {
            if (isLeaf)
            {
                logProbWeighted = logProbKT;
                return;
            }

            double logProbChildren = 0;

            if (children != null)
            {
                foreach (var child in children)
                {
                    if (child != null)
                        logProbChildren += child.logProbWeighted;
                }
            }

            logProbWeighted = Math.Log(0.5) + logSumExp(logProbKT, logProbChildren);
        }

        public double LogProbWeighted => logProbWeighted;

        private static double logSumExp(double a, double b)
        {
            double max = Math.Max(a, b);

            if (double.IsNegativeInfinity(max))
                return double.NegativeInfinity;

            return max + Math.Log(Math.Exp(a - max) + Math.Exp(b - max));
        }
    }

    public class ContextTreeWeighting
    {
        private readonly int maxDepth;
        private readonly int alphabetSize;
        private readonly CtwNode root;
        private readonly int[] contextBuffer;
        private int bufferCount;

        public ContextTreeWeighting(int maxDepth, int alphabetSize)
        {
            this.maxDepth = maxDepth;
            this.alphabetSize = alphabetSize;
            root = new CtwNode(alphabetSize);
            contextBuffer = new int[maxDepth];
        }

        private ContextTreeWeighting(ContextTreeWeighting other)
        {
            maxDepth = other.maxDepth;
            alphabetSize = other.alphabetSize;
            root = other.root.Clone();
            contextBuffer = (int[])other.contextBuffer.Clone();
            bufferCount = other.bufferCount;
        }

        public ContextTreeWeighting Clone() => new ContextTreeWeighting(this);

        // Returns surprise (-log P_ctw) for the symbol before updating the model.
        // Walks the context tree from root to leaf using the context buffer,
        // updates KT estimates bottom-up, then recomputes weighted probabilities.
        public double Update(int symbol)
        {
            double previousLogProb = root.LogProbWeighted;

            int depth = Math.Min(bufferCount, maxDepth);

            // Collect nodes along the context path (root to leaf)
            var path = new CtwNode[depth + 1];
            path[0] = root;

            for (int d = 0; d < depth; d++)
            {
                int contextSymbol = contextBuffer[(bufferCount - 1 - d) % maxDepth];
                path[d + 1] = path[d].GetOrCreateChild(contextSymbol);
            }

            // Update KT estimates at every node along the path
            for (int d = depth; d >= 0; d--)
                path[d].UpdateKT(symbol);

            // Recompute weighted probabilities bottom-up
            for (int d = depth; d >= 0; d--)
                path[d].RecomputeWeighted(d == depth);

            // Store symbol in circular context buffer
            contextBuffer[bufferCount % maxDepth] = symbol;
            bufferCount++;

            // Surprise = negative log-probability of this symbol under the CTW model
            return -(root.LogProbWeighted - previousLogProb);
        }
    }
}
