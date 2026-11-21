using System;
using UnityEngine;

namespace AntiqueTradingSimulator.Diagnostics
{
    /// <summary>
    /// Optionally seeds UnityEngine.Random so a run can be repeated and two runs compared.
    /// </summary>
    public static class TelemetrySeed
    {
        /// <summary>The seed applied, or -1 when Random was left untouched.</summary>
        public static int Seed { get; private set; } = -1;

        public static bool Applied { get; private set; }

#if ATS_DIAGNOSTICS
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Apply()
        {
            if (Applied) return;

            if (!TryResolve(out int seed)) return;

            Seed = seed;
            UnityEngine.Random.InitState(seed);
            // Runtime ids feed NPC and auction rolls, so they must repeat too (see Core.IdFactory).
            Core.IdFactory.Seed(seed);
            Applied = true;
            Debug.Log($"Telemetry: UnityEngine.Random seeded with {seed} (requested explicitly).");
        }
#endif

        private static bool TryResolve(out int seed)
        {
            seed = -1;

            try
            {
                foreach (string arg in Environment.GetCommandLineArgs())
                {
                    if (arg == null || !arg.StartsWith("-atsSeed:", StringComparison.OrdinalIgnoreCase)) continue;
                    if (int.TryParse(arg.Substring("-atsSeed:".Length), out seed)) return true;
                }

                string env = Environment.GetEnvironmentVariable("ATS_SEED");
                if (!string.IsNullOrEmpty(env) && int.TryParse(env, out seed)) return true;
            }
            catch
            {
                return false;
            }

            return false;
        }
    }
}
