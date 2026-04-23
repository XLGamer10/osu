// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;

namespace osu.Game.Rulesets.Osu.Difficulty.Preprocessing.Rhythm
{
    public class CtwNode
    {
        private readonly int alphabetSize;
        private readonly int[] counts;
        private double logProbKt;
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
            logProbKt = other.logProbKt;
            LogProbWeighted = other.LogProbWeighted;
            totalCount = other.totalCount;

            if (other.children == null) return;

            children = new CtwNode?[alphabetSize];

            for (int i = 0; i < alphabetSize; i++)
                children[i] = other.children[i]?.Clone();
        }

        public CtwNode Clone() => new CtwNode(this);

        /// <summary>
        /// Returns the KT-estimated log-probability of the symbol before updating counts.
        /// KT estimator: P(s) = (n_s + 0.5) / (n + K/2)
        /// </summary>
        public double UpdateKt(int symbol)
        {
            double prob = (counts[symbol] + 0.5) / (totalCount + alphabetSize / 2.0);
            double logProb = Math.Log(prob);

            logProbKt += logProb;
            counts[symbol]++;
            totalCount++;

            return logProb;
        }

        public CtwNode GetOrCreateChild(int symbol)
        {
            children ??= new CtwNode[alphabetSize];
            return children[symbol] ??= new CtwNode(alphabetSize);
        }

        public CtwNode? GetChild(int symbol)
        {
            return children?[symbol];
        }

        /// <summary>
        /// Recomputes weighted probability mixing KT estimate with children's predictions.
        /// At leaf depth, use the KT estimate directly; at internal nodes, average
        /// the KT estimate with the product of children's weighted probabilities.
        /// </summary>
        public void RecomputeWeighted(bool isLeaf)
        {
            if (isLeaf)
            {
                LogProbWeighted = logProbKt;
                return;
            }

            double logProbChildren = 0;

            if (children != null)
            {
                foreach (var child in children)
                {
                    if (child != null)
                        logProbChildren += child.LogProbWeighted;
                }
            }

            LogProbWeighted = Math.Log(0.5) + LogSumExp(logProbKt, logProbChildren);
        }

        public double LogProbWeighted { get; private set; }

        public static double LogSumExp(double a, double b)
        {
            double max = Math.Max(a, b);

            if (double.IsNegativeInfinity(max))
                return double.NegativeInfinity;

            return max + Math.Log(Math.Exp(a - max) + Math.Exp(b - max));
        }

        /// <summary>
        /// Only return the KT-estimated log-probability of the symbol.
        /// </summary>
        public double GetKtProb(int symbol)
        {
            return (counts[symbol] + 0.5) / (totalCount + alphabetSize / 2.0);
        }

        /// <summary>
        /// Return the entropy of the decision space at the node.
        /// </summary>
        public double GetEntropy()
        {
            double entropy = 0;

            for (int i = 0; i < alphabetSize; i++)
            {
                double p = (counts[i] + 0.5) / (totalCount + alphabetSize / 2.0);
                entropy -= p * Math.Log(p, 2);
            }

            return entropy;
        }

        /// <summary>
        /// Recursively recomputes weighted probabilities bottom-up.
        /// </summary>
        public void FinalizeProbabilities(int currentDepth, int maxDepth)
        {
            if (children != null)
            {
                foreach (var child in children)
                    child?.FinalizeProbabilities(currentDepth + 1, maxDepth);
            }

            RecomputeWeighted(currentDepth == maxDepth);
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

        public struct EvaluationResult
        {
            public double Surprisal;
            public double Entropy;
        }

        /// <summary>
        /// Evaluates the symbol against the frozen global tree.
        /// Returns surprisal and entropy without updating node counts.
        /// </summary>
        public EvaluationResult EvaluateTree(int symbol)
        {
            int maxSearchDepth = Math.Min(bufferCount, maxDepth);
            var path = new CtwNode[maxSearchDepth + 1];
            path[0] = root;

            int actualDepth = 0;

            // Collect the path
            for (int d = 0; d < maxSearchDepth; d++)
            {
                int contextSymbol = contextBuffer[(bufferCount - 1 - d) % maxDepth];
                var child = path[d].GetChild(contextSymbol);

                if (child == null)
                    break;

                path[d + 1] = child;
                actualDepth++;
            }

            // Calculate the weighted log-probability of THIS specific symbol
            // To be consistent with RecomputeWeighted, mix bottom-up
            double logProbMixed = 0;

            for (int d = actualDepth; d >= 0; d--)
            {
                double logProbKt = Math.Log(path[d].GetKtProb(symbol));

                if (d == actualDepth)
                {
                    logProbMixed = logProbKt;
                }
                else
                {
                    // Mix the KT estimate of the current node with the mixed estimate of the deeper context
                    // "Read-only" analog to dynamically constructing the CTW tree
                    // P_mixed = 0.5 * P_kt + 0.5 * P_deeper
                    logProbMixed = Math.Log(0.5) + CtwNode.LogSumExp(logProbKt, logProbMixed);
                }
            }

            double surprisal = -logProbMixed / Math.Log(2); // Convert to bits
            double entropy = path[actualDepth].GetEntropy(); // Contextual entropy at the deepest node

            // Update the sliding context window
            contextBuffer[bufferCount % maxDepth] = symbol;
            bufferCount++;

            return new EvaluationResult
            {
                Surprisal = surprisal,
                Entropy = entropy
            };
        }

        /// <summary>
        /// Gradually construct the global tree using the current symbol.
        /// </summary>
        public void ConstructTree(int symbol)
        {
            int depth = Math.Min(bufferCount, maxDepth);
            var path = new CtwNode[depth + 1];
            path[0] = root;

            for (int d = 0; d < depth; d++)
            {
                int contextSymbol = contextBuffer[(bufferCount - 1 - d) % maxDepth];
                path[d + 1] = path[d].GetOrCreateChild(contextSymbol);
            }

            // Strictly update counts along the path
            for (int d = depth; d >= 0; d--)
                path[d].UpdateKt(symbol);

            contextBuffer[bufferCount % maxDepth] = symbol;
            bufferCount++;
        }

        /// <summary>
        /// Finalize the probabilities of the entire tree to be ready for evaluation.
        /// </summary>
        public void FinalizeTree()
        {
            root.FinalizeProbabilities(0, maxDepth);

            // Reset the bufferCount and contextBuffer so the Evaluation pass
            // starts with a fresh context, exactly like the Construction pass did
            bufferCount = 0;
            Array.Clear(contextBuffer, 0, contextBuffer.Length);
        }
    }
}
