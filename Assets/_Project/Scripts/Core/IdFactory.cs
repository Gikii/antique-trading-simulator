using System;

namespace AntiqueTradingSimulator.Core
{
    /// <summary>
    /// Makes the ids of runtime objects (listings, NPCs, contracts, shipments, auctions, news).
    /// Normally a random GUID. Once seeded (by -atsSeed / autoplay) the ids come from a seeded
    /// generator instead, so two runs with the same seed get the same ids. That matters beyond
    /// labels: NPCs and auctions seed their own random rolls from their id, so random GUIDs made
    /// every run different even with UnityEngine.Random seeded.
    /// </summary>
    public static class IdFactory
    {
        private static Random _rng;

        /// <summary>True once <see cref="Seed"/> has been called: ids repeat from run to run.</summary>
        public static bool IsSeeded => _rng != null;

        public static void Seed(int seed) => _rng = new Random(seed);

        /// <summary>A new 32-character id (GUID "N" format).</summary>
        public static string NewId()
        {
            if (_rng == null) return Guid.NewGuid().ToString("N");

            var bytes = new byte[16];
            _rng.NextBytes(bytes);
            return new Guid(bytes).ToString("N");
        }
    }
}
