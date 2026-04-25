using System;
using System.Collections.Generic;

namespace PitStrategy.Core.Util
{
    public static class Statistics
    {
        public static double Mean(IReadOnlyList<double> values)
        {
            if (values.Count == 0) return double.NaN;
            double sum = 0;
            for (int i = 0; i < values.Count; i++) sum += values[i];
            return sum / values.Count;
        }

        public static double StdDev(IReadOnlyList<double> values)
        {
            if (values.Count < 2) return 0;
            double mean = Mean(values);
            double sumSq = 0;
            for (int i = 0; i < values.Count; i++)
            {
                double d = values[i] - mean;
                sumSq += d * d;
            }
            return Math.Sqrt(sumSq / (values.Count - 1));
        }

        /// <summary>Z-score of <paramref name="x"/> against the population in <paramref name="values"/>.</summary>
        public static double ZScore(double x, IReadOnlyList<double> values)
        {
            if (values.Count < 2) return 0;
            double sd = StdDev(values);
            if (sd <= 1e-9) return 0;
            return (x - Mean(values)) / sd;
        }

        public static double WeightedMean(IReadOnlyList<double> values, IReadOnlyList<double> weights)
        {
            if (values.Count != weights.Count) throw new ArgumentException("length mismatch");
            if (values.Count == 0) return double.NaN;
            double num = 0, den = 0;
            for (int i = 0; i < values.Count; i++)
            {
                num += values[i] * weights[i];
                den += weights[i];
            }
            return den <= 1e-12 ? double.NaN : num / den;
        }
    }
}
