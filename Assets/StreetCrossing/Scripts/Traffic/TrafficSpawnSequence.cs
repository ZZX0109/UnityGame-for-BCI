using System;

namespace StreetCrossing.Traffic
{
    /// <summary>Only traffic generation consumes this RNG. Rendering, audio and UI cannot change the sequence.</summary>
    public sealed class TrafficSpawnSequence
    {
        private readonly Random random;
        public TrafficSpawnSequence(int seed) { random = new Random(seed); }
        public float Between(float minimum, float maximum) => minimum + (maximum - minimum) * (float)random.NextDouble();
        public int NextColor() => random.Next(0, 4);
    }
}
