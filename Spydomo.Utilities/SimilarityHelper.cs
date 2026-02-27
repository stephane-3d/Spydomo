namespace Spydomo.Utilities
{
    public static class SimilarityHelper
    {
        public static double CosineSimilarity(IReadOnlyList<float> vectorA, IReadOnlyList<float> vectorB)
        {
            if (vectorA.Count != vectorB.Count)
                throw new ArgumentException("Vectors must be the same length");

            double dotProduct = 0.0;
            double normA = 0.0;
            double normB = 0.0;

            for (int i = 0; i < vectorA.Count; i++)
            {
                dotProduct += vectorA[i] * vectorB[i];
                normA += vectorA[i] * vectorA[i];
                normB += vectorB[i] * vectorB[i];
            }

            var denom = Math.Sqrt(normA) * Math.Sqrt(normB);
            return denom == 0 ? 0 : dotProduct / denom;
        }

        public static (T? Best, double BestScore, T? Second, double SecondScore)
            FindTop2EmbeddingMatch<T>(
                IReadOnlyList<float> source,
                IReadOnlyList<(T Item, IReadOnlyList<float> Embedding)> candidates)
        {
            double best = double.NegativeInfinity, second = double.NegativeInfinity;
            T? bestItem = default, secondItem = default;

            foreach (var c in candidates)
            {
                var s = CosineSimilarity(source, c.Embedding);

                if (s > best)
                {
                    second = best; secondItem = bestItem;
                    best = s; bestItem = c.Item;
                }
                else if (s > second)
                {
                    second = s; secondItem = c.Item;
                }
            }

            return (bestItem, best, secondItem, second);
        }

        // Optional helper for the “judge” step (top N candidates)
        public static List<(T Item, double Score)> GetTopN<T>(
            IReadOnlyList<float> source,
            IReadOnlyList<(T Item, IReadOnlyList<float> Embedding)> candidates,
            int n)
        {
            var list = new List<(T Item, double Score)>(candidates.Count);
            foreach (var c in candidates)
                list.Add((c.Item, CosineSimilarity(source, c.Embedding)));

            return list
                .OrderByDescending(x => x.Score)
                .Take(n)
                .ToList();
        }
    }
}
